// SPDX-License-Identifier: Apache-2.0

using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace InvenioTech.GateKeeper.Engine;

/// <summary>One validated exception entry (configuration section 11).</summary>
/// <param name="Id">The unique entry identifier.</param>
/// <param name="Rule">The rule scope, or null for a category scope.</param>
/// <param name="Category">The category scope, or null for a rule scope.</param>
/// <param name="Paths">The compiled path globs, in file order.</param>
/// <param name="Expires">The last active civil date.</param>
/// <param name="Severity">An override, or null to suppress.</param>
/// <param name="EntryHash">The fixed-serialisation hash, never a hash of matched content.</param>
public sealed record ExceptionEntry(string Id, string? Rule, string? Category, IReadOnlyList<PathGlob> Paths, DateOnly Expires, Severity? Severity, string EntryHash);

/// <summary>An application of one exception, without source content.</summary>
/// <param name="Id">The exception identifier.</param>
/// <param name="EntryHash">The exception entry hash.</param>
/// <param name="RuleId">The finding rule.</param>
/// <param name="Path">The finding path.</param>
/// <param name="Line">The finding line.</param>
/// <param name="Column">The finding column.</param>
/// <param name="Outcome">Either suppressed or overridden.</param>
public sealed record AppliedException(string Id, string EntryHash, string RuleId, string Path, int Line, int Column, string Outcome);

/// <summary>Baseline debt that actually matched this scan.</summary>
/// <param name="MatchedCount">The number of findings held back.</param>
/// <param name="OldestAgeDays">The greatest matched age in civil days.</param>
public sealed record BaselineSummary(int MatchedCount, int OldestAgeDays);

/// <summary>Validated policy input. The key is secret and this object must never be serialised or logged.</summary>
/// <param name="Exceptions">Validated exception entries.</param>
/// <param name="Baseline">Fingerprints and their first accepted dates.</param>
/// <param name="Key">The secret key bytes, or null when absent.</param>
/// <param name="Warnings">Missing-key and baseline warnings.</param>
public sealed record PolicyState(IReadOnlyList<ExceptionEntry> Exceptions, IReadOnlyDictionary<string, DateOnly> Baseline, byte[]? Key, IReadOnlyList<ScanWarning> Warnings);

/// <summary>A scan after exceptions and baseline application; no source content or secret key is retained.</summary>
/// <param name="EvaluatedOn">The single date input used for expiry and age.</param>
/// <param name="Findings">Reported work-tree findings, including accepted debt.</param>
/// <param name="History">Reported history matches, never baselined.</param>
/// <param name="Exceptions">Every exception application.</param>
/// <param name="Baseline">The matched debt summary.</param>
/// <param name="Warnings">Sorted diagnostics.</param>
public sealed record PolicyResult(DateOnly EvaluatedOn, IReadOnlyList<Finding> Findings, IReadOnlyList<HistoryMatch> History,
    IReadOnlyList<AppliedException> Exceptions, BaselineSummary Baseline, IReadOnlyList<ScanWarning> Warnings);

/// <summary>Resolves the one evaluation date from explicit inputs or an injected clock (IQ-01).</summary>
public static class EvaluationDate
{
    /// <summary>The flag wins over the epoch; otherwise the injected UTC date applies. Invalid input is a tool error.</summary>
    public static DateOnly Resolve(TimeProvider clock, string? explicitDate, string? sourceDateEpoch)
    {
        if (explicitDate is not null) return Parse(explicitDate, "--evaluated-on");
        if (sourceDateEpoch is null) return DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
        if (long.TryParse(sourceDateEpoch, NumberStyles.None, CultureInfo.InvariantCulture, out var seconds) && seconds <= 253402300799)
            return DateOnly.FromDateTime(DateTimeOffset.FromUnixTimeSeconds(seconds).UtcDateTime);
        throw new EngineToolException("SOURCE_DATE_EPOCH is not a non-negative supported epoch. Set integer seconds or remove the variable.");
    }

    internal static DateOnly Parse(string value, string field) => DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
        ? date : throw new EngineToolException($"{field} is not a civil date. Use a real date in YYYY-MM-DD form.");
}

/// <summary>Computes keyed fingerprints using configuration section 13.2, without retaining matched values.</summary>
public static class FingerprintCalculator
{
    /// <summary>Hashes rule, normalised path, matched span and occurrence index as four LF-terminated UTF-8 lines.</summary>
    public static string Compute(byte[] key, string ruleId, string path, string span, int occurrence)
    {
        if (key.Length != 32 || occurrence < 0) throw new ArgumentException("A fingerprint needs a 32-byte key and a non-negative occurrence index.");
        path = path.Replace('\\', '/').Normalize(NormalizationForm.FormC);
        while (path.StartsWith("./", StringComparison.Ordinal)) path = path[2..];
        var message = $"{ruleId}\n{path}\n{span}\n{occurrence.ToString(CultureInfo.InvariantCulture)}\n";
        return Convert.ToHexStringLower(HMACSHA256.HashData(key, Encoding.UTF8.GetBytes(message)));
    }
}

/// <summary>Applies disabled rules, overrides, exceptions and baseline in specification order.</summary>
public static class FindingPolicy
{
    /// <summary>Applies policy to a completed scan using an explicit date. Ambiguous exceptions are tool errors.</summary>
    public static PolicyResult Apply(ScanMatchResult scan, ScanSettings settings, PolicyState state, DateOnly evaluatedOn, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var disabled = settings.DisabledRules.Select(r => r.Id).ToHashSet(StringComparer.Ordinal);
        var byRule = state.Exceptions.Where(e => e.Expires >= evaluatedOn && e.Rule is not null).ToLookup(e => e.Rule!, StringComparer.Ordinal);
        var byCategory = state.Exceptions.Where(e => e.Expires >= evaluatedOn && e.Category is not null).ToLookup(e => e.Category!, StringComparer.Ordinal);
        var findings = new List<Finding>();
        var history = new List<HistoryMatch>();
        var applications = new List<AppliedException>();
        var texts = scan.Enumeration.Files.ToDictionary(file => file.Path, file => file.Text, StringComparer.Ordinal);
        var occurrences = new Dictionary<(string Rule, string Path, string Span), int>();
        var matchedCount = 0;
        var oldestAge = 0;
        foreach (var match in scan.Findings.OrderBy(m => m.Path, StringComparer.Ordinal).ThenBy(m => m.Line).ThenBy(m => m.Column).ThenBy(m => m.RuleId, StringComparer.Ordinal))
        {
            var adjusted = Adjust(match);
            if (adjusted is null) continue;
            string? fingerprint = null;
            var baseline = BaselineState.New;
            if (state.Key is not null)
            {
                var span = texts[match.Path].Substring(match.SpanStart, match.SpanLength);
                var series = (match.RuleId, match.Path, span);
                occurrences.TryGetValue(series, out var index);
                occurrences[series] = index + 1;
                fingerprint = FingerprintCalculator.Compute(state.Key, match.RuleId, match.Path, span, index);
                if (state.Baseline.TryGetValue(fingerprint, out var addedOn))
                {
                    var age = evaluatedOn.DayNumber - addedOn.DayNumber;
                    if (settings.BaselineMaxAgeDays == 0 || age <= settings.BaselineMaxAgeDays)
                    {
                        baseline = BaselineState.Unchanged;
                        matchedCount++;
                        oldestAge = Math.Max(oldestAge, age);
                    }
                }
            }
            findings.Add(new Finding(adjusted.RuleId, adjusted.Severity, adjusted.Category, adjusted.Path, adjusted.Line, adjusted.Column,
                adjusted.LocationKind, fingerprint, baseline, adjusted.Citation)
            { Processor = adjusted.Processor });
        }
        foreach (var item in scan.History)
        {
            var adjusted = Adjust(item.Match);
            if (adjusted is not null) history.Add(new HistoryMatch(item.CommitId, adjusted));
        }
        return new PolicyResult(evaluatedOn, findings.AsReadOnly(), history.AsReadOnly(),
            applications.OrderBy(a => a.Id, StringComparer.Ordinal).ThenBy(a => a.Path, StringComparer.Ordinal).ThenBy(a => a.Line).ThenBy(a => a.Column).ThenBy(a => a.RuleId, StringComparer.Ordinal).ToArray(),
            new BaselineSummary(matchedCount, oldestAge), state.Warnings.Concat(scan.Enumeration.Warnings).OrderBy(w => w.Code, StringComparer.Ordinal).ThenBy(w => w.Message, StringComparer.Ordinal).ToArray());

        RuleMatch? Adjust(RuleMatch match)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (disabled.Contains(match.RuleId)) return null;
            var severity = settings.SeverityOverrides.GetValueOrDefault(match.RuleId, match.Severity);
            var entry = Winner(byRule[match.RuleId]) ?? Winner(byCategory[match.Category]);
            if (entry is null) return match with { Severity = severity };

            ExceptionEntry? Winner(IEnumerable<ExceptionEntry> candidates)
            {
                ExceptionEntry? winner = null;
                foreach (var candidate in candidates)
                {
                    var matches = false;
                    foreach (var path in candidate.Paths)
                        if (path.IsMatch(match.Path)) { matches = true; break; }
                    if (!matches) continue;
                    if (winner is not null)
                    {
                        var ids = new[] { winner.Id, candidate.Id };
                        Array.Sort(ids, StringComparer.Ordinal);
                        throw new EngineToolException($"exceptions.toml entries '{ids[0]}' and '{ids[1]}' both match. Narrow their scope or paths so only one applies.");
                    }
                    winner = candidate;
                }
                return winner;
            }
            applications.Add(new AppliedException(entry.Id, entry.EntryHash, match.RuleId, match.Path, match.Line, match.Column, entry.Severity is null ? "suppressed" : "overridden"));
            return entry.Severity is { } replacement ? match with { Severity = replacement } : null;
        }
    }
}
