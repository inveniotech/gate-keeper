// SPDX-License-Identifier: Apache-2.0

namespace InvenioTech.GateKeeper.Cli;

/// <summary>
/// Parses the arguments of <c>gatekeeper-ndpa-check</c>. The parser has no
/// dependency of its own, uses only ordinal string comparison and does no
/// reflection, so it stays ahead-of-time safe (rule 7). The card's note about a
/// parser library stands as a rule-8 question; the parsing surface is small
/// enough that a hand-written parser is zero dependencies and exactly correct.
/// </summary>
public static class CliCommandParser
{
    private const string VersionFlag = "--version";
    private const string HelpFlag = "--help";
    private const string HelpFlagShort = "-h";

    /// <summary>
    /// Parses the argument list into one command.
    /// </summary>
    /// <param name="args">The arguments, without the executable name.</param>
    /// <param name="invocation">The parsed invocation, when the parse succeeded.</param>
    /// <param name="error">The user-facing error sentence, when the parse failed. It says what happened and what to do next.</param>
    /// <returns>True when the parse succeeded.</returns>
    public static bool TryParse(string[] args, out CliInvocation? invocation, out string? error)
    {
        if (args.Length == 0)
        {
            invocation = new CliInvocation(CliCommandKind.Scan);
            error = null;
            return true;
        }

        var head = args[0];
        if (head == VersionFlag)
        {
            return args.Length > 1
                ? FlagWithUnexpectedArgument(VersionFlag, args[1], out invocation, out error)
                : Success(new CliInvocation(CliCommandKind.Version), out invocation, out error);
        }

        if (head is HelpFlag or HelpFlagShort)
        {
            return args.Length > 1
                ? FlagWithUnexpectedArgument(HelpFlag, args[1], out invocation, out error)
                : Success(new CliInvocation(CliCommandKind.Help), out invocation, out error);
        }

        if (head.StartsWith('-'))
        {
            invocation = null;
            error = $"'{head}' is not a flag this build knows. Run the binary with {HelpFlag} to see what it carries.";
            return false;
        }

        if (head == "scaffold")
        {
            return ParseScaffold(args, out invocation, out error);
        }

        var commandKind = head switch
        {
            "scan" => CliCommandKind.Scan,
            "init" => CliCommandKind.Init,
            "baseline" => CliCommandKind.Baseline,
            "doctor" => CliCommandKind.Doctor,
            "support-bundle" => CliCommandKind.SupportBundle,
            "version" => CliCommandKind.Version,
            _ => (CliCommandKind?)null,
        };

        if (commandKind is null)
        {
            invocation = null;
            error = $"'{head}' is not a command this build knows. Run the binary with {HelpFlag} to see what it carries.";
            return false;
        }

        return args.Length > 1
            ? CommandWithUnexpectedArgument(head, args[1], out invocation, out error)
            : Success(new CliInvocation(commandKind.Value), out invocation, out error);
    }

    private static bool ParseScaffold(string[] args, out CliInvocation? invocation, out string? error)
    {
        if (args.Length == 1)
        {
            invocation = null;
            error = $"The 'scaffold' command needs its subcommand. This build carries 'scaffold consent'. Run the binary with {HelpFlag} to see what it carries.";
            return false;
        }

        if (args[1] != "consent")
        {
            invocation = null;
            error = $"The 'scaffold' command does not know '{args[1]}'. This build carries 'scaffold consent'. Run the binary with {HelpFlag} to see what it carries.";
            return false;
        }

        return args.Length > 2
            ? CommandWithUnexpectedArgument(args[0], args[2], out invocation, out error)
            : Success(new CliInvocation(CliCommandKind.ScaffoldConsent), out invocation, out error);
    }

    private static bool Success(CliInvocation parsed, out CliInvocation? invocation, out string? error)
    {
        invocation = parsed;
        error = null;
        return true;
    }

    private static bool FlagWithUnexpectedArgument(
        string flag,
        string unexpected,
        out CliInvocation? invocation,
        out string? error)
    {
        invocation = null;
        error = $"The {flag} flag takes no other argument. '{unexpected}' was passed with it. Run the binary with {HelpFlag} to see what it carries.";
        return false;
    }

    private static bool CommandWithUnexpectedArgument(
        string command,
        string unexpected,
        out CliInvocation? invocation,
        out string? error)
    {
        invocation = null;
        error = $"The '{command}' command takes no other argument in this build. '{unexpected}' was passed with it. Run the binary with {HelpFlag} to see what it carries.";
        return false;
    }
}
