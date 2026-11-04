// SPDX-License-Identifier: Apache-2.0

namespace InvenioTech.GateKeeper.Corpus.Tests;

/// <summary>
/// A fact that runs only when the symbolic-link fixture actually carries a
/// symbolic link on this checkout. Windows machines without the privilege
/// find a plain text file where the git symlink was, so the fixture could
/// never reach the behaviour it names; the scan then records that well-known
/// skip and never a flaky failure (rule 26, rule 30).
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public sealed class CorpusSymlinkFactAttribute : FactAttribute
{
    public CorpusSymlinkFactAttribute()
    {
        var linkPath = Path.Combine(
            CorpusPaths.Root,
            "symbolic-link",
            "repo",
            "link-to-dispatch-notes.log");

        if (!File.Exists(linkPath) || new FileInfo(linkPath).LinkTarget is null)
        {
            Skip = "This checkout did not materialise the corpus link as a symbolic link.";
        }
    }
}
