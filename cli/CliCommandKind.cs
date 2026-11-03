// SPDX-License-Identifier: Apache-2.0

namespace InvenioTech.GateKeeper.Cli;

/// <summary>
/// The command the command-line interface will run for one invocation. The
/// bare invocation, with no arguments at all, means
/// <see cref="CliCommandKind.Scan"/>. The help flag is a surface flag, not one
/// of the card's commands: it prints what the build carries and exits.
/// </summary>
public enum CliCommandKind
{
    /// <summary>Scan a repository and write the results files.</summary>
    Scan = 0,

    /// <summary>Create <c>.ndpa-check/</c> with a starter configuration and the fingerprint key (IQ-86).</summary>
    Init = 1,

    /// <summary>Write the baseline file from the current findings (IQ-19).</summary>
    Baseline = 2,

    /// <summary>Scaffold a consent helper into an application (IQ-30, built in Phase 3).</summary>
    ScaffoldConsent = 3,

    /// <summary>Explain the configuration, the licence and the installation state.</summary>
    Doctor = 4,

    /// <summary>Create the support bundle for a support request.</summary>
    SupportBundle = 5,

    /// <summary>Print the product version and the bundled pack version.</summary>
    Version = 6,

    /// <summary>Print the command list; produced by the <c>--help</c> and <c>-h</c> flags.</summary>
    Help = 7,
}

/// <summary>
/// One command-line invocation as parsed: the command to run. Version 1 of the
/// command surface carries no per-command options yet, so an invocation is only
/// the command name. The bare invocation with no arguments is the scan command
/// (the card for P1-01).
/// </summary>
/// <param name="Command">The command the arguments named.</param>
public sealed record CliInvocation(CliCommandKind Command);
