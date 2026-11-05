// SPDX-License-Identifier: Apache-2.0

using System.CommandLine;
using System.CommandLine.Invocation;
using InvenioTech.GateKeeper.Engine;

namespace InvenioTech.GateKeeper.Cli;

/// <summary>
/// The composition root of the command line: build the command tree with
/// System.CommandLine, parse the arguments, and run the one command they name.
/// Results go to the output writer and diagnostics to the error writer
/// (rule 31). The scan, version and baseline surfaces work in this build; each remaining
/// command is honest about not being implemented yet and exits with a tool
/// error, never pretending it scanned anything (rule 13, rule 23).
///
/// The library was approved under rule 8 by the owner on 3 November 2026,
/// after the hand-written parser shipped in the first attempt at this card.
/// The library brings the standard grammar, the standard help rendering and
/// shell-completion integration; the owner's law sentences stay ours, so the
/// parse-law messages are written by this module, never by the library.
/// </summary>
public static class CliModule
{
    private const string VersionFlag = "--version";
    private const string HelpFlag = "--help";

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
        var parsed = PrepareInvocation(args, output, errors, null);
        return parsed is null ? (int)ExitOutcome.ToolError : parsed.Invoke(new InvocationConfiguration { Output = output, Error = errors });
    }

    /// <summary>Runs an invocation asynchronously. The optional root supplies the repository for baseline; defaults to the current directory. Cancellation propagates.</summary>
    public static async Task<int> RunAsync(string[] args, TextWriter? output = null, TextWriter? errors = null,
        string? rootPath = null, CancellationToken cancellationToken = default)
    {
        output ??= Console.Out;
        errors ??= Console.Error;
        var parsed = PrepareInvocation(args, output, errors, rootPath);
        return parsed is null ? (int)ExitOutcome.ToolError
            : await parsed.InvokeAsync(new InvocationConfiguration { Output = output, Error = errors }, cancellationToken).ConfigureAwait(false);
    }

    private static ParseResult? PrepareInvocation(string[] args, TextWriter output, TextWriter errors, string? rootPath)
    {
        var rootCommand = BuildCommandTree(output, errors, rootPath);
        var parsed = rootCommand.Parse(args);
        if (parsed.Errors.Count > 0)
        {
            errors.WriteLine(ParseFailedLawSentence(parsed, rootCommand, args));
            return null;
        }
        var versionIndex = Array.IndexOf(args, VersionFlag);
        if (versionIndex >= 0 && args.Length > 1)
        {
            errors.WriteLine(VersionLawSentence(versionIndex == 0 ? args[1] : args[0]));
            return null;
        }
        return parsed;
    }

    private static RootCommand BuildCommandTree(TextWriter output, TextWriter errors, string? rootPath)
    {
        var rootCommand = new RootCommand(
            "Scans a repository for personal data that the Nigeria Data Protection Act 2023 protects. " +
            "The detection rules ship as data in rule packs. Running the binary with no arguments runs the scan.");

        ReplaceDefaultVersionOption(rootCommand, new PrintVersionAction(output));

        ScanCli.Configure(rootCommand, output, errors, rootPath ?? Directory.GetCurrentDirectory());

        var scan = new Command("scan", "Scans a repository and writes a console summary or evidence JSON");
        ScanCli.Configure(scan, output, errors, rootPath ?? Directory.GetCurrentDirectory());
        rootCommand.Subcommands.Add(scan);
        rootCommand.Subcommands.Add(NotImplementedCommand("init", "Creates .ndpa-check/ with a starter configuration and the fingerprint key; not implemented in this build", errors));
        rootCommand.Subcommands.Add(BaselineCli.Create(output, errors, rootPath ?? Directory.GetCurrentDirectory()));
        rootCommand.Subcommands.Add(NotImplementedCommand("doctor", "Explains the configuration, the licence and the installation state; not implemented in this build", errors));
        rootCommand.Subcommands.Add(NotImplementedCommand("support-bundle", "Creates the support bundle for a support request; not implemented in this build", errors));
        rootCommand.Subcommands.Add(new Command("version", "Prints the product version and the bundled pack version")
        {
            Action = new PrintVersionAction(output),
        });

        var scaffold = new Command(
            "scaffold",
            "Scaffolds a helper into an application. This build carries the 'consent' subcommand; the scaffolds for the other helpers arrive in Phase 3.");
        scaffold.Subcommands.Add(NotImplementedCommand("consent", "Scaffolds the consent helper into an application; built in a Phase 3 release", errors));

        // The scaffold need not run unimplemented sub-commands: the root law for
        // the unimplemented scaffold prints what build carries.
        scaffold.SetAction(_ =>
        {
            errors.WriteLine(
                "The 'scaffold' command needs its subcommand. This build carries 'scaffold consent'. " +
                $"Run the binary with {HelpFlag} to see what it carries.");
            return (int)ExitOutcome.ToolError;
        });

        rootCommand.Subcommands.Add(scaffold);
        return rootCommand;
    }

    /// <summary>
    /// Removes the library's default version option and puts our version
    /// action in its place, so the printed wording is ours and the parse errors
    /// of an unexpected argument are not swept away by a default behaviour.
    /// </summary>
    private static void ReplaceDefaultVersionOption(RootCommand rootCommand, SynchronousCommandLineAction action)
    {
        Option? previous = null;
        foreach (var option in rootCommand.Options)
        {
            if (option is VersionOption)
            {
                previous = option;
                break;
            }
        }

        if (previous is not null)
        {
            rootCommand.Options.Remove(previous);
        }

        var versionOption = new Option<bool>(VersionFlag)
        {
            Description = "Prints the product version and the bundled pack version",
            Arity = ArgumentArity.Zero,
            Action = action,
        };
        rootCommand.Options.Add(versionOption);
    }

    private static Command NotImplementedCommand(string name, string description, TextWriter errors)
    {
        return new Command(name, description)
        {
            Action = new NotYetImplementedAction(name, errors),
        };
    }

    private static int NotYetImplemented(string commandName, TextWriter errors)
    {
        errors.WriteLine(
            $"The {commandName} command is not in this build yet, so nothing ran and nothing was scanned. " +
            $"Run the binary with {HelpFlag} to see what this build carries.");
        return (int)ExitOutcome.ToolError;
    }

    private static string VersionLawSentence(string token) =>
        $"The {VersionFlag} flag takes no other argument. '{token}' was passed with it. " +
        $"Run the binary with {HelpFlag} to see what it carries.";

    private static string ParseFailedLawSentence(ParseResult parseResult, RootCommand rootCommand, string[] args)
    {
        var unmatched = parseResult.UnmatchedTokens;
        var first = unmatched.Count > 0 ? unmatched[0] : parseResult.Errors[0].Message;
        var matchedCommand = parseResult.CommandResult.Command;

        // Look for the version flag in the raw arguments, so the law sentence
        // for a version invocation never blames the wrong argument.
        if (Array.IndexOf(args, VersionFlag) >= 0)
        {
            return VersionLawSentence(first);
        }

        if (ReferenceEquals(matchedCommand, rootCommand))
        {
            // The root command matched, so the offending token names no command.
            return
                $"'{first}' is not a command or flag this build knows. " +
                $"Run the binary with {HelpFlag} to see what it carries.";
        }

        if (matchedCommand.Name == "scaffold" && unmatched.Count > 0)
        {
            return
                $"The '{matchedCommand.Name}' command does not know '{first}'. This build carries 'scaffold consent'. " +
                $"Run the binary with {HelpFlag} to see what it carries.";
        }

        return
            $"The '{matchedCommand.Name}' command takes no other argument in this build. '{first}' was passed with it. " +
            $"Run the binary with {HelpFlag} to see what it carries.";
    }

    /// <summary>
    /// Prints exactly the three lines the version contract requires: the
    /// product's name, the product's version, and the bundled pack version or
    /// the absence of one, because the pack build step does not run until a
    /// later card (rule 16: the absence is state, never an invented number).
    /// </summary>
    private sealed class PrintVersionAction : SynchronousCommandLineAction
    {
        private readonly TextWriter _output;

        public PrintVersionAction(TextWriter output)
        {
            _output = output;
        }

        public override int Invoke(ParseResult parseResult)
        {
            _output.WriteLine($"product: {EngineProduct.Product}");
            _output.WriteLine($"product version: {EngineProduct.EngineVersion}");
            _output.WriteLine(
                EngineProduct.BundledPackVersion is null
                    ? "bundled pack version: none; this build does not yet bundle a rule pack"
                    : $"bundled pack version: {EngineProduct.BundledPackVersion}");
            return (int)ExitOutcome.Pass;
        }
    }

    /// <summary>
    /// The tool-error action every unimplemented command shares: it names the
    /// command, says nothing ran, and points the user at the command list
    /// (rule 23).
    /// </summary>
    private sealed class NotYetImplementedAction : SynchronousCommandLineAction
    {
        private readonly string _name;
        private readonly TextWriter _errors;

        public NotYetImplementedAction(string name, TextWriter errors)
        {
            _name = name;
            _errors = errors;
        }

        public override int Invoke(ParseResult parseResult)
        {
            _errors.WriteLine(
                $"The {_name} command is not in this build yet, so nothing ran and nothing was scanned. " +
                $"Run the binary with {HelpFlag} to see what this build carries.");
            return (int)ExitOutcome.ToolError;
        }
    }
}
