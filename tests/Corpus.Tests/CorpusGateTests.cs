// SPDX-License-Identifier: Apache-2.0
using InvenioTech.GateKeeper.Engine;

namespace InvenioTech.GateKeeper.Corpus.Tests;

public sealed class CorpusGateTests
{
    [Theory]
    [InlineData(19, 1, true)]
    [InlineData(18, 1, false)]
    [InlineData(0, 0, true)]
    public void CorpusGate_Should_UseTheExactBoundary_When_ErrorFindingsAreScored(int positives, int falsePositives, bool passes)
    {
        var expected = Enumerable.Range(1, positives).Select(line => Expected("local.a", line)).ToArray();
        var findings = Enumerable.Range(1, positives + falsePositives).Select(line => Finding("local.a", line)).ToArray();
        var run = CorpusMeasurements.PerRule([(new CorpusCase("synthetic", "unused", expected), findings)]);
        run.Overall.ErrorTruePositiveCount.Should().Be(positives);
        run.Overall.ErrorFalsePositiveCount.Should().Be(falsePositives);
        run.FalsePositiveGatePassed.Should().Be(passes);
    }

    [Fact]
    public void CorpusGate_Should_ExcludeLowSeverities_When_TheyAreUnexpected()
    {
        var run = CorpusMeasurements.PerRule([(new CorpusCase("synthetic", "unused", [Expected("local.a", 1)]),
            new[] { Finding("local.a", 1), Finding("local.a", 2, Severity.Warning), Finding("local.a", 3, Severity.Note) })]);
        run.Overall.WarningCount.Should().Be(1);
        run.Overall.NoteCount.Should().Be(1);
        run.Overall.ErrorFalsePositiveCount.Should().Be(0);
        run.FalsePositiveGatePassed.Should().BeTrue();
    }

    [Fact]
    public void CorpusGate_Should_CountRejectedAndUnresolvedTriageAsFalse_When_TheirKeysMatch()
    {
        var expected = new[] { Expected("local.a", 1), Expected("local.a", 2, "false-positive"), Expected("local.a", 3, "unresolved") };
        var run = CorpusMeasurements.PerRule([(new CorpusCase("synthetic", "unused", expected),
            new[] { Finding("local.a", 1), Finding("local.a", 2), Finding("local.a", 3) })]);
        run.Overall.ExpectedCount.Should().Be(1);
        run.Overall.MatchedCount.Should().Be(1);
        run.Overall.ErrorFalsePositiveCount.Should().Be(2);
        run.FalsePositiveGatePassed.Should().BeFalse();
    }

    [Fact]
    public void CorpusGate_Should_FailOneBadRule_When_TheOverallRatePasses()
    {
        var expected = Enumerable.Range(1, 100).Select(line => Expected("local.a", line)).ToArray();
        var findings = Enumerable.Range(1, 100).Select(line => Finding("local.a", line)).Append(Finding("local.b", 1)).ToArray();
        var run = CorpusMeasurements.PerRule([(new CorpusCase("synthetic", "unused", expected), findings)]);
        run.Overall.FalsePositiveGatePassed.Should().BeTrue();
        run.FalsePositiveGatePassed.Should().BeFalse();
    }

    [Fact]
    public void CorpusGate_Should_AvoidIntegerOverflow_When_CountsAreLarge()
    {
        var boundary = new CorpusRuleMeasurement("synthetic", 0, 0, 0, [], 1_900_000_000, 100_000_000);
        boundary.FalsePositiveGatePassed.Should().BeTrue();
        (boundary with { ErrorFalsePositiveCount = 100_000_001 }).FalsePositiveGatePassed.Should().BeFalse();
    }

    [Fact]
    public void CorpusScorer_Should_RejectDuplicateKeys_When_TheyCouldInflateRecall()
    {
        Action duplicateExpected = () => CorpusMeasurements.Score([Expected("local.a", 1), Expected("local.a", 1)], []);
        Action duplicateFinding = () => CorpusMeasurements.Score([], [Finding("local.a", 1), Finding("local.a", 1)]);
        duplicateExpected.Should().ThrowExactly<ArgumentException>();
        duplicateFinding.Should().ThrowExactly<ArgumentException>();
    }

    private static CorpusExpectedEntry Expected(string id, int line, string triage = "true-positive") => new() { RuleId = id, Path = "sample.txt", Line = line, Column = 1, Triage = triage };
    private static Finding Finding(string id, int line, Severity severity = Severity.Error) => new(id, severity, "synthetic", "sample.txt", line, 1,
        LocationKind.Source, null, BaselineState.New, new Citation("Synthetic scoring acceptance.", null, null));
}
