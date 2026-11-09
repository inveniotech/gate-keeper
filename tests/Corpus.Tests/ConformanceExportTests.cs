// SPDX-License-Identifier: Apache-2.0
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using InvenioTech.GateKeeper.Engine;
using InvenioTech.GateKeeper.Middleware;

namespace InvenioTech.GateKeeper.Corpus.Tests;

public sealed partial class ConformanceExportTests
{
    [Fact]
    public void Conformance_Should_MatchCommittedVectors_When_CurrentSourcePacksAreLoaded()
    {
        var root = Path.GetDirectoryName(Path.GetDirectoryName(CorpusPaths.Root))!;
        var paths = Directory.GetFiles(Path.Combine(root, "rule-packs"), "*.pack.json")
            .OrderBy(path => path, StringComparer.Ordinal).ToArray();
        paths.Should().NotBeEmpty();
        var rules = paths.SelectMany(path => DetectionPackTests.Load(Path.GetFileName(path)[..^".pack.json".Length]))
            .OrderBy(rule => rule.Id, StringComparer.Ordinal).ToArray();
        rules.Select(rule => rule.Id).Should().OnlyHaveUniqueItems();
        var matcher = new MatcherEngine(rules, shapes: new ShapeRegexCache(Timeout.InfiniteTimeSpan));
        var loads = paths.Select(path => DetectionPackTests.LoadMerged(Path.GetFileName(path)[..^".pack.json".Length])).ToArray();
        var runtime = new RuntimeMatcher(new MergedRules(loads.SelectMany(load => load.Packs).ToArray(),
            loads.SelectMany(load => load.Rules).ToArray()), shapes: new ShapeRegexCache(Timeout.InfiniteTimeSpan));
        var samples = CorpusCase.LoadAll(CorpusPaths.Root)
            .Where(item => item.Name.StartsWith("p2-", StringComparison.Ordinal))
            .OrderBy(item => item.Name, StringComparer.Ordinal)
            .SelectMany(item => Directory.GetFiles(item.RepositoryRoot, "*", SearchOption.AllDirectories)
                .OrderBy(path => path, StringComparer.Ordinal)
                .Select(path => (Name: item.Name, Path: Path.GetRelativePath(item.RepositoryRoot, path).Replace('\\', '/'), Text: File.ReadAllText(path))))
            .Append((Name: "astral-before-bvn", Path: "src/astral.ts", Text: "😀 bvn 32826314843"))
            .ToArray();
        samples.Should().NotBeEmpty();
        using var stream = new MemoryStream();
        using (var json = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping }))
        {
            json.WriteStartObject();
            json.WriteNumber("version", 1);
            json.WriteString("scope", "Authored unsigned source packs loaded with in-memory development signatures; not a published bundled pack.");
            json.WriteString("redactionStatus", "Executable reference core against the owner-accepted contract; framework integration remains separate.");
            var versions = runtime.Versions();
            json.WriteStartObject("runtimeVersions"); json.WriteString("adapterVersion", versions.AdapterVersion); json.WriteStartArray("packs");
            foreach (var metadata in versions.Packs)
            {
                var source = paths.Single(path =>
                {
                    using var document = JsonDocument.Parse(File.ReadAllBytes(path));
                    return document.RootElement.GetProperty("id").GetString() == metadata.Id;
                });
                metadata.ContentDigest.Should().Be(Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(source))));
                json.WriteStartObject(); json.WriteString("id", metadata.Id); json.WriteString("version", metadata.Version);
                json.WriteString("contentDigest", metadata.ContentDigest); json.WriteEndObject();
            }
            json.WriteEndArray(); json.WriteEndObject();
            json.WriteStartArray("rules");
            foreach (var rule in rules) JsonSerializer.Serialize(json, rule, EngineJsonSerializerContext.Default.Rule);
            json.WriteEndArray();
            json.WriteStartArray("packs");
            foreach (var path in paths)
            {
                json.WriteStartObject();
                json.WriteString("path", "rule-packs/" + Path.GetFileName(path));
                using var pack = JsonDocument.Parse(File.ReadAllBytes(path));
                json.WriteString("id", pack.RootElement.GetProperty("id").GetString());
                json.WriteString("version", pack.RootElement.GetProperty("version").GetString());
                json.WriteString("contentDigest", Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path))));
                json.WriteEndObject();
            }
            json.WriteEndArray();
            json.WriteStartArray("cases");
            var count = 0;
            foreach (var sample in samples)
            {
                json.WriteStartObject();
                json.WriteString("name", sample.Name + "/" + sample.Path);
                json.WriteString("path", sample.Path);
                json.WriteString("text", sample.Text);
                json.WriteStartArray("expected");
                foreach (var match in matcher.Match(sample.Path, sample.Text))
                {
                    count++;
                    json.WriteStartObject();
                    json.WriteString("ruleId", match.RuleId);
                    json.WriteString("severity", match.Severity.ToString().ToLowerInvariant());
                    json.WriteNumber("line", match.Line);
                    json.WriteNumber("column", match.Column);
                    json.WriteNumber("start", sample.Text[..match.SpanStart].EnumerateRunes().Count());
                    json.WriteNumber("length", sample.Text.Substring(match.SpanStart, match.SpanLength).EnumerateRunes().Count());
                    json.WriteEndObject();
                }
                json.WriteEndArray();
                WriteRuntime(json, runtime, sample.Text);
                json.WriteEndObject();
            }
            count.Should().BeGreaterThan(0);
            json.WriteEndArray();
            json.WriteStartArray("syntheticRuntimeCases");
            foreach (var sample in RuntimeSamples())
            {
                var pack = new LoadedPack("synthetic.runtime", "2026.11.0", PackProvenance.Bundled, new string('a', 64));
                var syntheticRuntime = new RuntimeMatcher(new MergedRules([pack], sample.Rules.Select(rule => new LoadedRule(rule, pack)).ToArray()),
                    shapes: new ShapeRegexCache(Timeout.InfiniteTimeSpan));
                json.WriteStartObject();
                json.WriteString("name", sample.Name);
                json.WriteString("text", sample.Text);
                json.WriteStartArray("rules");
                foreach (var rule in sample.Rules) JsonSerializer.Serialize(json, rule, EngineJsonSerializerContext.Default.Rule);
                json.WriteEndArray();
                WriteRuntime(json, syntheticRuntime, sample.Text);
                json.WriteEndObject();
            }
            json.WriteEndArray();
            json.WriteEndObject();
        }
        Verify(root, "vectors.json", Encoding.UTF8.GetString(stream.ToArray()).Replace("\r\n", "\n", StringComparison.Ordinal) + "\n");

        var portability = new StringBuilder("# rule-id\tpattern-base64\ttext-base64\tfull-start:length:emit-start:length\n");
        foreach (var rule in rules.Where(rule => rule.Matcher.Pattern is not null))
        {
            var shape = CompiledShape.Compile(rule.Matcher.Pattern!.Shape, rule.Id, Timeout.InfiniteTimeSpan);
            var inputs = samples.Select(sample => sample.Text).Append("😀 ordinary synthetic near-miss\n").Distinct(StringComparer.Ordinal).ToArray();
            // Three positive and three negative candidates keep portability fast while compiling every pattern.
            var chosen = inputs.Where(text => shape.Regex.IsMatch(text)).Take(3)
                .Concat(inputs.Where(text => !shape.Regex.IsMatch(text)).Take(3));
            foreach (var text in chosen)
            {
                var expected = shape.Regex.Matches(text).Select(match =>
                {
                    var emitted = shape.HasEmitGroup ? match.Groups["emit"] : match;
                    return $"{match.Index}:{match.Length}:{emitted.Index}:{emitted.Length}";
                });
                portability.Append(rule.Id).Append('\t').Append(Base64(shape.Pattern)).Append('\t')
                    .Append(Base64(text)).Append('\t').AppendJoin(',', expected).Append('\n');
            }
        }
        Verify(root, "patterns.tsv", portability.ToString());
        using var synthetic = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(CorpusPaths.Root, "synthetic-values.json")));
        foreach (var (name, parameters) in new[]
        {
            ("digits-length", new RuleValidator(RuleValidatorName.DigitsLength, 11, null)),
            ("nuban", new RuleValidator(RuleValidatorName.Nuban, null, null)),
            ("ng-phone-prefix", rules.Select(rule => rule.Matcher.Pattern?.Validator)
                .First(validator => validator?.Name == RuleValidatorName.NgPhonePrefix)!),
            ("email-not-example-domain", new RuleValidator(RuleValidatorName.EmailNotExampleDomain, null, null)),
        })
        {
            RuleValidatorRegistry.Default.TryGet(parameters.Name, out var validator).Should().BeTrue();
            using var validatorStream = new MemoryStream();
            using (var json = new Utf8JsonWriter(validatorStream, new JsonWriterOptions { Indented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping }))
            {
                json.WriteStartObject();
                json.WriteString("validator", name);
                if (parameters.Length is { } length) json.WriteNumber("length", length);
                if (parameters.Prefixes is { } prefixes)
                {
                    json.WriteStartArray("prefixes");
                    foreach (var prefix in prefixes) json.WriteStringValue(prefix);
                    json.WriteEndArray();
                }
                json.WriteStartArray("cases");
                foreach (var text in synthetic.RootElement.EnumerateArray().Select(value => value.GetString()!)
                    .Append("").Append("ordinary synthetic near-miss").OrderBy(text => text, StringComparer.Ordinal))
                {
                    json.WriteStartObject();
                    json.WriteString("text", text);
                    json.WriteBoolean("accepted", validator!.Validate(text, parameters));
                    json.WriteEndObject();
                }
                json.WriteEndArray();
                json.WriteEndObject();
            }
            Verify(root, "validator-" + name + ".json", Encoding.UTF8.GetString(validatorStream.ToArray()).Replace("\r\n", "\n", StringComparison.Ordinal) + "\n");
        }
    }

    private static string Base64(string value) => Convert.ToBase64String(Encoding.UTF8.GetBytes(value));

    private static void WriteRuntime(Utf8JsonWriter json, RuntimeMatcher runtime, string text)
    {
        json.WriteStartArray("runtimeExpected");
        foreach (var finding in runtime.Matches(text)) WriteFinding(json, finding);
        json.WriteEndArray();
        json.WritePropertyName("classification");
        var selected = runtime.Classify(text, new RuntimeContext("value"));
        if (selected is null) json.WriteNullValue(); else WriteFinding(json, selected);
        json.WriteString("redacted", runtime.Redact(text));
    }

    private static void WriteFinding(Utf8JsonWriter json, RuntimeFinding finding)
    {
        json.WriteStartObject();
        json.WriteString("ruleId", finding.RuleId);
        json.WriteString("category", finding.Category);
        json.WriteString("severity", finding.Severity.ToString().ToLowerInvariant());
        json.WriteNumber("start", finding.Start);
        json.WriteNumber("length", finding.Length);
        json.WriteEndObject();
    }

    private static IEnumerable<(string Name, IReadOnlyList<Rule> Rules, string Text)> RuntimeSamples()
    {
        Rule Rule(string id, string literal, Severity severity) => new(id, severity, "synthetic",
            new Citation("Synthetic runtime conformance.", null, null), "2026.11.0", null, null, null, null,
            new RuleMatcher(new RulePatternMatcher(
            [new RuleShapePart(1, 1, "label:", null, null, null), new RuleShapePart(1, 1, literal, null, null, null, true)],
            MatchBoundary.None, null, null, null, new RuleLocation(LocationKind.Log, ["not/a/runtime/path"]), null, null, null), null));
        var rules = new[] { Rule("local.z", "mark", Severity.Error), Rule("local.b", "marker", Severity.Note), Rule("local.a", "marker", Severity.Warning) };
        yield return ("astral-emitting-overlap", rules, "😀 label:marker!");
        yield return ("reserved-unknown-mask", rules, "[REDACTED:local.previous] label:marker");
        yield return ("mask-removed-before-keyword", [Rule("local.mask", "marker", Severity.Note)], "label:[REDACTED:local.previous]marker");
        yield return ("empty", rules, "");
        yield return ("near-miss", rules, "ordinary synthetic text");
        yield return ("malformed-mask-is-text", rules, "[REDACTED:INVALID] label:marker");
    }

    internal static void Verify(string root, string name, string content)
    {
        var directory = Path.Combine(root, "tests", "conformance");
        var target = Path.Combine(directory, name);
        if (Environment.GetEnvironmentVariable("GATEKEEPER_WRITE_CONFORMANCE") == "1")
        {
            Directory.CreateDirectory(directory);
            File.WriteAllText(target, content, new UTF8Encoding(false));
        }
        File.Exists(target).Should().BeTrue("run the documented generator before checking reproducibility");
        File.ReadAllText(target).Should().Be(content, "pack or corpus changes must regenerate conformance vectors");
    }
}
