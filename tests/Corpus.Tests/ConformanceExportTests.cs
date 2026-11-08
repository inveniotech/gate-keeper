// SPDX-License-Identifier: Apache-2.0
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using InvenioTech.GateKeeper.Engine;

namespace InvenioTech.GateKeeper.Corpus.Tests;

public sealed class ConformanceExportTests
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
        var samples = CorpusCase.LoadAll(CorpusPaths.Root)
            .Where(item => item.Name.StartsWith("p2-", StringComparison.Ordinal))
            .OrderBy(item => item.Name, StringComparer.Ordinal)
            .SelectMany(item => Directory.GetFiles(item.RepositoryRoot, "*", SearchOption.AllDirectories)
                .OrderBy(path => path, StringComparer.Ordinal)
                .Select(path => (Name: item.Name, Path: Path.GetRelativePath(item.RepositoryRoot, path).Replace('\\', '/'), Text: File.ReadAllText(path))))
            .ToArray();
        samples.Should().NotBeEmpty();
        using var stream = new MemoryStream();
        using (var json = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true }))
        {
            json.WriteStartObject();
            json.WriteNumber("version", 1);
            json.WriteString("scope", "Authored unsigned source packs loaded with in-memory development signatures; not a published bundled pack.");
            json.WriteString("redactionStatus", "Pending accepted P3-01 contract and reference redactor; no redaction expectation is fabricated.");
            json.WriteStartArray("packs");
            foreach (var path in paths)
            {
                json.WriteStartObject();
                json.WriteString("path", "rule-packs/" + Path.GetFileName(path));
                json.WriteString("sha256", Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path))));
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
                json.WriteEndObject();
            }
            count.Should().BeGreaterThan(0);
            json.WriteEndArray();
            json.WriteEndObject();
        }
        Verify(root, "vectors.json", Encoding.UTF8.GetString(stream.ToArray()) + "\n");

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
            using (var json = new Utf8JsonWriter(validatorStream, new JsonWriterOptions { Indented = true }))
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
            Verify(root, "validator-" + name + ".json", Encoding.UTF8.GetString(validatorStream.ToArray()) + "\n");
        }
    }

    private static string Base64(string value) => Convert.ToBase64String(Encoding.UTF8.GetBytes(value));

    private static void Verify(string root, string name, string content)
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
