// SPDX-License-Identifier: Apache-2.0

namespace InvenioTech.GateKeeper.Engine;

/// <summary>
/// One history candidate: the decoded text of a git blob reachable from
/// <c>HEAD</c> at the path of the commit that contained it, with the commit
/// identifier of the newest commit whose tree still contains the blob.
/// History findings are never fingerprinted and never baselined (IQ-83); the
/// matched value is never printed (rule 10), and the commit identifier is
/// 40 lowercase hexadecimal characters.
/// </summary>
/// <param name="CommitId">The 40-character lowercase hexadecimal commit identifier.</param>
/// <param name="Path">The forward-slash path of the blob inside its commit.</param>
/// <param name="Text">The decoded text of the blob.</param>
/// <exception cref="ArgumentException">The commit identifier is not hexadecimal or the path is empty.</exception>
public sealed record HistoryCandidate(string CommitId, string Path, string Text)
{
    /// <summary>Gets the 40-character lowercase hexadecimal commit identifier.</summary>
    public string CommitId { get; } = IsCommitId(CommitId)
        ? CommitId
        : throw new ArgumentException(
            "A history candidate carries the 40 lowercase hexadecimal characters of the commit that contains it.",
            nameof(CommitId));

    /// <summary>Gets the forward-slash path of the blob inside its commit.</summary>
    public string Path { get; } = string.IsNullOrWhiteSpace(Path)
        ? throw new ArgumentException(
            "A history candidate needs the repository-relative path of the blob.",
            nameof(Path))
        : Path;

    private static bool IsCommitId(string candidate)
    {
        if (candidate.Length != 40)
        {
            return false;
        }

        foreach (var sign in candidate)
        {
            if ((sign < '0' || sign > '9') && (sign < 'a' || sign > 'f'))
            {
                return false;
            }
        }

        return true;
    }
}

