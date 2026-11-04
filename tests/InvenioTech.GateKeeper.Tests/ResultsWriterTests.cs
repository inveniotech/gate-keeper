// SPDX-License-Identifier: Apache-2.0

using System.Text;
using System.Text.Json;
using InvenioTech.GateKeeper.Engine;

namespace InvenioTech.GateKeeper.Tests;

public sealed class ResultsWriterTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Writer_Should_ValidateBothDocuments_When_TheScanIsCleanOrHasFindings(bool populated)
    {
        var documents = ResultsWriter.Write(Input(populated));
        var evidence = ResultsTestSupport.ValidateEvidence(Encoding.UTF8.GetString(documents.Evidence));
        var sarif = ResultsTestSupport.ValidateSarif(Encoding.UTF8.GetString(documents.Sarif));
        Assert.True(evidence.IsValid, string.Join("; ", evidence.Errors));
        Assert.True(sarif.IsValid, string.Join("; ", sarif.Errors));
        var official = ResultsTestSupport.ValidateOfficialSarif(Encoding.UTF8.GetString(documents.Sarif));
        Assert.True(official.IsValid, string.Join("; ", official.Errors));
    }

    [Fact]
    public void Writer_Should_ProduceIdenticalBytes_When_InputCollectionsAreReversed()
    {
        var input = Input(true);
        var first = ResultsWriter.Write(input);
        var shuffled = input with { Policy = input.Policy with { Findings = input.Policy.Findings.Reverse().ToArray(), History = input.Policy.History.Reverse().ToArray(), Warnings = input.Policy.Warnings.Reverse().ToArray() } };
        var second = ResultsWriter.Write(shuffled);
        Assert.Equal(first.Evidence, second.Evidence);
        Assert.Equal(first.Sarif, second.Sarif);
        Assert.Equal((byte)'\n', first.Evidence[^1]);
        Assert.Equal((byte)'\n', first.Sarif[^1]);
        Assert.DoesNotContain((byte)'\r', first.Evidence);
        Assert.DoesNotContain((byte)'\r', first.Sarif);
    }

    [Fact]
    public void Writer_Should_RecordExceptionsDebtAndInventory_When_ThePolicyAppliedThem()
    {
        var output = ResultsWriter.Write(Input(true));
        using var evidence = JsonDocument.Parse(output.Evidence);
        var root = evidence.RootElement;
        Assert.Equal(1, root.GetProperty("schemaVersion").GetInt32());
        Assert.Equal(EngineProduct.EngineVersion, root.GetProperty("engine").GetProperty("version").GetString());
        Assert.Equal("2027.1.0", root.GetProperty("packs")[0].GetProperty("version").GetString());
        Assert.Equal("2026-11-01", root.GetProperty("packs")[0].GetProperty("coverageDate").GetString());
        Assert.Equal("exc-test", root.GetProperty("exceptions")[0].GetProperty("id").GetString());
        Assert.Equal(1, root.GetProperty("baseline").GetProperty("matchedCount").GetInt32());
        Assert.Equal(2, root.GetProperty("inventory")[0].GetProperty("count").GetInt32());
        Assert.Equal(1, root.GetProperty("history").GetArrayLength());
        using var sarif = JsonDocument.Parse(output.Sarif);
        var results = sarif.RootElement.GetProperty("runs")[0].GetProperty("results");
        Assert.Equal(2, results.GetArrayLength());
        Assert.Equal("unchanged", results[0].GetProperty("baselineState").GetString());
        Assert.Equal(new string('a', 64), results[0].GetProperty("partialFingerprints").GetProperty("gatekeeper/v1").GetString());
        Assert.Equal(new[] { "evaluatedOn", "historyScanned", "profile", "repositoryId" }, sarif.RootElement.GetProperty("runs")[0].GetProperty("properties").EnumerateObject().Select(p => p.Name));
        Assert.DoesNotContain("snippet", Encoding.UTF8.GetString(output.Sarif));
    }

    [Fact]
    public void Writer_Should_RejectToolErrors_When_AnOutputWouldClaimAScanCompleted()
    {
        Assert.Throws<EngineToolException>(() => ResultsWriter.Write(Input(false) with { Outcome = ExitOutcome.ToolError }));
    }

    [Fact]
    public void OfficialSchema_Should_RejectAnInvalidRegion_When_TheGeneratedDocumentIsChanged()
    {
        var documents = ResultsWriter.Write(Input(true));
        var node = System.Text.Json.Nodes.JsonNode.Parse(documents.Sarif)!;
        node["runs"]![0]!["results"]![0]!["locations"]![0]!["physicalLocation"]!["region"]!["startLine"] = 0;
        Assert.False(ResultsTestSupport.ValidateOfficialSarif(node.ToJsonString()).IsValid);
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
        Assert.Empty(evidence.RootElement.GetProperty("findings").EnumerateArray());
        Assert.Equal("suppressed", evidence.RootElement.GetProperty("exceptions")[0].GetProperty("outcome").GetString());
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
