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
    private readonly IReadOnlyList<Rule>? rules;

    public CorpusScanRunner(CorpusCase @case, IReadOnlyList<Rule>? rules = null)
    {
        this.@case = @case;
        if (rules is { Count: 0 }) throw new ArgumentException("A measured corpus run needs at least one supplied rule.", nameof(rules));
        this.rules = rules?.ToArray();
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
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            CopyRepository(copyRoot);
            var enumeration = await new FileEnumerator().EnumerateAsync(
            new FileEnumerationOptions
            {
                Root = copyRoot,
            },
            cancellationToken).ConfigureAwait(true);

            if (rules is null) return new CorpusScan(this.@case, copyRoot, enumeration, Findings: []);
            // Corpus acceptance checks semantic results, independently of runner
            // scheduling. Production deadline propagation is covered in ShapeCompilerTests.
            var matcher = new MatcherEngine(rules, shapes: new ShapeRegexCache(Timeout.InfiniteTimeSpan));
            var result = await new ScanOrchestrator(matcher).ScanAsync(enumeration, cancellationToken).ConfigureAwait(true);
            var findings = result.Findings.Select(match => new Finding(match.RuleId, match.Severity, match.Category, match.Path,
                match.Line, match.Column, match.LocationKind, null, BaselineState.New, match.Citation)).ToArray();
            return new CorpusScan(this.@case, copyRoot, enumeration, findings, MatcherStagePresent: true);
        }
        catch
        {
            if (Directory.Exists(copyRoot)) Directory.Delete(copyRoot, recursive: true);
            throw;
        }
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
    IReadOnlyList<Finding> Findings,
    bool MatcherStagePresent = false)
{
    /// <summary>Gets the scanned work-tree files, in ordinal path order.</summary>
    public IReadOnlyList<EnumeratedFile> Files => this.Enumeration.Files;

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
