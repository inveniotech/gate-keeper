// SPDX-License-Identifier: Apache-2.0

using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Tomlyn;
using Tomlyn.Parsing;
using Tomlyn.Serialization;
using Tomlyn.Syntax;

namespace InvenioTech.GateKeeper.Engine;

/// <summary>Reads validated exception, baseline and key files, and owns their write lifecycle (P1-09).</summary>
public static class PolicyStore
{
    /// <summary>Loads root-local policy. Malformed, oversized or unreadable files raise a tool error without exposing contents.</summary>
    public static async Task<PolicyState> LoadAsync(string root, IReadOnlyList<Rule> rules, CancellationToken cancellationToken = default)
    {
        var exceptions = await ReadAsync(root, "exceptions.toml", cancellationToken).ConfigureAwait(false);
        var baseline = await ReadAsync(root, "baseline.json", cancellationToken).ConfigureAwait(false);
        var key = await ReadAsync(root, "fingerprint.key", cancellationToken).ConfigureAwait(false);
        var warnings = new List<ScanWarning>();
        byte[]? keyBytes = null;
        if (key is null)
        {
            warnings.Add(new("fingerprint.missing", "No fingerprint key is available, so fingerprints are omitted from the results."));
            if (baseline is not null) warnings.Add(new("baseline.not-applied", "The baseline was not applied. Findings that the baseline would have hidden are reported."));
        }
        else
        {
            if (key.Length != 65 || key[64] != '\n' || !IsHex(key[..64])) throw Error("fingerprint.key", "the key format is invalid; replace it with 64 lowercase hexadecimal characters and one LF");
            keyBytes = Convert.FromHexString(key[..64]);
        }
        return new PolicyState(exceptions is null ? [] : ParseExceptions(exceptions, rules), ParseBaseline(baseline), keyBytes,
            warnings.OrderBy(w => w.Code, StringComparer.Ordinal).ToArray());
    }

    /// <summary>Creates an absent random key with exclusive creation and restrictive Unix permissions, never replacing an existing key; ensures it is gitignored.</summary>
    public static async Task EnsureKeyAsync(string root, CancellationToken cancellationToken = default)
    {
        var folder = Path.Combine(root, ".ndpa-check");
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, "fingerprint.key");
        if (!File.Exists(path))
        {
            var options = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write, Options = FileOptions.Asynchronous };
            if (!OperatingSystem.IsWindows()) options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
            try
            {
                await using var stream = new FileStream(path, options);
                await stream.WriteAsync(Encoding.ASCII.GetBytes(Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32)) + "\n"), cancellationToken).ConfigureAwait(false);
            }
            catch (IOException) when (File.Exists(path))
            {
                // Exclusive creation preserves the key another invocation created.
            }
        }
        var ignore = Path.Combine(folder, ".gitignore");
        var previous = File.Exists(ignore) ? await File.ReadAllTextAsync(ignore, cancellationToken).ConfigureAwait(false) : "";
        if (!previous.Split('\n').Any(line => line.TrimEnd('\r') == "fingerprint.key"))
            await File.WriteAllTextAsync(ignore, previous + (previous.Length > 0 && !previous.EndsWith('\n') ? "\n" : "") + "fingerprint.key\n", new UTF8Encoding(false), cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Rewrites the current work-tree fingerprints, preserving dates for surviving entries. A missing or invalid key is a tool error.</summary>
    public static async Task WriteBaselineAsync(string root, PolicyResult result, CancellationToken cancellationToken = default)
    {
        var baseline = ParseBaseline(await ReadAsync(root, "baseline.json", cancellationToken).ConfigureAwait(false));
        var key = await ReadAsync(root, "fingerprint.key", cancellationToken).ConfigureAwait(false);
        if (key is null || key.Length != 65 || key[64] != '\n' || !IsHex(key[..64]) || result.Findings.Any(f => f.Fingerprint is null)) throw Error("fingerprint.key", "baseline requires a readable key; run baseline again after restoring the key");
        using var bytes = new MemoryStream();
        using (var writer = new Utf8JsonWriter(bytes, new JsonWriterOptions { Indented = true }))
        {
            writer.WriteStartObject(); writer.WriteNumber("schemaVersion", 1); writer.WriteStartArray("entries");
            foreach (var fingerprint in result.Findings.Select(f => f.Fingerprint!).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal))
            {
                var added = baseline.GetValueOrDefault(fingerprint, result.EvaluatedOn);
                writer.WriteStartObject(); writer.WriteString("fingerprint", fingerprint); writer.WriteString("addedOn", added.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)); writer.WriteEndObject();
            }
            writer.WriteEndArray(); writer.WriteEndObject();
        }
        bytes.WriteByte((byte)'\n');
        if (bytes.Length > 1_048_576) throw Error("baseline.json", "the new baseline exceeds the 1,048,576-byte limit; reduce the accepted findings");
        var path = Path.Combine(root, ".ndpa-check", "baseline.json");
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await File.WriteAllBytesAsync(temporary, bytes.ToArray(), cancellationToken).ConfigureAwait(false);
            File.Move(temporary, path, overwrite: true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    private static IReadOnlyList<ExceptionEntry> ParseExceptions(string text, IReadOnlyList<Rule> rules)
    {
        var document = SyntaxParser.Parse(text, "exceptions.toml", validate: true);
        if (document.Diagnostics.HasErrors) throw Error("exceptions.toml", $"TOML is invalid at line {document.Diagnostics[0].Span.Start.Line + 1}; correct the syntax");
        ExceptionsFile file;
        try { file = TomlSerializer.Deserialize(text, ExceptionsTomlContext.Default.ExceptionsFile) ?? throw Error("exceptions.toml", "the document is empty; supply schemaVersion = 1"); }
        catch (TomlException) { throw Error("exceptions.toml", "a field has the wrong type; use the types in configuration section 11"); }
        if (file.SchemaVersion != 1) throw Error("exceptions.toml", "schemaVersion must be the integer 1; correct the version");
        foreach (var node in document.KeyValues)
            if (node is KeyValueSyntax value && Key(value.Key) != "schemaVersion") throw Error("exceptions.toml", "an unknown root key exists; remove it");
        var entries = file.Exception ?? [];
        var tableIndex = 0;
        for (var index = 0; index < document.Tables.ChildrenCount; index++)
        {
            if (document.Tables.GetChild(index) is not TableSyntaxBase table) continue;
            if (Key(table.Name) != "exception") throw Error("exceptions.toml", "an unknown table exists; use [[exception]]");
            var id = tableIndex < entries.Count ? entries[tableIndex].Id ?? $"entry {tableIndex + 1}" : $"entry {tableIndex + 1}";
            for (var item = 0; item < table.Items.ChildrenCount; item++)
            {
                if (table.Items.GetChild(item) is KeyValueSyntax value && Key(value.Key) is not ("id" or "rule" or "category" or "paths" or "justification" or "owner" or "expires" or "severity"))
                    throw Error("exceptions.toml", $"entry '{id}' has an unknown key; remove it");
            }
            tableIndex++;
        }
        var result = new List<ExceptionEntry>();
        var identifiers = new HashSet<string>(StringComparer.Ordinal);
        for (var index = 0; index < entries.Count; index++)
        {
            var entry = entries[index];
            var id = entry.Id?.Trim() ?? $"entry {index + 1}";
            string Required(string? value, string name, int limit)
            {
                if (value is null || value.Contains('\n') || value.Contains('\r') || value.Trim().Length is 0 || value.Trim().Length > limit)
                    throw Error("exceptions.toml", $"entry '{id}' needs a valid {name}; add a single-line value within {limit} characters");
                return value.Trim();
            }
            id = Required(entry.Id, "id", 64);
            if (!char.IsAsciiLetterOrDigit(id[0]) || id.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not ('.' or '_' or '-')) || !identifiers.Add(id))
                throw Error("exceptions.toml", $"entry '{id}' has an invalid or duplicate id; use a unique identifier");
            if ((entry.Rule is null) == (entry.Category is null)) throw Error("exceptions.toml", $"entry '{id}' needs exactly one scope; supply rule or category");
            var rule = entry.Rule is null ? null : Required(entry.Rule, "rule", 200);
            var category = entry.Category is null ? null : Required(entry.Category, "category", 200);
            if (!rules.Any(r => rule is null ? r.Category == category : r.Id == rule)) throw Error("exceptions.toml", $"entry '{id}' names an unknown scope; name a loaded rule or category");
            var justification = Required(entry.Justification, "justification", 2000);
            var owner = Required(entry.Owner, "owner", 200);
            var expiry = Required(entry.Expires, "expires", 10);
            var expires = EvaluationDate.Parse(expiry, $"exceptions.toml entry '{id}' expires");
            Severity? severity = entry.Severity switch { null => null, "error" => Severity.Error, "warning" => Severity.Warning, "note" => Severity.Note, _ => throw Error("exceptions.toml", $"entry '{id}' has an invalid severity; use error, warning or note") };
            if (entry.Paths is not { Count: > 0 } paths || paths.Distinct(StringComparer.Ordinal).Count() != paths.Count)
                throw Error("exceptions.toml", $"entry '{id}' needs paths; supply unique path globs");
            var globs = paths.Select(PathGlob.Compile).ToArray();
            var lines = new List<string> { id, rule ?? "", category ?? "" }; lines.AddRange(paths); lines.AddRange([justification, owner, expiry, entry.Severity ?? ""]);
            var hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('\n', lines) + "\n")));
            result.Add(new ExceptionEntry(id, rule, category, globs, expires, severity, hash));
        }
        return result.AsReadOnly();
    }

    private static Dictionary<string, DateOnly> ParseBaseline(string? text)
    {
        var result = new Dictionary<string, DateOnly>(StringComparer.Ordinal);
        if (text is null) return result;
        try
        {
            using var document = JsonDocument.Parse(text, new JsonDocumentOptions { MaxDepth = 16 });
            var root = document.RootElement;
            ValidateKeys(root, "schemaVersion", "entries");
            if (root.GetProperty("schemaVersion").GetInt32() != 1) throw new FormatException();
            foreach (var entry in root.GetProperty("entries").EnumerateArray())
            {
                ValidateKeys(entry, "fingerprint", "addedOn");
                var fingerprint = entry.GetProperty("fingerprint").GetString()!;
                if (!IsHex(fingerprint)) throw new FormatException();
                result.Add(fingerprint, EvaluationDate.Parse(entry.GetProperty("addedOn").GetString()!, "baseline.json addedOn"));
            }
        }
        catch (Exception error) when (error is JsonException or InvalidOperationException or KeyNotFoundException or FormatException or ArgumentException)
        { throw Error("baseline.json", "the document is malformed; use version 1 with unique lowercase fingerprints and civil addedOn dates"); }
        return result;
    }

    private static void ValidateKeys(JsonElement element, params string[] expected)
    {
        var keys = element.EnumerateObject().Select(p => p.Name).ToArray();
        if (keys.Length != expected.Length || keys.Distinct(StringComparer.Ordinal).Count() != keys.Length || keys.Any(k => !expected.Contains(k, StringComparer.Ordinal))) throw new FormatException();
    }
    private static string Key(KeySyntax? key)
    {
        if (key is null) return "";
        var parts = new List<string>();
        static string Text(BareKeyOrStringValueSyntax? part) => part switch { BareKeySyntax bare => bare.Key?.Text ?? "", StringValueSyntax value => value.Value ?? "", _ => "" };
        parts.Add(Text(key.Key));
        for (var index = 0; index < key.DotKeys.ChildrenCount; index++)
            if (key.DotKeys.GetChild(index) is DottedKeyItemSyntax item) parts.Add(Text(item.Key));
        return string.Join('.', parts);
    }
    private static bool IsHex(string text) => text.Length == 64 && text.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f');
    private static EngineToolException Error(string file, string reason) => new($"'.ndpa-check/{file}' cannot be used because {reason}.");
    private static async Task<string?> ReadAsync(string root, string file, CancellationToken cancellationToken)
    {
        var path = Path.Combine(root, ".ndpa-check", file);
        try
        {
            await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.Asynchronous);
            if (stream.Length > 1_048_576) throw Error(file, "it exceeds the 1,048,576-byte limit; reduce the file");
            var bytes = new byte[checked((int)stream.Length)];
            await stream.ReadExactlyAsync(bytes, cancellationToken).ConfigureAwait(false);
            if (bytes.AsSpan().StartsWith(new byte[] { 239, 187, 191 })) throw Error(file, "a byte-order mark is present; re-save as plain UTF-8");
            return new UTF8Encoding(false, true).GetString(bytes);
        }
        catch (FileNotFoundException) { return null; }
        catch (DirectoryNotFoundException) { return null; }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or DecoderFallbackException)
        { throw Error(file, "it cannot be read as UTF-8; check its encoding and access permissions"); }
    }
}

internal sealed class ExceptionsFile
{
    public int? SchemaVersion { get; set; }
    public List<ExceptionFileEntry>? Exception { get; set; }
}
internal sealed class ExceptionFileEntry
{
    public string? Id { get; set; }
    public string? Rule { get; set; }
    public string? Category { get; set; }
    public List<string>? Paths { get; set; }
    public string? Justification { get; set; }
    public string? Owner { get; set; }
    public string? Expires { get; set; }
    public string? Severity { get; set; }
}
[TomlSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, MaxDepth = 16)]
[TomlSerializable(typeof(ExceptionsFile))]
internal sealed partial class ExceptionsTomlContext : TomlSerializerContext;
