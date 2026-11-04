// SPDX-License-Identifier: Apache-2.0

using InvenioTech.GateKeeper.Engine;

namespace InvenioTech.GateKeeper.Corpus.Tests;

/// <summary>
/// The scorer's arithmetic, exercised on fabricated scans so the counts and
/// the rates it prints are proven before the measurement work packages build
/// on them. Findings here are synthetic in every field (rule 10); no real
/// value can enter through a fabricated record.
/// </summary>
public sealed class CorpusScorerTests
{
    private static Finding Finding(string ruleId, string path, int line, int column) =>
        new(
            RuleId: ruleId,
            Severity: Severity.Error,
            Category: "synthetic-test-category",
            Path: path,
            Line: line,
            Column: column,
            LocationKind: LocationKind.Source,
            Fingerprint: null,
            BaselineState: BaselineState.New,
            Citation: new Citation(Title: "synthetic test citation", NdpaSection: null, GaidSection: null));

    private static CorpusExpectedEntry Expected(string ruleId, string path, int line, int column) =>
        new() { RuleId = ruleId, Path = path, Line = line, Column = column, Triage = "true-positive" };

    [Fact]
    public void CorpusScorer_Should_Match_TheExpectedLocation_When_TheFindingCarriesTheSameKey()
    {
        var expected = new[] { Expected("pii.bvn", "src/a.cs", 14, 9) };
        var findings = new[] { Finding("pii.bvn", "src/a.cs", 14, 9) };

        var diff = CorpusMeasurements.Score(expected, findings);

        diff.ExpectedCount.Should().Be(1);
        diff.ProducedCount.Should().Be(1);
        diff.MatchedCount.Should().Be(1);
        diff.UnexpectedCount.Should().Be(0);
    }

    [Fact]
    public void CorpusScorer_Should_NameTheUndeclaredLocation_When_TheFindingCarriesADifferentKey()
    {
        var expected = new[] { Expected("pii.bvn", "src/a.cs", 14, 9) };
        var findings = new[] { Finding("pii.bvn", "src/a.cs", 14, 9), Finding("pii.phone", "src/b.cs", 3, 1) };

        var diff = CorpusMeasurements.Score(expected, findings);

        diff.MatchedCount.Should().Be(1);
        diff.UnexpectedCount.Should().Be(1);
        diff.Unexpected.Should().ContainSingle()
            .Which.Should().BeEquivalentTo(
                new CorpusDiff.UnexpectedFinding("pii.phone", "src/b.cs", 3, 1),
                because: "an unexpected finding is named by rule and location only (rule 10)");
    }

    [Fact]
    public void CorpusScorer_Should_TreatAnEmptyFalsePositiveCaseCleanly_When_TheNearMissProducesNothing()
    {
        var diff = CorpusMeasurements.Score([], []);

        diff.ExpectedCount.Should().Be(0);
        diff.ProducedCount.Should().Be(0);
        diff.MatchedCount.Should().Be(0);
        diff.UnexpectedCount.Should().Be(0);
    }

    [Fact]
    public void CorpusMeasurements_Should_SumRulesAndGiveTheOverallRow_When_MultipleCasesRun()
    {
        var first = new CorpusCase(
            Name: "case-one",
            RepositoryRoot: "synthetic",
            Expected: [Expected("pii.bvn", "src/a.cs", 14, 9)]);
        var second = new CorpusCase(
            Name: "case-two",
            RepositoryRoot: "synthetic",
            Expected: [Expected("pii.bvn", "src/b.cs", 2, 1), Expected("pii.nin", "src/c.cs", 7, 5)]);

        var findingsOne = new[] { Finding("pii.bvn", "src/a.cs", 14, 9) };
        // The second scan misses one expected entry and carries one distortion
        // the case never declared, which is exactly the triage two-report bootstrap:
        var findingsTwo = new[] { Finding("pii.nin", "src/c.cs", 7, 5), Finding("pii.ng-11-digit-id", "src/d.cs", 1, 1) };

        var run = CorpusMeasurements.PerRule([(first, findingsOne), (second, findingsTwo)]);

        run.Overall.ExpectedCount.Should().Be(3);
        run.Overall.ProducedCount.Should().Be(3);
        run.Overall.MatchedCount.Should().Be(2);
        run.Overall.UnexpectedCount.Should().Be(1);

        var measurements = run.PerRule;
        measurements.Select(measurement => measurement.RuleId).Should().Equal("pii.bvn", "pii.ng-11-digit-id", "pii.nin");
        measurements[0].ExpectedCount.Should().Be(2);
        measurements[0].ProducedCount.Should().Be(1);
        measurements[0].MatchedCount.Should().Be(1);
        // The personal identity number rule matched its one expected entry.
        measurements[2].ExpectedCount.Should().Be(1);
        measurements[2].ProducedCount.Should().Be(1);
        measurements[2].MatchedCount.Should().Be(1);
        // The note rule was never expected here, so its finding is unexpected.
        measurements[1].ExpectedCount.Should().Be(0);
        measurements[1].UnexpectedCount.Should().Be(1);
    }

    [Fact]
    public void CorpusReport_Should_PrintUnmeasuredRatherThanRecall_When_TheMatcherStageIsAbsent()
    {
        var synthetic = new CorpusRuleMeasurement(
            "pii.bvn",
            ExpectedCount: 2,
            ProducedCount: 0,
            MatchedCount: 0,
            Unexpected: []);
        var run = new CorpusRunMeasurements([synthetic], synthetic);

        using var writer = new StringWriter();
        CorpusMeasurements.WriteReport(writer, run, matcherStagePresent: false);

        var report = writer.ToString();
        report.Should().Contain("pii.bvn");
        report.Should().Contain("unmeasured", because: "a scan without the matching stage proves nothing about missed detections");
    }

    [Fact]
    public void CorpusReport_Should_PrintTheRates_When_TheMatcherStageProducesFindings()
    {
        var synthetic = new CorpusRuleMeasurement(
            "pii.bvn",
            ExpectedCount: 3,
            ProducedCount: 2,
            MatchedCount: 1,
            Unexpected: [new CorpusDiff.UnexpectedFinding("pii.bvn", "src/f.cs", 4, 2)]);
        var run = new CorpusRunMeasurements([synthetic], synthetic);

        using var writer = new StringWriter();
        CorpusMeasurements.WriteReport(writer, run, matcherStagePresent: true);

        var report = writer.ToString();
        report.Should().Contain("| 50 |").And.Contain("| 33.3");
        // 1 of 2 produced matched with 1 unexpected: 50% precision;
        // 1 of 3 expected matched: 33.3% recall, all computed from the counts.
    }
}
