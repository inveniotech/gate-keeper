// SPDX-License-Identifier: Apache-2.0

using System.Globalization;
using System.Text;
using InvenioTech.GateKeeper.Engine;

namespace InvenioTech.GateKeeper.Corpus.Tests;

/// <summary>
/// One rule pack's identity as the weekly report records it: the pack
/// identifier, the version its manifest carries and the SHA-256 digest of the
/// pack bytes the measurement loaded.
/// </summary>
/// <param name="PackId">The rule pack's identifier.</param>
/// <param name="Version">The rule pack's version.</param>
/// <param name="Sha256">The lowercase hexadecimal SHA-256 digest of the pack bytes.</param>
public sealed record CorpusPackStamp(string PackId, string Version, string Sha256);

/// <summary>
/// One pinned validation repository, exactly as the weekly report records it
/// from <c>tests/corpus/validation/repos.json</c>: the fields section 4.3 of
/// the quality-budgets specification fixes for a pin. Triage is per finding
/// in the validation records, not a property of the pin, so no triage field
/// is read here. The row names the pin so a reader can reproduce the
/// measurement; it is not a rate input until the replay card after P2-06
/// lands.
/// </summary>
/// <param name="Repository">The repository name in <c>owner/repository</c> form.</param>
/// <param name="Revision">The pinned commit, 40 lowercase hexadecimal characters.</param>
/// <param name="Licence">The repository's licence at the pin, from the permissive list.</param>
/// <param name="PinnedOn">The UTC date the pin was set, in the <c>yyyy-MM-dd</c> form.</param>
/// <param name="Role">The role the pinned list gives the repository.</param>
public sealed record CorpusValidationPin(string Repository, string Revision, string Licence, string PinnedOn, string Role);

/// <summary>
/// Every input the dated weekly report records so a reader can reproduce the
/// numbers (the quality-budgets specification, section 5.5): the evaluation
/// date, the measured commit, the pack inventory, the validation pins when the
/// validation corpus is present, the synthetic case count and the measurements
/// themselves.
/// </summary>
/// <param name="EvaluationDate">The report's UTC evaluation date, in the <c>yyyy-MM-dd</c> form.</param>
/// <param name="MeasurementRevision">The commit the measurement ran at, 40 lowercase hexadecimal characters.</param>
/// <param name="PackStamps">Every authored rule pack the measurement loaded, at least one.</param>
/// <param name="ValidationPins">The pinned validation repositories, or null while the validation corpus is absent.</param>
/// <param name="ScannedCaseCount">The number of synthetic repositories the harness scanned, at least one.</param>
/// <param name="Measurements">The per-rule and overall measurements of the run.</param>
public sealed record CorpusWeeklyReportInput(
    string EvaluationDate,
    string MeasurementRevision,
    IReadOnlyList<CorpusPackStamp> PackStamps,
    IReadOnlyList<CorpusValidationPin>? ValidationPins,
    int ScannedCaseCount,
    CorpusRunMeasurements Measurements);

/// <summary>
/// Renders the dated Markdown false-positive report the weekly measurement
/// duty commits under <c>docs/reports/</c> (the quality-budgets
/// specification, section 5.5). The report reuses the per-rule table the
/// scorer already prints, so the two artefacts cannot disagree, and it never
/// sees a matched value, so it cannot name one (rule 10).
/// </summary>
public static class CorpusWeeklyReportWriter
{
    private const string DatePattern = "yyyy-MM-dd";

    /// <summary>
    /// Renders the report text. Every emitted line ends with a newline feed
    /// and the text is written in one piece, so the committed file is
    /// byte-identical across platforms (rule 22).
    /// </summary>
    /// <param name="input">The measurement inputs the report records.</param>
    /// <returns>The full Markdown report text.</returns>
    /// <exception cref="ArgumentException">An input is malformed or incomplete; the message says what to supply.</exception>
    public static string Write(CorpusWeeklyReportInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        ValidateDate(input.EvaluationDate);
        ValidateRevision(input.MeasurementRevision);
        if (input.PackStamps is null || input.PackStamps.Count == 0)
            throw new ArgumentException(
                "The weekly report records its pack inputs, so the measurement must supply at least one rule pack stamp.",
                nameof(input));
        if (input.PackStamps.Any(stamp => stamp is null))
            throw new ArgumentException("A rule pack stamp is missing.", nameof(input));
        if (input.PackStamps.Select(stamp => stamp.PackId).Distinct(StringComparer.Ordinal).Count() != input.PackStamps.Count)
            throw new ArgumentException(
                "Two rule pack stamps share an identifier; the report needs distinct rule pack identifiers so a reader can match each line to one pack.",
                nameof(input));
        foreach (var stamp in input.PackStamps)
        {
            if (string.IsNullOrWhiteSpace(stamp.PackId) || string.IsNullOrWhiteSpace(stamp.Version))
                throw new ArgumentException("A rule pack stamp is missing its identifier or version.", nameof(input));
            if (!IsDigest(stamp.Sha256, 64))
                throw new ArgumentException(
                    $"The SHA-256 digest of rule pack '{stamp.PackId}' is not 64 lowercase hexadecimal characters; record the digest of the exact pack bytes the measurement loaded.",
                    nameof(input));
        }
        if (input.ValidationPins is not null)
        {
            foreach (var pin in input.ValidationPins)
            {
                if (pin is null)
                    throw new ArgumentException("A validation pin is missing.", nameof(input));
                var separator = pin.Repository.IndexOf('/');
                if (separator <= 0 || separator == pin.Repository.Length - 1 || pin.Repository.IndexOf('/', separator + 1) >= 0)
                    throw new ArgumentException(
                        $"The validation repository '{pin.Repository}' is not in the owner/repository form the pinned list records.",
                        nameof(input));
                if (!IsDigest(pin.Revision, 40))
                    throw new ArgumentException(
                        $"The pinned revision of '{pin.Repository}' is not 40 lowercase hexadecimal characters; record the full commit the pin fixes.",
                        nameof(input));
                if (string.IsNullOrWhiteSpace(pin.Licence))
                    throw new ArgumentException(
                        $"The validation pin for '{pin.Repository}' is missing its licence; a pin names the licence the repository carries at its commit (section 4.3).",
                        nameof(input));
                if (!IsCalendarDate(pin.PinnedOn))
                    throw new ArgumentException(
                        $"The pin date of '{pin.Repository}' is not a UTC date in the yyyy-MM-dd form; a pin records the UTC date it was set (section 4.3).",
                        nameof(input));
                if (string.IsNullOrWhiteSpace(pin.Role))
                    throw new ArgumentException(
                        $"The validation pin for '{pin.Repository}' is missing its role; the pinned list records the role each repository plays (section 4.3).",
                        nameof(input));
            }
        }
        if (input.Measurements is null)
            throw new ArgumentException("The weekly report records a measurement; supply the run's per-rule and overall measurements.", nameof(input));
        if (input.ScannedCaseCount < 1)
            throw new ArgumentException(
                "The weekly report records the number of synthetic repositories scanned; a measurement with no case is a clean run, not a report.",
                nameof(input));

        var report = new StringBuilder();
        report.Append($"# False-positive report {input.EvaluationDate}\n\n");
        report.Append("<!-- Generated by Measure-Detection.ps1 through DetectionPackTests. Do not hand-edit. -->\n\n");
        report.Append("This report records the week's false-positive measurement for the release notes and the weekly record in `docs/reports/`. ");
        report.Append("The counts and arithmetic follow the false-positive policy in `docs/specs/quality-budgets.md`, sections 5.2 and 5.4: the gate is `20 × false positives <= true positives + false positives`, per rule and overall, and every unresolved finding counts as a false positive until the owner labels it (section 3.2). ");
        report.Append("Findings at `warning` and `note` severity are reported but sit outside the gate (section 5.3). No report line names a matched value (rule 10).\n\n");
        report.Append("**Inputs.**\n\n");
        report.Append($"- Harness revision `{input.MeasurementRevision}`, the commit this measurement ran at.\n");
        report.Append($"- Product version of `gatekeeper`: `{EngineProduct.EngineVersion}` (the constant every artefact stamps, NFR-031).\n");
        foreach (var stamp in input.PackStamps.OrderBy(item => item.PackId, StringComparer.Ordinal))
            report.Append($"- Rule pack `{stamp.PackId}` version `{stamp.Version}`, SHA-256 `{stamp.Sha256}`.\n");
        if (input.ValidationPins is null || input.ValidationPins.Count == 0)
        {
            report.Append("- Validation repositories: none measured. The validation corpus is prepared by P2-06 (issue #32) and has not merged at this revision, so every rate below covers the synthetic golden corpus only.\n");
        }
        else
        {
            foreach (var pin in input.ValidationPins.OrderBy(item => item.Repository, StringComparer.Ordinal))
                report.Append($"- Validation repository `{pin.Repository}` pinned at `{pin.Revision}` on {pin.PinnedOn} (licence {pin.Licence}, role {pin.Role}).\n");
            report.Append("- The rates below still cover the synthetic golden corpus only; replaying the pinned repositories and their per-finding triage records is the follow-up measurement card after P2-06 (issue #32) merges.\n");
        }
        report.Append($"- Synthetic cases scanned: {input.ScannedCaseCount}.\n\n");
        report.Append(input.Measurements.FalsePositiveGatePassed
            ? "**Gate: pass** (per rule and overall, exact integer arithmetic).\n\n"
            : "**Gate: fail.** The run fails NFR-013; the table's gate column names whether the overall row or a single rule breached the boundary.\n\n");
        if (!input.Measurements.FalsePositiveGatePassed)
        {
            var top = input.Measurements.PerRule
                .Where(rule => rule.ErrorFalsePositiveCount > 0)
                .OrderByDescending(rule => rule.ErrorFalsePositiveCount)
                .ThenBy(rule => rule.RuleId, StringComparer.Ordinal)
                .Take(3).ToArray();
            if (top.Length > 0)
            {
                report.Append("Top three rules by false positives: ");
                report.Append(string.Join(", ", top.Select(rule => $"`{rule.RuleId}` ({rule.ErrorFalsePositiveCount})")));
                report.Append(".\n\n");
            }
        }
        report.Append(CorpusMeasurements.WriteMarkdownTable(input.Measurements, matcherStagePresent: true));
        report.Append('\n');
        return report.ToString();
    }

    private static void ValidateDate(string evaluationDate)
    {
        if (!IsCalendarDate(evaluationDate))
            throw new ArgumentException(
                $"The evaluation date '{evaluationDate}' is not a UTC date in the yyyy-MM-dd form; pass the measurement's evaluation date from the run metadata, for example 2026-11-16.",
                nameof(evaluationDate));
    }

    private static bool IsCalendarDate(string? value) => value is { Length: 10 }
        && value[4] == '-' && value[7] == '-'
        && DateTime.TryParseExact(value, DatePattern, CultureInfo.InvariantCulture, DateTimeStyles.None, out _);

    private static void ValidateRevision(string measurementRevision)
    {
        if (!IsDigest(measurementRevision, 40))
            throw new ArgumentException(
                $"The measurement revision '{measurementRevision}' is not 40 lowercase hexadecimal characters; pass the commit the measurement runs at, for example git rev-parse HEAD.",
                nameof(measurementRevision));
    }

    private static bool IsDigest(string? value, int length) => value is not null
        && value.Length == length
        && value.All(character => character is >= '0' and <= '9' or >= 'a' and <= 'f');
}
