// SPDX-License-Identifier: Apache-2.0

using System.Text;

namespace InvenioTech.GateKeeper.Engine;

/// <summary>Repository identity and revision for evidence; remote credentials are never retained.</summary>
/// <param name="Id">The explicit or normalised remote identity, or empty.</param>
/// <param name="Revision">The current commit identifier, or empty outside Git or before the first commit.</param>
public sealed record RepositoryMetadata(string Id, string Revision)
{
    /// <summary>Reads local Git metadata under configuration section 10. Never fetches remote objects.</summary>
    public static async Task<RepositoryMetadata> ReadAsync(string root, string? explicitId, CancellationToken cancellationToken = default)
    {
        var git = new GitRunner();
        var revision = await git.TryRunAsync(root, ["rev-parse", "--verify", "HEAD"], cancellationToken).ConfigureAwait(false);
        var commit = revision is { ExitCode: 0 } r ? Encoding.UTF8.GetString(r.StandardOutput).Trim() : "";
        if (explicitId is not null) return new(explicitId, commit);
        var remotes = await git.TryRunAsync(root, ["remote"], cancellationToken).ConfigureAwait(false);
        var names = remotes is { ExitCode: 0 } result ? Encoding.UTF8.GetString(result.StandardOutput).Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries) : [];
        var name = names.Contains("origin", StringComparer.Ordinal) ? "origin" : names.Length == 1 ? names[0] : null;
        if (name is null) return new("", commit);
        var remote = await git.TryRunAsync(root, ["remote", "get-url", name], cancellationToken).ConfigureAwait(false);
        return new(remote is { ExitCode: 0 } value ? NormaliseRemote(Encoding.UTF8.GetString(value.StandardOutput).Trim()) : "", commit);
    }

    /// <summary>Normalises an HTTPS, SSH or SCP-style remote, removing user information, port, query and fragment. Local paths yield an empty identity.</summary>
    public static string NormaliseRemote(string remote)
    {
        if (!remote.Contains("://", StringComparison.Ordinal))
        {
            var at = remote.IndexOf('@');
            var colon = remote.IndexOf(':', at + 1);
            if (at < 0 || colon <= at + 1) return "";
            remote = "ssh://" + remote[..colon] + "/" + remote[(colon + 1)..];
        }
        if (!Uri.TryCreate(remote, UriKind.Absolute, out var uri) || uri.Scheme is not ("https" or "http" or "ssh" or "git") || uri.Host.Length == 0) return "";
        var path = uri.AbsolutePath.TrimEnd('/');
        if (path.EndsWith(".git", StringComparison.Ordinal)) path = path[..^4];
        return uri.Host.ToLowerInvariant() + path;
    }
}
