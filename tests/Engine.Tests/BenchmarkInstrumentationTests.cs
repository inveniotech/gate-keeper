// SPDX-License-Identifier: Apache-2.0
#if GATEKEEPER_BENCHMARK
using System.Text.Json;
using InvenioTech.GateKeeper.Engine;

namespace InvenioTech.GateKeeper.Engine.Tests;

public sealed class BenchmarkInstrumentationTests
{
    [Fact]
    public void Matcher_Should_CountEveryRegexSearchWithoutChangingFindings_When_InstrumentationIsEnabled()
    {
        var rule = TestRule();
        var matcher = new MatcherEngine([rule], shapes: new ShapeRegexCache(Timeout.InfiniteTimeSpan));
        var counters = new BenchmarkRegexCounters();
        var measured = matcher.Match("synthetic.txt", "357", default, counters);
        var normal = matcher.Match("synthetic.txt", "357");
        measured.Should().Equal(normal);
        counters.Values.Should().Equal(new BenchmarkRegexMetric("local.benchmark", 2, 6));
    }

    [Fact]
    public async Task Orchestrator_Should_RecordOneAnonymousMeasurementPerFile_When_UsingTheInstrumentedBuild()
    {
        var scan = await new ScanOrchestrator(new MatcherEngine([TestRule()], shapes: new ShapeRegexCache(Timeout.InfiniteTimeSpan)))
            .ScanAsync(new FileEnumerationResult([new("a.txt", "357"), new("b.txt", "abc")], [], [], [], false));
        scan.Measurements.Count.Should().Be(2);
        scan.Measurements.SelectMany(m => m.Regex).Sum(m => m.Calls).Should().Be(3);
        using var output = JsonDocument.Parse(BenchmarkMetrics.Write(scan.Measurements));
        output.RootElement.GetProperty("fileCount").GetInt32().Should().Be(2);
        output.RootElement.GetProperty("regex")[0].GetProperty("inputBytes").GetInt64().Should().Be(12);
        output.RootElement.GetRawText().Should().NotContain("a.txt").And.NotContain("b.txt").And.NotContain("357");
    }

    [Fact]
    public void ReferencePack_Should_ExerciseRegexAndValidationWithoutFindings_When_TheGeneratedTextIsAlphabetic()
    {
        var bytes = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "benchmark", "reference.pack.json"));
        var pack = PackLoader.Load([], [new("reference.pack.json", bytes)], PackTestSupport.Catalogue, BundledPacksOnlyPolicy.Instance);
        var matcher = new MatcherEngine(pack.Rules.Select(r => r.Rule).ToArray(), shapes: new ShapeRegexCache(Timeout.InfiniteTimeSpan));
        var counters = new BenchmarkRegexCounters();
        matcher.Match("synthetic.txt", new string('a', 32), default, counters).Should().BeEmpty();
        counters.Values.Should().Equal(new BenchmarkRegexMetric("local.benchmark.tokens", 2, 126));
    }

    [Fact]
    public async Task Orchestrator_Should_UseTheInjectedTimestampClock_When_MeasuringAFile()
    {
        var scan = await new ScanOrchestrator(new MatcherEngine([TestRule()], shapes: new ShapeRegexCache(Timeout.InfiniteTimeSpan)), new StepClock())
            .ScanAsync(new FileEnumerationResult([new("synthetic.txt", "357")], [], [], [], false));
        scan.Measurements.Single().Milliseconds.Should().Be(1);
    }

    private sealed class StepClock : TimeProvider
    {
        private long ticks;
        public override long TimestampFrequency => 1000;
        public override long GetTimestamp() => Interlocked.Increment(ref ticks);
    }

    private static Rule TestRule() => new("local.benchmark", Severity.Error, "synthetic", new("Synthetic benchmark citation.", null, null), "1", null, null, null, null,
        new(new RulePatternMatcher([new(3, 3, null, ShapeClass.Digit, null, null)], MatchBoundary.Digit, null, null, null, null, null, null, null), null));
}
#endif
