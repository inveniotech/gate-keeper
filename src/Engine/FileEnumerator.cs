// SPDX-License-Identifier: Apache-2.0

using System.Text;

namespace InvenioTech.GateKeeper.Engine;

/// <summary>
/// The file enumerator: the one component that decides which files a scan
/// reads. Inside a git work tree it enumerates the tracked files of
/// <c>git ls-files</c>; outside one it walks the directory, applies the
/// <c>.gitignore</c> semantics and records the outside-git warning; the
/// built-in exclusions, the <c>scan.exclude</c> globs, the size limit, the
/// binary and encoding classification, the Git LFS pointer skip, the
/// submodules, the <c>--changed-since</c> limit and, on request, the history
/// blobs reachable from <c>HEAD</c> are all its work (IQ-21, IQ-83,
/// sections 6 and 7 of the configuration specification).
///
/// Paths travel through this class as forward-slash repository-relative
/// paths in Unicode Normalisation Form C, in ordinal order (rule 6,
/// rule 22). The skip notes are user-facing sentences; the warnings carry
/// the closed set of codes the evidence file records.
/// </summary>
public sealed class FileEnumerator
{
    private const string OutsideGitWarningMessage =
        "This directory is not a git work tree. The scan walked the directory and applied the built-in exclusions.";

    private const string HistoryWithoutGitWarningMessage =
        "History scanning was requested and this directory is not a git work tree, so history was not scanned.";

    private const string ShallowHistoryWarningMessage =
        "History scanning covered only the commits in this shallow clone.";

    private const string GitlinkRecordMode = "160000";

    private readonly GitRunner git = new();

    /// <summary>
    /// Enumerates the scan input for the given options.
    /// </summary>
    /// <param name="options">The options for this run, whose setters already validated them.</param>
    /// <param name="cancellationToken">The token that cancels the run.</param>
    /// <returns>The files, the history candidates, the warnings and the notes.</returns>
    /// <exception cref="EngineToolException">A tool condition a user must repair: a scan-exclude glob outside the subset, a changed-since reference that does not resolve, or a root that is not the repository root.</exception>
    /// <exception cref="ArgumentException">The root is missing.</exception>
    public async Task<FileEnumerationResult> EnumerateAsync(
        FileEnumerationOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (string.IsNullOrWhiteSpace(options.Root))
        {
            throw new ArgumentException(
                "A scan needs the directory to scan. Give the repository root, or the directory to walk.",
                "root");
        }

        var excludeGlobs = options.ExcludeGlobs is { Count: > 0 } entries
            ? [.. entries.Select(PathGlob.Compile)]
            : Array.Empty<PathGlob>();
        var changedSince = string.IsNullOrWhiteSpace(options.ChangedSince) ? null : options.ChangedSince;

        var toplevel = await this.git.FindWorkTreeRootAsync(options.Root, cancellationToken).ConfigureAwait(false);

        if (toplevel is null)
        {
            return await this.EnumerateOutsideGitAsync(options, excludeGlobs, changedSince, cancellationToken)
                .ConfigureAwait(false);
        }

        // Both sides of the comparison go through the canonical form: git
        // prints the physical work-tree root, while the caller may name the
        // same directory through a symlink (the temporary directory on macOS
        // routes '/var' into '/private/var'), and a plain full path keeps the
        // link, so the comparison would refuse a legitimate root (rule 22).
        toplevel = Canonicalise(toplevel);
        var root = Canonicalise(options.Root);

        if (!SamePath(toplevel, root))
        {
            throw new EngineToolException(
                $"The scan must run on the repository root. '{options.Root}' is not the repository root; " +
                $"the repository root is '{toplevel}'.");
        }

        var notes = new List<string>();
        var warnings = new List<ScanWarning>();
        var files = new List<EnumeratedFile>();
        var history = new List<HistoryCandidate>();
        var coveredBlobs = new HashSet<string>(StringComparer.Ordinal);

        if (changedSince is not null)
        {
            await this.GuardChangedSinceReferenceAsync(toplevel, changedSince, cancellationToken).ConfigureAwait(false);
        }

        await this.EnumerateWorkTreeAsync(
            toplevel,
            options,
            excludeGlobs,
            changedSince,
            files,
            notes,
            warnings,
            coveredBlobs,
            cancellationToken).ConfigureAwait(false);

        if (options.ScanHistory)
        {
            await this.EnumerateHistoryAsync(
                toplevel,
                options,
                excludeGlobs,
                notes,
                warnings,
                history,
                coveredBlobs,
                cancellationToken).ConfigureAwait(false);
        }

        return Assemble(files, history, warnings, notes, isInsideGitWorkTree: true);
    }

    // ---------------------------------------------------------------------
    // Outside git.
    // ---------------------------------------------------------------------

    private async Task<FileEnumerationResult> EnumerateOutsideGitAsync(
        FileEnumerationOptions options,
        PathGlob[] excludeGlobs,
        string? changedSince,
        CancellationToken cancellationToken)
    {
        if (changedSince is not null)
        {
            throw new EngineToolException(
                $"The --changed-since flag works only inside a git work tree. " +
                $"'{options.Root}' is not a git work tree, so remove the flag and run the scan again.");
        }

        var files = new List<EnumeratedFile>();
        var notes = new List<string>();

        await DirectoryWalker.WalkAsync(
            Path.GetFullPath(options.Root),
            options,
            excludeGlobs,
            files,
            notes,
            cancellationToken).ConfigureAwait(false);

        var warnings = new List<ScanWarning>
        {
            new(OutsideGitWarningCode, OutsideGitWarningMessage),
        };

        if (options.ScanHistory)
        {
            warnings.Add(new ScanWarning(Code: "scan.history-without-git", HistoryWithoutGitWarningMessage));
        }

        return Assemble(files, [], warnings, notes, isInsideGitWorkTree: false);
    }

    private const string OutsideGitWarningCode = "scan.outside-git";

    // ---------------------------------------------------------------------
    // Inside a git work tree.
    // ---------------------------------------------------------------------

    private async Task EnumerateWorkTreeAsync(
        string toplevel,
        FileEnumerationOptions options,
        PathGlob[] excludeGlobs,
        string? changedSince,
        List<EnumeratedFile> files,
        List<string> notes,
        List<ScanWarning> warnings,
        HashSet<string> coveredBlobs,
        CancellationToken cancellationToken)
    {
        var indexListing = await this.git.TryRunAsync(
            toplevel,
            ["ls-files", "--cached", "--stage", "-z"],
            cancellationToken).ConfigureAwait(false);

        if (indexListing is not { ExitCode: 0 })
        {
            throw new EngineToolException(
                "The scan could not read the git index. Check that git is installed and the directory is a " +
                "git work tree, then run the scan again.");
        }

        var entries = ParseIndexEntries(indexListing.Value.StandardOutput);
        HashSet<string>? changedPaths = null;

        if (changedSince is not null)
        {
            changedPaths = [.. await this.ReadChangedPathsAsync(toplevel, changedSince, cancellationToken)
                .ConfigureAwait(false)];
        }

        var candidates = new List<(string Directory, string Path, string? Destination)>();
        foreach (var entry in entries)
        {
            if (!entry.IsSubmodule
                && changedPaths is not null
                && !changedPaths.Contains(entry.Path))
            {
                continue;
            }

            if (excludeGlobs.Length > 0 && excludeGlobs.Any(glob => glob.IsMatch(entry.Path)))
            {
                continue;
            }

            if (entry.IsSubmodule)
            {
                await this.EnumerateSubmoduleAsync(
                    toplevel,
                    entry.Path,
                    options,
                    excludeGlobs,
                    files,
                    notes,
                    warnings,
                    coveredBlobs,
                    cancellationToken).ConfigureAwait(false);
                continue;
            }

            candidates.Add((toplevel, entry.Path, null));
        }
        await ReadWorkTreeFilesAsync(candidates, options.MaxFileBytes, files, notes, coveredBlobs, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Reads one tracked file. The repository-relative path (for a check of
    /// the built-in exclusions) and the destination path (the scan's name for
    /// the file, including the submodule prefix) differ only under a
    /// submodule.
    /// </summary>
    private async Task ReadFileFromWorkTreeAsync(
        string directory,
        string repositoryRelativePath,
        string? destinationPath,
        long maxFileBytes,
        List<EnumeratedFile> files,
        List<string> notes,
        HashSet<string> coveredBlobs,
        CancellationToken cancellationToken)
    {
        var relativePath = NormalisePath(destinationPath ?? repositoryRelativePath);

        if (ScanExclusions.IsPathExcluded(relativePath))
        {
            return;
        }

        var fullPath = Path.Combine(directory, repositoryRelativePath);

        if (!File.Exists(fullPath))
        {
            notes.Add(FormattableString.Invariant(
                $"Skipped '{relativePath}': the file is tracked by git but no longer exists on disk."));
            return;
        }

        await EnumerationFileReader.ReadFileAsync(
            fullPath,
            relativePath,
            maxFileBytes,
            files,
            notes,
            coveredBlobs, cancellationToken).ConfigureAwait(false);
    }

    private async Task EnumerateSubmoduleAsync(
        string parentToplevel,
        string submodulePath,
        FileEnumerationOptions options,
        PathGlob[] excludeGlobs,
        List<EnumeratedFile> files,
        List<string> notes,
        List<ScanWarning> warnings,
        HashSet<string> coveredBlobs,
        CancellationToken cancellationToken)
    {
        if (!options.IncludeSubmodules)
        {
            return;
        }

        var submoduleDirectory = Path.Combine(parentToplevel, submodulePath);
        var moduleListing = await this.git.TryRunAsync(
            submoduleDirectory,
            ["ls-files", "--cached", "--stage", "-z"],
            cancellationToken).ConfigureAwait(false);

        if (moduleListing is not { ExitCode: 0 })
        {
            warnings.Add(new ScanWarning(
                Code: "submodule.not-checked-out",
                Message: $"The submodule at {submodulePath} is not checked out, so its files were not scanned."));
            return;
        }

        var moduleEntries = ParseIndexEntries(moduleListing.Value.StandardOutput);

        // A dead checkout answers with the gitlink itself, on the path './':
        // that is exactly the warning the evidence file records.
        if (moduleEntries.Count == 1 && moduleEntries[0].Path == "./" && moduleEntries[0].IsSubmodule)
        {
            warnings.Add(new ScanWarning(
                Code: "submodule.not-checked-out",
                Message: $"The submodule at {submodulePath} is not checked out, so its files were not scanned."));
            return;
        }

        var candidates = new List<(string Directory, string Path, string? Destination)>();
        foreach (var entry in moduleEntries)
        {
            if (entry.IsSubmodule)
            {
                // A nested submodule is included when the parent is included;
                // the '.git' walk-up junk never re-enters, because a checkout
                // that lost its work tree has already carried the warning.
                if (entry.Path == "./")
                {
                    continue;
                }

                await this.EnumerateSubmoduleAsync(
                    submoduleDirectory,
                    submodulePath + "/" + entry.Path,
                    options,
                    excludeGlobs,
                    files,
                    notes,
                    warnings,
                    coveredBlobs,
                    cancellationToken).ConfigureAwait(false);
                continue;
            }

            var moduleRelativePath = NormalisePath(submodulePath + "/" + entry.Path);

            if (ScanExclusions.IsPathExcluded(moduleRelativePath)
                || excludeGlobs.Any(glob => glob.IsMatch(moduleRelativePath)))
            {
                continue;
            }

            candidates.Add((submoduleDirectory, entry.Path, submodulePath + "/" + entry.Path));
        }
        await ReadWorkTreeFilesAsync(candidates, options.MaxFileBytes, files, notes, coveredBlobs, cancellationToken).ConfigureAwait(false);
    }

    private async Task ReadWorkTreeFilesAsync(
        IReadOnlyList<(string Directory, string Path, string? Destination)> candidates,
        long maxFileBytes, List<EnumeratedFile> files, List<string> notes,
        HashSet<string> coveredBlobs, CancellationToken cancellationToken)
    {
        var batches = await BoundedWorkers.MapAsync(candidates, async (candidate, token) =>
        {
            var localFiles = new List<EnumeratedFile>();
            var localNotes = new List<string>();
            var localBlobs = new HashSet<string>(StringComparer.Ordinal);
            await ReadFileFromWorkTreeAsync(candidate.Directory, candidate.Path, candidate.Destination,
                maxFileBytes, localFiles, localNotes, localBlobs, token).ConfigureAwait(false);
            return (Files: localFiles, Notes: localNotes, Blobs: localBlobs);
        }, cancellationToken).ConfigureAwait(false);
        foreach (var batch in batches)
        {
            files.AddRange(batch.Files);
            notes.AddRange(batch.Notes);
            coveredBlobs.UnionWith(batch.Blobs);
        }
    }

    private async Task<List<string>> ReadChangedPathsAsync(
        string toplevel,
        string changedSince,
        CancellationToken cancellationToken)
    {
        var diff = await this.git.TryRunAsync(
            toplevel,
            ["diff", "--name-only", "--no-renames", changedSince],
            cancellationToken).ConfigureAwait(false);

        if (diff is not { ExitCode: 0 })
        {
            throw new EngineToolException(
                "The scan could not compute the --changed-since comparison against " +
                $"'{changedSince}'. Check the reference and run the scan again.");
        }

        return SplitUtf8Lines(diff.Value.StandardOutput).Select(NormalisePath).ToList();
    }

    private async Task GuardChangedSinceReferenceAsync(
        string toplevel,
        string changedSince,
        CancellationToken cancellationToken)
    {
        var verification = await this.git.TryRunAsync(
            toplevel,
            ["rev-parse", "--quiet", "--verify", "--end-of-options", changedSince + "^{commit}"],
            cancellationToken).ConfigureAwait(false);

        if (verification is not { ExitCode: 0 })
        {
            throw new EngineToolException(
                $"The reference '{changedSince}' used with --changed-since does not resolve in this repository. " +
                "Check the reference name, for example a branch, a tag or a commit, and run the scan again.");
        }
    }

    private static bool SamePath(string left, string right) =>
        string.CompareOrdinal(left, right) == 0
        || string.Equals(left, right, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Resolves the symbolic links along a path the way the file system opens
    /// it, so a directory named through a link (macOS routes '/var' into
    /// '/private/var' and '/tmp' into '/private/tmp') compares equal to the
    /// physical path git prints for the same directory. Each component is
    /// resolved with <c>ResolveLinkTarget</c> down to its final target, and a
    /// component that is not a link leaves the walk unchanged. A resolved
    /// target is canonicalised again, because the stored target may itself
    /// name another link (a link into the temporary directory carries the
    /// un-resolved '/var' spelling), so the walk repeats until the path holds
    /// no link.
    /// </summary>
    /// <param name="path">The path to canonicalise, absolute or relative.</param>
    /// <returns>The full path with every symbolic link resolved.</returns>
    private static string Canonicalise(string path) => Canonicalise(path, depth: 0);

    private static string Canonicalise(string path, int depth)
    {
        var fullPath = Path.GetFullPath(path);

        // A pathological link chain could cycle; the limit keeps the scan
        // responsive and the comparison then falls back to the two names it
        // already compared.
        if (depth >= MaxSymbolicLinkDepth)
        {
            return fullPath;
        }

        var baseName = Path.GetPathRoot(fullPath) ?? string.Empty;
        var resolved = baseName;

        foreach (var segment in fullPath[baseName.Length..].Split(
                     [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
                     StringSplitOptions.RemoveEmptyEntries))
        {
            var candidate = Path.Combine(resolved, segment);

            if (Directory.Exists(candidate)
                && new DirectoryInfo(candidate).ResolveLinkTarget(returnFinalTarget: true) is { } target)
            {
                candidate = Canonicalise(target.FullName, depth + 1);
            }

            resolved = candidate;
        }

        return resolved;
    }

    private const int MaxSymbolicLinkDepth = 32;

    // ---------------------------------------------------------------------
    // History (IQ-83).
    // ---------------------------------------------------------------------

    private async Task EnumerateHistoryAsync(
        string toplevel,
        FileEnumerationOptions options,
        PathGlob[] excludeGlobs,
        List<string> notes,
        List<ScanWarning> warnings,
        List<HistoryCandidate> history,
        HashSet<string> coveredBlobs,
        CancellationToken cancellationToken)
    {
        var claims = await this.ReadHistoryClaimsAsync(toplevel, cancellationToken).ConfigureAwait(false);

        if (claims is null)
        {
            // An unborn HEAD or a broken traversal leaves the history empty;
            // the work-tree scan still ran.
            return;
        }

        // History ran, so the shallow-clone warning follows as soon as the
        // repository is shallow (section 7 of the configuration
        // specification).
        if (await this.IsShallowRepositoryAsync(toplevel, cancellationToken).ConfigureAwait(false))
        {
            warnings.Add(new ScanWarning(Code: "scan.shallow-history", ShallowHistoryWarningMessage));
        }

        // The work tree already read these bytes, so the blobs do not come
        // back for a second scan.
        claims.RemoveAll(claim => coveredBlobs.Contains(claim.ObjectId));

        if (claims.Count == 0)
        {
            return;
        }

        // Requesting the objects in the final order keeps the batch stream
        // deterministic.
        claims.Sort(static (left, right) =>
        {
            var byCommit = string.CompareOrdinal(left.CommitId, right.CommitId);
            return byCommit != 0 ? byCommit : string.CompareOrdinal(left.Path, right.Path);
        });

        await using var batchReader = GitBatchReader.Start(this.git, toplevel);

        if (batchReader is null)
        {
            throw new EngineToolException(
                "The scan could not start the git object reader for history. Check that git is installed and " +
                "run the scan again.");
        }

        foreach (var claim in claims)
        {
            var relativePath = NormalisePath(claim.Path);

            if (ScanExclusions.IsPathExcluded(relativePath)
                || excludeGlobs.Any(glob => glob.IsMatch(relativePath)))
            {
                continue;
            }

            var answered = await batchReader
                .ReadObjectAsync(claim.ObjectId, options.MaxFileBytes, cancellationToken)
                .ConfigureAwait(false);
            var content = ReadHistoryContent(relativePath, answered, options.MaxFileBytes, notes);

            if (content is null)
            {
                continue;
            }

            history.Add(new HistoryCandidate(claim.CommitId, relativePath, content));
        }
    }

    /// <summary>
    /// Applies the byte-level rules to a history blob's answer and gives the
    /// text the matcher runs on.
    /// </summary>
    private static string? ReadHistoryContent(
        string relativePath,
        GitBatchReader.BatchObject? answered,
        long maxFileBytes,
        List<string> notes)
    {
        if (answered is null)
        {
            return null;
        }

        var (type, size, bytes) = (answered!.Value.Type, answered!.Value.Size, answered!.Value.Bytes);

        if (type != "blob")
        {
            // A tree or a commit answered here only because the claim set
            // holds every object the traversal printed; only a blob claim
            // becomes a scan candidate.
            return null;
        }

        if (bytes is null)
        {
            notes.Add(FormattableString.Invariant(
                $"Skipped '{relativePath}': the file is {size} bytes, and the limit is {maxFileBytes} bytes."));
            return null;
        }

        return ContentRules.TryDecode(bytes, relativePath, notes);
    }

    /// <summary>
    /// Reads the object claims reachable from <c>HEAD</c>, each attributed to
    /// the newest commit whose tree still contains it. <see
    /// langword="null"/> means the listing did not run: an unborn
    /// <c>HEAD</c> or a broken walk.
    /// </summary>
    private async Task<List<HistoryClaim>?> ReadHistoryClaimsAsync(
        string toplevel,
        CancellationToken cancellationToken)
    {
        var listing = await this.git.TryRunAsync(
            toplevel,
            ["rev-list", "--objects", "--in-commit-order", "HEAD"],
            cancellationToken).ConfigureAwait(false);

        if (listing is not { ExitCode: 0 } flow)
        {
            return null;
        }

        var claims = new List<HistoryClaim>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        string? currentCommit = null;

        foreach (var line in SplitUtf8Lines(flow.StandardOutput))
        {
            var space = line.IndexOf(' ', StringComparison.Ordinal);

            if (space < 0)
            {
                currentCommit = line.Length == 40 && IsLowerHex(line) ? line : currentCommit;
                continue;
            }

            var objectId = line[..space];
            var path = line[(space + 1)..];

            if (currentCommit is null
                || objectId.Length != 40
                || !IsLowerHex(objectId)
                || !seen.Add(objectId))
            {
                continue;
            }

            claims.Add(new HistoryClaim(currentCommit, objectId, path));
        }

        return claims;
    }

    private async Task<bool> IsShallowRepositoryAsync(string toplevel, CancellationToken cancellationToken)
    {
        var probe = await this.git.TryRunAsync(
            toplevel,
            ["rev-parse", "--is-shallow-repository"],
            cancellationToken).ConfigureAwait(false);

        if (probe is not { ExitCode: 0 })
        {
            return false;
        }

        var text = Encoding.UTF8.GetString(probe.Value.StandardOutput).Trim();

        return string.Equals(text, "true", StringComparison.Ordinal);
    }

    private static bool IsLowerHex(string candidate)
    {
        foreach (var sign in candidate)
        {
            if ((sign < '0' || sign > '9') && (sign < 'a' || sign > 'f'))
            {
                return false;
            }
        }

        return true;
    }

    // ---------------------------------------------------------------------
    // Shared helpers.
    // ---------------------------------------------------------------------

    private static FileEnumerationResult Assemble(
        List<EnumeratedFile> files,
        List<HistoryCandidate> history,
        List<ScanWarning> warnings,
        List<string> notes,
        bool isInsideGitWorkTree)
    {
        // Every emitted list is sorted, so two runs of one repository give
        // the same result (rule 6).
        files.Sort(static (left, right) => string.CompareOrdinal(left.Path, right.Path));
        history.Sort(static (left, right) =>
        {
            var byCommit = string.CompareOrdinal(left.CommitId, right.CommitId);
            return byCommit != 0 ? byCommit : string.CompareOrdinal(left.Path, right.Path);
        });
        notes.Sort(string.CompareOrdinal);
        warnings.Sort(static (left, right) =>
        {
            var byCode = string.CompareOrdinal(left.Code, right.Code);
            return byCode != 0 ? byCode : string.CompareOrdinal(left.Message, right.Message);
        });

        return new FileEnumerationResult(
            files,
            history,
            warnings,
            notes,
            isInsideGitWorkTree);
    }

    /// <summary>
    /// Normalises one repository-relative path: forward slashes, Unicode
    /// Normalisation Form C (rule 22, section 6 of the configuration
    /// specification).
    /// </summary>
    private static string NormalisePath(string path)
    {
        var unified = path.Replace('\\', '/');

        if (IsPlainAscii(unified))
        {
            return unified;
        }

        return unified.Normalize(System.Text.NormalizationForm.FormC);
    }

    private static bool IsPlainAscii(string candidate)
    {
        foreach (var sign in candidate)
        {
            if (sign > 0x7F)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Splits a byte listing on newline boundaries, treating the last
    /// unterminated line as a line. NUL-separated listings use their own
    /// parser.
    /// </summary>
    private static IEnumerable<string> SplitUtf8Lines(byte[] output)
    {
        var text = Encoding.UTF8.GetString(output);
        var index = 0;

        while (index < text.Length)
        {
            var newline = text.IndexOf('\n', index);

            if (newline < 0)
            {
                var rest = text[index..].TrimEnd('\r');

                if (rest.Length > 0)
                {
                    yield return rest;
                }

                yield break;
            }

            var line = text[index..newline].TrimEnd('\r');

            if (line.Length > 0)
            {
                yield return line;
            }

            index = newline + 1;
        }
    }

    /// <summary>
    /// Splits a NUL-separated listing into non-empty records.
    /// </summary>
    internal static IEnumerable<string> SplitUtf8Records(string listing)
    {
        var index = 0;

        while (index < listing.Length)
        {
            var terminator = listing.IndexOf('\0', index);

            if (terminator < 0)
            {
                yield return listing[index..];
                yield break;
            }

            if (terminator > index)
            {
                yield return listing[index..terminator];
            }

            index = terminator + 1;
        }
    }

    /// <summary>
    /// Parses a NUL-separated <c>git ls-files --stage</c> listing: each
    /// record is '<c>mode object stage</c>', a tab, then the path. A gitlink
    /// (a submodule entry) sits in mode 160000. Conflict stages dedupe to
    /// one entry per path.
    /// </summary>
    internal static IReadOnlyList<IndexEntry> ParseIndexEntries(byte[] listing)
    {
        var entries = new List<IndexEntry>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var record in SplitUtf8Records(Encoding.UTF8.GetString(listing)))
        {
            var tab = record.IndexOf('\t', StringComparison.Ordinal);

            if (tab < 0)
            {
                continue;
            }

            var header = record[..tab];
            var path = record[(tab + 1)..];
            var firstSpace = header.IndexOf(' ', StringComparison.Ordinal);

            if (firstSpace < 0)
            {
                continue;
            }

            // Anything after the first space in the header belongs to the
            // object id and the stage; the mode is what decides the entry.
            var mode = header[..firstSpace];
            var candidate = new IndexEntry(NormalisePath(path), IsSubmodule: mode == GitlinkRecordMode);

            if (seen.Add(candidate.Path))
            {
                entries.Add(candidate);
            }
        }

        return entries;
    }

    /// <summary>One entry of the git index listing.</summary>
    internal sealed record IndexEntry(string Path, bool IsSubmodule);

    /// <summary>One object of the listing reachable from <c>HEAD</c>.</summary>
    /// <param name="CommitId">The 40-character identifier of the newest commit whose tree still contains the object.</param>
    /// <param name="ObjectId">The 40-character lowercase hexadecimal object identifier.</param>
    /// <param name="Path">The path the traversal printed; empty for a root tree.</param>
    private readonly record struct HistoryClaim(string CommitId, string ObjectId, string Path);
}
