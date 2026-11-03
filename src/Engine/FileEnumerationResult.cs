// SPDX-License-Identifier: Apache-2.0

namespace InvenioTech.GateKeeper.Engine;

/// <summary>
/// The outcome of one enumeration run: the scanned files of the work tree,
/// the history candidates when history was requested, the closed-set
/// warnings the evidence file records, the skip notes the console shows, and
/// whether the scan ran inside a git work tree (section 6 of the
/// configuration specification). Every list is sorted the way the
/// specification orders it, so two runs of one repository produce the same
/// result (rule 6).
/// </summary>
/// <param name="Files">The scanned work-tree files, in ordinal path order.</param>
/// <param name="History">The history candidates, in commit identifier then path order.</param>
/// <param name="Warnings">The closed-set warnings, in code then message order.</param>
/// <param name="Notes">The skip notes, in ordinal path order.</param>
/// <param name="IsInsideGitWorkTree">Whether the scan ran with git's index as the source of paths.</param>
public sealed record FileEnumerationResult(
    IReadOnlyList<EnumeratedFile> Files,
    IReadOnlyList<HistoryCandidate> History,
    IReadOnlyList<ScanWarning> Warnings,
    IReadOnlyList<string> Notes,
    bool IsInsideGitWorkTree)
{
    /// <summary>Gets the scanned work-tree files, in ordinal path order.</summary>
    public IReadOnlyList<EnumeratedFile> Files { get; } = Files;

    /// <summary>Gets the history candidates, in commit identifier then path order.</summary>
    public IReadOnlyList<HistoryCandidate> History { get; } = History;

    /// <summary>Gets the closed-set warnings, in code then message order.</summary>
    public IReadOnlyList<ScanWarning> Warnings { get; } = Warnings;

    /// <summary>Gets the skip notes, in ordinal path order.</summary>
    public IReadOnlyList<string> Notes { get; } = Notes;
}
