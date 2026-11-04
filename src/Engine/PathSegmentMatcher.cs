// SPDX-License-Identifier: Apache-2.0

namespace InvenioTech.GateKeeper.Engine;

/// <summary>
/// The one matcher a glob-like segment shares: the <c>*</c> wildcard inside
/// one segment (zero characters included), the <c>?</c> single-character
/// wildcard and the <c>[a-z]</c>, <c>[!a-z]</c> character classes.
/// Matching is ordinal. A backslash has no metacharacter meaning. The unit
/// backs <see cref="PathGlob"/> (whose validation keeps <c>?</c> and classes
/// out of <c>scan.exclude</c>) and the <c>.gitignore</c> reader (whose
/// grammar keeps them in).
/// </summary>
internal static class PathSegmentMatcher
{
    /// <summary>
    /// Reports whether one path segment matches one pattern segment.
    /// </summary>
    /// <param name="patternSegment">The pattern's segment.</param>
    /// <param name="name">The path's segment, with no slash inside.</param>
    /// <returns><see langword="true"/> when the segment matches.</returns>
    internal static bool Matches(string patternSegment, ReadOnlySpan<char> name)
    {
        var pattern = patternSegment.AsSpan();
        var patternIndex = 0;
        var nameIndex = 0;
        var starIndex = -1;
        var starNameIndex = 0;

        while (nameIndex < name.Length)
        {
            if (patternIndex >= pattern.Length)
            {
                // Only a remembered star can still consume characters.
                if (starIndex < 0)
                {
                    return false;
                }

                starNameIndex++;
                nameIndex = starNameIndex;
                patternIndex = starIndex + 1;
                continue;
            }

            var sign = pattern[patternIndex];

            if (sign == '*')
            {
                starIndex = patternIndex++;
                starNameIndex = nameIndex;
                continue;
            }

            if (MatchesNameSign(sign, pattern, patternIndex, name, nameIndex, out var patternNext)
                && name[nameIndex] != '/')
            {
                patternIndex = patternNext;
                nameIndex++;
                continue;
            }

            if (starIndex < 0)
            {
                return false;
            }

            starNameIndex++;
            nameIndex = starNameIndex;
            patternIndex = starIndex + 1;
        }

        while (patternIndex < pattern.Length && pattern[patternIndex] == '*')
        {
            patternIndex++;
        }

        return patternIndex == pattern.Length;
    }

    private static bool MatchesNameSign(
        char sign,
        ReadOnlySpan<char> pattern,
        int patternIndex,
        ReadOnlySpan<char> name,
        int nameIndex,
        out int patternNext)
    {
        if (sign == '?')
        {
            patternNext = patternIndex + 1;
            return true;
        }

        if (sign == '[')
        {
            return ClassContains(pattern, patternIndex, name[nameIndex], out patternNext);
        }

        patternNext = patternIndex + 1;
        return sign == name[nameIndex];
    }

    private static bool ClassContains(ReadOnlySpan<char> pattern, int bracket, char value, out int patternNext)
    {
        patternNext = pattern.Length;
        var index = bracket + 1;

        if (index >= pattern.Length)
        {
            return false;
        }

        var negate = pattern[index] is '^' or '!';

        if (negate)
        {
            index++;
        }

        var contained = false;

        while (index < pattern.Length && pattern[index] != ']')
        {
            var from = pattern[index++];

            if (index + 1 < pattern.Length && pattern[index] == '-' && pattern[index + 1] != ']')
            {
                var to = pattern[index + 1];

                if (value >= from && value <= to)
                {
                    contained = true;
                }

                index += 2;
                continue;
            }

            if (value == from)
            {
                contained = true;
            }
        }

        if (index >= pattern.Length)
        {
            // An unterminated class matches nothing; the walker never feeds
            // one, because gitignore never leaves one unterminated.
            return false;
        }

        patternNext = index + 1;
        return negate ? !contained : contained;
    }
}
