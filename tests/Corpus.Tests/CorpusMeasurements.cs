// SPDX-License-Identifier: Apache-2.0

using System.Globalization;
using InvenioTech.GateKeeper.Engine;

namespace InvenioTech.GateKeeper.Corpus.Tests;

/// <summary>
/// The scorer the harness uses to diff one scan's findings against a case's
/// expected list and to report precision and recall per rule. The golden
/// corpus answers the triage labels on its own (the quality-budgets
/// specification, section 3.2): a finding in a positive case that matches an
/// expected entry is a true positive, any other finding is a false positive,
/// and a missing expected entry is a missed detection that recall exposes.
/// </summary>
public static class CorpusMeasurements
{
    private const string OverallRuleId = "(overall)";
    private const string NotApplicableRate = "n/a";
    private const string UnmeasuredStatus = "unmeasured";
    private const string PassingGateStatus = "pass";
    private const string FailingGateStatus = "fail";
    private const string RateFormat = "0.#";
    private const string ReportHeader = "rule id | expected | produced | matched | unexpected | precision | recall | error true positives | error false positives | warnings | notes | gate | error false-positive rate";
    private const string PerRuleSectionHeader = "-- per rule, in rule-identifier order --";
    private const string ReportRowFormat = "{0} | {1} | {2} | {3} | {4} | {5} | {6} | {7} | {8} | {9} | {10} | {11} | {12}";

    /// <summary>
    /// Diffs one scan's findings against one case's expected entries. A
    /// produced finding is a match when a same-key expected entry exists:
    /// the same rule identifier, path, line and column. Every other produced
    /// finding is unexpected, so a near-miss case that produces anything at
    /// all shows up as a false positive the case can name exactly.
    /// </summary>
    /// <param name="expected">The case's expected entries.</param>
    /// <param name="findings">The findings the scan produced.</param>
    public static CorpusDiff Score(IReadOnlyList<CorpusExpectedEntry> expected, IReadOnlyList<Finding> findings)
    {
        ArgumentNullException.ThrowIfNull(expected);
        ArgumentNullException.ThrowIfNull(findings);

        var expectedKeys = new Dictionary<CorpusKey, string?>();
        foreach (var entry in expected)
        {
            if (entry.Triage is not (null or CorpusTriage.TruePositive or CorpusTriage.FalsePositive or CorpusTriage.Unresolved))
                throw new ArgumentException("A corpus triage label is invalid.", nameof(expected));
            if (!expectedKeys.TryAdd(new CorpusKey(entry.RuleId, entry.Path, entry.Line, entry.Column), entry.Triage))
                throw new ArgumentException("Duplicate corpus expectation keys cannot be scored.", nameof(expected));
        }

        var unexpected = new List<CorpusDiff.UnexpectedFinding>();
        var matched = 0;
        var errorTruePositives = 0;
        var errorFalsePositives = 0;
        var warnings = 0;
        var notes = 0;
        var seen = new HashSet<CorpusKey>();

        foreach (var finding in findings)
        {
            var key = new CorpusKey(finding.RuleId, finding.Path, finding.Line, finding.Column);
            if (!seen.Add(key)) throw new ArgumentException("Duplicate finding keys cannot be scored.", nameof(findings));
            var accepted = expectedKeys.TryGetValue(key, out var triage) && triage is null or CorpusTriage.TruePositive;
            if (accepted)
            {
                matched += 1;
            }
            else
            {
                unexpected.Add(new CorpusDiff.UnexpectedFinding(
                    finding.RuleId, finding.Path, finding.Line, finding.Column));
            }
            switch (finding.Severity)
            {
                case Severity.Error:
                    if (accepted) errorTruePositives++; else errorFalsePositives++;
                    break;
                case Severity.Warning: warnings++; break;
                case Severity.Note: notes++; break;
                default: throw new ArgumentException("A finding severity is invalid.", nameof(findings));
            }
        }

        return new CorpusDiff(expected.Count(entry => entry.Triage is null or CorpusTriage.TruePositive), findings.Count, matched,
            unexpected.OrderBy(item => item.Path, StringComparer.Ordinal).ThenBy(item => item.Line).ThenBy(item => item.Column)
                .ThenBy(item => item.RuleId, StringComparer.Ordinal).ToArray(), errorTruePositives, errorFalsePositives, warnings, notes);
    }

    /// <summary>
    /// Builds one measurement per rule that either the expected list or the
    /// produced findings name, plus the overall totals, sorted by rule
    /// identifier ordinal (rule 6).
    /// </summary>
    /// <param name="runs">One pair per scan: the case's expected entries and the findings the scan produced.</param>
    public static CorpusRunMeasurements PerRule(IReadOnlyList<(CorpusCase Case, IReadOnlyList<Finding> Findings)> runs)
    {
        ArgumentNullException.ThrowIfNull(runs);

        var ids = runs.SelectMany(run => run.Case.Expected.Select(entry => entry.RuleId).Concat(run.Findings.Select(finding => finding.RuleId)))
            .Distinct(StringComparer.Ordinal).OrderBy(id => id, StringComparer.Ordinal);
        var measurements = ids.Select(id => Aggregate(id, runs.Select(run => Score(
            run.Case.Expected.Where(entry => entry.RuleId == id).ToArray(), run.Findings.Where(finding => finding.RuleId == id).ToArray())).ToArray())).ToArray();
        return new CorpusRunMeasurements(measurements, Aggregate(OverallRuleId, runs.Select(run => Score(run.Case.Expected, run.Findings)).ToArray()));
    }

    private static CorpusRuleMeasurement Aggregate(string id, IReadOnlyList<CorpusDiff> diffs) => new(id,
        diffs.Sum(diff => diff.ExpectedCount), diffs.Sum(diff => diff.ProducedCount), diffs.Sum(diff => diff.MatchedCount),
        diffs.SelectMany(diff => diff.Unexpected).OrderBy(item => item.Path, StringComparer.Ordinal).ThenBy(item => item.Line)
            .ThenBy(item => item.Column).ThenBy(item => item.RuleId, StringComparer.Ordinal).ToArray(),
        diffs.Sum(diff => diff.ErrorTruePositiveCount), diffs.Sum(diff => diff.ErrorFalsePositiveCount),
        diffs.Sum(diff => diff.WarningCount), diffs.Sum(diff => diff.NoteCount));

    /// <summary>
    /// Renders the per-rule report the harness hands to the test's output:
    /// expected, produced, matched, unexpected and the two rates. While the
    /// engine's matching stage is not in the build, recall is printed as
    /// <c>unmeasured</c> rather than zero, because a scan that cannot see any
    /// rule proves nothing about missed detections.
    /// </summary>
    /// <param name="writer">The writer the report goes to.</param>
    /// <param name="runMeasurements">The per-rule measurements and the overall totals.</param>
    /// <param name="matcherStagePresent">Whether the build carries the engine's matching stage.</param>
    public static void WriteReport(TextWriter writer, CorpusRunMeasurements runMeasurements, bool matcherStagePresent)
    {
        writer.WriteLine(ReportHeader);
        WriteRow(writer, runMeasurements.Overall, matcherStagePresent);
        writer.WriteLine(PerRuleSectionHeader);

        foreach (var measurement in runMeasurements.PerRule)
        {
            WriteRow(writer, measurement, matcherStagePresent);
        }
    }

    /// <summary>
    /// Renders the per-rule table as the Markdown rows the committed reports
    /// embed: the header row, a separator row sized to the columns, then the
    /// overall and per-rule rows in the report's own order, without the plain
    /// section headers the console report prints. The weekly report and the
    /// synthetic report share this one rendering, so the two artefacts cannot
    /// disagree about a count.
    /// </summary>
    /// <param name="runMeasurements">The per-rule measurements and the overall totals.</param>
    /// <param name="matcherStagePresent">Whether the build carries the engine's matching stage.</param>
    /// <returns>The Markdown table text, with newline-feed line endings.</returns>
    public static string WriteMarkdownTable(CorpusRunMeasurements runMeasurements, bool matcherStagePresent)
    {
        var table = new StringWriter { NewLine = "\n" };
        WriteReport(table, runMeasurements, matcherStagePresent);
        var lines = table.ToString().Split('\n').Where(line => line.Length > 0 && !line.StartsWith("--", StringComparison.Ordinal)).ToList();
        lines.Insert(1, string.Join(" | ", Enumerable.Repeat("---", lines[0].Split('|').Length)));
        return string.Join('\n', lines);
    }

    private static void WriteRow(TextWriter writer, CorpusRuleMeasurement measurement, bool matcherStagePresent)
    {
        var precision = measurement.ProducedCount > 0
            ? FormatRate(100d * measurement.MatchedCount / measurement.ProducedCount)
            : NotApplicableRate;

        string recall;
        if (measurement.ExpectedCount == 0)
        {
            recall = NotApplicableRate;
        }
        else if (!matcherStagePresent)
        {
            recall = UnmeasuredStatus;
        }
        else
        {
            recall = FormatRate(100d * measurement.MatchedCount / measurement.ExpectedCount);
        }

        var errorCount = (long)measurement.ErrorTruePositiveCount + measurement.ErrorFalsePositiveCount;
        var errorRate = errorCount == 0 ? FormatRate(0) : FormatRate(100d * measurement.ErrorFalsePositiveCount / errorCount);
        writer.WriteLine(string.Format(
            CultureInfo.InvariantCulture,
            ReportRowFormat,
            measurement.RuleId,
            measurement.ExpectedCount,
            measurement.ProducedCount,
            measurement.MatchedCount,
            measurement.Unexpected.Count,
            precision,
            recall, measurement.ErrorTruePositiveCount, measurement.ErrorFalsePositiveCount, measurement.WarningCount, measurement.NoteCount,
            !matcherStagePresent ? UnmeasuredStatus : measurement.FalsePositiveGatePassed ? PassingGateStatus : FailingGateStatus,
            matcherStagePresent ? errorRate : UnmeasuredStatus));
    }

    private static string FormatRate(double rate) => rate.ToString(RateFormat, CultureInfo.InvariantCulture);

    private readonly record struct CorpusKey(string RuleId, string Path, int Line, int Column);
}

/// <summary>
/// The outcome of one scored scan: how many entries the expected list fixed,
/// how many findings the scan produced, how many produced findings matched,
/// and which produced findings were unexpected.
/// </summary>
/// <param name="ExpectedCount">The number of expected entries.</param>
/// <param name="ProducedCount">The number of findings the scan produced.</param>
/// <param name="MatchedCount">The number of produced findings that also sit in the expected list.</param>
/// <param name="Unexpected">The produced findings no expected entry names, each with its location.</param>
/// <param name="ErrorTruePositiveCount">Accepted findings at error severity.</param>
/// <param name="ErrorFalsePositiveCount">Rejected, unresolved or unexpected findings at error severity.</param>
/// <param name="WarningCount">Findings at warning severity, outside the gate.</param>
/// <param name="NoteCount">Findings at note severity, outside the gate.</param>
public sealed record CorpusDiff(
    int ExpectedCount,
    int ProducedCount,
    int MatchedCount,
    IReadOnlyList<CorpusDiff.UnexpectedFinding> Unexpected,
    int ErrorTruePositiveCount = 0,
    int ErrorFalsePositiveCount = 0,
    int WarningCount = 0,
    int NoteCount = 0)
{
    /// <summary>Gets the number of produced findings that carry no match.</summary>
    public int UnexpectedCount => this.Unexpected.Count;

    /// <summary>
    /// One produced finding no expected entry names, reduced to the shape the
    /// report prints: the rule identifier and the location only, never the
    /// matched value (rule 10).
    /// </summary>
    /// <param name="RuleId">The rule identifier that produced the finding.</param>
    /// <param name="Path">The repository-relative path with forward slashes.</param>
    /// <param name="Line">The 1-based line.</param>
    /// <param name="Column">The 1-based column.</param>
    public sealed record UnexpectedFinding(string RuleId, string Path, int Line, int Column);
}

/// <summary>
/// One rule's measurement across a corpus run: the counts the report prints.
/// The rates are not stored; the report computes them from the counts, so the
/// printed form always matches (the quality-budgets specification, section 5.2).
/// </summary>
/// <param name="RuleId">The rule identifier, or <c>(overall)</c> for the total.</param>
/// <param name="ExpectedCount">The number of expected entries.</param>
/// <param name="ProducedCount">The number of findings produced.</param>
/// <param name="MatchedCount">The number of produced findings that matched an expected entry.</param>
/// <param name="Unexpected">The produced findings no expected entry names.</param>
/// <param name="ErrorTruePositiveCount">Accepted findings at error severity.</param>
/// <param name="ErrorFalsePositiveCount">Rejected, unresolved or unexpected findings at error severity.</param>
/// <param name="WarningCount">Findings at warning severity, outside the gate.</param>
/// <param name="NoteCount">Findings at note severity, outside the gate.</param>
public sealed record CorpusRuleMeasurement(
    string RuleId,
    int ExpectedCount,
    int ProducedCount,
    int MatchedCount,
    IReadOnlyList<CorpusDiff.UnexpectedFinding> Unexpected,
    int ErrorTruePositiveCount = 0,
    int ErrorFalsePositiveCount = 0,
    int WarningCount = 0,
    int NoteCount = 0)
{
    /// <summary>Gets the number of produced findings that carry no match.</summary>
    public int UnexpectedCount => this.Unexpected.Count;

    /// <summary>Gets whether the failing-finding rate is at most five per cent, using exact integer arithmetic.</summary>
    public bool FalsePositiveGatePassed => 20L * ErrorFalsePositiveCount <= (long)ErrorTruePositiveCount + ErrorFalsePositiveCount;
}

/// <summary>
/// The measurements of one corpus run: every rule that either side names, and
/// the overall totals the false-positive policy cares about.
/// </summary>
/// <param name="PerRule">The per-rule measurements, in rule-identifier order.</param>
/// <param name="Overall">The totals across all rules.</param>
public sealed record CorpusRunMeasurements(
    IReadOnlyList<CorpusRuleMeasurement> PerRule,
    CorpusRuleMeasurement Overall)
{
    /// <summary>Gets whether both every rule and the overall total meet the exact false-positive bound.</summary>
    public bool FalsePositiveGatePassed => Overall.FalsePositiveGatePassed && PerRule.All(rule => rule.FalsePositiveGatePassed);
}
