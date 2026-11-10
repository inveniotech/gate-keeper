// SPDX-License-Identifier: Apache-2.0

using System.Text.RegularExpressions;

namespace InvenioTech.GateKeeper.Engine;

/// <summary>
/// Runs the rules a pack load produced (P1-06, IQ-12, IQ-14). Each shape is
/// compiled once through a <see cref="ShapeRegexCache"/>, then a file is
/// searched with the boundary, the keyword window, the named validator and
/// the exclusions. A list rule is an exact token match against its package
/// and hostname entries.
/// </summary>
/// <remarks>
/// <para>
/// The resume rule (section 7.5) is: a rejected match resumes one scalar
/// value after its start, and an accepted match resumes at its end. That is
/// what stops a longer digit run from hiding a shorter value, and what stops
/// an accepted value from being reported twice on overlapping alignments.
/// </para>
/// <para>
/// Keywords are the literal prefilter (section 7.4). When a rule carries
/// them, the shape runs only if the file contains one, and a match counts
/// only when a keyword sits within <c>window</c> scalar values of the shape.
/// The gap is the scalar values between the keyword and the shape, on either
/// side, so a label after the value counts as well as a label before it. The
/// keyword is a substring: <c>bvn</c> inside <c>customerBvn</c> counts.
/// Comparison folds ASCII A–Z only (section 7.2).
/// </para>
/// <para>
/// <c>location.paths</c> limits which repository-relative paths a rule sees.
/// <c>location.kind</c> is the kind stamped on the match; a rule that names
/// none reports <see cref="LocationKind.Source"/>. The engine does not decide
/// that a line is a log call. That heuristic is pack data (P2-04).
/// </para>
/// <para>
/// <c>suppressionHints</c> are copied onto the match as help text. The engine
/// never reads them as an instruction (section 4).
/// </para>
/// <para>
/// A list entry matches only as a whole token. A token is a maximal run of
/// ASCII letters, digits, <c>.</c>, <c>-</c>, <c>_</c> and <c>+</c>. A shorter
/// entry therefore does not match inside a longer package name or hostname
/// (section 5.2). Comparison is ordinal: the entries are exact pack data.
/// </para>
/// </remarks>
public sealed class MatcherEngine
{
    private const int MaxWindowScalarValues = 4_096;

    private readonly PreparedRule[] _rules;

    /// <summary>
    /// Compiles the rules. A shape that breaks section 7.1, a keyword list
    /// with no window, a validator the registry does not carry, or a matcher
    /// that is neither one pattern nor one list fails here, before any file
    /// is scanned.
    /// </summary>
    /// <param name="rules">The rules to run, in the order the caller supplies. Results are sorted afterwards.</param>
    /// <param name="validators">The validator registry, or the default closed set when null.</param>
    /// <param name="shapes">The shape cache, or a cache with the default match timeout when null.</param>
    /// <exception cref="EngineToolException">A rule cannot be compiled.</exception>
    public MatcherEngine(
        IReadOnlyList<Rule> rules,
        RuleValidatorRegistry? validators = null,
        ShapeRegexCache? shapes = null)
    {
        ArgumentNullException.ThrowIfNull(rules);
        validators ??= RuleValidatorRegistry.Default;
        shapes ??= new ShapeRegexCache();

        var prepared = new PreparedRule[rules.Count];

        for (var index = 0; index < rules.Count; index++)
        {
            prepared[index] = Prepare(rules[index], validators, shapes);
        }

        _rules = prepared;
    }

    /// <summary>
    /// Matches every prepared rule against one file.
    /// </summary>
    /// <param name="relativePath">The repository-relative path with forward slashes.</param>
    /// <param name="text">The decoded text of the file. The span indexes on each match point into this string.</param>
    /// <param name="cancellationToken">The token that cancels a long file.</param>
    /// <returns>The accepted matches, sorted by line, column, rule identifier and span length.</returns>
    /// <exception cref="ArgumentException">The path is empty or whitespace.</exception>
    /// <exception cref="ArgumentNullException">The text is null.</exception>
    /// <exception cref="EngineToolException">A match exceeded its match timeout.</exception>
    public IReadOnlyList<RuleMatch> Match(string relativePath, string text, CancellationToken cancellationToken = default)
        => MatchCore(relativePath, text, cancellationToken
#if GATEKEEPER_BENCHMARK
            , null
#endif
            );

#if GATEKEEPER_BENCHMARK
    /// <summary>Matches a file while recording search counters in the benchmark-only build.</summary>
    public IReadOnlyList<RuleMatch> Match(string relativePath, string text, CancellationToken cancellationToken, BenchmarkRegexCounters measurements)
        => MatchCore(relativePath, text, cancellationToken, measurements);
#endif

    private IReadOnlyList<RuleMatch> MatchCore(string relativePath, string text, CancellationToken cancellationToken
#if GATEKEEPER_BENCHMARK
        , BenchmarkRegexCounters? measurements
#endif
        )
    {
        if (string.IsNullOrWhiteSpace(relativePath))
        {
            throw new ArgumentException(
                "A match needs the repository-relative path, with forward slashes.",
                nameof(relativePath));
        }

        ArgumentNullException.ThrowIfNull(text);

        var matches = new List<RuleMatch>();

        foreach (var prepared in _rules)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!prepared.PathAllows(relativePath))
            {
                continue;
            }

            if (prepared.Shape is not null)
            {
                MatchPattern(prepared, relativePath, text, matches, cancellationToken
#if GATEKEEPER_BENCHMARK
                    , measurements
#endif
                    );
            }
            else
            {
                MatchList(prepared, relativePath, text, matches);
            }
        }

        matches.Sort(CompareMatches);
        return Deduplicate(matches);
    }

    private static PreparedRule Prepare(Rule rule, RuleValidatorRegistry validators, ShapeRegexCache shapes)
    {
        var matcher = rule.Matcher;

        if ((matcher.Pattern is null) == (matcher.List is null))
        {
            throw new EngineToolException(
                $"The rule '{rule.Id}' does not carry exactly one matcher. A rule has a pattern or a list, and never " +
                "both (section 5).");
        }

        if (matcher.List is not null)
        {
            return new PreparedRule(rule, shape: null, validator: null, paths: null, foldedKeywords: null, window: 0);
        }

        var pattern = matcher.Pattern!;
        IRuleValidator? validator = null;

        if (pattern.Validator is { } parameters)
        {
            if (!validators.TryGet(parameters.Name, out validator) || validator is null)
            {
                throw new EngineToolException(
                    $"The rule '{rule.Id}' names the validator '{RuleValidatorRegistry.PackName(parameters.Name)}', and this engine's registry does " +
                    "not carry it. A missing validator fails at start-up rather than skipping the rule (section 8.1).");
            }
        }

        string[]? foldedKeywords = null;
        var window = 0;

        if (pattern.Keywords is { Count: > 0 } keywords)
        {
            if (pattern.Window is not int reach || reach is < 1 or > MaxWindowScalarValues)
            {
                throw new EngineToolException(
                    $"The rule '{rule.Id}' sets keywords and does not set window to a value from 1 to " +
                    $"{MaxWindowScalarValues}. The prefilter's reach is the window, in scalar values (section 7.4).");
            }

            window = reach;
            foldedKeywords = new string[keywords.Count];

            for (var index = 0; index < keywords.Count; index++)
            {
                foldedKeywords[index] = FoldAscii(keywords[index]);
            }
        }

        PathGlob[]? paths = null;

        if (pattern.Location?.Paths is { Count: > 0 } locationPaths)
        {
            paths = new PathGlob[locationPaths.Count];

            for (var index = 0; index < locationPaths.Count; index++)
            {
                try
                {
                    paths[index] = PathGlob.Compile(locationPaths[index]);
                }
                catch (EngineToolException exception)
                {
                    throw new EngineToolException(
                        $"The rule '{rule.Id}' sets the location path '{locationPaths[index]}', which the path-glob " +
                        $"subset rejects. {exception.Message}");
                }
            }
        }

        var shape = shapes.GetOrCompile(pattern.Shape, rule.Id);
        return new PreparedRule(rule, shape, validator, paths, foldedKeywords, window);
    }

    private static void MatchPattern(
        PreparedRule prepared,
        string relativePath,
        string text,
        List<RuleMatch> matches,
        CancellationToken cancellationToken
#if GATEKEEPER_BENCHMARK
        , BenchmarkRegexCounters? measurements
#endif
        )
    {
        if (prepared.FoldedKeywords is not null && !ContainsKeyword(text, prepared.FoldedKeywords))
        {
            return;
        }

        var pattern = prepared.Rule.Matcher.Pattern!;
        var regex = prepared.Shape!.Regex;
        var searchFrom = 0;

        while (searchFrom <= text.Length)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Match match;

            try
            {
#if GATEKEEPER_BENCHMARK
                measurements?.Record(prepared.Rule.Id, (text.Length - searchFrom) * 2L);
#endif
                match = regex.Match(text, searchFrom);
            }
            catch (RegexMatchTimeoutException)
            {
                throw new EngineToolException(
                    $"The rule '{prepared.Rule.Id}' did not finish matching within its match timeout. The scan stops " +
                    "rather than letting one pattern stall it. Shorten the file or the shape.");
            }

            if (!match.Success || match.Index < searchFrom)
            {
                return;
            }

            var accepted = match.Length > 0 && TryAcceptPattern(prepared, pattern, relativePath, text, match, matches);
            var next = accepted
                ? match.Index + match.Length
                : TextPositions.NextScalar(text, match.Index);

            searchFrom = next > match.Index ? next : match.Index + 1;
        }
    }

    private static bool TryAcceptPattern(
        PreparedRule prepared,
        RulePatternMatcher pattern,
        string relativePath,
        string text,
        Match match,
        List<RuleMatch> matches)
    {
        if (!BoundaryAllows(text, match.Index, match.Length, pattern.Boundary))
        {
            return false;
        }

        if (prepared.FoldedKeywords is not null
            && !KeywordWithinWindow(text, match.Index, match.Length, prepared.FoldedKeywords, prepared.Window))
        {
            return false;
        }

        if (!TryFindingSpan(prepared.Shape!, match, out var spanStart, out var spanLength))
        {
            return false;
        }

        if (prepared.Validator is not null
            && !prepared.Validator.Validate(text.AsSpan(spanStart, spanLength), pattern.Validator!))
        {
            return false;
        }

        if (RuleExclusions.Voids(text, spanStart, spanLength, pattern))
        {
            return false;
        }

        matches.Add(CreateMatch(prepared.Rule, relativePath, text, spanStart, spanLength));
        return true;
    }

    private static bool TryFindingSpan(CompiledShape shape, Match match, out int spanStart, out int spanLength)
    {
        if (!shape.HasEmitGroup)
        {
            spanStart = match.Index;
            spanLength = match.Length;
            return spanLength > 0;
        }

        var group = match.Groups[shape.EmitGroupNumber];

        if (!group.Success || group.Length == 0)
        {
            spanStart = 0;
            spanLength = 0;
            return false;
        }

        spanStart = group.Index;
        spanLength = group.Length;
        return true;
    }

    private static bool BoundaryAllows(string text, int start, int length, MatchBoundary boundary)
    {
        if (boundary == MatchBoundary.None)
        {
            return true;
        }

        if (start > 0 && IsBoundaryNeighbour(text[start - 1], boundary))
        {
            return false;
        }

        var end = start + length;

        if (end >= text.Length)
        {
            return true;
        }

        return boundary == MatchBoundary.Email
            ? !IsEmailTrailingNeighbour(text[end])
            : !IsBoundaryNeighbour(text[end], boundary);
    }

    private static bool IsBoundaryNeighbour(char character, MatchBoundary boundary)
    {
        var digit = character is >= '0' and <= '9';

        if (boundary == MatchBoundary.Digit)
        {
            return digit;
        }

        if (boundary == MatchBoundary.Email)
        {
            // The leading side keeps '.', so a match can never start inside a
            // longer local part after an internal dot (IQ-91, issue 112).
            return digit || character is (>= 'A' and <= 'Z') or (>= 'a' and <= 'z')
                or '.' or '_' or '%' or '+' or '-';
        }

        return digit || character is (>= 'A' and <= 'Z') or (>= 'a' and <= 'z');
    }

    /// <summary>The trailing neighbour set leaves out '.', so a sentence-ending dot after the domain does not reject the match (IQ-91).</summary>
    private static bool IsEmailTrailingNeighbour(char character)
    {
        return character is (>= 'A' and <= 'Z') or (>= 'a' and <= 'z')
            or (>= '0' and <= '9') or '_' or '%' or '+' or '-';
    }

    private static bool ContainsKeyword(string text, string[] foldedKeywords)
    {
        foreach (var keyword in foldedKeywords)
        {
            if (IndexOfFolded(text, keyword, 0) >= 0)
            {
                return true;
            }
        }

        return false;
    }

    private static bool KeywordWithinWindow(string text, int shapeStart, int shapeLength, string[] foldedKeywords, int window)
    {
        var shapeEnd = shapeStart + shapeLength;

        foreach (var keyword in foldedKeywords)
        {
            var searchFrom = 0;

            while (searchFrom <= text.Length - keyword.Length)
            {
                var found = IndexOfFolded(text, keyword, searchFrom);

                if (found < 0)
                {
                    break;
                }

                var keywordEnd = found + keyword.Length;

                if (ScalarGap(text, found, keywordEnd, shapeStart, shapeEnd) <= window)
                {
                    return true;
                }

                searchFrom = found + 1;
            }
        }

        return false;
    }

    private static int ScalarGap(string text, int keywordStart, int keywordEnd, int shapeStart, int shapeEnd)
    {
        if (keywordEnd <= shapeStart)
        {
            return TextPositions.CountScalars(text.AsSpan(keywordEnd, shapeStart - keywordEnd));
        }

        if (keywordStart >= shapeEnd)
        {
            return TextPositions.CountScalars(text.AsSpan(shapeEnd, keywordStart - shapeEnd));
        }

        return 0;
    }

    private static int IndexOfFolded(string text, string foldedNeedle, int start)
    {
        var limit = text.Length - foldedNeedle.Length;

        for (var index = start; index <= limit; index++)
        {
            if (EqualsFolded(text, index, foldedNeedle))
            {
                return index;
            }
        }

        return -1;
    }

    private static bool EqualsFolded(string text, int index, string foldedNeedle)
    {
        for (var offset = 0; offset < foldedNeedle.Length; offset++)
        {
            if (FoldAscii(text[index + offset]) != foldedNeedle[offset])
            {
                return false;
            }
        }

        return true;
    }

    private static string FoldAscii(string value)
    {
        var characters = value.ToCharArray();

        for (var index = 0; index < characters.Length; index++)
        {
            characters[index] = FoldAscii(characters[index]);
        }

        return new string(characters);
    }

    private static char FoldAscii(char character)
    {
        return character is >= 'A' and <= 'Z' ? (char)(character + 32) : character;
    }

    private static void MatchList(PreparedRule prepared, string relativePath, string text, List<RuleMatch> matches)
    {
        var list = prepared.Rule.Matcher.List!;

        if (list.Packages is not null)
        {
            FindEntries(prepared.Rule, relativePath, text, list.Packages, matches);
        }

        if (list.Hostnames is not null)
        {
            FindEntries(prepared.Rule, relativePath, text, list.Hostnames, matches);
        }
    }

    private static void FindEntries(
        Rule rule,
        string relativePath,
        string text,
        IReadOnlyList<string> entries,
        List<RuleMatch> matches)
    {
        foreach (var entry in entries)
        {
            if (entry.Length == 0 || entry.Length > text.Length)
            {
                continue;
            }

            var from = 0;

            while (from <= text.Length - entry.Length)
            {
                var index = text.IndexOf(entry, from, StringComparison.Ordinal);

                if (index < 0)
                {
                    break;
                }

                if (IsWholeToken(text, index, entry.Length))
                {
                    matches.Add(CreateMatch(rule, relativePath, text, index, entry.Length));
                }

                from = index + 1;
            }
        }
    }

    /// <summary>
    /// A whole token has a boundary on each side. The token alphabet is ASCII
    /// letters and digits plus <c>.</c>, <c>-</c>, <c>_</c> and <c>+</c>, which
    /// is what keeps a shorter package or hostname from matching inside a longer one.
    /// </summary>
    private static bool IsWholeToken(string text, int index, int length)
    {
        if (index > 0 && IsTokenCharacter(text[index - 1]))
        {
            return false;
        }

        var end = index + length;
        return end >= text.Length || !IsTokenCharacter(text[end]);
    }

    private static bool IsTokenCharacter(char character)
    {
        return character is (>= '0' and <= '9') or (>= 'A' and <= 'Z') or (>= 'a' and <= 'z') or '.' or '-' or '_' or '+';
    }

    private static RuleMatch CreateMatch(Rule rule, string relativePath, string text, int spanStart, int spanLength)
    {
        TextPositions.LineAndColumn(text, spanStart, out var line, out var column);
        var kind = rule.Matcher.Pattern?.Location?.Kind ?? LocationKind.Source;

        return new RuleMatch(
            rule.Id,
            rule.Severity,
            rule.Category,
            relativePath,
            line,
            column,
            kind,
            rule.Citation,
            rule.SuppressionHints ?? [],
            spanStart,
            spanLength);
    }

    private static int CompareMatches(RuleMatch left, RuleMatch right)
    {
        var line = left.Line.CompareTo(right.Line);

        if (line != 0)
        {
            return line;
        }

        var column = left.Column.CompareTo(right.Column);

        if (column != 0)
        {
            return column;
        }

        var rule = string.CompareOrdinal(left.RuleId, right.RuleId);

        if (rule != 0)
        {
            return rule;
        }

        return left.SpanLength.CompareTo(right.SpanLength);
    }

    private static RuleMatch[] Deduplicate(List<RuleMatch> matches)
    {
        if (matches.Count < 2)
        {
            return matches.ToArray();
        }

        var kept = new List<RuleMatch>(matches.Count);

        foreach (var match in matches)
        {
            if (kept.Count > 0 && SameSpan(kept[^1], match))
            {
                continue;
            }

            kept.Add(match);
        }

        return kept.ToArray();
    }

    private static bool SameSpan(RuleMatch left, RuleMatch right)
    {
        return left.SpanStart == right.SpanStart
            && left.SpanLength == right.SpanLength
            && string.Equals(left.RuleId, right.RuleId, StringComparison.Ordinal);
    }

    private sealed class PreparedRule(
        Rule rule,
        CompiledShape? shape,
        IRuleValidator? validator,
        PathGlob[]? paths,
        string[]? foldedKeywords,
        int window)
    {
        public Rule Rule { get; } = rule;

        public CompiledShape? Shape { get; } = shape;

        public IRuleValidator? Validator { get; } = validator;

        public string[]? FoldedKeywords { get; } = foldedKeywords;

        public int Window { get; } = window;

        private PathGlob[]? Paths { get; } = paths;

        public bool PathAllows(string path)
        {
            if (Paths is null)
            {
                return true;
            }

            foreach (var glob in Paths)
            {
                if (glob.IsMatch(path))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
