// SPDX-License-Identifier: Apache-2.0
using InvenioTech.GateKeeper.Engine;

namespace InvenioTech.GateKeeper.Corpus.Tests;

public sealed class CorpusMatcherTests
{
    [Theory]
    [InlineData("marker", 1)]
    [InlineData("longer-marker-token", 0)]
    public async Task CorpusRunner_Should_RunTheRealMatcher_When_RulesAreSupplied(string text, int count)
    {
        var root = Path.Combine(Path.GetTempPath(), "gate-keeper-corpus-input-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            File.WriteAllText(Path.Combine(root, "sample.txt"), text);
            var item = new CorpusCase("synthetic-matcher", root, []);
            var first = await new CorpusScanRunner(item, [Rule()]).RunAsync();
            using var firstCleanup = new CorpusScanScavenger(first);
            var second = await new CorpusScanRunner(item, [Rule()]).RunAsync();
            using var secondCleanup = new CorpusScanScavenger(second);
            first.MatcherStagePresent.Should().BeTrue();
            first.Findings.Should().HaveCount(count).And.Equal(second.Findings);
            if (count == 1)
            {
                var finding = first.Findings.Single();
                finding.RuleId.Should().Be("local.synthetic");
                finding.Path.Should().Be("sample.txt");
                finding.Line.Should().Be(1);
                finding.Column.Should().Be(1);
                finding.Fingerprint.Should().BeNull();
                finding.Citation.Title.Should().Be("Synthetic matcher acceptance.");
            }
            var inspection = await new CorpusScanRunner(item).RunAsync();
            using var inspectionCleanup = new CorpusScanScavenger(inspection);
            inspection.MatcherStagePresent.Should().BeFalse();
            inspection.Findings.Should().BeEmpty();
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void CorpusRunner_Should_RejectAnEmptyRuleList_When_MeasurementIsRequested()
    {
        Action create = () => new CorpusScanRunner(new CorpusCase("synthetic", "unused", []), []);
        create.Should().ThrowExactly<ArgumentException>();
    }

    [Fact]
    public async Task CorpusRunner_Should_LeaveNoCopy_When_AlreadyCancelled()
    {
        var name = "synthetic-cancel-" + Guid.NewGuid().ToString("N");
        using var source = new CancellationTokenSource();
        source.Cancel();
        Func<Task> run = async () => await new CorpusScanRunner(new CorpusCase(name, "unused", []), [Rule()]).RunAsync(source.Token);
        await run.Should().ThrowAsync<OperationCanceledException>();
        var parent = Path.Combine(Path.GetTempPath(), "corpus-harness");
        if (Directory.Exists(parent)) Directory.GetDirectories(parent, name + "-*").Should().BeEmpty();
    }

    [Fact]
    public async Task CorpusRunner_Should_RemoveItsCopy_When_MatcherConstructionFails()
    {
        var root = Path.Combine(Path.GetTempPath(), "gate-keeper-corpus-input-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var name = "synthetic-failure-" + Guid.NewGuid().ToString("N");
        try
        {
            File.WriteAllText(Path.Combine(root, "sample.txt"), "marker");
            var invalid = Rule() with { Matcher = new RuleMatcher(null, null) };
            Func<Task> run = async () => await new CorpusScanRunner(new CorpusCase(name, root, []), [invalid]).RunAsync();
            await run.Should().ThrowAsync<EngineToolException>();
            Directory.GetDirectories(Path.Combine(Path.GetTempPath(), "corpus-harness"), name + "-*").Should().BeEmpty();
        }
        finally { Directory.Delete(root, true); }
    }

    private static Rule Rule() => new("local.synthetic", Severity.Error, "synthetic", new Citation("Synthetic matcher acceptance.", null, null), "1",
        null, null, null, null, new RuleMatcher(null, new RuleListMatcher(["marker"], [])));
}
