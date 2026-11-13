// SPDX-License-Identifier: Apache-2.0

using System.Text;
using System.Text.Json;
using InvenioTech.GateKeeper.Engine;

namespace InvenioTech.GateKeeper.Tests;

public sealed class ResultsWriterTests
{
    [Fact]
    public void Writer_Should_ExposeProcessorMetadata_When_TheFindingCarriesCatalogueData()
    {
        var input = Input(true);
        var processor = new ProcessorInfo(["synthetic category"], "Synthetic region caveat.");
        input = input with { Policy = input.Policy with { Findings = input.Policy.Findings.Select(f => f with { Processor = processor }).ToArray() } };
        var documents = ResultsWriter.Write(input);
        ResultsTestSupport.ValidateEvidence(Encoding.UTF8.GetString(documents.Evidence)).IsValid.Should().BeTrue();
        ResultsTestSupport.ValidateSarif(Encoding.UTF8.GetString(documents.Sarif)).IsValid.Should().BeTrue();
        using var evidence = JsonDocument.Parse(documents.Evidence);
        evidence.RootElement.GetProperty("findings")[0].GetProperty("processor").GetProperty("usualRegion").GetString().Should().Be(processor.UsualRegion);
        using var sarif = JsonDocument.Parse(documents.Sarif);
        var run = sarif.RootElement.GetProperty("runs")[0];
        run.GetProperty("tool").GetProperty("driver").GetProperty("rules")[0].GetProperty("properties").GetProperty("processor").GetProperty("dataCategories")[0].GetString().Should().Be("synthetic category");
        run.GetProperty("results")[0].TryGetProperty("properties", out _).Should().BeFalse();
        ResultsWriter.Write(input).Evidence.Should().Equal(documents.Evidence);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Writer_Should_ValidateBothDocuments_When_TheScanIsCleanOrHasFindings(bool populated)
    {
        var documents = ResultsWriter.Write(Input(populated));
        var evidence = ResultsTestSupport.ValidateEvidence(Encoding.UTF8.GetString(documents.Evidence));
        var sarif = ResultsTestSupport.ValidateSarif(Encoding.UTF8.GetString(documents.Sarif));
        evidence.IsValid.Should().BeTrue(string.Join("; ", evidence.Errors));
        sarif.IsValid.Should().BeTrue(string.Join("; ", sarif.Errors));
        var official = ResultsTestSupport.ValidateOfficialSarif(Encoding.UTF8.GetString(documents.Sarif));
        official.IsValid.Should().BeTrue(string.Join("; ", official.Errors));
    }

    [Fact]
    public void Writer_Should_ProduceIdenticalBytes_When_InputCollectionsAreReversed()
    {
        var input = Input(true);
        var first = ResultsWriter.Write(input);
        var shuffled = input with { Policy = input.Policy with { Findings = input.Policy.Findings.Reverse().ToArray(), History = input.Policy.History.Reverse().ToArray(), Warnings = input.Policy.Warnings.Reverse().ToArray() } };
        var second = ResultsWriter.Write(shuffled);
        second.Evidence.Should().Equal(first.Evidence);
        second.Sarif.Should().Equal(first.Sarif);
        first.Evidence[^1].Should().Be((byte)'\n');
        first.Sarif[^1].Should().Be((byte)'\n');
        first.Evidence.Should().NotContain((byte)'\r');
        first.Sarif.Should().NotContain((byte)'\r');
    }

    [Fact]
    public void Writer_Should_RecordExceptionsDebtAndInventory_When_ThePolicyAppliedThem()
    {
        var output = ResultsWriter.Write(Input(true));
        using var evidence = JsonDocument.Parse(output.Evidence);
        var root = evidence.RootElement;
        root.GetProperty("schemaVersion").GetInt32().Should().Be(1);
        root.GetProperty("engine").GetProperty("version").GetString().Should().Be(EngineProduct.EngineVersion);
        root.GetProperty("packs")[0].GetProperty("version").GetString().Should().Be("2027.1.0");
        root.GetProperty("packs")[0].GetProperty("coverageDate").GetString().Should().Be("2026-11-01");
        root.GetProperty("exceptions")[0].GetProperty("id").GetString().Should().Be("exc-test");
        root.GetProperty("baseline").GetProperty("matchedCount").GetInt32().Should().Be(1);
        root.GetProperty("inventory")[0].GetProperty("count").GetInt32().Should().Be(2);
        root.GetProperty("history").GetArrayLength().Should().Be(1);
        using var sarif = JsonDocument.Parse(output.Sarif);
        var results = sarif.RootElement.GetProperty("runs")[0].GetProperty("results");
        results.GetArrayLength().Should().Be(2);
        results[0].GetProperty("baselineState").GetString().Should().Be("unchanged");
        results[0].GetProperty("partialFingerprints").GetProperty("gatekeeper/v1").GetString().Should().Be(new string('a', 64));
        sarif.RootElement.GetProperty("runs")[0].GetProperty("properties").EnumerateObject().Select(p => p.Name).Should().Equal(new[] { "evaluatedOn", "historyScanned", "packs", "profile", "repositoryId" });
        Encoding.UTF8.GetString(output.Sarif).Should().NotContain("snippet");
    }

    [Fact]
    public void Writer_Should_RejectToolErrors_When_AnOutputWouldClaimAScanCompleted()
    {
        ((Action)(() => ResultsWriter.Write(Input(false) with { Outcome = ExitOutcome.ToolError }))).Should().ThrowExactly<EngineToolException>();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Writer_Should_StampAllLoadedPacks_When_TheScanIsCleanOrHasFindings(bool populated)
    {
        var input = Input(populated);
        input = input with { Packs = [new("local.example", "2", PackProvenance.Local, new string('f', 64)), .. input.Packs] };
        var documents = ResultsWriter.Write(input);
        using var evidence = JsonDocument.Parse(documents.Evidence);
        using var sarif = JsonDocument.Parse(documents.Sarif);
        var stamps = sarif.RootElement.GetProperty("runs")[0].GetProperty("properties").GetProperty("packs");
        JsonElement.DeepEquals(evidence.RootElement.GetProperty("packs"), stamps).Should().BeTrue();
        stamps[0].GetProperty("packId").GetString().Should().Be("invenio.ng.synthetic.p1");
        stamps[0].GetProperty("version").GetString().Should().Be("2027.1.0");
        stamps[1].GetProperty("version").GetString().Should().Be("2");
        stamps[1].TryGetProperty("coverageDate", out _).Should().BeFalse();
        var shuffled = ResultsWriter.Write(input with { Packs = input.Packs.Reverse().ToArray() });
        shuffled.Sarif.Should().Equal(documents.Sarif);
        shuffled.Evidence.Should().Equal(documents.Evidence);
        var valid = ResultsTestSupport.ValidateSarif(Encoding.UTF8.GetString(documents.Sarif));
        valid.IsValid.Should().BeTrue(string.Join("; ", valid.Errors));
        ResultsTestSupport.ValidateOfficialSarif(Encoding.UTF8.GetString(documents.Sarif)).IsValid.Should().BeTrue();
    }

    [Fact]
    public void Writer_Should_EmitAnEmptyPackArray_When_NoPackWasLoaded()
    {
        using var sarif = JsonDocument.Parse(ResultsWriter.Write(Input(false) with { Packs = [] }).Sarif);
        sarif.RootElement.GetProperty("runs")[0].GetProperty("properties").GetProperty("packs").EnumerateArray().Should().BeEmpty();
    }

    [Fact]
    public void Profile_Should_RejectInvalidPackVersions_When_StampsArePresent()
    {
        var documents = ResultsWriter.Write(Input(false));
        var node = System.Text.Json.Nodes.JsonNode.Parse(documents.Sarif)!;
        node["runs"]![0]!["properties"]!["packs"]![0]!["version"] = "latest";
        ResultsTestSupport.ValidateSarif(node.ToJsonString()).IsValid.Should().BeFalse();
    }

    [Fact]
    public void OfficialSchema_Should_RejectAnInvalidRegion_When_TheGeneratedDocumentIsChanged()
    {
        var documents = ResultsWriter.Write(Input(true));
        var node = System.Text.Json.Nodes.JsonNode.Parse(documents.Sarif)!;
        node["runs"]![0]!["results"]![0]!["locations"]![0]!["physicalLocation"]!["region"]!["startLine"] = 0;
        ResultsTestSupport.ValidateOfficialSarif(node.ToJsonString()).IsValid.Should().BeFalse();
    }

    [Fact]
    public void Writer_Should_RecordSuppressionEvidence_When_AnExceptionRemovesTheFinding()
    {
        var input = Input(false);
        var citation = new Citation("Synthetic test citation.", null, null);
        var match = new RuleMatch("local.test", Severity.Error, "synthetic", "src/a.cs", 1, 1, LocationKind.Source, citation, [], 0, 6);
        var scan = new ScanMatchResult([match], [], new FileEnumerationResult([new("src/a.cs", "marker")], [], [], [], false));
        var state = new PolicyState([new("exc-test", "local.test", null, [PathGlob.Compile("src/**")], new(2026, 11, 2), null, new string('a', 64))],
            new Dictionary<string, DateOnly>(), null, []);
        var policy = FindingPolicy.Apply(scan, input.Settings, state, new(2026, 11, 2));
        using var evidence = JsonDocument.Parse(ResultsWriter.Write(input with { Policy = policy }).Evidence);
        evidence.RootElement.GetProperty("findings").EnumerateArray().Should().BeEmpty();
        evidence.RootElement.GetProperty("exceptions")[0].GetProperty("outcome").GetString().Should().Be("suppressed");
    }

    private static ResultsInput Input(bool populated)
    {
        var citation = new Citation("Synthetic test citation.", null, null);
        Finding[] findings = populated ? [new("local.test", Severity.Error, "synthetic", "src/a.cs", 1, 1, LocationKind.Source, new string('a', 64), BaselineState.Unchanged, citation),
            new("local.test", Severity.Warning, "synthetic", "src/z.cs", 2, 1, LocationKind.Source, null, BaselineState.New, citation)] : [];
        HistoryMatch[] history = populated ? [new(new string('b', 40), new RuleMatch("local.test", Severity.Note, "synthetic", "old.cs", 1, 1, LocationKind.Source, citation, [], 0, 1))] : [];
        AppliedException[] exceptions = populated ? [new("exc-test", new string('c', 64), "local.test", "src/z.cs", 2, 1, "overridden")] : [];
        var policy = new PolicyResult(new(2026, 11, 2), findings, history, exceptions, new(populated ? 1 : 0, populated ? 10 : 0),
            populated ? [new("fingerprint.missing", "No fingerprint key is available, so fingerprints are omitted from the results.")] : []);
        return new ResultsInput(policy, ConfigurationLoader.Load(Path.GetTempPath()).Settings!,
            [new LoadedPack("invenio.ng.synthetic.p1", "2027.1.0", PackProvenance.Bundled, new string('d', 64)) { CoverageDate = "2026-11-01" }],
            "example/repository", new string('e', 40), populated, ExitOutcome.Pass);
    }
}
