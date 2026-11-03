// SPDX-License-Identifier: Apache-2.0

using InvenioTech.GateKeeper.Engine;

namespace InvenioTech.GateKeeper.Cli;

/// <summary>
/// The composition root of the command line: parse the arguments, then run the
/// one command they name. Results go to the output writer and diagnostics to
/// the error writer (rule 31). Only the version surface works in this build;
/// every other command is honest about not being implemented yet and exits
/// with a tool error, never pretending it scanned anything (rule 13, rule 23).
/// </summary>
public static class CliModule
{
    /// <summary>
    /// Runs the command line for the given arguments, writing to the console's
    /// standard output and standard error.
    /// </summary>
    /// <param name="args">The arguments, without the executable name.</param>
    /// <returns>The process exit code: 0 pass, 1 violations, 2 tool error (rule 13).</returns>
    public static int Run(string[] args) => Run(args, Console.Out, Console.Error);

    /// <summary>
    /// Runs the command line against the given writers, so tests can observe
    /// exactly what was written and where.
    /// </summary>
    /// <param name="args">The arguments, without the executable name.</param>
    /// <param name="output">The writer for command results, which is standard output in production.</param>
    /// <param name="errors">The writer for diagnostics, which is standard error in production.</param>
    /// <returns>The process exit code: 0 pass, 1 violations, 2 tool error (rule 13).</returns>
    public static int Run(string[] args, TextWriter output, TextWriter errors)
    {
        if (!CliCommandParser.TryParse(args, out var invocation, out var parseError))
        {
            errors.WriteLine(parseError);
            return (int)ExitOutcome.ToolError;
        }

        return invocation!.Command switch
        {
            CliCommandKind.Version => PrintVersion(output),
            CliCommandKind.Help => PrintHelp(output),
            _ => PrintNotYetImplemented(invocation.Command, errors),
        };
    }

    private static int PrintVersion(TextWriter output)
    {
        output.WriteLine($"product: {EngineProduct.Product}");
        output.WriteLine($"product version: {EngineProduct.EngineVersion}");
        output.WriteLine(
            EngineProduct.BundledPackVersion is null
                ? "bundled pack version: none; this build does not yet bundle a rule pack"
                : $"bundled pack version: {EngineProduct.BundledPackVersion}");
        return (int)ExitOutcome.Pass;
    }

    private static int PrintHelp(TextWriter output)
    {
        output.WriteLine("gatekeeper-ndpa-check — scans a repository for personal data the Nigeria Data Protection Act 2023 protects. The detection rules ship as data in rule packs.");
        output.WriteLine(string.Empty);
        output.WriteLine("Commands in this build:");
        output.WriteLine("  scan (the default)   scans a repository and writes the results files; not implemented yet");
        output.WriteLine("  init                 creates .ndpa-check/ with a starter configuration and the fingerprint key; not implemented yet");
        output.WriteLine("  baseline             writes the baseline file from the current findings; not implemented yet");
        output.WriteLine("  scaffold consent     scaffolds the consent helper into an application; built in Phase 3");
        output.WriteLine("  doctor               explains the configuration, the licence and the installation state; not implemented yet");
        output.WriteLine("  support-bundle       creates the support bundle for a support request; not implemented yet");
        output.WriteLine("  version              prints the product version and the bundled pack version");
        output.WriteLine(string.Empty);
        output.WriteLine($"Flags: --version, {HelpFlag}.");
        return (int)ExitOutcome.Pass;
    }

    private static int PrintNotYetImplemented(CliCommandKind command, TextWriter errors)
    {
        var named = command == CliCommandKind.Scan ? "scan (the default)"
            : command == CliCommandKind.Init ? "init"
            : command == CliCommandKind.Baseline ? "baseline"
            : command == CliCommandKind.ScaffoldConsent ? "scaffold consent"
            : command == CliCommandKind.Doctor ? "doctor"
            : "support-bundle";

        errors.WriteLine(
            $"The {named} command is not in this build yet, so nothing ran and nothing was scanned. "
            + "Run the binary with --help to see what this build carries.");
        return (int)ExitOutcome.ToolError;
    }

    private const string HelpFlag = "--help";
}
