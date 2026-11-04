// SPDX-License-Identifier: Apache-2.0

using System.Globalization;
using System.Text.Json;

namespace InvenioTech.GateKeeper.Engine;

/// <summary>
/// Reads and validates one rule-pack content document by hand (card P1-03: the
/// hand-written reader the card allows, so no reflection-based schema library
/// is pulled in under NativeAOT). Every check the JSON Schema
/// <c>schemas/rulepack.schema.json</c> carries is enforced here, plus the two
/// load-time checks the schema cannot express: the shape's scalar-value sum and
/// <c>min</c> not exceeding <c>max</c> (rule-pack format, sections 3 and 7.1).
///
/// The reader proves the shape, not the signature: it runs after the loader has
/// verified the manifest and digests, so hostile bytes can only arrive at a
/// hostile pack's owner. Every failure is a loud <see cref="EngineToolException"/>
/// whose message is a complete en-GB sentence naming the pack; the scan
/// orchestrator maps the exception to exit code 2 (rules 13 and 23).
/// </summary>
internal static class PackContentReader
{
    private const int MaxShapeParts = 32;
    private const int MaxShapeScalarValues = 256;
    private const int MaxShapePartRepetitions = 64;
    private const int MaxWindowScalarValues = 4_096;
    private const int MaxValidatorDigitLength = 256;

    /// <summary>The top-level keys an Invenio content document carries (rule-pack format, section 3).</summary>
    private static readonly IReadOnlySet<string> InvenioRootKeys = new HashSet<string>(StringComparer.Ordinal)
    {
        "schemaVersion", "id", "version", "provenance", "minEngineVersion", "coverageDate", "contentIssuedAt", "baseVersion", "rules",
    };

    /// <summary>The top-level keys a local content document carries (rule-pack format, sections 3 and 10).</summary>
    private static readonly IReadOnlySet<string> LocalRootKeys = new HashSet<string>(StringComparer.Ordinal)
    {
        "schemaVersion", "id", "version", "provenance", "minEngineVersion", "rules",
    };

    private static readonly IReadOnlySet<string> RuleKeys = new HashSet<string>(StringComparer.Ordinal)
    {
        "id", "severity", "category", "citation", "sinceVersion", "redaction", "profiles", "suppressionHints", "processor", "matcher",
    };
    private static readonly IReadOnlySet<string> CitationKeys = new HashSet<string>(StringComparer.Ordinal) { "title", "ndpa", "gaid" };
    private static readonly IReadOnlySet<string> RedactionKeys = new HashSet<string>(StringComparer.Ordinal) { "optional" };
    private static readonly IReadOnlySet<string> ProcessorKeys = new HashSet<string>(StringComparer.Ordinal) { "dataCategories", "usualRegion" };
    private static readonly IReadOnlySet<string> MatcherKeys = new HashSet<string>(StringComparer.Ordinal) { "pattern", "list" };
    private static readonly IReadOnlySet<string> PatternKeys = new HashSet<string>(StringComparer.Ordinal)
    {
        "shape", "validator", "boundary", "keywords", "window", "location", "exclusions", "excludePrefixes", "excludeValues",
    };
    private static readonly IReadOnlySet<string> ShapePartKeys = new HashSet<string>(StringComparer.Ordinal)
    {
        "min", "max", "literal", "class", "ranges", "oneOf", "emit",
    };
    private static readonly IReadOnlySet<string> ValidatorKeys = new HashSet<string>(StringComparer.Ordinal) { "name", "length", "prefixes" };
    private static readonly IReadOnlySet<string> ListMatcherKeys = new HashSet<string>(StringComparer.Ordinal) { "package", "hostname" };
    private static readonly IReadOnlySet<string> LocationKeys = new HashSet<string>(StringComparer.Ordinal) { "kind", "paths" };

    /// <summary>Reads and validates the bytes of one signed Invenio pack's content document.</summary>
    internal static PackContentLoaded ReadSigned(string packName, byte[] contentBytes)
    {
        return Read(packName, contentBytes, expectLocal: false);
    }

    /// <summary>
    /// Reads and validates the bytes of one unsigned local pack's content
    /// document (IQ-25): the same checks as the signed path, with the
    /// provenance fixed to <c>local</c> and a positive-integer version.
    /// </summary>
    internal static PackContentLoaded ReadLocal(string packName, byte[] contentBytes)
    {
        return Read(packName, contentBytes, expectLocal: true);
    }

    /// <summary>Parses one calendar pack line <c>YYYY.MM.P</c> into its numeric triple (IQ-36).</summary>
    internal static bool TryParseCalendarVersion(string text, out CalendarVersionValue version)
    {
        version = default;
        var parts = text.Split('.');
        if (parts.Length != 3
            || parts[0].Length != 4
            || parts[1].Length is not 1 and not 2
            || parts[2].Length == 0
            || !TryParseDigits(parts[0], out var year)
            || !TryParseDigits(parts[1], out var month)
            || !TryParseDigits(parts[2], out var patch))
        {
            return false;
        }

        version = new CalendarVersionValue(year, month, patch);
        return true;
    }

    /// <summary>Parses one civil date <c>YYYY-MM-DD</c> (IQ-36). The calendar's rules apply: no month 13, no 30 February.</summary>
    internal static bool TryParseCivilDate(string text, out DateOnly date)
    {
        return DateOnly.TryParseExact(
            text,
            "yyyy-MM-dd",
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out date);
    }

    private static PackContentLoaded Read(string packName, byte[] contentBytes, bool expectLocal)
    {
        if (contentBytes.Length is 0)
        {
            Fail(packName, "Its content bytes are empty: a pack is a content document, not a nothing. Give the loader the pack bytes.");
        }

        if (StartsWithByteOrderMark(contentBytes))
        {
            Fail(packName, "Its content bytes begin with a byte-order mark. Content bytes are UTF-8 without a " +
                "byte-order mark (rule-pack format, section 6); re-save the pack without the mark.");
        }

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(contentBytes);
        }
        catch (JsonException exception)
        {
            throw new EngineToolException(
                $"The rule pack '{packName}' cannot be loaded. Its content document is not valid JSON: the parser " +
                $"reports {exception.Message}. Rebuild or re-download the pack; nothing parses a damaged document halfway.");
        }

        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind is not JsonValueKind.Object)
            {
                Fail(packName, "Its content document is not a JSON object at the top level. A pack is one JSON object.");
            }

            var properties = ReadProperties(packName, root, "the content document");
            var allowedKeys = expectLocal ? LocalRootKeys : InvenioRootKeys;
            foreach (var name in properties.Keys)
            {
                if (!allowedKeys.Contains(name))
                {
                    Fail(packName, $"Its content document carries the key '{name}', which the rule-pack version 1 " +
                        "schema does not list for this provenance. Every object sets additionalProperties to false" +
                        "(rule-pack format, section 3): remove the key, or correct its spelling.");
                }
            }

            // schemaVersion is checked first, because every other field reads
            // only under the version the document declares.
            var schemaVersion = ReadInt(packName, properties, "schemaVersion", "the content document");
            if (schemaVersion > RulePack.SupportedSchemaVersion)
            {
                throw new EngineToolException(
                    $"The rule pack '{packName}' declares schema version {schemaVersion}, which no CLI of major " +
                    $"{RulePack.SupportedSchemaVersion} reads. Upgrade the CLI to a build that reads schema version " +
                    $"{schemaVersion}, then rescan.");
            }

            if (schemaVersion != RulePack.SupportedSchemaVersion)
            {
                Fail(packName, $"Its content document sets schemaVersion to {schemaVersion}, which is not version 1 " +
                    "of the rule-pack format. Set it to 1.");
            }

            var packId = ReadString(packName, properties, "id", "the pack");
            var provenanceWord = ReadString(packName, properties, "provenance", "the content document");
            PackProvenance provenance;
            if (provenanceWord == "bundled")
            {
                provenance = PackProvenance.Bundled;
            }
            else if (provenanceWord == "plan-gated")
            {
                provenance = PackProvenance.PlanGated;
            }
            else if (provenanceWord == "local")
            {
                provenance = PackProvenance.Local;
            }
            else
            {
                Fail(packName, $"Its content document sets provenance to '{provenanceWord}', which is not one of " +
                    "'bundled', 'plan-gated' or 'local' (IQ-25). Correct the provenance so a reader knows which media the pack belongs to.");
                provenance = default;
            }

            if (expectLocal && provenance is not PackProvenance.Local)
            {
                throw new EngineToolException(
                    $"The pack '{packName}' given as a local pack declares provenance '{provenanceWord}'. A local pack " +
                    "is unsigned, so only provenance 'local' (IQ-25) loads from the local list: give the loader a local " +
                    "pack, or load this pack as a signed Invenio pack.");
            }

            if (!expectLocal && provenance is PackProvenance.Local)
            {
                throw new EngineToolException(
                    $"The signed pack '{packName}' declares provenance 'local', but signed content cannot be local " +
                    "(IQ-25). Signed content is 'bundled' or 'plan-gated'; rebuild the pack with the provenance an " +
                    "Invenio pack carries.");
            }

            var isLocalPack = provenance is PackProvenance.Local;
            PackVersionValue packVersion;
            var versionElement = properties["version"];
            string versionText;
            if (isLocalPack)
            {
                var localNumber = 0;
                if (versionElement.ValueKind is not JsonValueKind.Number
                    || !versionElement.TryGetInt32(out localNumber)
                    || localNumber <= 0)
                {
                    Fail(packName, "Its content document sets version to something that is not a positive integer. " +
                        "A local pack is not a pack line, so its version is a positive integer such as 1 (IQ-36); " +
                        "the calendar form would only invent a month.");
                }

                versionText = localNumber.ToString(CultureInfo.InvariantCulture);
                packVersion = PackVersionValue.ForLocal(localNumber);
            }
            else
            {
                if (versionElement.ValueKind is not JsonValueKind.String)
                {
                    Fail(packName, "Its content document does not set version to a string, which an Invenio pack " +
                        "requires: the version is the calendar pack line YYYY.MM.P.");
                }

                versionText = versionElement.GetString()!;
                if (!TryParseCalendarVersion(versionText, out var calendar))
                {
                    Fail(packName, $"Its content document sets version to '{versionText}', which is not a calendar pack " +
                        "line YYYY.MM.P. Write an integer year, a 1-2 digit month and a patch number, such as 2027.1.0 (IQ-36).");
                }

                packVersion = PackVersionValue.ForCalendar(calendar.Year, calendar.Month, calendar.Patch);
            }

            if (!properties.TryGetValue("minEngineVersion", out var minEngineElement)
                || !IsNonEmptyString(minEngineElement)
                || !IsSemanticVersion(minEngineElement.GetString()!))
            {
                Fail(packName, "Its content document does not set minEngineVersion to a semantic version " +
                    "major.minor.patch, such as 1.0.0. The field is mandatory (IQ-24): it is the oldest engine that may load the pack.");
            }

            var minEngineText = minEngineElement.GetString()!;

            DateOnly? coverageDate = null;
            DateOnly? contentIssuedAt = null;
            if (!isLocalPack)
            {
                coverageDate = ReadCivilDate(packName, properties, "coverageDate");
                contentIssuedAt = ReadCivilDate(packName, properties, "contentIssuedAt");
            }
            else if (properties.ContainsKey("coverageDate") || properties.ContainsKey("contentIssuedAt") || properties.ContainsKey("baseVersion"))
            {
                Fail(packName, "Its content document sets a date field, but a local pack carries none: it is not a " +
                    "pack line, so it carries neither coverageDate nor contentIssuedAt nor baseVersion (IQ-36). Remove the dates.");
            }

            string? baseVersionText = null;
            var hasBaseVersion = properties.TryGetValue("baseVersion", out var baseVersionElement);
            if (!isLocalPack && hasBaseVersion)
            {
                if (!IsNonEmptyString(baseVersionElement) || !TryParseCalendarVersion(baseVersionElement.GetString()!, out var baseVersion) || baseVersion.Patch != 0)
                {
                    Fail(packName, "Its content document sets baseVersion to something that is not a line-opening " +
                        "YYYY.MM.0 version. A correction carries the base version of the line it corrects; set baseVersion " +
                        "to the line's calendar version with patch 0 (IQ-36).");
                }

                baseVersionText = baseVersionElement.GetString();
            }

            var rulesElement = properties["rules"];
            if (rulesElement.ValueKind is not JsonValueKind.Array || rulesElement.GetArrayLength() == 0)
            {
                Fail(packName, "Its content document does not set rules to a non-empty array. A pack carries at least one rule.");
            }

            var rules = new List<Rule>();
            var seenRuleIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (var ruleElement in rulesElement.EnumerateArray())
            {
                rules.Add(ReadRule(packName, provenance, ruleElement, seenRuleIds));
            }

            var pack = new RulePack(
                SchemaVersion: schemaVersion,
                Id: packId,
                Version: versionText,
                Provenance: provenance,
                MinEngineVersion: minEngineText,
                Rules: rules,
                CoverageDate: coverageDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                ContentIssuedAt: contentIssuedAt?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                BaseVersion: baseVersionText);

            return new PackContentLoaded(pack, packVersion);
        }
    }

    /// <summary>Reads and validates one rule object (rule-pack format, section 4).</summary>
    private static Rule ReadRule(string packName, PackProvenance provenance, JsonElement ruleElement, HashSet<string> seenRuleIds)
    {
        if (ruleElement.ValueKind is not JsonValueKind.Object)
        {
            Fail(packName, "Its content document carries a rule that is not a JSON object. Every element of rules is one rule object.");
        }

        var properties = ReadProperties(packName, ruleElement, "a rule");
        foreach (var name in properties.Keys)
        {
            if (!RuleKeys.Contains(name))
            {
                Fail(packName, $"A rule carries the key '{name}', which the rule-pack version 1 schema does not list. " +
                    "Every rule object sets additionalProperties to false: remove the key, or correct its spelling.");
            }
        }

        var ruleId = ReadString(packName, properties, "id", "a rule");
        if (!IsRuleIdentifier(ruleId))
        {
            Fail(packName, $"A rule sets id to '{ruleId}', which is not a dotted lowercase identifier of segments " +
                "that each open with a letter, such as pii.bvn.log. Dotted lowercase keeps the identifiers comparable " +
                "across packs.");
        }

        if (provenance is PackProvenance.Local)
        {
            if (!ruleId.StartsWith("local.", StringComparison.Ordinal))
            {
                throw new EngineToolException(
                    $"The rule pack '{packName}' carries the rule identifier '{ruleId}', which does not start with " +
                    "'local.'. A local pack's rule identifiers all start with 'local.' and nothing else may carry such " +
                    "an identifier (IQ-25): rename the rule, because it cannot override an Invenio one.");
            }
        }
        else if (ruleId.StartsWith("local.", StringComparison.Ordinal))
        {
            throw new EngineToolException(
                $"The rule pack '{packName}' carries the rule identifier '{ruleId}', which starts with 'local.'. " +
                "Only local packs may carry those identifiers (IQ-25): rename the rule so the namespace stays local's.");
        }

        if (!seenRuleIds.Add(ruleId))
        {
            throw new EngineToolException(
                $"The rule pack '{packName}' carries the rule identifier '{ruleId}' twice. A pack may carry an " +
                "identifier once: remove the duplicate rule, or give the second one its own identifier (IQ-24).");
        }

        var severityWord = ReadString(packName, properties, "severity", $"the rule '{ruleId}'");
        var severity = severityWord switch
        {
            "error" => Severity.Error,
            "warning" => Severity.Warning,
            "note" => Severity.Note,
            _ => Fail<Severity>(packName, $"The rule '{ruleId}' sets severity to '{severityWord}', which is not one of " +
                "'error', 'warning' or 'note' (IQ-22). Correct the severity so the threshold reads it."),
        };

        var category = ReadString(packName, properties, "category", $"the rule '{ruleId}'");

        var citationElement = properties["citation"];
        if (citationElement.ValueKind is not JsonValueKind.Object)
        {
            Fail(packName, $"The rule '{ruleId}' does not set citation to a JSON object.");
        }

        var citationProperties = ReadProperties(packName, citationElement, $"the citation of the rule '{ruleId}'");
        foreach (var name in citationProperties.Keys)
        {
            if (!CitationKeys.Contains(name))
            {
                Fail(packName, $"The citation of the rule '{ruleId}' carries the key '{name}', which the schema does not list " +
                    "(a citation has title, and optionally ndpa and gaid). Remove or correct the key.");
            }
        }

        var title = ReadString(packName, citationProperties, "title", $"the citation of the rule '{ruleId}'");
        var citation = new Citation(
            Title: title,
            NdpaSection: OptionalNonEmptyString(packName, citationProperties, "ndpa", $"the citation of the rule '{ruleId}'"),
            GaidSection: OptionalNonEmptyString(packName, citationProperties, "gaid", $"the citation of the rule '{ruleId}'"));

        if (!properties.TryGetValue("sinceVersion", out var sinceVersionElement))
        {
            Fail(packName, $"The rule '{ruleId}' does not set sinceVersion. It is the pack version from which the rule takes effect (section 4).");
        }

        if (provenance is PackProvenance.Local)
        {
            if (sinceVersionElement.ValueKind is not JsonValueKind.Number || !sinceVersionElement.TryGetInt32(out var sinceNumber) || sinceNumber <= 0)
            {
                Fail(packName, $"The rule '{ruleId}' sets sinceVersion to something that is not a positive integer. " +
                    "In a local pack it mirrors the pack's own positive-integer version (IQ-36).");
            }
        }
        else
        {
            if (sinceVersionElement.ValueKind is not JsonValueKind.String || !TryParseCalendarVersion(sinceVersionElement.GetString()!, out _))
            {
                Fail(packName, $"The rule '{ruleId}' sets sinceVersion to something that is not a calendar version " +
                    "YYYY.MM.P. In an Invenio pack it mirrors the pack's own version form (IQ-36).");
            }
        }

        var sinceVersionText = sinceVersionElement.ValueKind is JsonValueKind.Number
            ? sinceVersionElement.ToString()
            : sinceVersionElement.GetString()!;

        RedactionOptions? redaction = null;
        if (properties.TryGetValue("redaction", out var redactionElement))
        {
            if (redactionElement.ValueKind is not JsonValueKind.Object)
            {
                Fail(packName, $"The rule '{ruleId}' does not set redaction to a JSON object.");
            }

            var redactionProperties = ReadProperties(packName, redactionElement, $"the redaction of the rule '{ruleId}'");
            foreach (var name in redactionProperties.Keys)
            {
                if (!RedactionKeys.Contains(name))
                {
                    Fail(packName, $"The redaction of the rule '{ruleId}' carries the key '{name}', which the schema does not list. Remove it.");
                }
            }

            var optional = false;
            if (redactionProperties.TryGetValue("optional", out var optionalElement))
            {
                if (optionalElement.ValueKind is not JsonValueKind.True && optionalElement.ValueKind is not JsonValueKind.False)
                {
                    Fail(packName, $"The redaction of the rule '{ruleId}' sets optional to something that is not a boolean (IQ-76).");
                }

                optional = optionalElement.GetBoolean();
            }

            redaction = new RedactionOptions(Optional: optional);
        }

        IReadOnlyList<string>? profiles = null;
        if (properties.TryGetValue("profiles", out var profilesElement))
        {
            if (profilesElement.ValueKind is not JsonValueKind.Array)
            {
                Fail(packName, $"The rule '{ruleId}' does not set profiles to a JSON array.");
            }

            var profileWords = new HashSet<string>(StringComparer.Ordinal);
            var profileList = new List<string>();
            foreach (var profileElement in profilesElement.EnumerateArray())
            {
                if (profileElement.ValueKind is not JsonValueKind.String)
                {
                    Fail(packName, $"The rule '{ruleId}' carries a profile that is not a string. Profiles are 'default' or 'deep' (IQ-23).");
                }

                var word = profileElement.GetString()!;
                if (word is not ("default" or "deep"))
                {
                    Fail(packName, $"The rule '{ruleId}' sets a profile to '{word}', which is not 'default' or 'deep' (IQ-23).");
                }

                if (!profileWords.Add(word))
                {
                    Fail(packName, $"The rule '{ruleId}' lists the profile '{word}' twice. A rule may carry each profile once (IQ-23).");
                }

                profileList.Add(word);
            }

            if (profileList.Count == 0)
            {
                Fail(packName, $"The rule '{ruleId}' sets profiles to an empty array. Omit profiles for both profiles, or list them (IQ-23).");
            }

            profiles = profileList;
        }

        IReadOnlyList<string>? suppressionHints = null;
        if (properties.TryGetValue("suppressionHints", out var hintsElement))
        {
            if (hintsElement.ValueKind is not JsonValueKind.Array || hintsElement.GetArrayLength() == 0)
            {
                Fail(packName, $"The rule '{ruleId}' does not set suppressionHints to a non-empty array (or omit the key).");
            }

            var hints = new List<string>();
            foreach (var hintElement in hintsElement.EnumerateArray())
            {
                if (!IsNonEmptyString(hintElement))
                {
                    Fail(packName, $"The rule '{ruleId}' carries a suppressionHint that is not a non-empty string. " +
                        "Hints are the help text shown to the user (section 4); they are never executed.");
                }

                hints.Add(hintElement.GetString()!);
            }

            suppressionHints = hints;
        }

        ProcessorInfo? processor = null;
        if (properties.TryGetValue("processor", out var processorElement))
        {
            if (processorElement.ValueKind is not JsonValueKind.Object)
            {
                Fail(packName, $"The rule '{ruleId}' does not set processor to a JSON object.");
            }

            var processorProperties = ReadProperties(packName, processorElement, $"the processor block of the rule '{ruleId}'");
            foreach (var name in processorProperties.Keys)
            {
                if (!ProcessorKeys.Contains(name))
                {
                    Fail(packName, $"The processor block of the rule '{ruleId}' carries the key '{name}', which the " +
                        "schema does not list (IQ-82). Remove it.");
                }
            }

            var dataCategoriesElement = propertiesOrMissing(packName, processorProperties, "dataCategories", $"the processor block of the rule '{ruleId}'");
            if (dataCategoriesElement.ValueKind is not JsonValueKind.Array || dataCategoriesElement.GetArrayLength() == 0)
            {
                Fail(packName, $"The processor block of the rule '{ruleId}' does not set dataCategories to a non-empty array (IQ-82).");
            }

            var dataCategories = new List<string>();
            foreach (var categoryElement in dataCategoriesElement.EnumerateArray())
            {
                if (!IsNonEmptyString(categoryElement))
                {
                    Fail(packName, $"The rule '{ruleId}' carries a processor data category that is not a non-empty string.");
                }

                dataCategories.Add(categoryElement.GetString()!);
            }

            var usualRegion = ReadString(packName, processorProperties, "usualRegion", $"the processor block of the rule '{ruleId}'");
            processor = new ProcessorInfo(DataCategories: dataCategories, UsualRegion: usualRegion);
        }

        if (!properties.TryGetValue("matcher", out var matcherElement) || matcherElement.ValueKind is not JsonValueKind.Object)
        {
            Fail(packName, $"The rule '{ruleId}' does not set matcher to a JSON object. Exactly one kind of matcher, " +
                "'pattern' or 'list', must be present (section 5).");
        }

        var matcher = ReadMatcher(packName, ruleId, matcherElement);
        return new Rule(
            Id: ruleId,
            Severity: severity,
            Category: category,
            Citation: citation,
            SinceVersion: sinceVersionText,
            Redaction: redaction,
            Profiles: profiles,
            SuppressionHints: suppressionHints,
            Processor: processor,
            Matcher: matcher);
    }

    /// <summary>Reads and validates the matcher of one rule: exactly one kind (section 5).</summary>
    private static RuleMatcher ReadMatcher(string packName, string ruleId, JsonElement matcherElement)
    {
        var properties = ReadProperties(packName, matcherElement, $"the matcher of the rule '{ruleId}'");
        foreach (var name in properties.Keys)
        {
            if (!MatcherKeys.Contains(name))
            {
                Fail(packName, $"The matcher of the rule '{ruleId}' carries the key '{name}', which the schema does not " +
                    "list for a matcher ('pattern' or 'list'). Remove it or correct its spelling.");
            }
        }

        var hasPattern = properties.TryGetValue("pattern", out var patternElement);
        var hasList = properties.TryGetValue("list", out var listElement);
        if (hasPattern == hasList)
        {
            Fail(packName, $"The matcher of the rule '{ruleId}' carries {DescribeMatcherKinds(hasPattern, hasList)}. " +
                "A matcher is exactly one kind: a pattern or a list (section 5).");
        }

        if (hasPattern)
        {
            return new RuleMatcher(Pattern: ReadPattern(packName, ruleId, patternElement), List: null);
        }

        return new RuleMatcher(Pattern: null, List: ReadListMatcher(packName, ruleId, listElement));
    }

    private static string DescribeMatcherKinds(bool hasPattern, bool hasList) => (hasPattern, hasList) switch
    {
        (true, true) => "both 'pattern' and 'list', which leaves the kind ambiguous",
        _ => "neither 'pattern' nor 'list', which leaves the matcher empty",
    };

    /// <summary>Reads and validates one pattern matcher body (sections 5.1 and 7 to 8).</summary>
    private static RulePatternMatcher ReadPattern(string packName, string ruleId, JsonElement patternElement)
    {
        var properties = ReadProperties(packName, patternElement, $"the pattern matcher of the rule '{ruleId}'");
        foreach (var name in properties.Keys)
        {
            if (!PatternKeys.Contains(name))
            {
                Fail(packName, $"The pattern matcher of the rule '{ruleId}' carries the key '{name}', which the schema " +
                    "does not list for a pattern matcher. Remove it or correct its spelling.");
            }
        }

        var shapeElement = properties["shape"];
        if (shapeElement.ValueKind is not JsonValueKind.Array)
        {
            Fail(packName, $"The rule '{ruleId}' does not set shape to a JSON array (section 7.1).");
        }

        if (shapeElement.GetArrayLength() is 0 or > MaxShapeParts)
        {
            Fail(packName, $"The rule '{ruleId}' carries a shape of {shapeElement.GetArrayLength()} parts. A shape has " +
                $"1 to {MaxShapeParts} parts (section 7.1).");
        }

        var parts = new List<RuleShapePart>();
        var scalarSum = 0;
        var emitCount = 0;
        foreach (var partElement in shapeElement.EnumerateArray())
        {
            var part = ReadShapePart(packName, ruleId, partElement);
            parts.Add(part);
            scalarSum += part.Max * part.AtomWidth;
            if (part.Emit)
            {
                emitCount++;
            }
        }

        if (emitCount > 1)
        {
            Fail(packName, $"The rule '{ruleId}' marks {emitCount} shape parts with emit. At most one part emits, and " +
                "that part's span is the finding span used for redaction (section 7.1).");
        }

        if (scalarSum > MaxShapeScalarValues)
        {
            Fail(packName, $"The shape of the rule '{ruleId}' could consume {scalarSum} scalar values, over the " +
                $"{MaxShapeScalarValues} limit a part-sum may reach (section 7.1). Shorten the shape: a fixed-bound " +
                "scan must be able to try it in place.");
        }

        var boundary = properties["boundary"];
        if (boundary.ValueKind is not JsonValueKind.String)
        {
            Fail(packName, $"The rule '{ruleId}' does not set boundary to a string ('none', 'digit' or 'alnum'). " +
                "The boundary is a field, not a look-around (section 5.1).");
        }

        var boundaryWord = boundary.GetString()!;
        var matchBoundary = boundaryWord switch
        {
            "none" => MatchBoundary.None,
            "digit" => MatchBoundary.Digit,
            "alnum" => MatchBoundary.Alnum,
            _ => Fail<MatchBoundary>(packName, $"The rule '{ruleId}' sets boundary to '{boundaryWord}', which is not one of " +
                "'none', 'digit' or 'alnum' (section 5.1)."),
        };

        RuleValidator? validator = null;
        if (properties.TryGetValue("validator", out var validatorElement))
        {
            validator = ReadValidator(packName, ruleId, validatorElement);
        }

        IReadOnlyList<string>? keywords = null;
        if (properties.TryGetValue("keywords", out var keywordsElement))
        {
            if (keywordsElement.ValueKind is not JsonValueKind.Array || keywordsElement.GetArrayLength() == 0)
            {
                Fail(packName, $"The rule '{ruleId}' does not set keywords to a non-empty array, or omitted it. " +
                    "Keywords are the literal prefilter (section 7.4).");
            }

            var keywordList = new List<string>();
            foreach (var keywordElement in keywordsElement.EnumerateArray())
            {
                if (!IsAsciiPrintable(keywordElement))
                {
                    Fail(packName, $"The rule '{ruleId}' carries a keyword that is not a non-empty ASCII string (section 7.2). " +
                        "ASCII-only keywords keep the engines' case folding identical across stacks.");
                }

                keywordList.Add(keywordElement.GetString()!);
            }

            keywords = keywordList;
        }

        int? window = null;
        if (properties.TryGetValue("window", out var windowElement))
        {
            var windowValue = 0;
            if (windowElement.ValueKind is not JsonValueKind.Number || !windowElement.TryGetInt32(out windowValue))
            {
                Fail(packName, $"The rule '{ruleId}' sets window to something that is not an integer.");
            }

            if (windowValue is < 1 or > MaxWindowScalarValues)
            {
                Fail(packName, $"The rule '{ruleId}' sets window to {windowValue}, which is outside the 1 to " +
                    $"{MaxWindowScalarValues} range. The window is the prefilter's reach in scalar values (section 7.4).");
            }

            window = windowValue;
        }

        RuleLocation? location = null;
        if (properties.TryGetValue("location", out var locationElement))
        {
            location = ReadLocation(packName, ruleId, locationElement);
        }

        IReadOnlyList<RuleExclusion>? exclusions = null;
        if (properties.TryGetValue("exclusions", out var exclusionsElement))
        {
            if (exclusionsElement.ValueKind is not JsonValueKind.Array || exclusionsElement.GetArrayLength() == 0)
            {
                Fail(packName, $"The rule '{ruleId}' does not set exclusions to a non-empty array (or omits the key).");
            }

            var exclusionList = new List<RuleExclusion>();
            var seenExclusions = new HashSet<string>(StringComparer.Ordinal);
            foreach (var exclusionElement in exclusionsElement.EnumerateArray())
            {
                if (exclusionElement.ValueKind is not JsonValueKind.String)
                {
                    Fail(packName, $"The rule '{ruleId}' carries an exclusion that is not a string. Exclusions are the " +
                        "set of named exclusions the schema closes (section 8.2).");
                }

                var word = exclusionElement.GetString()!;
                var exclusion = word switch
                {
                    "repeated-digits" => RuleExclusion.RepeatedDigits,
                    "sequential-digits" => RuleExclusion.SequentialDigits,
                    "hex-fragment" => RuleExclusion.HexFragment,
                    "uuid-fragment" => RuleExclusion.UuidFragment,
                    "version-string" => RuleExclusion.VersionString,
                    "date-or-timestamp" => RuleExclusion.DateOrTimestamp,
                    _ => Fail<RuleExclusion>(packName, $"The rule '{ruleId}' names the exclusion '{word}', which is not in the closed set of named exclusions (section 8.2)."),
                };

                if (!seenExclusions.Add(word))
                {
                    Fail(packName, $"The rule '{ruleId}' lists the exclusion '{word}' twice.");
                }

                exclusionList.Add(exclusion);
            }

            exclusions = exclusionList;
        }

        IReadOnlyList<string>? excludePrefixes = null;
        if (properties.TryGetValue("excludePrefixes", out var excludePrefixesElement))
        {
            excludePrefixes = ReadNonEmptyArray(packName, ruleId, excludePrefixesElement, "excludePrefixes");
        }

        IReadOnlyList<string>? excludeValues = null;
        if (properties.TryGetValue("excludeValues", out var excludeValuesElement))
        {
            excludeValues = ReadNonEmptyArray(packName, ruleId, excludeValuesElement, "excludeValues");
        }

        return new RulePatternMatcher(
            Shape: parts,
            Boundary: matchBoundary,
            Validator: validator,
            Keywords: keywords,
            Window: window,
            Location: location,
            Exclusions: exclusions,
            ExcludePrefixes: excludePrefixes,
            ExcludeValues: excludeValues);
    }

    /// <summary>Reads and validates one shape part: exactly one atom with repetition bounds (section 7.1).</summary>
    private static RuleShapePart ReadShapePart(string packName, string ruleId, JsonElement partElement)
    {
        if (partElement.ValueKind is not JsonValueKind.Object)
        {
            Fail(packName, $"The rule '{ruleId}' carries a shape part that is not a JSON object (section 7.1).");
        }

        var properties = ReadProperties(packName, partElement, $"a shape part of the rule '{ruleId}'");
        foreach (var name in properties.Keys)
        {
            if (!ShapePartKeys.Contains(name))
            {
                Fail(packName, $"A shape part of the rule '{ruleId}' carries the key '{name}', which the schema does not " +
                    "list for a part. Remove it or correct its spelling.");
            }
        }

        var min = ReadInt(packName, properties, "min", $"a shape part of the rule '{ruleId}'");
        var max = ReadInt(packName, properties, "max", $"a shape part of the rule '{ruleId}'");
        if (min < 0 || min > MaxShapePartRepetitions)
        {
            Fail(packName, $"A shape part of the rule '{ruleId}' sets min to {min}, outside the 0 to {MaxShapePartRepetitions} range.");
        }

        if (max < 1 || max > MaxShapePartRepetitions)
        {
            Fail(packName, $"A shape part of the rule '{ruleId}' sets max to {max}, outside the 1 to {MaxShapePartRepetitions} range.");
        }

        if (min > max)
        {
            Fail(packName, $"A shape part of the rule '{ruleId}' sets min ({min}) above its max ({max}). A part that " +
                "cannot get enough repetitions matches nothing and is a producer bug (section 7.1).");
        }

        var hasLiteral = properties.TryGetValue("literal", out var literalElement);
        var hasClass = properties.TryGetValue("class", out var classElement);
        var hasRanges = properties.TryGetValue("ranges", out var rangesElement);
        var hasOneOf = properties.TryGetValue("oneOf", out var oneOfElement);
        if ((hasLiteral && (hasClass || hasRanges || hasOneOf))
            || (hasClass && (hasRanges || hasOneOf))
            || (hasRanges && hasOneOf)
            || (!hasLiteral && !hasClass && !hasRanges && !hasOneOf))
        {
            Fail(packName, $"A shape part of the rule '{ruleId}' does not carry exactly one atom of literal, class, " +
                "ranges or oneOf. One part is one atom (section 7.1); a part with two kinds is ambiguous.");
        }

        string? literal = null;
        if (hasLiteral)
        {
            if (!IsAsciiPrintable(literalElement))
            {
                Fail(packName, $"A shape part of the rule '{ruleId}' sets literal to something that is not a non-empty " +
                    "ASCII string (section 7.2).");
            }

            literal = literalElement.GetString()!;
        }

        ShapeClass? shapeClass = null;
        if (hasClass)
        {
            if (classElement.ValueKind is not JsonValueKind.String)
            {
                Fail(packName, $"A shape part of the rule '{ruleId}' sets class to something that is not a string.");
            }

            var classWord = classElement.GetString()!;
            shapeClass = classWord switch
            {
                "digit" => ShapeClass.Digit,
                "alnum" => ShapeClass.Alnum,
                _ => Fail<ShapeClass>(packName, $"A shape part of the rule '{ruleId}' sets class to '{classWord}', which is not 'digit' or 'alnum' (section 7.1)."),
            };
        }

        IReadOnlyList<RuleCharRange>? ranges = null;
        if (hasRanges)
        {
            if (rangesElement.ValueKind is not JsonValueKind.Array || rangesElement.GetArrayLength() == 0)
            {
                Fail(packName, $"A shape part of the rule '{ruleId}' does not set ranges to a non-empty array.");
            }

            var rangeList = new List<RuleCharRange>();
            foreach (var rangeElement in rangesElement.EnumerateArray())
            {
                if (rangeElement.ValueKind is not JsonValueKind.Array || rangeElement.GetArrayLength() != 2)
                {
                    Fail(packName, $"A range of the rule '{ruleId}' is not a two-element array ['low', 'high'] (section 7.1).");
                }

                var range = rangeElement.EnumerateArray().ToArray();
                if (range[0].ValueKind is not JsonValueKind.String || range[1].ValueKind is not JsonValueKind.String)
                {
                    Fail(packName, $"A range of the rule '{ruleId}' is not two one-character strings (section 7.1).");
                }

                var lowText = range[0].GetString()!;
                var highText = range[1].GetString()!;
                if (lowText.Length != 1 || highText.Length != 1)
                {
                    Fail(packName, $"A range of the rule '{ruleId}' is not two one-character strings (section 7.1).");
                }

                if (!IsAsciiPrintable(range[0]) || !IsAsciiPrintable(range[1]))
                {
                    Fail(packName, $"A range of the rule '{ruleId}' is not ASCII, which keeps the classes comparable across the stacks (section 7.2).");
                }

                var low = lowText[0];
                var high = highText[0];
                rangeList.Add(new RuleCharRange(low, high));
            }

            ranges = rangeList;
        }

        IReadOnlyList<string>? oneOf = null;
        if (hasOneOf)
        {
            if (oneOfElement.ValueKind is not JsonValueKind.Array || oneOfElement.GetArrayLength() == 0)
            {
                Fail(packName, $"A shape part of the rule '{ruleId}' does not set oneOf to a non-empty array.");
            }

            var alternatives = new List<string>();
            foreach (var alternativeElement in oneOfElement.EnumerateArray())
            {
                if (!IsAsciiPrintable(alternativeElement))
                {
                    Fail(packName, $"An oneOf alternative of the rule '{ruleId}' is not a non-empty ASCII string (section 7.2).");
                }

                alternatives.Add(alternativeElement.GetString()!);
            }

            oneOf = alternatives;
            if (max != 1)
            {
                Fail(packName, $"An oneOf part of the rule '{ruleId}' repeats (max {max}). An alternation must not " +
                    "repeat: its max is 1, which removes the ambiguous-alternation case each engine (section 7.1).");
            }
        }

        var emit = false;
        if (properties.TryGetValue("emit", out var emitElement))
        {
            if (emitElement.ValueKind is not JsonValueKind.True && emitElement.ValueKind is not JsonValueKind.False)
            {
                Fail(packName, $"A shape part of the rule '{ruleId}' sets emit to something that is not a boolean.");
            }

            emit = emitElement.GetBoolean();
        }

        return new RuleShapePart(
            Min: min,
            Max: max,
            Literal: literal,
            Class: shapeClass,
            Ranges: ranges,
            OneOf: oneOf,
            Emit: emit);
    }

    /// <summary>Reads and validates the named validator of a pattern matcher (section 8.1).</summary>
    private static RuleValidator ReadValidator(string packName, string ruleId, JsonElement validatorElement)
    {
        var properties = ReadProperties(packName, validatorElement, $"the validator of the rule '{ruleId}'");
        foreach (var key in properties.Keys)
        {
            if (!ValidatorKeys.Contains(key))
            {
                Fail(packName, $"The validator of the rule '{ruleId}' carries the key '{key}', which the schema does " +
                    "not list for a validator. Remove it or correct its spelling.");
            }
        }

        var nameElement = propertiesOrMissing(packName, properties, "name", $"the validator of the rule '{ruleId}'");
        if (nameElement.ValueKind is not JsonValueKind.String)
        {
            Fail(packName, $"The validator of the rule '{ruleId}' does not set name to a string.");
        }

        var name = nameElement.GetString()!;
        var validatorName = name switch
        {
            "digits-length" => RuleValidatorName.DigitsLength,
            "nuban" => RuleValidatorName.Nuban,
            "ng-phone-prefix" => RuleValidatorName.NgPhonePrefix,
            "email-not-example-domain" => RuleValidatorName.EmailNotExampleDomain,
            _ => Fail<RuleValidatorName>(packName, $"The rule '{ruleId}' names the validator '{name}', which this engine does not carry. The named " +
                "validators are a closed set (section 8.1): a new name needs an engine capability with an engine " +
                "version bump, it cannot be invented in a pack."),
        };

        int? length = null;
        IReadOnlyList<string>? prefixes = null;
        if (validatorName is RuleValidatorName.DigitsLength)
        {
            var lengthValue = 0;
            if (!properties.TryGetValue("length", out var lengthElement) || lengthElement.ValueKind is not JsonValueKind.Number
                || !lengthElement.TryGetInt32(out lengthValue) || lengthValue < 1 || lengthValue > MaxValidatorDigitLength)
            {
                Fail(packName, $"The rule '{ruleId}' carries the validator 'digits-length' without an integer length of " +
                    $"1 to {MaxValidatorDigitLength}. The digits-length capability stays generic and carries its length as pack data.");
            }

            length = lengthValue;
        }
        else if (validatorName is RuleValidatorName.NgPhonePrefix)
        {
            if (!properties.TryGetValue("prefixes", out var prefixesElement)
                || prefixesElement.ValueKind is not JsonValueKind.Array
                || prefixesElement.GetArrayLength() == 0)
            {
                Fail(packName, $"The rule '{ruleId}' carries the validator 'ng-phone-prefix' without a non-empty " +
                    "prefixes array. The ng-phone-prefix capability stays generic and carries its prefixes as pack data.");
            }

            var prefixList = new List<string>();
            foreach (var prefixElement in prefixesElement.EnumerateArray())
            {
                var prefixText = prefixElement.ValueKind is JsonValueKind.String ? prefixElement.GetString() : null;
                if (prefixText is null || prefixText.Length == 0 || prefixText.Any(character => character is < '0' or > '9'))
                {
                    Fail(packName, $"The rule '{ruleId}' carries an ng-phone-prefix prefix that is not a digit string.");
                }

                prefixList.Add(prefixText!);
            }

            prefixes = prefixList;
        }
        else if (properties.ContainsKey("length") || properties.ContainsKey("prefixes"))
        {
            Fail(packName, $"The rule '{ruleId}' carries the key 'length' or 'prefixes' with the validator '{name}', which " +
                "takes no such parameter (section 8.1). Remove it: only 'digits-length' carries length and only " +
                "'ng-phone-prefix' carries prefixes.");
        }

        return new RuleValidator(Name: validatorName, Length: length, Prefixes: prefixes);
    }

    /// <summary>Reads and validates one location restriction (section 5.3).</summary>
    private static RuleLocation ReadLocation(string packName, string ruleId, JsonElement locationElement)
    {
        var properties = ReadProperties(packName, locationElement, $"the location of the rule '{ruleId}'");
        foreach (var name in properties.Keys)
        {
            if (!LocationKeys.Contains(name))
            {
                Fail(packName, $"The location of the rule '{ruleId}' carries the key '{name}', which the schema does not " +
                    "list for a location (of kind, and optionally paths). Remove it.");
            }
        }

        var kindElement = propertiesOrMissing(packName, properties, "kind", $"the location of the rule '{ruleId}'");
        if (kindElement.ValueKind is not JsonValueKind.String)
        {
            Fail(packName, $"The location of the rule '{ruleId}' does not set kind to a string.");
        }

        var kindWord = kindElement.GetString()!;
        var kind = kindWord switch
        {
            "log" => LocationKind.Log,
            "migration" => LocationKind.Migration,
            "api-model" => LocationKind.ApiModel,
            "configuration" => LocationKind.Configuration,
            "fixture" => LocationKind.Fixture,
            "source" => LocationKind.Source,
            _ => Fail<LocationKind>(packName, $"The location of the rule '{ruleId}' sets kind to '{kindWord}', which is not in the " +
                "closed inventory 'log', 'migration', 'api-model', 'configuration', 'fixture' or 'source' (section 5.3)."),
        };

        IReadOnlyList<string>? paths = null;
        if (properties.TryGetValue("paths", out var pathsElement))
        {
            if (pathsElement.ValueKind is not JsonValueKind.Array || pathsElement.GetArrayLength() == 0)
            {
                Fail(packName, $"The location of the rule '{ruleId}' does not set paths to a non-empty array (or omits it).");
            }

            var pathList = new List<string>();
            foreach (var pathElement in pathsElement.EnumerateArray())
            {
                if (!IsNonEmptyString(pathElement) || !OnlyGlobCharacters(pathElement.GetString()!))
                {
                    Fail(packName, $"The location of the rule '{ruleId}' carries a path glob with characters outside the " +
                        "closed glob subset: literal segments, '*' and '**', forward slashes only (section 5.3).");
                }

                pathList.Add(pathElement.GetString()!);
            }

            paths = pathList;
        }

        return new RuleLocation(Kind: kind, Paths: paths);
    }

    /// <summary>Reads and validates one list matcher (section 5.2).</summary>
    private static RuleListMatcher ReadListMatcher(string packName, string ruleId, JsonElement listElement)
    {
        var properties = ReadProperties(packName, listElement, $"the list matcher of the rule '{ruleId}'");
        foreach (var name in properties.Keys)
        {
            if (!ListMatcherKeys.Contains(name))
            {
                Fail(packName, $"The list matcher of the rule '{ruleId}' carries the key '{name}', which the schema does " +
                    "not list for a list ('package' or 'hostname'). Remove it.");
            }
        }

        IReadOnlyList<string>? packages = null;
        if (properties.TryGetValue("package", out var packagesElement))
        {
            packages = ReadNonEmptyArray(packName, ruleId, packagesElement, "package");
        }

        IReadOnlyList<string>? hostnames = null;
        if (properties.TryGetValue("hostname", out var hostnamesElement))
        {
            hostnames = ReadNonEmptyArray(packName, ruleId, hostnamesElement, "hostname");
        }

        if (packages is null && hostnames is null)
        {
            Fail(packName, $"The list matcher of the rule '{ruleId}' carries neither package entries nor hostname " +
                "entries. At least one of the two must be present (section 5.2).");
        }

        return new RuleListMatcher(Packages: packages, Hostnames: hostnames);
    }

    private static IReadOnlyList<string> ReadNonEmptyArray(string packName, string ruleId, JsonElement arrayElement, string field)
    {
        if (arrayElement.ValueKind is not JsonValueKind.Array || arrayElement.GetArrayLength() == 0)
        {
            Fail(packName, $"The rule '{ruleId}' does not set {field} to a non-empty array (or omit the key).");
        }

        var values = new List<string>();
        foreach (var valueElement in arrayElement.EnumerateArray())
        {
            if (!IsNonEmptyString(valueElement))
            {
                Fail(packName, $"The rule '{ruleId}' carries a {field} entry that is not a non-empty string.");
            }

            values.Add(valueElement.GetString()!);
        }

        return values;
    }

    // --- shared small readers ---------------------------------------------------

    private static Dictionary<string, JsonElement> ReadProperties(string packName, JsonElement element, string what)
    {
        var properties = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (var property in element.EnumerateObject())
        {
            if (!properties.TryAdd(property.Name, property.Value))
            {
                Fail(packName, $"It carries the key '{property.Name}' twice in {what}. One key, one value: JSON adds " +
                    "identical names ambiguously, so the schema refuses the file.");
            }
        }

        return properties;
    }

    private static JsonElement propertiesOrMissing(string packName, Dictionary<string, JsonElement> properties, string name, string what)
    {
        if (!properties.TryGetValue(name, out var element))
        {
            Fail(packName, $"It does not set {name}, which {what} requires.");
        }

        return element;
    }

    private static string ReadString(string packName, Dictionary<string, JsonElement> properties, string name, string what)
    {
        if (!properties.TryGetValue(name, out var element)
            || !IsNonEmptyString(element))
        {
            Fail(packName, $"It does not set {name} to a non-empty string, which {what} requires.");
        }

        return element.GetString()!;
    }

    private static string? OptionalNonEmptyString(string packName, Dictionary<string, JsonElement> properties, string name, string what)
    {
        if (!properties.TryGetValue(name, out var element))
        {
            return null;
        }

        if (!IsNonEmptyString(element))
        {
            Fail(packName, $"It sets {name} to something that is not a non-empty string, which {what} requires when present.");
        }

        return element.GetString()!;
    }

    private static int ReadInt(string packName, Dictionary<string, JsonElement> properties, string name, string what)
    {
        var value = 0;
        if (!properties.TryGetValue(name, out var element)
            || element.ValueKind is not JsonValueKind.Number
            || !element.TryGetInt32(out value))
        {
            Fail(packName, $"It does not set {name} to an integer, which {what} requires.");
        }

        return value;
    }

    private static DateOnly ReadCivilDate(string packName, Dictionary<string, JsonElement> properties, string name)
    {
        var date = default(DateOnly);
        if (!properties.TryGetValue(name, out var element) || element.ValueKind is not JsonValueKind.String
            || !TryParseCivilDate(element.GetString() ?? string.Empty, out date))
        {
            Fail(packName, $"It does not set {name} to a civil date YYYY-MM-DD, which an Invenio pack requires " +
                "(IQ-36). Dates are facts about the pack line, so the loader parses them against the calendar.");
        }

        return date;
    }

    // --- character-shape checks --------------------------------------------------

    private static bool IsNonEmptyString(JsonElement element)
    {
        return element.ValueKind is JsonValueKind.String && element.GetString() is { } text && text.Length > 0;
    }

    private static bool IsAsciiPrintable(JsonElement element)
    {
        return element.ValueKind is JsonValueKind.String
            && element.GetString() is { } text
            && text.Length > 0
            && text.All(IsAsciiPrintableCharacter);
    }

    private static bool IsAsciiPrintableCharacter(char character) => character is >= ' ' and <= '~';

    private static bool OnlyGlobCharacters(string path)
    {
        return path.Length > 0 && path.All(character => character is >= 'A' and <= 'Z' or >= 'a' and <= 'z' or >= '0' and <= '9' or '.' or '_' or '*' or '/' or '-');
    }

    private static bool IsRuleIdentifier(string id)
    {
        if (id.Length == 0)
        {
            return false;
        }

        foreach (var segment in id.Split('.'))
        {
            if (segment.Length == 0 || !IsLowercaseIdentifierSegment(segment))
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsLowercaseIdentifierSegment(string segment)
    {
        if (segment[0] is < 'a' or > 'z')
        {
            return false;
        }

        foreach (var character in segment[1..])
        {
            if (character is not (>= 'a' and <= 'z' or >= '0' and <= '9'))
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsSemanticVersion(string text)
    {
        var parts = text.Split('.');
        if (parts.Length != 3 || !TryParseDigits(parts[0], out _) || !TryParseDigits(parts[1], out _) || !TryParseDigits(parts[2], out _))
        {
            return false;
        }

        return !parts.Any(part => part.Length == 0 || (part.Length > 1 && part[0] == '0'));
    }

    private static bool TryParseDigits(string text, out int value)
    {
        if (text.Length == 0 || text.Any(character => character is < '0' or > '9'))
        {
            value = 0;
            return false;
        }

        return int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out value);
    }

    private static bool StartsWithByteOrderMark(byte[] bytes)
    {
        return bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF;
    }

    // --- failure helpers ------------------------------------------------------

    private static T Fail<T>(string packName, string detail)
    {
        throw new EngineToolException($"The rule pack '{packName}' cannot be loaded. {detail}");
    }

    private static void Fail(string packName, string detail)
    {
        throw new EngineToolException($"The rule pack '{packName}' cannot be loaded. {detail}");
    }
}
