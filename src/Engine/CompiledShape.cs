// SPDX-License-Identifier: Apache-2.0

using System.Text;
using System.Text.RegularExpressions;

namespace InvenioTech.GateKeeper.Engine;

/// <summary>
/// One rule shape compiled into a single <see cref="Regex"/> (P1-06, card 1).
/// The shape language of the rule-pack format (section 7) has no
/// regular-expression string: this compiler is the one place a shape becomes
/// one, so every engine and every adapter shares the same translation. The
/// compiled regular expression uses <see cref="RegexOptions.NonBacktracking"/>,
/// which the NativeAOT spike (P0-16) measured inside the per-file budget and
/// which cannot backtrack catastrophically, and it carries a match timeout as
/// the belt-and-braces bound the work package asks for.
/// </summary>
/// <remarks>
/// <para>
/// The translation is fixed and total: a literal part becomes its escaped
/// text, a class part becomes <c>[0-9]</c> or <c>[0-9A-Za-z]</c>, a ranges part
/// becomes one character class, and an alternation part becomes a
/// non-capturing group whose alternatives are tried longest first, so a
/// shorter alternative never masks a longer one at the same position and the
/// pack's listing order carries no priority. Repetition bounds become the
/// bounded quantifier <c>{min,max}</c>; the language allows nothing else, so
/// the output always sits inside the portable profile the specification fixes
/// (section 7.3).
/// </para>
/// <para>
/// The timeout honesty note: .NET's <c>NonBacktracking</c> engine accepts a
/// match timeout in the constructor and does not enforce it, because that
/// engine is linear in the input by construction. The timeout is still passed
/// so any runtime that enforces one is bounded, and the real protection is
/// structural, exactly as the specification states (section 1.2 of the
/// rule-pack format: the shape bounds are the safety control) — at most 32
/// parts, at most 64 repetitions per part, at most 256 scalar values consumed
/// and no nesting, all checked here before a regular expression is built.
/// </para>
/// </remarks>
public sealed class CompiledShape
{
    private CompiledShape(string pattern, Regex regex, bool hasEmitGroup, int emitGroupNumber)
    {
        Pattern = pattern;
        Regex = regex;
        HasEmitGroup = hasEmitGroup;
        EmitGroupNumber = emitGroupNumber;
    }

    /// <summary>Gets the translated pattern text, which is also the cache key.</summary>
    public string Pattern { get; }

    /// <summary>
    /// Gets the compiled regular expression: <see cref="RegexOptions.NonBacktracking"/>
    /// with <see cref="RegexOptions.CultureInvariant"/> and the configured match timeout.
    /// </summary>
    public Regex Regex { get; }

    /// <summary>
    /// Gets whether one shape part emits: the emitting part's span is the
    /// finding span (section 7.1). When no part emits, the whole match is the
    /// finding span.
    /// </summary>
    public bool HasEmitGroup { get; }

    /// <summary>Gets the capture-group number of the emitting part, or 0 when no part emits.</summary>
    internal int EmitGroupNumber { get; }

    /// <summary>
    /// Compiles one shape, first checking every structural bound the
    /// rule-pack format fixes (section 7.1). The checks mirror the pack
    /// loader's, so a loaded pack never fails here; they exist because the
    /// engine is a public library and a hand-built matcher must fail loudly
    /// rather than compile into something the format never allowed.
    /// </summary>
    /// <param name="shape">The shape parts, 1 to 32 of them.</param>
    /// <param name="ruleId">The rule identifier, for the error sentences.</param>
    /// <param name="matchTimeout">The match timeout the regular expression carries.</param>
    /// <returns>The compiled shape.</returns>
    /// <exception cref="EngineToolException">The shape breaks a bound of section 7.1.</exception>
    public static CompiledShape Compile(IReadOnlyList<RuleShapePart> shape, string ruleId, TimeSpan matchTimeout)
    {
        if (shape.Count is 0 or > 32)
        {
            throw new EngineToolException(
                $"The rule '{ruleId}' carries a shape of {shape.Count} parts. A shape has 1 to 32 parts (section 7.1).");
        }

        var builder = new StringBuilder();
        var emitCount = 0;
        var scalarSum = 0;

        for (var index = 0; index < shape.Count; index++)
        {
            var part = shape[index];
            var atom = CompilePart(part, ruleId);

            if (part.Emit)
            {
                emitCount++;
                if (emitCount > 1)
                {
                    throw new EngineToolException(
                        $"The rule '{ruleId}' marks {emitCount} shape parts with emit. At most one part emits, and " +
                        "that part's span is the finding span (section 7.1).");
                }

                atom = $"(?<emit>{atom})";
            }

            scalarSum += part.Max * part.AtomWidth;
            builder.Append(atom);
        }

        if (scalarSum > 256)
        {
            throw new EngineToolException(
                $"The shape of the rule '{ruleId}' could consume {scalarSum} scalar values, over the 256 limit a " +
                "part-sum may reach (section 7.1). Shorten the shape: a fixed-bound scan must know its width.");
        }

        var pattern = builder.ToString();
        var regex = new Regex(pattern, RegexOptions.NonBacktracking | RegexOptions.CultureInvariant, matchTimeout);
        var hasEmitGroup = emitCount == 1;

        return new CompiledShape(pattern, regex, hasEmitGroup, hasEmitGroup ? regex.GroupNumberFromName("emit") : 0);
    }

    /// <summary>Compiles one part: its atom with its repetition bounds (section 7.1).</summary>
    private static string CompilePart(RuleShapePart part, string ruleId)
    {
        if (part.Min < 0 || part.Min > 64 || part.Max < 1 || part.Max > 64)
        {
            throw new EngineToolException(
                $"A shape part of the rule '{ruleId}' sets its repetitions to min {part.Min} and max {part.Max}, " +
                "outside the 0 to 64 range (section 7.1).");
        }

        if (part.Min > part.Max)
        {
            throw new EngineToolException(
                $"A shape part of the rule '{ruleId}' sets min ({part.Min}) above its max ({part.Max}). A part that " +
                "repeats must be able to satisfy its own bounds (section 7.1).");
        }

        var atomCount = (part.Literal is null ? 0 : 1) + (part.Class is null ? 0 : 1)
            + (part.Ranges is null ? 0 : 1) + (part.OneOf is null ? 0 : 1);

        if (atomCount != 1)
        {
            throw new EngineToolException(
                $"A shape part of the rule '{ruleId}' does not carry exactly one atom of literal, class, ranges or " +
                "oneOf. One part is one atom (section 7.1); a part with two kinds is ambiguous.");
        }

        string atom;

        if (part.Literal is not null)
        {
            if (part.Literal.Length == 0)
            {
                throw new EngineToolException(
                    $"A shape part of the rule '{ruleId}' sets literal to an empty string (section 7.1).");
            }

            atom = Regex.Escape(part.Literal);
        }
        else if (part.Class is not null)
        {
            atom = part.Class switch
            {
                ShapeClass.Digit => "[0-9]",
                ShapeClass.Alnum => "[0-9A-Za-z]",
                _ => throw new EngineToolException(
                    $"A shape part of the rule '{ruleId}' sets class to '{part.Class}', which is not 'digit' or 'alnum' (section 7.1)."),
            };
        }
        else if (part.Ranges is not null)
        {
            if (part.Ranges.Count == 0)
            {
                throw new EngineToolException(
                    $"A shape part of the rule '{ruleId}' does not set ranges to a non-empty array (section 7.1).");
            }

            var ranges = new StringBuilder("[");
            foreach (var range in part.Ranges)
            {
                ranges.Append(EscapeClassCharacter(range.Low)).Append('-').Append(EscapeClassCharacter(range.High));
            }

            atom = ranges.Append(']').ToString();
        }
        else
        {
            if (part.OneOf is null || part.OneOf.Count == 0)
            {
                throw new EngineToolException(
                    $"A shape part of the rule '{ruleId}' does not set oneOf to a non-empty array (section 7.1).");
            }

            if (part.Max != 1)
            {
                throw new EngineToolException(
                    $"An oneOf part of the rule '{ruleId}' repeats (max {part.Max}). An alternation must not " +
                    "repeat: its max is 1, which removes the ambiguous-alternation case (section 7.1).");
            }

            // Longest first, then ordinal, so every engine tries the same
            // alternative first and a shorter one never masks a longer one.
            var alternatives = new string[part.OneOf.Count];
            for (var index = 0; index < part.OneOf.Count; index++)
            {
                var alternative = part.OneOf[index];

                if (alternative.Length == 0)
                {
                    throw new EngineToolException(
                        $"An oneOf alternative of the rule '{ruleId}' is empty (section 7.1).");
                }

                alternatives[index] = alternative;
            }

            Array.Sort(alternatives, (left, right) =>
            {
                var byLength = right.Length.CompareTo(left.Length);
                return byLength != 0 ? byLength : string.CompareOrdinal(left, right);
            });

            var alternation = new StringBuilder("(?:");
            for (var index = 0; index < alternatives.Length; index++)
            {
                if (index > 0)
                {
                    alternation.Append('|');
                }

                alternation.Append(Regex.Escape(alternatives[index]));
            }

            atom = alternation.Append(')').ToString();
        }

        var quantifier = part.Min == part.Max ? $"{{{part.Min}}}" : $"{{{part.Min},{part.Max}}}";
        return atom + quantifier;
    }

    /// <summary>
    /// Escapes one character for use inside a character class, where only
    /// <c>\</c>, <c>]</c>, <c>^</c>, <c>-</c> and <c>[</c> carry meaning.
    /// </summary>
    private static string EscapeClassCharacter(char character)
    {
        return character is '\\' or ']' or '^' or '-' or '['
            ? "\\" + character
            : character.ToString();
    }
}

/// <summary>
/// The cache every compiled shape is shared through (P1-06, card 1): one
/// compiled regular expression per distinct pattern text, so two rules whose
/// shapes translate to the same pattern share it and a long-lived adapter
/// process compiles each shape once. The cache is instance state the caller
/// owns — there is no static cache anywhere in the engine (rule B1) — and it
/// is safe to share between engines because compilation happens under a lock.
/// </summary>
public sealed class ShapeRegexCache
{
    /// <summary>
    /// The match timeout every compiled shape carries when the caller names
    /// none: 100 milliseconds, an order above the whole per-file budget of
    /// NFR-011, so a legitimate single match never approaches it. The
    /// <c>NonBacktracking</c> engine does not enforce a timeout (it is linear
    /// by construction); the value bounds any runtime that does.
    /// </summary>
    public static readonly TimeSpan DefaultMatchTimeout = TimeSpan.FromMilliseconds(100);

    private readonly object _gate = new();
    private readonly Dictionary<string, CompiledShape> _shapes = new(StringComparer.Ordinal);
    private readonly TimeSpan _matchTimeout;

    /// <summary>
    /// Creates a cache with the default match timeout, or the timeout the
    /// caller names.
    /// </summary>
    /// <param name="matchTimeout">
    /// The timeout every compiled shape carries, or null for
    /// <see cref="DefaultMatchTimeout"/>. <see cref="Timeout.InfiniteTimeSpan"/>
    /// disables it.
    /// </param>
    public ShapeRegexCache(TimeSpan? matchTimeout = null)
    {
        _matchTimeout = matchTimeout ?? DefaultMatchTimeout;
    }

    /// <summary>
    /// Gets the compiled shape for one rule shape, compiling it the first
    /// time that pattern text is seen. The pattern text is the key, so two
    /// shapes that translate to the same pattern — for example a
    /// <c>digit</c> class and the explicit range <c>0-9</c> — share one
    /// compiled regular expression.
    /// </summary>
    /// <param name="shape">The shape parts.</param>
    /// <param name="ruleId">The rule identifier, for the error sentences.</param>
    /// <returns>The compiled shape, shared with every equal shape.</returns>
    public CompiledShape GetOrCompile(IReadOnlyList<RuleShapePart> shape, string ruleId)
    {
        // Compile outside the lock so a slow first compilation never blocks a
        // reader, then keep the first copy that made it into the dictionary.
        var candidate = CompiledShape.Compile(shape, ruleId, _matchTimeout);

        lock (_gate)
        {
            if (_shapes.TryGetValue(candidate.Pattern, out var shared))
            {
                return shared;
            }

            _shapes.Add(candidate.Pattern, candidate);
            return candidate;
        }
    }
}
