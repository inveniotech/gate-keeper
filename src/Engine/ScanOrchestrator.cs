// SPDX-License-Identifier: Apache-2.0

namespace InvenioTech.GateKeeper.Engine;

/// <summary>Runs prepared matchers over enumerated files with processor-bounded workers (P1-07).</summary>
/// <param name="matcher">The prepared, read-only matcher shared by the workers.</param>
public sealed class ScanOrchestrator(MatcherEngine matcher)
{
    private readonly MatcherEngine matcher = matcher ?? throw new ArgumentNullException(nameof(matcher));
#if GATEKEEPER_BENCHMARK
    private readonly TimeProvider measurementClock = TimeProvider.System;

    /// <summary>Creates the benchmark-only orchestrator with an injected monotonic clock.</summary>
    public ScanOrchestrator(MatcherEngine matcher, TimeProvider clock) : this(matcher)
    {
        measurementClock = clock ?? throw new ArgumentNullException(nameof(clock));
    }
#endif

    /// <summary>Enumerates the repository and matches its work tree and requested history.</summary>
    /// <param name="options">The existing file selection and decoding options.</param>
    /// <param name="cancellationToken">Cancels enumeration and matching without returning partial results.</param>
    /// <returns>Sorted matches and the enumeration diagnostics.</returns>
    /// <exception cref="EngineToolException">Enumeration or matching failed; the caller must report a tool error.</exception>
    public async Task<ScanMatchResult> ScanAsync(FileEnumerationOptions options, CancellationToken cancellationToken = default)
    {
        var input = await new FileEnumerator().EnumerateAsync(options, cancellationToken).ConfigureAwait(false);
        return await ScanAsync(input, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Matches an enumeration, including one supplied in shuffled order, before returning any results.</summary>
    /// <param name="input">The decoded files, history and diagnostics. The caller must not mutate these during a scan.</param>
    /// <param name="cancellationToken">Cancels matching without returning partial results.</param>
    /// <returns>Work-tree matches sorted by ordinal path, line, column and rule identifier; history remains separate.</returns>
    /// <exception cref="EngineToolException">A match exceeded the configured timeout.</exception>
    public async Task<ScanMatchResult> ScanAsync(FileEnumerationResult input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        cancellationToken.ThrowIfCancellationRequested();
#if GATEKEEPER_BENCHMARK
        var measured = await BoundedWorkers.MapAsync(input.Files, (file, token) =>
        {
            var counters = new BenchmarkRegexCounters();
            var allocated = GC.GetAllocatedBytesForCurrentThread();
            var started = measurementClock.GetTimestamp();
            var matches = this.matcher.Match(file.Path, file.Text, token, counters);
            var elapsed = measurementClock.GetElapsedTime(started).TotalMilliseconds;
            var allocationBytes = GC.GetAllocatedBytesForCurrentThread() - allocated;
            return Task.FromResult((Matches: matches, Metric: new BenchmarkFileMetric(elapsed, allocationBytes, counters.Values)));
        }, cancellationToken).ConfigureAwait(false);
        var batches = measured.Select(b => b.Matches);
#else
        var batches = await BoundedWorkers.MapAsync(input.Files, (file, token) =>
            Task.FromResult(this.matcher.Match(file.Path, file.Text, token)), cancellationToken).ConfigureAwait(false);
#endif
        var findings = new List<RuleMatch>();
        foreach (var batch in batches)
        {
            findings.AddRange(batch);
        }

        findings.Sort(Compare);
        var historyBatches = await BoundedWorkers.MapAsync(input.History, (file, token) =>
        {
            var matches = this.matcher.Match(file.Path, file.Text, token);
            var results = new List<HistoryMatch>(matches.Count);
            foreach (var match in matches)
            {
                results.Add(new HistoryMatch(file.CommitId, match));
            }

            return Task.FromResult(results);
        }, cancellationToken).ConfigureAwait(false);
        var history = new List<HistoryMatch>();
        foreach (var batch in historyBatches)
        {
            history.AddRange(batch);
        }

        history.Sort(static (left, right) =>
        {
            var order = string.CompareOrdinal(left.CommitId, right.CommitId);
            if (order != 0) return order;
            order = string.CompareOrdinal(left.Match.Path, right.Match.Path);
            if (order != 0) return order;
            order = left.Match.Line.CompareTo(right.Match.Line);
            if (order != 0) return order;
            order = string.CompareOrdinal(left.Match.RuleId, right.Match.RuleId);
            return order != 0 ? order : left.Match.Column.CompareTo(right.Match.Column);
        });
        cancellationToken.ThrowIfCancellationRequested();
        return new ScanMatchResult(findings.AsReadOnly(), history.AsReadOnly(), input)
#if GATEKEEPER_BENCHMARK
        { Measurements = measured.Select(b => b.Metric).ToArray() }
#endif
        ;
    }

    private static int Compare(RuleMatch left, RuleMatch right)
    {
        var order = string.CompareOrdinal(left.Path, right.Path);
        if (order != 0) return order;
        order = left.Line.CompareTo(right.Line);
        if (order != 0) return order;
        order = left.Column.CompareTo(right.Column);
        if (order != 0) return order;
        order = string.CompareOrdinal(left.RuleId, right.RuleId);
        return order != 0 ? order : left.SpanLength.CompareTo(right.SpanLength);
    }
}

/// <summary>Pre-policy matches; fingerprinting, baseline policy and output formatting run in later stages.</summary>
/// <param name="Findings">Sorted work-tree matches without matched values.</param>
/// <param name="History">History matches sorted by commit identifier, path, line, rule identifier and column (results format section 5.4).</param>
/// <param name="Enumeration">The original enumeration, including warnings and skip notes; never serialise its source text.</param>
public sealed record ScanMatchResult(IReadOnlyList<RuleMatch> Findings, IReadOnlyList<HistoryMatch> History, FileEnumerationResult Enumeration)
{
#if GATEKEEPER_BENCHMARK
    /// <summary>Anonymous matching-stage measurements available only in instrumented benchmark builds.</summary>
    public IReadOnlyList<BenchmarkFileMetric> Measurements { get; init; } = [];
#endif
}

/// <summary>A history match has no fingerprint or baseline state (IQ-83).</summary>
/// <param name="CommitId">The commit identifier supplied by the enumerator.</param>
/// <param name="Match">The location and rule, without the matched value.</param>
public sealed record HistoryMatch(string CommitId, RuleMatch Match);
