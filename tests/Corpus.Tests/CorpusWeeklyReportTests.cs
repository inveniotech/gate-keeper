// SPDX-License-Identifier: Apache-2.0

using InvenioTech.GateKeeper.Engine;

namespace InvenioTech.GateKeeper.Corpus.Tests;

public sealed class CorpusWeeklyReportTests
{
    private const string Revision = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    private const string ContentDigest = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";

    [Fact]
    public void CorpusWeeklyReport_Should_NameTheEvaluationDateAndInputs_When_TheReportIsWritten()
    {
        var report = CorpusWeeklyReportWriter.Write(Input());
        report.Should().StartWith("# False-positive report 2026-11-16\n");
        report.Should().Contain($"- Harness revision `{Revision}`, the commit this measurement ran at.\n");
        report.Should().Contain($"- Product version of `gatekeeper-ndpa-check`: `{EngineProduct.EngineVersion}` (the constant every artefact stamps, NFR-031).\n");
        report.Should().Contain($"- Rule pack `invenio.bvn` version `2026.11.0`, SHA-256 `{ContentDigest}`.\n");
        report.Should().Contain("- Synthetic cases scanned: 3.\n");
    }

    [Fact]
    public void CorpusWeeklyReport_Should_TraceThePolicySections_When_TheReportIsWritten()
    {
        var report = CorpusWeeklyReportWriter.Write(Input());
        report.Should().Contain("`docs/specs/quality-budgets.md`, sections 5.2 and 5.4");
        report.Should().Contain("every unresolved finding counts as a false positive until the owner labels it (section 3.2)");
        report.Should().Contain("Findings at `warning` and `note` severity are reported but sit outside the gate (section 5.3)");
        report.Should().Contain("No report line names a matched value (rule 10)");
        report.Should().NotContain("\r");
    }

    [Fact]
    public void CorpusWeeklyReport_Should_StateTheValidationCorpusIsUnmeasured_When_ThePinsAreAbsent()
    {
        var report = CorpusWeeklyReportWriter.Write(Input());
        report.Should().Contain("- Validation repositories: none measured. The validation corpus is prepared by P2-06 (issue #32) and has not merged at this revision, so every rate below covers the synthetic golden corpus only.\n");
    }

    [Fact]
    public void CorpusWeeklyReport_Should_ListTheValidationPinsInOrdinalOrder_When_TheValidationCorpusIsPresent()
    {
        var pins = new[]
        {
            new CorpusValidationPin("zeta-maintained/tool", Revision, "Apache-2.0", "2026-11-05", "data-access"),
            new CorpusValidationPin("alpha-maintained/tool", Revision, "MIT", "2026-11-05", "framework"),
        };
        var report = CorpusWeeklyReportWriter.Write(Input(validationPins: pins));
        report.Should().Contain("- Validation repository `alpha-maintained/tool` pinned at `aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa` on 2026-11-05 (licence MIT, role framework).\n");
        var alphaIndex = report.IndexOf("alpha-maintained/tool", StringComparison.Ordinal);
        var zetaIndex = report.IndexOf("zeta-maintained/tool", StringComparison.Ordinal);
        (alphaIndex < zetaIndex).Should().BeTrue("the pins sort by repository name ordinal (rule 6)");
        report.Should().Contain("The rates below still cover the synthetic golden corpus only; replaying the pinned repositories and their per-finding triage records is the follow-up measurement card after P2-06 (issue #32) merges.\n");
    }

    [Fact]
    public void CorpusWeeklyReport_Should_ReportThePassingGate_When_EveryRateSitsInsideTheBoundary()
    {
        var report = CorpusWeeklyReportWriter.Write(Input(measurements: Measure(("local.a", 20, 1))));
        report.Should().Contain("**Gate: pass** (per rule and overall, exact integer arithmetic).\n");
        report.Should().NotContain("Top three rules by false positives");
    }

    [Fact]
    public void CorpusWeeklyReport_Should_NameTheTopThreeRulesByFalsePositives_When_TheGateFails()
    {
        var measurements = Measure(("local.a", 20, 10), ("local.c", 20, 6), ("local.b", 20, 6), ("local.d", 20, 2));
        var report = CorpusWeeklyReportWriter.Write(Input(measurements: measurements));
        report.Should().Contain("**Gate: fail.** The run fails NFR-013; the table's gate column names whether the overall row or a single rule breached the boundary.\n");
        report.Should().Contain("Top three rules by false positives: `local.a` (10), `local.b` (6), `local.c` (6).\n");
        report.Should().NotContain("`local.d` (2)");
    }

    [Fact]
    public void CorpusWeeklyReport_Should_ReportAFailingRun_When_OneRuleFailsAndTheOverallRowPasses()
    {
        var measurements = Measure(("local.a", 100, 10), ("local.b", 200, 0));
        measurements.Overall.FalsePositiveGatePassed.Should().BeTrue("the fabricated overall rate sits inside the boundary");
        measurements.FalsePositiveGatePassed.Should().BeFalse("only the single rule breaches it");
        var report = CorpusWeeklyReportWriter.Write(Input(measurements: measurements));
        report.Should().Contain("**Gate: fail.** The run fails NFR-013; the table's gate column names whether the overall row or a single rule breached the boundary.\n");
        report.Should().Contain("Top three rules by false positives: `local.a` (10).\n");
    }

    [Fact]
    public void CorpusWeeklyReport_Should_PrintThePerRuleTable_When_TheReportIsWritten()
    {
        var report = CorpusWeeklyReportWriter.Write(Input(measurements: Measure(("local.a", 20, 1))));
        report.Should().Contain("rule id | expected | produced | matched | unexpected | precision | recall | error true positives | error false positives | warnings | notes | gate | error false-positive rate\n");
        report.Should().Contain("(overall) | 20 | 21 | 20 | 1 | 95.2 | 100 | 20 | 1 | 0 | 0 | pass | 4.8\n");
    }

    [Theory]
    [InlineData("20261116")]
    [InlineData("2026-13-01")]
    [InlineData("2026-11-16T00:00:00Z")]
    [InlineData("")]
    public void CorpusWeeklyReport_Should_RejectAMalformedEvaluationDate_When_ItIsNotAYyyyMmDdDate(string evaluationDate)
    {
        Action write = () => CorpusWeeklyReportWriter.Write(Input(evaluationDate: evaluationDate));
        write.Should().ThrowExactly<ArgumentException>().WithMessage("*yyyy-MM-dd*");
    }

    [Theory]
    [InlineData("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")]
    [InlineData("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
    [InlineData("gggggggggggggggggggggggggggggggggggggggg")]
    [InlineData("")]
    public void CorpusWeeklyReport_Should_RejectAMalformedRevision_When_ItIsNot40LowercaseHexadecimalCharacters(string measurementRevision)
    {
        Action write = () => CorpusWeeklyReportWriter.Write(Input(measurementRevision: measurementRevision));
        write.Should().ThrowExactly<ArgumentException>().WithMessage("*40 lowercase hexadecimal characters*");
    }

    [Fact]
    public void CorpusWeeklyReport_Should_RejectAnEmptyPackInventory_When_TheReportNamesItsPackInputs()
    {
        Action write = () => CorpusWeeklyReportWriter.Write(Input(packStamps: []));
        write.Should().ThrowExactly<ArgumentException>().WithMessage("*at least one rule pack*");
    }

    [Fact]
    public void CorpusWeeklyReport_Should_RejectDuplicatePackIdentifiers_When_TheInventoryCouldDoubleCount()
    {
        var packStamps = new[]
        {
            new CorpusPackStamp("invenio.bvn", "2026.11.0", ContentDigest),
            new CorpusPackStamp("invenio.bvn", "2026.12.0", ContentDigest),
        };
        Action write = () => CorpusWeeklyReportWriter.Write(Input(packStamps: packStamps));
        write.Should().ThrowExactly<ArgumentException>().WithMessage("*distinct rule pack identifiers*");
    }

    [Fact]
    public void CorpusWeeklyReport_Should_RejectAMalformedValidationPin_When_TheRepositoryOrPinDateIsWrong()
    {
        Action missingOwner = () => CorpusWeeklyReportWriter.Write(Input(validationPins: [new CorpusValidationPin("no-owner-slash", Revision, "MIT", "2026-11-05", "framework")]));
        Action malformedPinDate = () => CorpusWeeklyReportWriter.Write(Input(validationPins: [new CorpusValidationPin("alpha/tool", Revision, "MIT", "20261105", "framework")]));
        missingOwner.Should().ThrowExactly<ArgumentException>().WithMessage("*owner/repository*");
        malformedPinDate.Should().ThrowExactly<ArgumentException>().WithMessage("*yyyy-MM-dd*");
    }

    private static CorpusWeeklyReportInput Input(
        string evaluationDate = "2026-11-16",
        string measurementRevision = Revision,
        IReadOnlyList<CorpusPackStamp>? packStamps = null,
        IReadOnlyList<CorpusValidationPin>? validationPins = null,
        CorpusRunMeasurements? measurements = null) => new(
        evaluationDate,
        measurementRevision,
        packStamps ?? [new CorpusPackStamp("invenio.bvn", "2026.11.0", ContentDigest)],
        validationPins,
        3,
        measurements ?? Measure(("local.a", 20, 1)));

    private static CorpusRunMeasurements Measure(params (string Id, int Positives, int FalsePositives)[] rules)
    {
        var expected = rules.SelectMany(rule => Enumerable.Range(1, rule.Positives).Select(line => Expected(rule.Id, line))).ToArray();
        var findings = rules.SelectMany(rule => Enumerable.Range(1, rule.Positives + rule.FalsePositives).Select(line => Finding(rule.Id, line))).ToArray();
        return CorpusMeasurements.PerRule([(new CorpusCase("synthetic", "unused", expected), findings)]);
    }

    private static CorpusExpectedEntry Expected(string id, int line, string triage = CorpusTriage.TruePositive) => new() { RuleId = id, Path = "sample.txt", Line = line, Column = 1, Triage = triage };

    private static Finding Finding(string id, int line, Severity severity = Severity.Error) => new(id, severity, "synthetic", "sample.txt", line, 1,
        LocationKind.Source, null, BaselineState.New, new Citation("Synthetic scoring acceptance.", null, null));
}
