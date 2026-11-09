// SPDX-License-Identifier: Apache-2.0
using System.Text;
using System.Text.RegularExpressions;
using InvenioTech.GateKeeper.Engine;

namespace InvenioTech.GateKeeper.Middleware;

/// <summary>Runtime context metadata; it never selects rules.</summary>
/// <param name="Surface">Exactly log, response or value.</param>
/// <param name="FieldName">An optional non-empty field name.</param>
public sealed record RuntimeContext(string Surface, string? FieldName = null);

/// <summary>A content-free runtime finding with zero-based scalar offsets.</summary>
/// <param name="RuleId">The selected rule identifier.</param>
/// <param name="Category">The rule's category.</param>
/// <param name="Severity">The pack severity; every severity is redacted.</param>
/// <param name="Start">The zero-based scalar start of the emitting span.</param>
/// <param name="Length">The scalar length of the emitting span.</param>
public sealed record RuntimeFinding(string RuleId, string Category, Severity Severity, int Start, int Length);

/// <summary>Raised when a middleware call exceeds the rule-pack scalar cap.</summary>
public sealed class InputLimitException : Exception
{
    /// <summary>Creates an error without retaining the input.</summary>
    public InputLimitException() : base("The middleware input or result exceeds 65,536 Unicode scalars. Split the value before processing it.") { }
}

/// <summary>The exact, verified metadata reported by the runtime.</summary>
/// <param name="AdapterVersion">The adapter code version.</param>
/// <param name="Packs">The loaded pack metadata, sorted by identifier.</param>
public sealed record RuntimeVersions(string AdapterVersion, IReadOnlyList<RuntimePackVersion> Packs);

/// <summary>Exact loaded-pack identity without application inputs.</summary>
/// <param name="Id">The pack identifier.</param>
/// <param name="Version">Its calendar version.</param>
/// <param name="ContentDigest">The SHA-256 of the verified content bytes.</param>
public sealed record RuntimePackVersion(string Id, string Version, string ContentDigest);

/// <summary>
/// The .NET classification and redaction core (P3-00/P3-01). It reuses the
/// existing matcher and accepts only a previously verified, non-local load.
/// Framework response and logging hooks remain separate adapters.
/// </summary>
public sealed class RuntimeMatcher
{
    /// <summary>The rule-pack section 7.5 limit, in Unicode scalar values.</summary>
    public const int MaximumScalars = 65_536;
    private static readonly Regex ReservedMask = new(
        @"\[REDACTED:[a-z][a-z0-9]*(?:\.[a-z][a-z0-9]*)*\]",
        RegexOptions.NonBacktracking | RegexOptions.CultureInvariant);
    private readonly MatcherEngine matcher;
    private readonly RuntimeVersions versions;

    /// <summary>Prepares already verified rule data without scanner policy inputs.</summary>
    /// <param name="verified">A successful signed-only load from the existing pack loader.</param>
    /// <param name="disabled">Only pack-marked optional identifiers may be disabled.</param>
    /// <param name="diagnostic">Receives one metadata-only start-up event for disabled rules.</param>
    /// <param name="shapes">The existing cache seam; semantic tests may disable its deadline.</param>
    /// <exception cref="ArgumentException">The input is empty, local or requests invalid disablement.</exception>
    public RuntimeMatcher(MergedRules verified, IReadOnlyList<string>? disabled = null,
        Action<string>? diagnostic = null, ShapeRegexCache? shapes = null)
    {
        ArgumentNullException.ThrowIfNull(verified);
        if (verified.Packs.Count == 0 || verified.Rules.Count == 0
            || verified.Packs.Any(pack => pack.Provenance == PackProvenance.Local))
            throw new ArgumentException("Middleware requires a non-empty verified signed pack load.", nameof(verified));
        var ids = verified.Rules.Select(item => item.Rule.Id).ToHashSet(StringComparer.Ordinal);
        if (ids.Count != verified.Rules.Count) throw new ArgumentException("Duplicate runtime rule identifiers are invalid.", nameof(verified));
        var off = (disabled ?? []).ToHashSet(StringComparer.Ordinal);
        foreach (var id in off)
            if (!verified.Rules.Any(item => item.Rule.Id == id && item.Rule.Redaction?.Optional == true))
                throw new ArgumentException("Only a loaded optional rule may be disabled.", nameof(disabled));
        if (off.Count > 0)
        {
            ArgumentNullException.ThrowIfNull(diagnostic);
            diagnostic("Disabled optional redaction rules: " + string.Join(", ", off.Order(StringComparer.Ordinal)));
        }
        var rules = verified.Rules.Where(item => !off.Contains(item.Rule.Id)).Select(item =>
        {
            var rule = item.Rule;
            return rule.Matcher.Pattern is { Location: { } location } pattern
                ? rule with { Matcher = rule.Matcher with { Pattern = pattern with { Location = location with { Paths = null } } } }
                : rule;
        }).ToArray();
        matcher = new MatcherEngine(rules, shapes: shapes);
        versions = new RuntimeVersions(RuntimeBuildVersion.Value, Array.AsReadOnly(verified.Packs.OrderBy(pack => pack.Id, StringComparer.Ordinal)
            .Select(pack => new RuntimePackVersion(pack.Id, pack.Version, pack.ContentDigest)).ToArray()));
    }

    /// <summary>Reports immutable versions without application inputs or side effects.</summary>
    public RuntimeVersions Versions() => versions;

    /// <summary>Returns every rule's own matches with scalar offsets and no matched text.</summary>
    /// <param name="text">Valid Unicode text within the scalar cap.</param>
    public IReadOnlyList<RuntimeFinding> Matches(string text)
    {
        ValidateText(text);
        var scalarOffsets = new int[text.Length + 1];
        var scalars = 0;
        for (var at = 0; at < text.Length; at++)
        {
            scalarOffsets[at] = scalars;
            if (char.IsHighSurrogate(text[at])) scalarOffsets[++at] = scalars;
            scalarOffsets[at + 1] = ++scalars;
        }
        return NativeMatches(text).Select(match => new RuntimeFinding(match.RuleId, match.Category, match.Severity,
            scalarOffsets[match.SpanStart],
            scalarOffsets[match.SpanStart + match.SpanLength] - scalarOffsets[match.SpanStart])).ToArray();
    }

    /// <summary>Selects earliest, longest, then ordinal rule, without filtering by context.</summary>
    /// <param name="text">The runtime string; empty text has no classification.</param>
    /// <param name="context">Validated descriptive context, not scan policy.</param>
    public RuntimeFinding? Classify(string text, RuntimeContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (context.Surface is not ("log" or "response" or "value") || context.FieldName is { Length: 0 })
            throw new ArgumentException("Use log, response or value and a non-empty optional field name.", nameof(context));
        return Matches(text).OrderBy(item => item.Start).ThenByDescending(item => item.Length)
            .ThenBy(item => item.RuleId, StringComparer.Ordinal).FirstOrDefault();
    }

    /// <summary>Replaces only emitted spans; literal masks are reserved and remain unchanged.</summary>
    /// <param name="text">The input; no context or scanner policy is accepted.</param>
    public string Redact(string text)
    {
        ValidateText(text);
        var output = new StringBuilder(text.Length);
        var cursor = 0;
        var size = 0;
        foreach (var match in NativeMatches(text).OrderBy(item => item.SpanStart)
            .ThenByDescending(item => item.SpanLength).ThenBy(item => item.RuleId, StringComparer.Ordinal))
        {
            if (match.SpanStart < cursor) continue;
            size += CountScalars(text.AsSpan(cursor, match.SpanStart - cursor)) + 11 + match.RuleId.Length;
            if (size > MaximumScalars) throw new InputLimitException();
            output.Append(text, cursor, match.SpanStart - cursor);
            output.Append("[REDACTED:").Append(match.RuleId).Append(']');
            cursor = match.SpanStart + match.SpanLength;
        }
        size += CountScalars(text.AsSpan(cursor));
        if (size > MaximumScalars) throw new InputLimitException();
        output.Append(text, cursor, text.Length - cursor);
        var result = output.ToString();
        ValidateText(result);
        return result;
    }

    private IReadOnlyList<RuleMatch> NativeMatches(string text)
    {
        var matches = new List<RuleMatch>();
        var cursor = 0;
        foreach (Match mask in ReservedMask.Matches(text))
        {
            Add(cursor, mask.Index - cursor);
            cursor = mask.Index + mask.Length;
        }
        Add(cursor, text.Length - cursor);
        return matches;

        void Add(int start, int length)
        {
            if (length == 0) return;
            foreach (var match in matcher.Match("runtime/value", text.Substring(start, length)))
                matches.Add(match with { SpanStart = match.SpanStart + start });
        }
    }

    private static int CountScalars(ReadOnlySpan<char> text)
    {
        var count = 0;
        foreach (var rune in text.EnumerateRunes()) count++;
        return count;
    }

    internal static void ValidateText(string text, string parameterName = "text")
    {
        ArgumentNullException.ThrowIfNull(text);
        var count = 0;
        for (var index = 0; index < text.Length; index++)
        {
            if (char.IsHighSurrogate(text[index]))
            {
                if (++index >= text.Length || !char.IsLowSurrogate(text[index]))
                    throw new ArgumentException("Middleware text must contain valid Unicode scalars.", parameterName);
            }
            else if (char.IsLowSurrogate(text[index]))
                throw new ArgumentException("Middleware text must contain valid Unicode scalars.", parameterName);
            if (++count > MaximumScalars) throw new InputLimitException();
        }
    }
}
