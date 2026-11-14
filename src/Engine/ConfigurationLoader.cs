// SPDX-License-Identifier: Apache-2.0

using System.Text;
using Tomlyn;
using Tomlyn.Parsing;
using Tomlyn.Syntax;

namespace InvenioTech.GateKeeper.Engine;

/// <summary>
/// Locates, reads, validates and resolves the settings file
/// <c>.gatekeeper/gatekeeper.toml</c> (configuration specification, sections 2
/// and 4). Everything read from a customer repository lives in
/// <c>.gatekeeper/</c>; the loader never walks parent directories and never
/// reads a root-level configuration file (IQ-02).
///
/// When the file is absent the section 4 defaults apply and nothing is warned
/// about. An unknown key warns and does not fail the scan (IQ-17). A known key
/// with the wrong value, a wrong <c>schemaVersion</c>, a byte-order mark, an
/// oversized file or a document TOML cannot parse is a tool error, so the
/// invoking command exits 2: the configuration cannot be used (rule 13).
///
/// The loader is one shared implementation for the scan orchestrator and the
/// middleware; it never writes the settings file back, and it never claims a
/// scan ran.
/// </summary>
public static class ConfigurationLoader
{
    private const int MaxSettingsFileBytes = 1_048_576;
    private const int MinScanFileBytes = 1;
    private const int MaxScanFileBytes = 104_857_600;
    private const int MaxDisabledReasonCharacters = 2_000;
    private const int MaxRepositoryIdentityCharacters = 200;
    private const string SettingsFileRelativePath = ".gatekeeper/gatekeeper.toml";
    private const string SettingsFileName = "gatekeeper.toml";
    private const string CheckFolderName = ".gatekeeper";

    private static readonly string[] KnownRootKeys =
    [
        "schemaVersion", "repository", "scan", "baseline", "fail-on", "severity", "rules", "packs",
    ];

    private static readonly IReadOnlySet<string> RepositoryChildKeys = new HashSet<string>(StringComparer.Ordinal) { "id" };
    private static readonly IReadOnlySet<string> ScanChildKeys = new HashSet<string>(StringComparer.Ordinal)
    {
        "profile", "history", "maxFileBytes", "exclude", "includeSubmodules",
    };
    private static readonly IReadOnlySet<string> BaselineChildKeys = new HashSet<string>(StringComparer.Ordinal) { "maxAgeDays" };
    private static readonly IReadOnlySet<string> SeverityChildKeys = new HashSet<string>(StringComparer.Ordinal) { "overrides" };
    private static readonly IReadOnlySet<string> RulesChildKeys = new HashSet<string>(StringComparer.Ordinal) { "disabled" };
    private static readonly IReadOnlySet<string> DisabledEntryKeys = new HashSet<string>(StringComparer.Ordinal) { "id", "reason" };
    private static readonly IReadOnlySet<string> PacksChildKeys = new HashSet<string>(StringComparer.Ordinal) { "local" };

    /// <summary>
    /// Loads the settings file for the tree rooted at <paramref name="rootPath"/>.
    /// </summary>
    /// <param name="rootPath">The scan root: the git work-tree root when the tree is a git work tree, and the directory the user asked to scan otherwise.</param>
    /// <returns>The effective settings, with the load's warnings and tool errors.</returns>
    public static ConfigurationLoadResult Load(string rootPath)
    {
        var settingsPath = Path.Combine(rootPath, CheckFolderName, SettingsFileName);
        if (!File.Exists(settingsPath))
        {
            return new ConfigurationLoadResult(Defaults(), [], []);
        }

        var errors = new List<string>();
        var warnings = new List<string>();
        var bytes = File.ReadAllBytes(settingsPath);
        var hasByteOrderMark = StartsWithByteOrderMark(bytes);
        var isOversized = bytes.Length > MaxSettingsFileBytes;

        if (hasByteOrderMark)
        {
            errors.Add(
                $"'{SettingsFileRelativePath}' begins with a byte-order mark. The settings file must be plain UTF-8, " +
                "because a byte-order mark changes the meaning of the first key. Re-save the file as UTF-8 without the mark.");
        }

        if (isOversized)
        {
            errors.Add(
                $"'{SettingsFileRelativePath}' is {bytes.Length} bytes, which is over the 1,048,576-byte limit. " +
                "Move anything that is not settings out of the file: settings name paths, limits and dates, never value data.");
        }

        if (!hasByteOrderMark && !isOversized)
        {
            var toml = Encoding.UTF8.GetString(bytes);
            var document = SyntaxParser.Parse(toml, SettingsFileName, validate: true);

            if (document.Diagnostics.HasErrors)
            {
                errors.Add(InvalidTomlSentence(document.Diagnostics[0]));
            }
            else
            {
                foreach (var unknownKey in CollectUnknownKeys(document).OrderBy(key => key, StringComparer.Ordinal))
                {
                    warnings.Add(
                        $"'{SettingsFileRelativePath}' carries the unknown key '{unknownKey}', which the scan ignores (IQ-17). " +
                        "Compare the key with the configuration specification, then correct or remove it: an ignored key " +
                        "silently drops any effect you meant it to have.");
                }

                ScanSettingsFile? file = null;
                try
                {
                    file = TomlSerializer.Deserialize(toml, ScanSettingsTomlContext.Default.ScanSettingsFile);
                }
                catch (TomlException exception)
                {
                    errors.Add(
                        $"'{SettingsFileRelativePath}' cannot be read: {exception.Message}. " +
                        "Give each known key the type the configuration specification lists, or remove the key.");
                }

                var settings = file is null ? null : ResolveSettings(file, errors);
                if (settings is not null)
                {
                    return new ConfigurationLoadResult(settings, warnings, []);
                }
            }
        }

        return new ConfigurationLoadResult(null, warnings, errors);
    }

    /// <summary>The effective settings when the settings file is absent: every section 4 default applies.</summary>
    private static ScanSettings Defaults()
    {
        return new ScanSettings(
            Profile: ScanProfile.Default,
            ScanHistory: false,
            MaxFileBytes: DefaultMaxFileBytesFor(ScanProfile.Default),
            Exclude: [],
            IncludeSubmodules: false,
            RepositoryId: null,
            BaselineMaxAgeDays: 0,
            FailOn: Severity.Error,
            SeverityOverrides: new Dictionary<string, Severity>(),
            DisabledRules: [],
            LocalPacks: []);
    }

    private static int DefaultMaxFileBytesFor(ScanProfile profile) =>
        profile == ScanProfile.Deep ? 10_485_760 : 1_048_576;

    private static string InvalidTomlSentence(DiagnosticMessage diagnostic) =>
        $"'{SettingsFileRelativePath}' is not valid TOML at line {LineOf(diagnostic.Span)}. " +
        $"The parser says: {diagnostic.Message}. Fix the syntax the parser names.";

    /// <summary>
    /// Checks every key the file carries in the order section 4 fixes them, so
    /// the first error for the same file is always the same, then resolves the
    /// settings once nothing failed.
    /// </summary>
    /// <returns>The settings, or null when any check failed.</returns>
    private static ScanSettings? ResolveSettings(ScanSettingsFile file, List<string> errors)
    {
        // schemaVersion is checked first because every other key reads only
        // under the version the file is declared with.
        if (file.SchemaVersion is null)
        {
            errors.Add(
                $"'{SettingsFileRelativePath}' does not set the integer schemaVersion. " +
                "Add the line 'schemaVersion = 1'; with the file present, nothing reads without it.");
            return null;
        }

        if (file.SchemaVersion != 1)
        {
            errors.Add(
                $"'{SettingsFileRelativePath}' sets schemaVersion to {file.SchemaVersion}, which this build does not read. " +
                "Keep the file at version 1, or use a build that reads the version you wrote.");
            return null;
        }

        var profile = ScanProfile.Default;
        if (file.Scan?.Profile is { } profileWord)
        {
            if (!TryMapProfile(profileWord, out profile))
            {
                errors.Add(
                    $"'{SettingsFileRelativePath}' sets scan.profile to '{profileWord}', which is not a profile this build carries. " +
                    "Set it to 'default' or 'deep', or remove the key to use 'default'.");
                return null;
            }
        }

        var scanHistory = file.Scan?.History ?? profile == ScanProfile.Deep;
        var maxFileBytes = file.Scan?.MaxFileBytes ?? DefaultMaxFileBytesFor(profile);
        if (file.Scan?.MaxFileBytes is { } limit && limit is < MinScanFileBytes or > MaxScanFileBytes)
        {
            errors.Add(
                $"'{SettingsFileRelativePath}' sets scan.maxFileBytes to {limit}, which is outside the allowed range of " +
                $"{MinScanFileBytes} to {MaxScanFileBytes}. Set the limit to a byte count inside that range.");
        }

        if (file.Baseline?.MaxAgeDays is { } ageCap && ageCap < 0)
        {
            errors.Add(
                $"'{SettingsFileRelativePath}' sets baseline.maxAgeDays to {ageCap}, which is below 0. " +
                "Set it to 0 so baseline entries never age out, or to a positive number of days.");
        }

        var failOn = Severity.Error;
        if (file.FailOn is { } failOnWord && !TryMapSeverity(failOnWord, out failOn))
        {
            errors.Add(
                $"'{SettingsFileRelativePath}' sets fail-on to '{failOnWord}', which is not a severity this build carries. " +
                "Set it to 'error', 'warning' or 'note', or remove the key to use 'error'.");
        }

        var severityOverrides = new Dictionary<string, Severity>();
        if (file.Severity?.Overrides is not null)
        {
            foreach (var entry in file.Severity.Overrides)
            {
                if (!TryMapSeverity(entry.Value, out var severity))
                {
                    errors.Add(
                        $"'{SettingsFileRelativePath}' maps the rule '{entry.Key}' in severity.overrides to '{entry.Value}', " +
                        "which is not a severity this build carries. Set the value to 'error', 'warning' or 'note'.");
                }
                else
                {
                    severityOverrides.Add(entry.Key, severity);
                }
            }
        }

        var disabledRules = new List<DisabledRule>();
        if (file.Rules?.Disabled is not null)
        {
            var seenIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (var entry in file.Rules.Disabled)
            {
                var id = entry.Id?.Trim();
                if (string.IsNullOrEmpty(id))
                {
                    errors.Add(
                        $"'{SettingsFileRelativePath}' carries a rules.disabled entry without a rule identifier. " +
                        "Set id = to the rule you are disabling.");
                    continue;
                }

                var reason = entry.Reason?.Trim();
                if (reason is null || reason.Length is 0)
                {
                    errors.Add(
                        $"'{SettingsFileRelativePath}' disables the rule '{id}' without a written reason. " +
                        "Add reason = with one line saying why the rule is disabled (IQ-24); the reason is recorded in the evidence.");
                }
                else if (reason.Contains('\r') || reason.Contains('\n'))
                {
                    errors.Add(
                        $"'{SettingsFileRelativePath}' disables the rule '{id}' with a reason that spans lines. " +
                        "Write the reason as a single line without a carriage return or line feed.");
                }
                else if (reason.Length > MaxDisabledReasonCharacters)
                {
                    errors.Add(
                        $"'{SettingsFileRelativePath}' disables the rule '{id}' with a reason of {reason.Length} characters, " +
                        "over the 2,000-character limit. Shorten the reason: the evidence records it verbatim.");
                }

                if (!seenIds.Add(id))
                {
                    errors.Add(
                        $"'{SettingsFileRelativePath}' disables the rule '{id}' twice. " +
                        "Keep one rules.disabled entry per rule identifier.");
                    continue;
                }

                disabledRules.Add(new DisabledRule(id, reason ?? string.Empty));
            }
        }

        var localPacks = new List<string>();
        if (file.Packs?.Local is not null)
        {
            foreach (var packPath in file.Packs.Local)
            {
                if (packPath.StartsWith('/') || packPath.Contains('\\') || packPath.Contains(".."))
                {
                    errors.Add(
                        $"'{SettingsFileRelativePath}' lists the path '{packPath}' in packs.local, which escapes the tree. " +
                        "Local pack paths (IQ-25) are relative to the repository root, use '/' separators only, and " +
                        "cannot contain '..', a leading '/' or any backslash.");
                }
                else
                {
                    localPacks.Add(packPath);
                }
            }
        }

        if (file.Repository?.Id is { } identity && !IsValidRepositoryIdentity(identity))
        {
            errors.Add(
                $"'{SettingsFileRelativePath}' sets repository.id to a value this build cannot use as a relative path. " +
                $"The identity is 1 to {MaxRepositoryIdentityCharacters} characters with no whitespace, no backslash, no leading " +
                "'/' and no '..' segment (IQ-29). Write a plain identifier such as 'github.com/acme/payments'.");
        }

        if (errors.Count is not 0)
        {
            return null;
        }

        return new ScanSettings(
            Profile: profile,
            ScanHistory: scanHistory,
            MaxFileBytes: maxFileBytes,
            Exclude: file.Scan?.Exclude ?? [],
            IncludeSubmodules: file.Scan?.IncludeSubmodules ?? false,
            RepositoryId: file.Repository?.Id,
            BaselineMaxAgeDays: file.Baseline?.MaxAgeDays ?? 0,
            FailOn: failOn,
            SeverityOverrides: severityOverrides,
            DisabledRules: disabledRules,
            LocalPacks: localPacks);
    }

    private static bool TryMapProfile(string word, out ScanProfile profile)
    {
        switch (word)
        {
            case "default":
                profile = ScanProfile.Default;
                return true;
            case "deep":
                profile = ScanProfile.Deep;
                return true;
            default:
                profile = default;
                return false;
        }
    }

    private static bool TryMapSeverity(string word, out Severity severity)
    {
        switch (word)
        {
            case "error":
                severity = Severity.Error;
                return true;
            case "warning":
                severity = Severity.Warning;
                return true;
            case "note":
                severity = Severity.Note;
                return true;
            default:
                severity = default;
                return false;
        }
    }

    /// <summary>
    /// True when the identity satisfies section 10 of the configuration
    /// specification: 1 to 200 characters, no whitespace, no backslash, no
    /// leading slash and no '..' segment, so the value stays usable as a path
    /// under a sink root.
    /// </summary>
    private static bool IsValidRepositoryIdentity(string identity)
    {
        if (identity.Length is 0 or > MaxRepositoryIdentityCharacters)
        {
            return false;
        }

        if (identity.Any(char.IsWhiteSpace) || identity.Contains('\\') || identity.StartsWith('/'))
        {
            return false;
        }

        foreach (var segment in identity.Split('/'))
        {
            if (segment == "..")
            {
                return false;
            }
        }

        return true;
    }

    private static bool StartsWithByteOrderMark(byte[] bytes)
    {
        return bytes.Length switch
        {
            >= 3 when bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF => true,
            >= 2 when bytes[0] == 0xFF && bytes[1] == 0xFE => true,
            >= 2 when bytes[0] == 0xFE && bytes[1] == 0xFF => true,
            _ => false,
        };
    }

    /// <summary>
    /// Collects every key the document carries that the configuration
    /// specification does not list, as a dotted path such as
    /// <c>scan.supervisionDepth</c>. Quoted keys count as one path segment, so a
    /// quoted rule identifier under <c>severity.overrides</c> is never unknown,
    /// while an unquoted dotted key still reads segment by segment.
    /// </summary>
    private static IReadOnlyList<string> CollectUnknownKeys(DocumentSyntax document)
    {
        var unknownKeys = new List<string>();
        CollectUnknowns(document.KeyValues, string.Empty, unknownKeys);

        foreach (var tableNode in Children(document.Tables))
        {
            if (tableNode is not TableSyntaxBase table)
            {
                continue;
            }

            var tableName = table.Name is { } name ? FlattenKey(name) : string.Empty;
            if (tableName.Length is 0)
            {
                continue;
            }
            if (!KeyIsKnown(tableName.Split('.')))
            {
                unknownKeys.Add(tableName);
            }

            foreach (var item in Children(table.Items))
            {
                CollectUnknowns(new[] { item }, tableName, unknownKeys);
            }
        }

        return unknownKeys;
    }

    private static void CollectUnknowns(IEnumerable<SyntaxNode> keyValues, string prefix, List<string> unknownKeys)
    {
        foreach (var node in keyValues)
        {
            if (node is not KeyValueSyntax keyValue)
            {
                continue;
            }

            var keyPath = keyValue.Key is { } keySyntax ? FlattenKey(keySyntax) : string.Empty;
            if (keyPath.Length is 0)
            {
                continue;
            }
            var segments = prefix.Length is 0
                ? keyPath.Split('.')
                : [.. prefix.Split('.'), .. keyPath.Split('.')];

            if (!KeyIsKnown(segments))
            {
                unknownKeys.Add(string.Join('.', segments));
            }
        }
    }

    /// <summary>
    /// Walks the known-key trie for one dotted key segment by segment; the
    /// children of <c>severity.overrides</c> are rule identifiers of any name,
    /// so the walk accepts any segment there.
    /// </summary>
    private static bool KeyIsKnown(string[] segments)
    {
        if (segments.Length is 0)
        {
            return false;
        }

        if (!KnownRootKeys.Contains(segments[0]))
        {
            return false;
        }

        for (var index = 1; index < segments.Length; index++)
        {
            var segment = segments[index];
            var parent = string.Join('.', segments, 0, index);

            if (parent == "severity.overrides")
            {
                // The children are rule identifiers; any name is valid.
                return true;
            }

            var allowedChildren = parent switch
            {
                "repository" => RepositoryChildKeys,
                "scan" => ScanChildKeys,
                "baseline" => BaselineChildKeys,
                "severity" => SeverityChildKeys,
                "rules" => RulesChildKeys,
                "rules.disabled" => DisabledEntryKeys,
                "packs" => PacksChildKeys,
                _ => null,
            };

            if (allowedChildren is null || !allowedChildren.Contains(segment))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Reads the children of a syntax list by index, in document order.</summary>
    private static IEnumerable<SyntaxNode> Children(SyntaxList list)
    {
        for (var index = 0; index < list.ChildrenCount; index++)
        {
            if (list.GetChild(index) is { } child)
            {
                yield return child;
            }
        }
    }

    /// <summary>Reads the children of a typed syntax list by index, in document order.</summary>
    private static IEnumerable<TSyntaxNode> Children<TSyntaxNode>(SyntaxList<TSyntaxNode> list)
        where TSyntaxNode : SyntaxNode
    {
        for (var index = 0; index < list.ChildrenCount; index++)
        {
            if (list.GetChild(index) is { } child)
            {
                yield return child;
            }
        }
    }

    /// <summary>
    /// Joins a key and its dotted continuations into one dotted path, stripping
    /// escape sequences from quoted parts so a quoted path segment reads the
    /// same as its unquoted spelling.
    /// </summary>
    private static string FlattenKey(KeySyntax key)
    {
        var parts = new List<string>();
        if (key.Key is { } firstPart && KeyText(firstPart) is { } firstText)
        {
            parts.Add(firstText);
        }

        foreach (var child in Children(key.DotKeys))
        {
            if (child is DottedKeyItemSyntax item && item.Key is { } partKey && KeyText(partKey) is { } partText)
            {
                parts.Add(partText);
            }
        }

        return string.Join('.', parts);
    }

    /// <summary>The text of one key part: a bare key's token text, or a quoted key's decoded value.</summary>
    private static string? KeyText(BareKeyOrStringValueSyntax key) => key switch
    {
        BareKeySyntax bare => bare.Key?.Text,
        StringValueSyntax value => value.Value,
        _ => null,
    };

    private static int LineOf(SourceSpan span) => span.Start.Line + 1;
}
