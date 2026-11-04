// SPDX-License-Identifier: Apache-2.0

using System.Text;

namespace InvenioTech.GateKeeper.Engine;

/// <summary>
/// One pattern of the <c>scan.exclude</c> subset (section 6.3 of the
/// configuration specification, which reuses section 5.3 of the rule-pack
/// specification). The only metacharacters are <c>*</c>, which matches inside
/// one path segment and never crosses a slash, and <c>**</c>, which matches
/// across segments and also across zero segments. Every other metacharacter,
/// a leading slash, a backslash and a <c>..</c> segment are tool errors.
/// Matching is ordinal and case-sensitive against the forward-slash path,
/// relative to the repository root.
/// </summary>
public sealed class PathGlob
{
    private const string SubsetSummary =
        "The only metacharacters are '*' inside one path segment and '**' across segments.";

    private PathGlob(string pattern, string[] segments)
    {
        Pattern = pattern;
        Segments = segments;
    }

    /// <summary>Gets the pattern text the glob was compiled from.</summary>
    public string Pattern { get; }

    private string[] Segments { get; }

    /// <summary>
    /// Compiles a pattern, rejecting every value outside the subset with a
    /// tool error that names the pattern and the rule it breaks.
    /// </summary>
    /// <param name="pattern">The glob pattern, relative to the repository root.</param>
    /// <returns>The compiled glob.</returns>
    /// <exception cref="EngineToolException">The pattern is outside the subset.</exception>
    public static PathGlob Compile(string pattern)
    {
        if (string.IsNullOrEmpty(pattern))
        {
            throw new EngineToolException(
                "The 'scan.exclude' glob is empty. Name a forward-slash path relative to the repository root. " +
                SubsetSummary);
        }

        if (pattern.Contains('\\'))
        {
            throw new EngineToolException(
                $"The 'scan.exclude' glob '{pattern}' is not in the supported pattern subset. " +
                "A backslash is not valid: use '/' only. " + SubsetSummary);
        }

        if (pattern.StartsWith('/'))
        {
            throw new EngineToolException(
                $"The 'scan.exclude' glob '{pattern}' is not in the supported pattern subset. " +
                "A leading '/' is not valid: globs are relative to the repository root. " + SubsetSummary);
        }

        if (pattern.Contains('[') || pattern.Contains(']'))
        {
            throw new EngineToolException(
                $"The 'scan.exclude' glob '{pattern}' is not in the supported pattern subset. " +
                "Character classes are not supported. " + SubsetSummary);
        }

        if (pattern.Contains('{') || pattern.Contains('}'))
        {
            throw new EngineToolException(
                $"The 'scan.exclude' glob '{pattern}' is not in the supported pattern subset. " +
                "Brace expansion is not supported. " + SubsetSummary);
        }

        var rawSegments = pattern.Split('/');

        foreach (var rawSegment in rawSegments)
        {
            if (rawSegment.Contains('?', StringComparison.Ordinal))
            {
                throw new EngineToolException(
                    $"The 'scan.exclude' glob '{pattern}' is not in the supported pattern subset. " +
                    "The '?' character is not supported. " + SubsetSummary);
            }

            if (rawSegment.Length == 2 && rawSegment == "..")
            {
                throw new EngineToolException(
                    $"The 'scan.exclude' glob '{pattern}' is not in the supported pattern subset. " +
                    "The '..' segment is not valid: globs cannot name a path outside the repository root. " +
                    SubsetSummary);
            }
        }

        return new PathGlob(pattern, rawSegments);
    }

    /// <summary>
    /// Reports whether the forward-slash path is excluded by this pattern.
    /// </summary>
    /// <param name="path">The forward-slash path relative to the repository root.</param>
    /// <returns><see langword="true"/> when the path matches the pattern.</returns>
    public bool IsMatch(string path) => Matches(Segments, path, 0, 0);

    private static bool Matches(string[] pattern, string path, int patternIndex, int pathIndex)
    {
        if (patternIndex >= pattern.Length)
        {
            return pathIndex >= path.Length;
        }

        var segment = pattern[patternIndex];

        if (segment == "**")
        {
            // '**' matches zero or more remaining segments: try each depth.
            if (Matches(pattern, path, patternIndex + 1, pathIndex))
            {
                return true;
            }

            var index = pathIndex;
            while (index <= path.Length)
            {
                var slash = path.IndexOf('/', index);
                var nextIndex = slash == -1 ? path.Length : slash + 1;

                if (Matches(pattern, path, patternIndex + 1, nextIndex))
                {
                    return true;
                }

                if (slash == -1)
                {
                    break;
                }

                index = slash + 1;
            }

            return false;
        }

        var segmentEnd = path.IndexOf('/', pathIndex);

        if (segmentEnd == -1)
        {
            segmentEnd = path.Length;
        }

        return SegmentMatches(segment, path.AsSpan(pathIndex, segmentEnd - pathIndex))
            && Matches(pattern, path, patternIndex + 1, segmentEnd == path.Length ? path.Length : segmentEnd + 1);
    }

    private static bool SegmentMatches(string patternSegment, ReadOnlySpan<char> name) =>
        PathSegmentMatcher.Matches(patternSegment, name);
}
