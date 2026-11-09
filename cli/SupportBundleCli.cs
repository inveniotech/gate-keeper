// SPDX-License-Identifier: Apache-2.0

using System.CommandLine;
using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using InvenioTech.GateKeeper.Engine;

namespace InvenioTech.GateKeeper.Cli;

internal static class SupportBundleCli
{
    private const string Mask = "[masked]";

    internal static Command Create(TextWriter output, TextWriter errors, string root,
        Func<IReadOnlyList<Rule>, MatcherEngine>? matcherFactory = null)
    {
        var command = new Command("support-bundle", "Writes a local ZIP of masked configuration, versions and finding counts");
        var date = new Option<string?>("--evaluated-on") { Description = "The evaluation date in YYYY-MM-DD form" };
        var destination = new Option<string>("--output") { Description = "The repository-relative ZIP filename", DefaultValueFactory = _ => ".ndpa-check/support-bundle.zip" };
        command.Options.Add(date);
        command.Options.Add(destination);
        command.SetAction(async (parse, token) =>
        {
            string? temporary = null;
            try
            {
                var relative = parse.GetValue(destination)!;
                var path = DoctorCli.ResolveOutput(root, relative);
                var prepared = await ScanCli.PrepareAsync(root, parse.GetValue(date), token, matcherFactory).ConfigureAwait(false);
                var bytes = Build(prepared, matcherFactory);
                var parent = Path.GetDirectoryName(path)!;
                Directory.CreateDirectory(parent);
                temporary = Path.Combine(parent, $".support-bundle-{Guid.NewGuid():N}.tmp");
                await using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.Asynchronous))
                    await stream.WriteAsync(bytes, token).ConfigureAwait(false);
                token.ThrowIfCancellationRequested();
                File.Move(temporary, path, overwrite: true);
                await output.WriteLineAsync($"The local support bundle was written to {relative.Replace('\\', '/')}. Review it before sharing it; nothing was uploaded.").ConfigureAwait(false);
                return (int)ExitOutcome.Pass;
            }
            catch (EngineToolException exception)
            { await errors.WriteLineAsync(exception.Message).ConfigureAwait(false); return (int)ExitOutcome.ToolError; }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            { await errors.WriteLineAsync("The support bundle could not read its inputs or write the output ZIP. Check .ndpa-check paths and output permissions, then run support-bundle again.").ConfigureAwait(false); return (int)ExitOutcome.ToolError; }
            finally { if (temporary is not null && File.Exists(temporary)) File.Delete(temporary); }
        });
        return command;
    }

    private static byte[] Build(PreparedScan scan, Func<IReadOnlyList<Rule>, MatcherEngine>? matcherFactory)
    {
        // Diagnostic checks remove contextual restrictions so metadata cannot evade a source-specific rule.
        var rules = scan.Packs.Rules.Select(r => r.Rule with
        {
            Matcher = r.Rule.Matcher.Pattern is { } pattern
                ? new RuleMatcher(pattern with { Location = null, Keywords = null, Window = null }, null) : r.Rule.Matcher,
        }).ToArray();
        var privacy = matcherFactory is null ? new MatcherEngine(rules) : matcherFactory(rules);
        string Safe(string value) => privacy.Match("support-metadata.json", value).Count == 0 ? value : Mask;
        var input = scan.Results;
        var settings = input.Settings;
        var documents = new SortedDictionary<string, byte[]>(StringComparer.Ordinal)
        {
            ["configuration.json"] = Json(writer =>
            {
                writer.WriteStartObject(); writer.WriteNumber("schemaVersion", 1); writer.WriteString("profile", settings.Profile.ToString().ToLowerInvariant());
                writer.WriteBoolean("history", settings.ScanHistory); writer.WriteNumber("maxFileBytes", settings.MaxFileBytes);
                writer.WriteBoolean("includeSubmodules", settings.IncludeSubmodules); writer.WriteNumber("baselineMaxAgeDays", settings.BaselineMaxAgeDays);
                writer.WriteString("failOn", settings.FailOn.ToString().ToLowerInvariant()); writer.WriteString("repositoryId", Mask);
                writer.WriteStartArray("exclude"); foreach (var unused in settings.Exclude) writer.WriteStringValue(Mask); writer.WriteEndArray();
                writer.WriteStartArray("localPacks"); foreach (var unused in settings.LocalPacks) writer.WriteStringValue(Mask); writer.WriteEndArray();
                writer.WriteStartArray("disabledRules");
                foreach (var unused in settings.DisabledRules) { writer.WriteStartObject(); writer.WriteString("id", Mask); writer.WriteString("reason", Mask); writer.WriteEndObject(); }
                writer.WriteEndArray(); writer.WriteStartArray("severityOverrides");
                foreach (var entry in settings.SeverityOverrides.OrderBy(e => e.Key, StringComparer.Ordinal))
                { writer.WriteStartObject(); writer.WriteString("id", Mask); writer.WriteString("severity", entry.Value.ToString().ToLowerInvariant()); writer.WriteEndObject(); }
                writer.WriteEndArray(); writer.WriteEndObject();
            }),
            ["findings.json"] = Json(writer =>
            {
                writer.WriteStartObject(); writer.WriteNumber("schemaVersion", 1);
                writer.WriteNumber("workTreeCount", input.Policy.Findings.Count); writer.WriteNumber("historyCount", input.Policy.History.Count);
                writer.WriteStartArray("rules");
                var ids = input.Policy.Findings.Select(f => f.RuleId).Concat(input.Policy.History.Select(f => f.Match.RuleId)).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal);
                foreach (var id in ids)
                {
                    writer.WriteStartObject(); writer.WriteString("ruleId", Safe(id));
                    writer.WriteNumber("workTreeCount", input.Policy.Findings.Count(f => f.RuleId == id));
                    writer.WriteNumber("historyCount", input.Policy.History.Count(f => f.Match.RuleId == id)); writer.WriteEndObject();
                }
                writer.WriteEndArray(); writer.WriteEndObject();
            }),
            ["versions.json"] = Json(writer =>
            {
                writer.WriteStartObject(); writer.WriteNumber("schemaVersion", 1); writer.WriteString("evaluatedOn", input.Policy.EvaluatedOn.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
                writer.WriteStartObject("engine"); writer.WriteString("product", EngineProduct.Product); writer.WriteString("version", EngineProduct.EngineVersion); writer.WriteEndObject();
                writer.WriteStartArray("packs");
                foreach (var pack in scan.Packs.Packs.OrderBy(p => p.Id, StringComparer.Ordinal))
                {
                    writer.WriteStartObject(); writer.WriteString("packId", Safe(pack.Id)); writer.WriteString("version", Safe(pack.Version));
                    writer.WriteString("provenance", pack.Provenance == PackProvenance.PlanGated ? "plan-gated" : pack.Provenance.ToString().ToLowerInvariant());
                    writer.WriteString("contentDigest", Safe(pack.ContentDigest)); writer.WriteEndObject();
                }
                writer.WriteEndArray(); writer.WriteEndObject();
            }),
        };
        foreach (var document in documents)
            if (privacy.Match(document.Key, Encoding.UTF8.GetString(document.Value)).Count > 0)
                throw new EngineToolException("The support bundle failed its privacy check, so no archive was written. Review the loaded local rules and reproduce the problem with synthetic data before sharing diagnostic information.");
        using var bytes = new MemoryStream();
        using (var archive = new ZipArchive(bytes, ZipArchiveMode.Create, leaveOpen: true))
            foreach (var document in documents)
            {
                var entry = archive.CreateEntry(document.Key, CompressionLevel.NoCompression);
                entry.LastWriteTime = new DateTimeOffset(1980, 1, 1, 0, 0, 0, TimeSpan.Zero);
                entry.ExternalAttributes = 0;
                using var stream = entry.Open();
                stream.Write(document.Value);
            }
        return bytes.ToArray();
    }

    private static byte[] Json(Action<Utf8JsonWriter> write)
    {
        using var bytes = new MemoryStream();
        using (var writer = new Utf8JsonWriter(bytes, new JsonWriterOptions { Indented = true, NewLine = "\n" })) write(writer);
        bytes.WriteByte((byte)'\n');
        return bytes.ToArray();
    }
}
