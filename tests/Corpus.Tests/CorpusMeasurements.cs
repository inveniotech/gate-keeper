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

        var expectedKeys = expected
            .Select(entry => new CorpusKey(entry.RuleId, entry.Path, entry.Line, entry.Column))
            .ToHashSet();

        var unexpected = new List<CorpusDiff.UnexpectedFinding>();
        var matched = 0;

        foreach (var finding in findings)
        {
            if (expectedKeys.Contains(new CorpusKey(finding.RuleId, finding.Path, finding.Line, finding.Column)))
            {
                matched += 1;
            }
            else
            {
                unexpected.Add(new CorpusDiff.UnexpectedFinding(
                    finding.RuleId, finding.Path, finding.Line, finding.Column));
            }
        }

        return new CorpusDiff(expected.Count, findings.Count, matched, unexpected);
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

        var perRule = new Dictionary<string, (int Expected, int Produced, int Matched, List<CorpusDiff.UnexpectedFinding> Unexpected)>(StringComparer.Ordinal);

        foreach (var run in runs)
        {
            var expectedKeys = run.Case.Expected
                .Select(entry => new CorpusKey(entry.RuleId, entry.Path, entry.Line, entry.Column))
                .ToHashSet();

            foreach (var entry in run.Case.Expected)
            {
                var slot = GetSlot(perRule, entry.RuleId);
                perRule[entry.RuleId] = (slot.Expected + 1, slot.Produced, slot.Matched, slot.Unexpected);
            }

            foreach (var finding in run.Findings)
            {
                var slot = GetSlot(perRule, finding.RuleId);

                if (expectedKeys.Contains(new CorpusKey(finding.RuleId, finding.Path, finding.Line, finding.Column)))
                {
                    perRule[finding.RuleId] = (slot.Expected, slot.Produced + 1, slot.Matched + 1, slot.Unexpected);
                }
                else
                {
                    slot.Unexpected.Add(new CorpusDiff.UnexpectedFinding(
                        finding.RuleId, finding.Path, finding.Line, finding.Column));
                    perRule[finding.RuleId] = (slot.Expected, slot.Produced + 1, slot.Matched, slot.Unexpected);
                }
            }
        }

        var measurements = perRule
            .Select(pair => new CorpusRuleMeasurement(
                pair.Key,
                pair.Value.Expected,
                pair.Value.Produced,
                pair.Value.Matched,
                pair.Value.Unexpected))
            .OrderBy(measurement => measurement.RuleId, StringComparer.Ordinal)
            .ToArray();

        var overall = new CorpusRuleMeasurement(
            "(overall)",
            measurements.Sum(measurement => measurement.ExpectedCount),
            measurements.Sum(measurement => measurement.ProducedCount),
            measurements.Sum(measurement => measurement.MatchedCount),
            [.. measurements.SelectMany(measurement => measurement.Unexpected)]);

        return new CorpusRunMeasurements(measurements, overall);
    }

    private static (int Expected, int Produced, int Matched, List<CorpusDiff.UnexpectedFinding> Unexpected) GetSlot(
        Dictionary<string, (int Expected, int Produced, int Matched, List<CorpusDiff.UnexpectedFinding> Unexpected)> perRule,
        string ruleId)
    {
        return perRule.TryGetValue(ruleId, out var slot)
            ? slot
            : (0, 0, 0, []);
    }

    /// <summary>
    /// Prints the per-rule report the harness hands to the test's output:
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
        writer.WriteLine("rule id | expected | produced | matched | unexpected | precision | recall");
        WriteRow(writer, runMeasurements.Overall, matcherStagePresent);
        writer.WriteLine("-- per rule, in rule-identifier order --");

        foreach (var measurement in runMeasurements.PerRule)
        {
            WriteRow(writer, measurement, matcherStagePresent);
        }
    }

    private static void WriteRow(TextWriter writer, CorpusRuleMeasurement measurement, bool matcherStagePresent)
    {
        var precision = measurement.ProducedCount > 0
            ? FormatRate(100d * measurement.MatchedCount / measurement.ProducedCount)
            : "n/a";

        string recall;
        if (measurement.ExpectedCount == 0)
        {
            recall = "n/a";
        }
        else if (!matcherStagePresent)
        {
            recall = "unmeasured";
        }
        else
        {
            recall = FormatRate(100d * measurement.MatchedCount / measurement.ExpectedCount);
        }

        writer.WriteLine(string.Format(
            CultureInfo.InvariantCulture,
            "{0} | {1} | {2} | {3} | {4} | {5} | {6}",
            measurement.RuleId,
            measurement.ExpectedCount,
            measurement.ProducedCount,
            measurement.MatchedCount,
            measurement.Unexpected.Count,
            precision,
            recall));
    }

    private static string FormatRate(double rate) => rate.ToString("0.#", CultureInfo.InvariantCulture);

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
public sealed record CorpusDiff(
    int ExpectedCount,
    int ProducedCount,
    int MatchedCount,
    IReadOnlyList<CorpusDiff.UnexpectedFinding> Unexpected)
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
public sealed record CorpusRuleMeasurement(
    string RuleId,
    int ExpectedCount,
    int ProducedCount,
    int MatchedCount,
    IReadOnlyList<CorpusDiff.UnexpectedFinding> Unexpected)
{
    /// <summary>Gets the number of produced findings that carry no match.</summary>
    public int UnexpectedCount => this.Unexpected.Count;
}

/// <summary>
/// The measurements of one corpus run: every rule that either side names, and
/// the overall totals the false-positive policy cares about.
/// </summary>
/// <param name="PerRule">The per-rule measurements, in rule-identifier order.</param>
/// <param name="Overall">The totals across all rules.</param>
public sealed record CorpusRunMeasurements(
    IReadOnlyList<CorpusRuleMeasurement> PerRule,
    CorpusRuleMeasurement Overall);
