// SPDX-License-Identifier: Apache-2.0

namespace InvenioTech.GateKeeper.Cli;

/// <summary>
/// Identifies the command-line interface while it is still a placeholder.
/// </summary>
public static class CliModule
{
    /// <summary>
    /// Gets the scaffold status. The value is <c>not-started</c> until the scan command is implemented.
    /// </summary>
    public const string Status = "not-started";

    /// <summary>
    /// Runs the placeholder. It scans nothing and makes no network access.
    /// </summary>
    /// <returns>Zero, because the placeholder completed without a tool error.</returns>
    public static int Run() => 0;
}
