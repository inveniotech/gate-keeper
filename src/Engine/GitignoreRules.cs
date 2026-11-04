// SPDX-License-Identifier: Apache-2.0

namespace InvenioTech.GateKeeper.Engine;

/// <summary>
/// The <c>.gitignore</c> rules of one directory, parsed once per directory,
/// with the semantics git applies: a negation re-includes, a trailing slash
/// matches directories only, a pattern with a slash anywhere but the
/// trailing one is anchored to the directory the file sits in, an
/// unanchored pattern matches at any depth, <c>*</c> never crosses a slash,
/// <c>**</c> does, and the last matching pattern decides. Across files, the
/// deeper <c>.gitignore</c> decides over its ancestors.
/// </summary>
internal sealed class GitignoreRules
{
    private GitignoreRules(int depth, IReadOnlyList<GitignoreRule> rules)
    {
        Depth = depth;
        Rules = rules;
    }

    /// <summary>
    /// Gets how deep under the walk root the owning directory sits: a root
    /// <c>.gitignore</c> has depth 0.
    /// </summary>
    internal int Depth { get; }

    /// <summary>Gets the rules in the order the file wrote them, top to bottom.</summary>
    internal IReadOnlyList<GitignoreRule> Rules { get; }

    /// <summary>
    /// Loads the <c>.gitignore</c> of one directory, when it has one.
    /// </summary>
    /// <param name="directoryPath">The absolute directory path.</param>
    /// <param name="depth">The directory's depth under the walk root.</param>
    /// <param name="cancellationToken">The token that cancels the run.</param>
    /// <returns>The rules, or <see langword="null"/> when the directory carries none.</returns>
    internal static async Task<GitignoreRules?> LoadAsync(
        string directoryPath,
        int depth,
        CancellationToken cancellationToken)
    {
        var located = Path.Combine(directoryPath, ".gitignore");

        if (!File.Exists(located))
        {
            return null;
        }

        var text = await File.ReadAllTextAsync(located, cancellationToken).ConfigureAwait(false);
        return new GitignoreRules(depth, GitignoreParser.Parse(text));
    }

    /// <summary>
    /// Applies every loaded ignore file in the walk's stack to a candidate:
    /// within one file the last matching pattern decides, across files the
    /// deeper file decides (section 6.2 of the configuration specification).
    /// </summary>
    /// <param name="stack">The loaded ignore files, shallowest first.</param>
    /// <param name="relativeSegments">The candidate's segments under the walk root.</param>
    /// <param name="isDirectory">Whether the candidate decides a directory.</param>
    /// <returns>
    /// <see langword="true"/> when a rule ignores the candidate, <see
    /// langword="false"/> when a rule re-includes it, and <see
    /// langword="null"/> when no rule names it.
    /// </returns>
    internal static bool? Decide(
        IReadOnlyList<GitignoreRules> stack,
        IReadOnlyList<string> relativeSegments,
        bool isDirectory)
    {
        bool? decision = null;

        foreach (var ignoreFile in stack)
        {
            bool? fileDecision = null;

            foreach (var rule in ignoreFile.Rules)
            {
                if (MatchesRule(rule, ignoreFile.Depth, relativeSegments, isDirectory))
                {
                    // A plain matching pattern ignores the candidate; only
                    // the negation sign re-includes it.
                    fileDecision = !rule.Negate;
                }
            }

            if (fileDecision.HasValue)
            {
                decision = fileDecision;
            }
        }

        return decision;
    }

    private static bool MatchesRule(
        GitignoreRule rule,
        int depth,
        IReadOnlyList<string> relativeSegments,
        bool isDirectory)
    {
        if (rule.DirectoryOnly && !isDirectory)
        {
            return false;
        }

        if (rule.Anchored)
        {
            return SegmentListMatches(rule.Segments, relativeSegments, depth);
        }

        // An unanchored pattern matches at any depth: try every window that
        // starts at or after the owning directory.
        for (var start = depth; start < relativeSegments.Count; start++)
        {
            if (SegmentListMatches(rule.Segments, relativeSegments, start))
            {
                return true;
            }
        }

        return false;
    }

    private static bool SegmentListMatches(
        IReadOnlyList<string> pattern,
        IReadOnlyList<string> pathSegments,
        int pathIndex) =>
        SegmentListMatchesCore(pattern, 0, pathSegments, pathIndex);

    private static bool SegmentListMatchesCore(
        IReadOnlyList<string> pattern,
        int patternIndex,
        IReadOnlyList<string> pathSegments,
        int pathIndex)
    {
        if (patternIndex >= pattern.Count)
        {
            return pathIndex >= pathSegments.Count;
        }

        var segment = pattern[patternIndex];

        if (segment == "**")
        {
            // '**' matches zero or more remaining segments.
            if (SegmentListMatchesCore(pattern, patternIndex + 1, pathSegments, pathIndex))
            {
                return true;
            }

            for (var index = pathIndex; index < pathSegments.Count; index++)
            {
                if (SegmentListMatchesCore(pattern, patternIndex + 1, pathSegments, index + 1))
                {
                    return true;
                }
            }

            return false;
        }

        return pathIndex < pathSegments.Count
            && PathSegmentMatcher.Matches(segment, pathSegments[pathIndex])
            && SegmentListMatchesCore(pattern, patternIndex + 1, pathSegments, pathIndex + 1);
    }
}

/// <summary>One parsed <c>.gitignore</c> line.</summary>
/// <param name="Negate">Whether the line re-includes what it names.</param>
/// <param name="DirectoryOnly">Whether the line ends in a slash and matches directories only.</param>
/// <param name="Anchored">Whether the line carries a slash and matches at the owning depth only.</param>
/// <param name="Segments">The pattern's segments, with the leading-slash marker gone.</param>
internal readonly record struct GitignoreRule(
    bool Negate,
    bool DirectoryOnly,
    bool Anchored,
    string[] Segments);

/// <summary>
/// Parses the character format of a <c>.gitignore</c> file: comments, the
/// negation sign, the directory-only slash and the anchoring test.
/// </summary>
internal static class GitignoreParser
{
    /// <summary>
    /// Parses the file's lines into rules, keeping only the lines that name
    /// something.
    /// </summary>
    /// <param name="text">The UTF-8 text of the file.</param>
    /// <returns>The rules in file order.</returns>
    internal static IReadOnlyList<GitignoreRule> Parse(string text)
    {
        var rules = new List<GitignoreRule>();

        foreach (var raw in text.Split('\n'))
        {
            var line = raw.TrimEnd('\r');

            // A trailing backspace before an escaped-sensitive trailing space
            // keeps it; plain trailing spaces are dropped.
            if (line.EndsWith("\\"))
            {
                line = line[..^1].TrimEnd() + "\\ ";
            }
            else
            {
                line = line.TrimEnd();
            }

            if (line.Length == 0 || line.StartsWith('#'))
            {
                continue;
            }

            var negate = false;
            var rest = line;

            if (rest.StartsWith('!'))
            {
                negate = true;
                rest = rest[1..];
            }

            var directoryOnly = false;
            var anchored = false;

            if (rest.EndsWith('/'))
            {
                directoryOnly = true;
                rest = rest[..^1];
            }

            if (rest.StartsWith('/'))
            {
                anchored = true;
                rest = rest[1..];
            }
            else if (rest.Contains('/'))
            {
                anchored = true;
            }

            var segments = rest.Split('/', StringSplitOptions.RemoveEmptyEntries);

            if (segments.Length == 0)
            {
                continue;
            }

            rules.Add(new GitignoreRule(negate, directoryOnly, anchored, segments));
        }

        return rules;
    }
}
