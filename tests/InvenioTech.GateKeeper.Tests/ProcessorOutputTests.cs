// SPDX-License-Identifier: Apache-2.0

using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using InvenioTech.GateKeeper.Engine;

namespace InvenioTech.GateKeeper.Tests;

public sealed class ProcessorOutputTests
{
    private const string Marker = "synthetic-processor-input";
    private static readonly DateOnly Day = new(2026, 11, 13);

    [Fact]
    public void Writer_Should_DeduplicateAuthoredCategories_When_PolicyInputHasRepeatedMetadata()
    {
        var input = Input();
        var documents = ResultsWriter.Write(input);
        using var evidence = JsonDocument.Parse(documents.Evidence);
        var finding = evidence.RootElement.GetProperty("findings")[0];
        AssertProcessor(finding.GetProperty("processor"), ["second category", "first category"]);
        AssertProcessor(evidence.RootElement.GetProperty("history")[0].GetProperty("processor"), ["second category", "first category"]);
        input.Policy.Findings[0].Processor!.DataCategories.Should().Equal("second category", "first category", "second category");
        finding.EnumerateObject().Select(p => p.Name).TakeLast(2).Should().Equal("citation", "processor");
        using var sarif = JsonDocument.Parse(documents.Sarif);
        AssertProcessor(RuleProcessor(sarif.RootElement), ["second category", "first category"]);
    }

    [Fact]
    public async Task Pipeline_Should_UseWinningPackMetadata_When_VersionsAndCategoriesDiffer()
    {
        var first = await Pipeline(["second category", "first category", "second category"]);
        var second = await Pipeline(["second category", "first category", "second category"], reversePacks: true);
        var reordered = await Pipeline(["first category", "second category"]);
        first.Evidence.Should().Equal(second.Evidence);
        first.Sarif.Should().Equal(second.Sarif);
        using var evidence = JsonDocument.Parse(first.Evidence);
        AssertProcessor(evidence.RootElement.GetProperty("findings")[0].GetProperty("processor"), ["second category", "first category"]);
        using var changed = JsonDocument.Parse(reordered.Evidence);
        AssertProcessor(changed.RootElement.GetProperty("findings")[0].GetProperty("processor"), ["first category", "second category"]);
        using var sarif = JsonDocument.Parse(first.Sarif);
        var run = sarif.RootElement.GetProperty("runs")[0];
        run.GetProperty("tool").GetProperty("driver").GetProperty("rules").GetArrayLength().Should().Be(1);
        AssertProcessor(RuleProcessor(sarif.RootElement), ["second category", "first category"]);
        run.GetProperty("results")[0].TryGetProperty("properties", out _).Should().BeFalse();
        run.GetProperty("results")[0].GetProperty("level").GetString().Should().Be("note");
        foreach (var bytes in new[] { first.Evidence, first.Sarif })
            Encoding.UTF8.GetString(bytes).Should().NotContain(Marker).And.NotContain("losing category");
        ResultsTestSupport.ValidateEvidence(Encoding.UTF8.GetString(first.Evidence)).IsValid.Should().BeTrue();
        ResultsTestSupport.ValidateSarif(Encoding.UTF8.GetString(first.Sarif)).IsValid.Should().BeTrue();
        ResultsTestSupport.ValidateOfficialSarif(Encoding.UTF8.GetString(first.Sarif)).IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task Pipeline_Should_RetainMetadataAndFingerprint_When_BaselinedOrSuppressed()
    {
        var load = Load(["second category", "first category"]);
        var scan = await Scan(load);
        var settings = ConfigurationLoader.Load(Path.GetTempPath()).Settings!;
        var state = new PolicyState([], new Dictionary<string, DateOnly>(), new byte[32], []);
        var original = FindingPolicy.Apply(scan, settings, state, Day);
        var fingerprint = original.Findings[0].Fingerprint!;
        var without = scan with { Findings = scan.Findings.Select(m => m with { Processor = null }).ToArray() };
        FindingPolicy.Apply(without, settings, state, Day).Findings[0].Fingerprint.Should().Be(fingerprint);
        var policy = FindingPolicy.Apply(scan, settings, state with { Baseline = new Dictionary<string, DateOnly> { [fingerprint] = Day } }, Day);
        policy.Findings[0].BaselineState.Should().Be(BaselineState.Unchanged);
        policy.Findings[0].Processor.Should().BeSameAs(load.Rules[0].Rule.Processor);
        SeverityPolicy.Evaluate(original, settings.FailOn).Should().Be(ExitOutcome.Pass);
        var documents = ResultsWriter.Write(new(policy, settings, load.Packs, "synthetic/repo", "", true, ExitOutcome.Pass));
        using var sarif = JsonDocument.Parse(documents.Sarif);
        var results = sarif.RootElement.GetProperty("runs")[0].GetProperty("results");
        results.GetArrayLength().Should().Be(1);
        results[0].GetProperty("baselineState").GetString().Should().Be("unchanged");
        results[0].GetProperty("partialFingerprints").EnumerateObject().Select(p => p.Name).Should().Equal("gatekeeper/v1");
        results[0].GetProperty("partialFingerprints").GetProperty("gatekeeper/v1").GetString().Should().Be(fingerprint);
        using var evidence = JsonDocument.Parse(documents.Evidence);
        evidence.RootElement.GetProperty("inventory")[0].GetProperty("count").GetInt32().Should().Be(1);
        AssertProcessor(evidence.RootElement.GetProperty("history")[0].GetProperty("processor"), ["second category", "first category"]);
        var suppressed = FindingPolicy.Apply(scan, settings, state with
        {
            Exceptions = [new("synthetic-exception", "local.processor", null, [PathGlob.Compile("**")], Day, null, new string('a', 64))],
        }, Day);
        var hidden = ResultsWriter.Write(new(suppressed, settings, load.Packs, "synthetic/repo", "", true, ExitOutcome.Pass));
        using var hiddenEvidence = JsonDocument.Parse(hidden.Evidence);
        foreach (var property in new[] { "findings", "history", "inventory" }) hiddenEvidence.RootElement.GetProperty(property).EnumerateArray().Should().BeEmpty();
        using var hiddenSarif = JsonDocument.Parse(hidden.Sarif);
        hiddenSarif.RootElement.GetProperty("runs")[0].GetProperty("tool").GetProperty("driver").GetProperty("rules").EnumerateArray().Should().BeEmpty();
        suppressed.Exceptions.Should().HaveCount(2);
    }

    [Theory]
    [InlineData("unknown")]
    [InlineData("non-string")]
    [InlineData("empty")]
    [InlineData("missing-region")]
    public void Schemas_Should_RejectMalformedProcessorMetadata_When_ItAppearsInAnyAllowedPosition(string mutation)
    {
        var documents = ResultsWriter.Write(Input());
        foreach (var position in new[] { "findings", "history", "sarif" })
        {
            var node = JsonNode.Parse(position == "sarif" ? documents.Sarif : documents.Evidence)!;
            var processor = position == "sarif" ? node["runs"]![0]!["tool"]!["driver"]!["rules"]![0]!["properties"]!["processor"]!
                : node[position]![0]!["processor"]!;
            switch (mutation)
            {
                case "unknown": processor["unknown"] = true; break;
                case "non-string": processor["dataCategories"]![0] = 1; break;
                case "empty": processor["dataCategories"] = new JsonArray(); break;
                case "missing-region": processor.AsObject().Remove("usualRegion"); break;
            }
            var summary = position == "sarif" ? ResultsTestSupport.ValidateSarif(node.ToJsonString()) : ResultsTestSupport.ValidateEvidence(node.ToJsonString());
            summary.IsValid.Should().BeFalse($"{position}: {mutation}");
        }
    }

    [Fact]
    public void Writer_Should_OmitProcessorMetadata_When_TheRuleIsOrdinary()
    {
        var input = Input();
        input = input with
        {
            Policy = input.Policy with
            {
                Findings = input.Policy.Findings.Select(f => f with { Processor = null }).ToArray(),
                History = input.Policy.History.Select(h => h with { Match = h.Match with { Processor = null } }).ToArray(),
            }
        };
        var documents = ResultsWriter.Write(input);
        Encoding.UTF8.GetString(documents.Evidence).Should().NotContain("\"processor\"");
        Encoding.UTF8.GetString(documents.Sarif).Should().NotContain("\"processor\"");
        ResultsTestSupport.ValidateEvidence(Encoding.UTF8.GetString(documents.Evidence)).IsValid.Should().BeTrue();
        ResultsTestSupport.ValidateSarif(Encoding.UTF8.GetString(documents.Sarif)).IsValid.Should().BeTrue();
    }

    private static ResultsInput Input()
    {
        var processor = new ProcessorInfo(["second category", "first category", "second category"], "Synthetic region caveat.");
        var citation = new Citation("Synthetic citation.", null, null);
        var finding = new Finding("local.processor", Severity.Note, "synthetic", "source.txt", 1, 1, LocationKind.Source, null, BaselineState.New, citation) { Processor = processor };
        var history = new HistoryMatch(new string('a', 40), new("local.processor", Severity.Note, "synthetic", "old.txt", 1, 1, LocationKind.Source, citation, [], 0, 1) { Processor = processor });
        return new(new(Day, [finding], [history], [], new(0, 0), []), ConfigurationLoader.Load(Path.GetTempPath()).Settings!, [], "synthetic/repo", "", true, ExitOutcome.Pass);
    }

    private static async Task<ResultDocuments> Pipeline(string[] categories, bool reversePacks = false)
    {
        var load = Load(categories, reversePacks);
        var scan = await Scan(load);
        var settings = ConfigurationLoader.Load(Path.GetTempPath()).Settings!;
        var policy = FindingPolicy.Apply(scan, settings, new([], new Dictionary<string, DateOnly>(), new byte[32], []), Day);
        return ResultsWriter.Write(new(policy, settings, load.Packs, "synthetic/repo", "", true, ExitOutcome.Pass));
    }

    private static Task<ScanMatchResult> Scan(MergedRules load) => new ScanOrchestrator(new MatcherEngine(load.Rules.Select(r => r.Rule).ToArray()))
        .ScanAsync(new FileEnumerationResult([new("source.txt", Marker)], [new(new string('a', 40), "old.txt", Marker)], [], [], true));

    private static MergedRules Load(string[] categories, bool reversePacks = false)
    {
        LocalPackInput Pack(int version, string[] values) => new($"synthetic-{version}.json", Encoding.UTF8.GetBytes($$$$"""
            {"schemaVersion":1,"id":"local.synthetic","version":{{{{version}}}},"minEngineVersion":"0.1.0","provenance":"local","rules":[
            {"id":"local.processor","severity":"note","category":"synthetic","citation":{"title":"Synthetic citation."},"sinceVersion":1,
            "matcher":{"list":{"package":["{{{{Marker}}}}"]}},"processor":{"dataCategories":{{{{new JsonArray(values.Select(v => (JsonNode?)JsonValue.Create(v)).ToArray()).ToJsonString()}}}},"usualRegion":"Synthetic region caveat."}}]}
            """));
        var packs = new[] { Pack(1, ["losing category"]), Pack(2, categories) };
        return PackLoader.Load([], reversePacks ? packs.Reverse().ToArray() : packs, new NoKeys(), BundledPacksOnlyPolicy.Instance);
    }

    private static JsonElement RuleProcessor(JsonElement sarif) => sarif.GetProperty("runs")[0].GetProperty("tool").GetProperty("driver").GetProperty("rules")[0].GetProperty("properties").GetProperty("processor");

    private static void AssertProcessor(JsonElement processor, string[] categories)
    {
        processor.EnumerateObject().Select(p => p.Name).Should().Equal("dataCategories", "usualRegion");
        processor.GetProperty("dataCategories").EnumerateArray().Select(v => v.GetString()).Should().Equal(categories);
        processor.GetProperty("usualRegion").GetString().Should().Be("Synthetic region caveat.");
    }

    private sealed class NoKeys : IPackKeyCatalogue
    {
        public bool TryGetPackSigningKey(string keyId, out byte[] publicKey) { publicKey = []; return false; }
        public bool TryGetWatermarkKey(string keyId, out byte[] publicKey) { publicKey = []; return false; }
    }
}
