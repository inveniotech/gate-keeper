// SPDX-License-Identifier: Apache-2.0

using System.Globalization;
using System.Text.Json;
using SarifKit.IO;
using SarifKit.Models;
using SarifBaselineState = SarifKit.Models.BaselineState;

namespace InvenioTech.GateKeeper.Engine;

/// <summary>The completed scan facts required by both output formats (P1-11). No key or source content is accepted.</summary>
/// <param name="Policy">The post-policy findings, exceptions and baseline summary.</param>
/// <param name="Settings">The effective configuration.</param>
/// <param name="Packs">Every surviving loaded pack and its version stamps.</param>
/// <param name="RepositoryId">The explicit or derived repository identity.</param>
/// <param name="Revision">The scanned commit identifier, or empty outside Git.</param>
/// <param name="HistoryScanned">Whether history actually ran.</param>
/// <param name="Outcome">A completed scan's pass or violations outcome; tool errors cannot write results.</param>
public sealed record ResultsInput(PolicyResult Policy, ScanSettings Settings, IReadOnlyList<LoadedPack> Packs,
    string RepositoryId, string Revision, bool HistoryScanned, ExitOutcome Outcome);

/// <summary>The UTF-8, LF-terminated documents from one scan. The caller chooses their destination.</summary>
/// <param name="Evidence">Gate-Keeper evidence JSON bytes.</param>
/// <param name="Sarif">Static Analysis Results Interchange Format 2.1.0 bytes.</param>
public sealed record ResultDocuments(byte[] Evidence, byte[] Sarif);

/// <summary>Builds the closed results profile with deterministic ordering and no source snippets or key material.</summary>
public static class ResultsWriter
{
    private const string SarifSchema = "https://raw.githubusercontent.com/oasis-tcs/sarif-spec/main/sarif-2.1/schema/sarif-schema-2.1.0.json";

    /// <summary>Writes both documents in memory using SarifKit for SARIF. Tool errors and missing Invenio coverage stamps are rejected before returning output.</summary>
    public static ResultDocuments Write(ResultsInput input)
    {
        if (input.Outcome is not (ExitOutcome.Pass or ExitOutcome.Violations))
            throw new EngineToolException("A tool error cannot write scan results. Repair the reported error and run the scan again.");
        if (!input.HistoryScanned && input.Policy.History.Count > 0)
            throw new EngineToolException("History findings cannot be written when history did not run. Supply the completed scan's history state.");
        if (input.Packs.Any(p => p.Provenance != PackProvenance.Local && p.CoverageDate is null))
            throw new EngineToolException("A loaded Invenio pack has no coverage date. Reload the pack before writing version-stamped results.");
        var findings = input.Policy.Findings.OrderBy(f => f.Path, StringComparer.Ordinal).ThenBy(f => f.Line).ThenBy(f => f.Column).ThenBy(f => f.RuleId, StringComparer.Ordinal).ToArray();
        var warnings = input.Policy.Warnings.OrderBy(w => w.Code, StringComparer.Ordinal).ThenBy(w => w.Message, StringComparer.Ordinal).ToArray();
        return new ResultDocuments(Evidence(input, findings, warnings), Sarif(input, findings, warnings));
    }

    private static byte[] Evidence(ResultsInput input, Finding[] findings, ScanWarning[] warnings) => Bytes(writer =>
    {
        writer.WriteStartObject(); writer.WriteNumber("schemaVersion", 1); writer.WriteString("evaluatedOn", Day(input.Policy.EvaluatedOn));
        writer.WriteStartObject("engine"); writer.WriteString("product", EngineProduct.Product); writer.WriteString("version", EngineProduct.EngineVersion); writer.WriteEndObject();
        writer.WriteStartObject("repository"); writer.WriteString("id", input.RepositoryId); writer.WriteString("revision", input.Revision); writer.WriteEndObject();
        writer.WriteString("profile", Profile(input.Settings)); writer.WriteBoolean("historyScanned", input.HistoryScanned);
        writer.WriteString("failOn", Word(input.Settings.FailOn)); writer.WriteNumber("exitCode", (int)input.Outcome);
        writer.WritePropertyName("packs"); WritePacks(writer, input.Packs);
        writer.WriteStartObject("rules"); writer.WriteStartArray("overrides");
        foreach (var entry in input.Settings.SeverityOverrides.OrderBy(p => p.Key, StringComparer.Ordinal))
        { writer.WriteStartObject(); writer.WriteString("id", entry.Key); writer.WriteString("severity", Word(entry.Value)); writer.WriteEndObject(); }
        writer.WriteEndArray(); writer.WriteStartArray("disabled");
        foreach (var entry in input.Settings.DisabledRules.OrderBy(p => p.Id, StringComparer.Ordinal))
        { writer.WriteStartObject(); writer.WriteString("id", entry.Id); writer.WriteString("reason", entry.Reason); writer.WriteEndObject(); }
        writer.WriteEndArray(); writer.WriteEndObject(); writer.WriteStartArray("findings");
        foreach (var finding in findings) JsonSerializer.Serialize(writer, finding, EngineJsonSerializerContext.Default.Finding);
        writer.WriteEndArray(); writer.WriteStartArray("history");
        foreach (var entry in input.Policy.History.OrderBy(h => h.CommitId, StringComparer.Ordinal).ThenBy(h => h.Match.Path, StringComparer.Ordinal).ThenBy(h => h.Match.Line).ThenBy(h => h.Match.RuleId, StringComparer.Ordinal).ThenBy(h => h.Match.Column))
        {
            var match = entry.Match;
            writer.WriteStartObject(); writer.WriteString("commitId", entry.CommitId); writer.WriteString("ruleId", match.RuleId);
            writer.WriteString("severity", Word(match.Severity)); writer.WriteString("category", match.Category); writer.WriteString("path", match.Path);
            writer.WriteNumber("line", match.Line); writer.WriteNumber("column", match.Column); writer.WriteString("locationKind", Kind(match.LocationKind));
            writer.WritePropertyName("citation"); JsonSerializer.Serialize(writer, match.Citation, EngineJsonSerializerContext.Default.Citation); writer.WriteEndObject();
        }
        writer.WriteEndArray(); writer.WriteStartArray("exceptions");
        foreach (var entry in input.Policy.Exceptions.OrderBy(e => e.Id, StringComparer.Ordinal).ThenBy(e => e.Path, StringComparer.Ordinal).ThenBy(e => e.Line).ThenBy(e => e.Column).ThenBy(e => e.RuleId, StringComparer.Ordinal))
            JsonSerializer.Serialize(writer, entry, EngineJsonSerializerContext.Default.AppliedException);
        writer.WriteEndArray(); writer.WritePropertyName("baseline"); JsonSerializer.Serialize(writer, input.Policy.Baseline, EngineJsonSerializerContext.Default.BaselineSummary);
        writer.WriteStartArray("inventory");
        foreach (var group in findings.GroupBy(f => (f.Category, Kind: Kind(f.LocationKind))).OrderBy(g => g.Key.Category, StringComparer.Ordinal).ThenBy(g => g.Key.Kind, StringComparer.Ordinal))
        { writer.WriteStartObject(); writer.WriteString("category", group.Key.Category); writer.WriteString("locationKind", group.Key.Kind); writer.WriteNumber("count", group.Count()); writer.WriteEndObject(); }
        writer.WriteEndArray(); writer.WriteStartArray("warnings");
        foreach (var warning in warnings)
        { writer.WriteStartObject(); writer.WriteString("code", warning.Code); writer.WriteString("message", warning.Message); writer.WriteEndObject(); }
        writer.WriteEndArray(); writer.WriteEndObject();
    });

    private static void WritePacks(Utf8JsonWriter writer, IReadOnlyList<LoadedPack> packs)
    {
        writer.WriteStartArray();
        foreach (var pack in packs.OrderBy(p => p.Id, StringComparer.Ordinal).ThenBy(p => p.Version, StringComparer.Ordinal))
        {
            writer.WriteStartObject(); writer.WriteString("packId", pack.Id); writer.WriteString("version", pack.Version);
            writer.WriteString("provenance", pack.Provenance == PackProvenance.PlanGated ? "plan-gated" : pack.Provenance.ToString().ToLowerInvariant());
            if (pack.Provenance != PackProvenance.Local) writer.WriteString("coverageDate", pack.CoverageDate);
            writer.WriteString("contentDigest", pack.ContentDigest); writer.WriteEndObject();
        }
        writer.WriteEndArray();
    }

    private static JsonElement PackStamps(IReadOnlyList<LoadedPack> packs)
    {
        using var document = JsonDocument.Parse(Bytes(writer => WritePacks(writer, packs)));
        return document.RootElement.Clone();
    }

    private static byte[] Sarif(ResultsInput input, Finding[] findings, ScanWarning[] warnings)
    {
        var descriptors = findings.GroupBy(f => f.RuleId, StringComparer.Ordinal).OrderBy(g => g.Key, StringComparer.Ordinal).Select(g =>
        {
            var citation = g.First().Citation;
            var properties = new SortedDictionary<string, JsonElement>(StringComparer.Ordinal);
            if (citation.GaidSection is not null) properties.Add("gaid", Text(citation.GaidSection));
            if (citation.NdpaSection is not null) properties.Add("ndpa", Text(citation.NdpaSection));
            return new ReportingDescriptor { Id = g.Key, ShortDescription = new MultiformatMessageString { Text = citation.Title }, Properties = properties.Count > 0 ? properties : null };
        }).ToArray();
        var indexes = descriptors.Select((d, index) => (d.Id, index)).ToDictionary(p => p.Id!, p => p.index, StringComparer.Ordinal);
        var results = findings.Select(f => new Result
        {
            RuleId = f.RuleId,
            RuleIndex = indexes[f.RuleId],
            Level = Level(f.Severity),
            Message = new Message { Text = $"{f.RuleId}. {f.Citation.Title}." },
            Locations = [new SarifKit.Models.Location { PhysicalLocation = new PhysicalLocation
            { ArtifactLocation = new ArtifactLocation { Uri = f.Path }, Region = new Region { StartLine = f.Line, StartColumn = f.Column } } }],
            BaselineState = f.BaselineState == BaselineState.Unchanged ? SarifBaselineState.Unchanged : SarifBaselineState.New,
            PartialFingerprints = f.Fingerprint is null ? null : new SortedDictionary<string, string>(StringComparer.Ordinal) { ["gatekeeper/v1"] = f.Fingerprint },
        }).ToArray();
        var log = new SarifLog
        {
            Schema = SarifSchema,
            Version = "2.1.0",
            Runs = [new SarifRun
            {
                Tool = new Tool { Driver = new ToolComponent { Name = "gatekeeper-ndpa-check", Version = EngineProduct.EngineVersion, InformationUri = "https://gatekeeper.africa/", Rules = descriptors } },
                Invocations = [new Invocation { ExecutionSuccessful = true, ExitCode = (int)input.Outcome,
                    ToolExecutionNotifications = warnings.Length == 0 ? null : warnings.Select(w => new Notification { Level = FailureLevel.Warning, Message = new Message { Text = w.Message } }).ToArray() }],
                Results = results,
                Properties = new SortedDictionary<string, JsonElement>(StringComparer.Ordinal)
                {
                    ["evaluatedOn"] = Text(Day(input.Policy.EvaluatedOn)),
                    ["historyScanned"] = JsonSerializer.SerializeToElement(input.HistoryScanned, EngineJsonSerializerContext.Default.Boolean),
                    ["packs"] = PackStamps(input.Packs),
                    ["profile"] = Text(Profile(input.Settings)), ["repositoryId"] = Text(input.RepositoryId),
                },
            }],
        };
        using var document = JsonDocument.Parse(SarifWriter.WriteToString(log));
        return Bytes(writer => Canonical(writer, document.RootElement, "root"));
    }

    private static void Canonical(Utf8JsonWriter writer, JsonElement element, string parent)
    {
        if (element.ValueKind == JsonValueKind.Array)
        { writer.WriteStartArray(); foreach (var item in element.EnumerateArray()) Canonical(writer, item, parent); writer.WriteEndArray(); return; }
        if (element.ValueKind != JsonValueKind.Object) { element.WriteTo(writer); return; }
        string[] keys = parent switch
        {
            "root" => ["$schema", "version", "runs"],
            "runs" => ["tool", "invocations", "results", "properties"],
            "tool" => ["driver"],
            "driver" => ["name", "version", "informationUri", "rules"],
            "packs" => ["packId", "version", "provenance", "coverageDate", "contentDigest"],
            "rules" => ["id", "shortDescription", "properties"],
            "invocations" => ["executionSuccessful", "exitCode", "toolExecutionNotifications"],
            "toolExecutionNotifications" => ["level", "message"],
            "results" => ["ruleId", "ruleIndex", "level", "message", "locations", "baselineState", "partialFingerprints"],
            "message" or "shortDescription" => ["text"],
            "locations" => ["physicalLocation"],
            "physicalLocation" => ["artifactLocation", "region"],
            "artifactLocation" => ["uri"],
            "region" => ["startLine", "startColumn"],
            _ => element.EnumerateObject().Select(p => p.Name).Order(StringComparer.Ordinal).ToArray(),
        };
        writer.WriteStartObject();
        foreach (var key in keys)
            if (element.TryGetProperty(key, out var value)) { writer.WritePropertyName(key); Canonical(writer, value, key); }
        writer.WriteEndObject();
    }

    private static byte[] Bytes(Action<Utf8JsonWriter> write)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true, NewLine = "\n" })) write(writer);
        stream.WriteByte((byte)'\n');
        return stream.ToArray();
    }
    private static JsonElement Text(string text) => JsonSerializer.SerializeToElement(text, EngineJsonSerializerContext.Default.String);
    private static string Day(DateOnly day) => day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    private static string Profile(ScanSettings settings) => settings.Profile == ScanProfile.Deep ? "deep" : "default";
    private static string Word(Severity severity) => severity.ToString().ToLowerInvariant();
    private static string Kind(LocationKind kind) => kind == LocationKind.ApiModel ? "api-model" : kind.ToString().ToLowerInvariant();
    private static FailureLevel Level(Severity severity) => severity switch { Severity.Error => FailureLevel.Error, Severity.Warning => FailureLevel.Warning, Severity.Note => FailureLevel.Note, _ => throw new ArgumentOutOfRangeException(nameof(severity)) };
}
