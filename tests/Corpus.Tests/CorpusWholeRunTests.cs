// SPDX-License-Identifier: Apache-2.0

using System.Diagnostics;
using InvenioTech.GateKeeper.Engine;
using Xunit.Abstractions;

namespace InvenioTech.GateKeeper.Corpus.Tests;

/// <summary>
/// The whole-corpus run: the harness scans every case once, prints the
/// per-rule report the false-positive policy measures with, and keeps the
/// one rule that holds in every build: no finding appears that a case does
/// not declare. The run's wall time goes to the test output, where the
/// handbook records it; a test never asserts a timing (rule 26).
/// </summary>
public sealed class CorpusWholeRunTests(ITestOutputHelper output)
{
    /// <summary>
    /// Gets whether this harness ran the matcher over a rule pack. The matcher
    /// engine is in the build (P1-06); the scan orchestrator (P1-07) and the
    /// cited pack are not, so the report marks recall unmeasured and no test
    /// claims a measurement this build cannot take (rule 16).
    /// </summary>
    private const bool MatcherStagePresent = false;

    [Fact]
    public async Task Corpus_Should_PrintThePerRuleReport_When_TheHarnessRunsEveryCase()
    {
        var cases = CorpusCase.LoadAll(CorpusPaths.Root);
        var runs = new List<(CorpusCase Case, IReadOnlyList<Finding> Findings)>();
        var stopwatch = Stopwatch.StartNew();

        try
        {
            foreach (var item in cases)
            {
                var runner = new CorpusScanRunner(item);
                var scan = await runner.RunAsync().ConfigureAwait(true);

                runs.Add((item, scan.Findings));
                new CorpusScanScavenger(scan).Dispose();
            }
        }
        finally
        {
            stopwatch.Stop();
        }

        var runMeasurements = CorpusMeasurements.PerRule(runs);

        // The only verdict this build can take: nothing the engine produced
        // escapes a case's declared expectations. When the matcher stage
        // lands, the same assertion carries the near-miss protection.
        runMeasurements.Overall.UnexpectedCount.Should().Be(
            0,
            because: "every produced finding must sit in the case's expected list");

        var report = new StringWriter();
        CorpusMeasurements.WriteReport(report, runMeasurements, MatcherStagePresent);
        output.WriteLine($"harness scan wall time: {stopwatch.ElapsedMilliseconds} ms across {cases.Count} cases");
        output.WriteLine(report.ToString());
    }
}
