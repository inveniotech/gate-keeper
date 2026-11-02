// SPDX-License-Identifier: Apache-2.0

using System.Text.Json;

namespace InvenioTech.GateKeeper.Tests;

public sealed class ResultsConsistencyTests
{
    private const string BvnFingerprint = "00cca3a4746d65803db2fa8f18dfa1d9f05ca1c3c382ca92b2078b31a0e9c3a7";
    private const string PhoneFingerprint = "79d1fade04a4d84a7c7b361aeee52791d6fbd3f1545e286bac9f8255816db879";
    private const string FixtureEntryHash = "f9de64c03afa66c3d6def1cb5d697dcb9e8f0f7c14824717ed5e1e31cbe6d8b0";

    [Fact]
    public void Clean_scan_records_that_history_was_not_scanned()
    {
        using var document = JsonDocument.Parse(ResultsTestSupport.ReadOutputFile("examples/results/minimal.gk.json"));
        var root = document.RootElement;

        Assert.False(root.GetProperty("historyScanned").GetBoolean());
        Assert.Equal(0, root.GetProperty("history").GetArrayLength());
        Assert.Equal(0, root.GetProperty("exitCode").GetInt32());
        Assert.Equal(0, root.GetProperty("baseline").GetProperty("matchedCount").GetInt32());
    }

    [Fact]
    public void Full_evidence_example_uses_the_published_fingerprint_and_entry_hash_vectors()
    {
        using var document = JsonDocument.Parse(ResultsTestSupport.ReadOutputFile("examples/results/full.gk.json"));
        var root = document.RootElement;
        var findings = root.GetProperty("findings");

        Assert.Equal(BvnFingerprint, findings[0].GetProperty("fingerprint").GetString());
        Assert.Equal(PhoneFingerprint, findings[1].GetProperty("fingerprint").GetString());
        Assert.Equal(FixtureEntryHash, root.GetProperty("exceptions")[0].GetProperty("entryHash").GetString());
        Assert.Equal(1, root.GetProperty("exitCode").GetInt32());
        Assert.Equal(1, root.GetProperty("baseline").GetProperty("matchedCount").GetInt32());
        Assert.True(root.GetProperty("historyScanned").GetBoolean());
        Assert.Equal(1, root.GetProperty("history").GetArrayLength());
    }

    [Fact]
    public void Inventory_counts_reported_work_tree_findings_only()
    {
        using var document = JsonDocument.Parse(ResultsTestSupport.ReadOutputFile("examples/results/full.gk.json"));
        var root = document.RootElement;
        var counted = 0;
        foreach (var group in root.GetProperty("inventory").EnumerateArray())
        {
            counted += group.GetProperty("count").GetInt32();
        }

        Assert.Equal(root.GetProperty("findings").GetArrayLength(), counted);
    }

    [Fact]
    public void Full_sarif_example_points_each_result_at_its_rule_and_carries_no_history()
    {
        using var document = JsonDocument.Parse(ResultsTestSupport.ReadOutputFile("examples/results/full.sarif.json"));
        var run = document.RootElement.GetProperty("runs")[0];
        var rules = run.GetProperty("tool").GetProperty("driver").GetProperty("rules");
        var results = run.GetProperty("results");

        Assert.Equal(2, results.GetArrayLength());
        for (var index = 0; index < results.GetArrayLength(); index++)
        {
            var result = results[index];
            var ruleIndex = result.GetProperty("ruleIndex").GetInt32();
            Assert.Equal(result.GetProperty("ruleId").GetString(), rules[ruleIndex].GetProperty("id").GetString());
        }

        Assert.Equal(BvnFingerprint, results[0].GetProperty("partialFingerprints").GetProperty("gatekeeper/v1").GetString());
        Assert.Equal(PhoneFingerprint, results[1].GetProperty("partialFingerprints").GetProperty("gatekeeper/v1").GetString());
        Assert.DoesNotContain("old/removed.cs", ResultsTestSupport.ReadOutputFile("examples/results/full.sarif.json"));
    }

    [Theory]
    [InlineData("examples/results/minimal.gk.json")]
    [InlineData("examples/results/full.gk.json")]
    [InlineData("examples/results/minimal.sarif.json")]
    [InlineData("examples/results/full.sarif.json")]
    public void Example_files_use_lf_line_endings_and_a_trailing_newline(string relativePath)
    {
        var text = ResultsTestSupport.ReadOutputFile(relativePath);

        Assert.DoesNotContain("\r", text);
        Assert.EndsWith("\n", text);
    }
}
