// SPDX-License-Identifier: Apache-2.0

using InvenioTech.GateKeeper.Engine;

namespace InvenioTech.GateKeeper.Corpus.Tests;

/// <summary>
/// Runs the engine over one corpus case's repository and hands the scan
/// output to the scorers. The case's <c>repo/</c> folder is copied into the
/// machine's temporary directory first: the corpus lives inside this
/// repository's own work tree, and the engine refuses a root that is not the
/// repository root (section 6.1 of the configuration specification), so the
/// scan has to run on a directory outside any work tree. The copy keeps
/// symbolic links as symbolic links.
/// </summary>
internal sealed class CorpusScanRunner
{
    private const string ScannerRootMarker = "corpus-harness";

    private readonly CorpusCase @case;

    public CorpusScanRunner(CorpusCase @case)
    {
        this.@case = @case;
    }

    /// <summary>
    /// Copies the case's repository into the temporary directory and
    /// enumerates it through the engine's file enumerator. The returned
    /// instance owns the copy; dispose it to remove the directory.
    /// </summary>
    /// <param name="cancellationToken">The token that cancels the run.</param>
    public async Task<CorpusScan> RunAsync(CancellationToken cancellationToken = default)
    {
        var copyRoot = Path.Combine(
            Path.GetTempPath(),
            ScannerRootMarker,
            $"{this.@case.Name}-{Guid.NewGuid():N}");
        CopyRepository(copyRoot);

        var enumeration = await new FileEnumerator().EnumerateAsync(
            new FileEnumerationOptions
            {
                Root = copyRoot,
            },
            cancellationToken).ConfigureAwait(true);

        // The matcher engine (P1-06) can run rules. This harness still reports
        // an empty findings list: the scan orchestrator (P1-07) is what runs
        // the matcher over the enumeration, and the cited rule pack is later
        // work (P2). The scorers treat every expected finding as unmeasured
        // rather than missed, because a scan that is not yet given those
        // rules proves nothing about detection.
        return new CorpusScan(this.@case, copyRoot, enumeration, Findings: []);
    }

    private void CopyRepository(string copyRoot)
    {
        Directory.CreateDirectory(copyRoot);
        CopyDirectory(new DirectoryInfo(this.@case.RepositoryRoot), copyRoot);
    }

    private static void CopyDirectory(DirectoryInfo source, string destination)
    {
        foreach (var entry in source.EnumerateFileSystemInfos())
        {
            if (entry is DirectoryInfo directory)
            {
                var childDirectory = Path.Combine(destination, directory.Name);
                Directory.CreateDirectory(childDirectory);
                CopyDirectory(directory, childDirectory);
                continue;
            }

            var childPath = Path.Combine(destination, ((FileInfo)entry).Name);

            if (entry.LinkTarget is null)
            {
                ((FileInfo)entry).CopyTo(childPath, overwrite: false);
                continue;
            }

            if (Directory.Exists(entry.FullName))
            {
                Directory.CreateSymbolicLink(childPath, entry.LinkTarget);
            }
            else
            {
                File.CreateSymbolicLink(childPath, entry.LinkTarget);
            }
        }
    }
}

/// <summary>
/// One scan of one case repository: the engine's enumeration, the findings
/// the scan produced, and the temporary directory the scan ran on.
/// </summary>
internal sealed record CorpusScan(
    CorpusCase Case,
    string CopyRoot,
    FileEnumerationResult Enumeration,
    IReadOnlyList<Finding> Findings)
{
    /// <summary>Gets the scanned work-tree files, in ordinal path order.</summary>
    public IReadOnlyList<EnumeratedFile> Files => this.Enumeration.Files;

    /// <summary>
    /// Gets whether this harness ran the matcher over a rule pack. The matcher
    /// exists (P1-06); the orchestrator and the pack do not, so recall stays
    /// unmeasured.
    /// </summary>
    public bool MatcherStagePresent => false;
}

/// <summary>
/// Removes a scan's temporary directory when the test is done with it.
/// </summary>
internal sealed class CorpusScanScavenger : IDisposable
{
    private readonly CorpusScan scan;

    public CorpusScanScavenger(CorpusScan scan)
    {
        this.scan = scan;
    }

    public void Dispose()
    {
        var directory = new DirectoryInfo(this.scan.CopyRoot);

        if (directory.Exists)
        {
            directory.Delete(recursive: true);
        }
    }
}
