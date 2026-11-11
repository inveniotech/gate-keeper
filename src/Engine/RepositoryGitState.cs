// SPDX-License-Identifier: Apache-2.0

namespace InvenioTech.GateKeeper.Engine;

/// <summary>Local Git installation facts used by doctor, without retaining command output or key contents.</summary>
/// <param name="Available">Whether the local Git executable runs successfully.</param>
/// <param name="InsideWorkTree">Whether the directory is inside a Git work tree.</param>
/// <param name="FingerprintKeyTracked">Whether the index tracks the root-local fingerprint key.</param>
public sealed record RepositoryGitState(bool Available, bool InsideWorkTree, bool FingerprintKeyTracked)
{
    /// <summary>Checks the executable, work-tree state and key path through the existing local Git runner. Cancellation propagates and no remote is contacted.</summary>
    public static async Task<RepositoryGitState> ReadAsync(string root, CancellationToken cancellationToken = default)
    {
        var git = new GitRunner();
        var version = await git.TryRunAsync(root, ["--version"], cancellationToken).ConfigureAwait(false);
        if (version is not { ExitCode: 0 }) return new(false, false, false);
        var tree = await git.TryRunAsync(root, ["rev-parse", "--is-inside-work-tree"], cancellationToken).ConfigureAwait(false);
        if (tree is not { ExitCode: 0 } value || System.Text.Encoding.UTF8.GetString(value.StandardOutput).Trim() != "true") return new(true, false, false);
        var tracked = await git.TryRunAsync(root, ["ls-files", "--error-unmatch", "--", ".gatekeeper/fingerprint.key"], cancellationToken).ConfigureAwait(false);
        return new(true, true, tracked is { ExitCode: 0 });
    }
}
