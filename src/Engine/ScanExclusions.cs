// SPDX-License-Identifier: Apache-2.0

namespace InvenioTech.GateKeeper.Engine;

/// <summary>
/// The built-in exclusions that always apply, whatever git tracks, whatever
/// the directory carries and whatever the <c>scan.exclude</c> globs name
/// (section 6.3 of the configuration specification). A segment is compared
/// under ordinal ignore-case, exactly as the specification says.
/// </summary>
internal static class ScanExclusions
{
    private static readonly string[] ExcludedSegments = [".git", "node_modules", "bin", "obj", "dist", "build", "vendor", ".gatekeeper"];

    private static readonly string[] LockFileNames =
    [
        "package-lock.json",
        "yarn.lock",
        "pnpm-lock.yaml",
        "bun.lock",
        "packages.lock.json",
        "Cargo.lock",
        "composer.lock",
        "Gemfile.lock",
        "poetry.lock",
        "Pipfile.lock",
        "gradle.lockfile",
        "pubspec.lock",
    ];

    private static readonly string[] MinifiedSuffixes = [".min.js", ".min.css"];

    /// <summary>
    /// Reports whether the forward-slash repository-relative path falls under
    /// a built-in exclusion: an excluded segment, a lock-file name or a
    /// minified-file name.
    /// </summary>
    /// <param name="path">The forward-slash path, relative to the repository root.</param>
    /// <returns><see langword="true"/> when a built-in exclusion names the path.</returns>
    internal static bool IsPathExcluded(string path)
    {
        var span = path.AsSpan();
        var start = 0;

        while (start < span.Length)
        {
            var slash = span[start..].IndexOf('/');
            var segment = slash < 0 ? span[start..] : span.Slice(start, slash);

            if (IsOneOf(segment, ExcludedSegments))
            {
                return true;
            }

            if (slash < 0)
            {
                return IsOneOf(segment, LockFileNames) || HasMinifiedSuffix(segment);
            }

            start += slash + 1;
        }

        return false;
    }

    private static bool IsOneOf(ReadOnlySpan<char> segment, string[] candidates)
    {
        foreach (var candidate in candidates)
        {
            if (segment.Equals(candidate, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static bool HasMinifiedSuffix(ReadOnlySpan<char> name)
    {
        foreach (var suffix in MinifiedSuffixes)
        {
            if (name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
