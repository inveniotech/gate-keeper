// SPDX-License-Identifier: Apache-2.0

namespace InvenioTech.GateKeeper.Engine.Tests;

/// <summary>
/// A fact that runs when the machine can create symbolic links, and is
/// reported as skipped when it cannot: unprivileged Windows installations
/// refuse symbolic links, so the fixture would never reach the behaviour it
/// names (rule 26, rule 30). The decision is made once per run, at test
/// discovery, so the outcome for this machine is a recorded skip and never a
/// flaky failure.
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public sealed class SymlinkFactAttribute : FactAttribute
{
    private static bool? canCreateSymlinks;

    public SymlinkFactAttribute()
    {
        if (!CanCreateSymlinks())
        {
            Skip = "This machine does not grant the symbolic-link privilege the fixture needs.";
        }
    }

    private static bool CanCreateSymlinks()
    {
        if (canCreateSymlinks.HasValue)
        {
            return canCreateSymlinks.Value;
        }

        var probeDirectory = Path.Combine(
            Path.GetTempPath(),
            "gatekeeper-tests",
            $"gk-probe-{Guid.NewGuid():N}");

        try
        {
            Directory.CreateDirectory(probeDirectory);
            var target = Path.Combine(probeDirectory, "target.txt");
            File.WriteAllText(target, "probe\n");

            File.CreateSymbolicLink(Path.Combine(probeDirectory, "link.txt"), target);
            canCreateSymlinks = true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            canCreateSymlinks = false;
        }
        finally
        {
            if (Directory.Exists(probeDirectory))
            {
                Directory.Delete(probeDirectory, recursive: true);
            }
        }

        return canCreateSymlinks.Value;
    }
}
