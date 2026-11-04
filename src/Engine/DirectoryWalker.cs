// SPDX-License-Identifier: Apache-2.0

namespace InvenioTech.GateKeeper.Engine;

/// <summary>
/// The directory walk that backs the outside-git mode of <see
/// cref="FileEnumerator"/> (section 6.2 of the configuration
/// specification): it reads every regular file, keeps the
/// <c>.gitignore</c> stack of the walk, applies the built-in exclusions and
/// the <c>scan.exclude</c> globs in a fixed order, and decides file by
/// file. A <c>.gitignore</c> negation can never bring a built-in exclusion
/// back: the built-in exclusions run first.
/// </summary>
internal static class DirectoryWalker
{
    /// <summary>
    /// Walks the directory tree, filling the results lists the caller gave:
    /// the scanned files, and the notes the skipped candidates deserve.
    /// </summary>
    /// <param name="root">The absolute root directory of the walk.</param>
    /// <param name="options">The options that carry the size limit.</param>
    /// <param name="excludeGlobs">The compiled <c>scan.exclude</c> globs.</param>
    /// <param name="files">The files the scan keeps.</param>
    /// <param name="notes">The notes the walk records.</param>
    /// <param name="cancellationToken">The token that cancels the run.</param>
    internal static async Task WalkAsync(
        string root,
        FileEnumerationOptions options,
        PathGlob[] excludeGlobs,
        List<EnumeratedFile> files,
        List<string> notes,
        CancellationToken cancellationToken)
    {
        if (!Directory.Exists(root))
        {
            notes.Add(FormattableString.Invariant(
                $"The directory '{root}' does not exist, so nothing was scanned."));
            return;
        }

        await WalkDirectoryAsync(
            root,
            [],
            0,
            [],
            options,
            excludeGlobs,
            files,
            notes,
            cancellationToken).ConfigureAwait(false);
    }

    private static async Task WalkDirectoryAsync(
        string root,
        IReadOnlyList<string> parentSegments,
        int depth,
        IReadOnlyList<GitignoreRules> parentRules,
        FileEnumerationOptions options,
        PathGlob[] excludeGlobs,
        List<EnumeratedFile> files,
        List<string> notes,
        CancellationToken cancellationToken)
    {
        var directoryPath = Path.Combine([root, .. parentSegments]);
        var rules = await GitignoreRules.LoadAsync(directoryPath, depth, cancellationToken).ConfigureAwait(false);
        IReadOnlyList<GitignoreRules> activeRules = rules is null
            ? parentRules
            : [.. parentRules, rules];

        FileSystemInfo[] entries;

        try
        {
            entries =
            [
                .. new DirectoryInfo(directoryPath).EnumerateFileSystemInfos(),
            ];
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            notes.Add(FormattableString.Invariant(
                $"Skipped '{JoinForward(parentSegments)}': the subfolder could not be read."));
            return;
        }

        Array.Sort(entries, static (left, right) => string.CompareOrdinal(left.Name, right.Name));

        foreach (var entry in entries)
        {
            IReadOnlyList<string> relativeSegments = [.. parentSegments, entry.Name];
            var relativePath = JoinForward(relativeSegments);

            if (entry.LinkTarget is not null)
            {
                notes.Add(FormattableString.Invariant(
                    $"Skipped '{relativePath}': the entry is a symbolic link, so the scan did not read it."));
                continue;
            }

            if (entry is DirectoryInfo directory)
            {
                if (!ScanExclusions.IsPathExcluded(relativePath))
                {
                    var decision = GitignoreRules.Decide(activeRules, relativeSegments, isDirectory: true);

                    if (decision is not true)
                    {
                        await WalkDirectoryAsync(
                            root,
                            relativeSegments,
                            depth + 1,
                            activeRules,
                            options,
                            excludeGlobs,
                            files,
                            notes,
                            cancellationToken).ConfigureAwait(false);
                    }
                }

                continue;
            }

            if (ScanExclusions.IsPathExcluded(relativePath))
            {
                continue;
            }

            if (IsExcludedByGlobs(relativePath, excludeGlobs))
            {
                continue;
            }

            if (GitignoreRules.Decide(activeRules, relativeSegments, isDirectory: false) is true)
            {
                continue;
            }

            await EnumerationFileReader.ReadFileAsync(
                fullPath: Path.Combine(directoryPath, entry.Name),
                relativePath: relativePath,
                maxFileBytes: options.MaxFileBytes,
                files,
                notes,
                coveredBlobs: null).ConfigureAwait(false);
        }
    }

    private static bool IsExcludedByGlobs(string path, PathGlob[] excludeGlobs)
    {
        foreach (var glob in excludeGlobs)
        {
            if (glob.IsMatch(path))
            {
                return true;
            }
        }

        return false;
    }

    private static string JoinForward(IReadOnlyList<string> segments) => string.Join('/', segments);
}
