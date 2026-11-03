// SPDX-License-Identifier: Apache-2.0

namespace InvenioTech.GateKeeper.Engine;

/// <summary>
/// The only three exit codes the command-line interface uses: 0 for a pass,
/// 1 for violations found, and 2 for a tool error (rule 13). A tool error is
/// never used for "violations found", and licence problems never change the
/// exit code.
/// </summary>
public enum ExitOutcome
{
    /// <summary>
    /// The command finished and no reported finding that is still able to fail
    /// meets the failure threshold.
    /// </summary>
    Pass = 0,

    /// <summary>
    /// The scan finished and at least one reported finding meets the failure
    /// threshold.
    /// </summary>
    Violations = 1,

    /// <summary>
    /// The command could not run as asked: an invalid argument, an unreadable
    /// configuration or a similar fault. Nothing is claimed about findings.
    /// </summary>
    ToolError = 2,
}
