// SPDX-License-Identifier: Apache-2.0

namespace InvenioTech.GateKeeper.Engine;

/// <summary>
/// The file enumerator: the one component that decides which files a scan
/// reads. Inside a git work tree it enumerates the tracked files of
/// <c>git ls-files</c>; outside one it walks the directory, applies the
/// <c>.gitignore</c> semantics and writes the outside-git warning; the
/// built-in exclusions, the <c>scan.exclude</c> globs, the size limit, the
/// binary and encoding classification, the Git LFS pointer skip, the
/// submodules, the <c>--changed-since</c> limit and, on request, the history
/// blobs reachable from <c>HEAD</c> are all its work (IQ-21, IQ-83, sections
/// 6 and 7 of the configuration specification).
/// </summary>
public sealed class FileEnumerator
{
    /// <summary>
    /// Enumerates the scan input for the given options.
    /// </summary>
    /// <param name="options">The validated options for this run.</param>
    /// <param name="cancellationToken">The token that cancels the run.</param>
    /// <returns>The files, the history candidates, the warnings and the notes.</returns>
    /// <exception cref="EngineToolException">A tool condition the user must repair: a bad glob, a reference that does not resolve, or a root that is not the repository root.</exception>
    public Task<FileEnumerationResult> EnumerateAsync(
        FileEnumerationOptions options,
        CancellationToken cancellationToken = default) =>

        throw new NotImplementedException("The file enumerator is not implemented yet.");
}
