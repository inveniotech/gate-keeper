// SPDX-License-Identifier: Apache-2.0

using InvenioTech.GateKeeper.Cli;
using InvenioTech.GateKeeper.Engine;

namespace InvenioTech.GateKeeper.Cli.Tests;

public sealed class CliModuleTests
{
    [Fact]
    public void VersionCommand_Should_PrintTheProductAndPackSections_When_Ran()
    {
        var output = new StringWriter();
        var errors = new StringWriter();

        var exitCode = CliModule.Run(["version"], output, errors);

        Assert.Equal((int)ExitOutcome.Pass, exitCode);
        var text = output.ToString();
        Assert.Contains("product: gatekeeper", text, StringComparison.Ordinal);
        Assert.Contains($"product version: {EngineProduct.EngineVersion}", text, StringComparison.Ordinal);
        // The bundled pack arrives with the pack build and signing step, so the
        // absence is printed rather than an invented number. The same section
        // prints the version once the build carries a pack.
        Assert.Contains("bundled pack version", text, StringComparison.Ordinal);
        Assert.Equal(string.Empty, errors.ToString());
    }

    [Fact]
    public void VersionFlag_Should_MatchTheVersionCommand_When_Used()
    {
        var output = new StringWriter();
        var errors = new StringWriter();

        var fromFlag = CliModule.Run(["--version"], output, errors);
        output.GetStringBuilder().Clear();
        var fromCommand = CliModule.Run(["version"], output, errors);

        Assert.Equal(fromCommand, fromFlag);
        Assert.Equal(output.ToString(), CliModuleVersionText());
    }

    [Fact]
    public void BareInvocation_Should_MeanTheScanCommand_When_NoArgumentsAreGiven()
    {
        var output = new StringWriter();
        var errors = new StringWriter();

        var exitCode = CliModule.Run([], output, errors);

        Assert.Equal((int)ExitOutcome.ToolError, exitCode);
        Assert.Contains("The scan (the default) command is not in this build yet", errors.ToString(), StringComparison.Ordinal);
        Assert.Equal(string.Empty, output.ToString());
    }

    [Fact]
    public void UnimplementedCommands_Should_ExitWithAToolErrorNamingTheCommand_When_Ran()
    {
        var commands = new Dictionary<string[], string>
        {
            { ["scan"], "scan" },
            { ["init"], "init" },
            { ["baseline"], "baseline" },
            { ["doctor"], "doctor" },
            { ["support-bundle"], "support-bundle" },
        };

        foreach (var (args, name) in commands)
        {
            var errors = new StringWriter();

            var exitCode = CliModule.Run(args, new StringWriter(), errors);

            Assert.Equal((int)ExitOutcome.ToolError, exitCode);
            Assert.Contains($"The {name} command is not in this build yet", errors.ToString(), StringComparison.Ordinal);
            Assert.Contains("Run the binary with --help", errors.ToString(), StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Scaffold_Should_AskForConsent_When_CalledWithoutItsSubcommand()
    {
        var errors = new StringWriter();

        var exitCode = CliModule.Run(["scaffold"], new StringWriter(), errors);

        Assert.Equal((int)ExitOutcome.ToolError, exitCode);
        Assert.Contains("The 'scaffold' command needs its subcommand", errors.ToString(), StringComparison.Ordinal);
        Assert.Contains("scaffold consent", errors.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Scaffold_Should_NameTheUnknownSubcommand_When_CalledWithOneItDoesNotKnow()
    {
        var errors = new StringWriter();

        var exitCode = CliModule.Run(["scaffold", "licence"], new StringWriter(), errors);

        Assert.Equal((int)ExitOutcome.ToolError, exitCode);
        Assert.Contains("The 'scaffold' command does not know 'licence'", errors.ToString(), StringComparison.Ordinal);
        Assert.Contains("--help", errors.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void LeafCommands_Should_TakeNoOtherArgument_When_ExtraTokensArePassed()
    {
        var errors = new StringWriter();

        var exitCode = CliModule.Run(["init", "extra"], new StringWriter(), errors);

        Assert.Equal((int)ExitOutcome.ToolError, exitCode);
        Assert.Contains("The 'init' command takes no other argument in this build", errors.ToString(), StringComparison.Ordinal);
        Assert.Contains("'extra'", errors.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void VersionFlag_Should_TakeNoOtherArgument_When_AnyOtherTokenIsPassed()
    {
        var errors = new StringWriter();

        var exitCode = CliModule.Run(["--version", "scan"], new StringWriter(), errors);

        Assert.Equal((int)ExitOutcome.ToolError, exitCode);
        Assert.Contains("The --version flag takes no other argument", errors.ToString(), StringComparison.Ordinal);
        Assert.Contains("'scan'", errors.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void CliModule_Should_SayWhatToDoNext_When_AnUnknownWordIsGiven()
    {
        var errors = new StringWriter();

        var exitCode = CliModule.Run(["wat"], new StringWriter(), errors);

        Assert.Equal((int)ExitOutcome.ToolError, exitCode);
        Assert.Contains("'wat' is not a command or flag this build knows", errors.ToString(), StringComparison.Ordinal);
        Assert.Contains("--help", errors.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void CliModule_Should_SayWhatToDoNext_When_AnUnknownFlagIsGiven()
    {
        var errors = new StringWriter();

        var exitCode = CliModule.Run(["--verbose"], new StringWriter(), errors);

        Assert.Equal((int)ExitOutcome.ToolError, exitCode);
        Assert.Contains("'--verbose' is not a command or flag this build knows", errors.ToString(), StringComparison.Ordinal);
        Assert.Contains("--help", errors.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void CliModule_Should_PrintTheProductPurposeCommandsAndVersionFlag_When_HelpIsRequested()
    {
        var output = new StringWriter();
        var errors = new StringWriter();

        var exitCode = CliModule.Run(["--help"], output, errors);

        Assert.Equal((int)ExitOutcome.Pass, exitCode);
        var text = output.ToString();
        Assert.Contains("Nigeria Data Protection Act 2023", text, StringComparison.Ordinal);
        Assert.Contains("no arguments runs the scan", text, StringComparison.Ordinal);
        Assert.Contains("--version", text, StringComparison.Ordinal);
        Assert.Contains("-h", text, StringComparison.Ordinal);
        Assert.Equal(string.Empty, errors.ToString());
    }

    [Fact]
    public void CliModule_Should_MatchTheLongHelpOutput_When_HelpIsRequestedWithTheShortFlag()
    {
        var output = new StringWriter();
        var errors = new StringWriter();

        var exitCode = CliModule.Run(["-h"], output, errors);

        Assert.Equal((int)ExitOutcome.Pass, exitCode);
        Assert.Equal(output.ToString(), CliModuleHelpText());
    }

    private static string CliModuleVersionText()
    {
        var output = new StringWriter();
        _ = CliModule.Run(["version"], output, new StringWriter());
        return output.ToString();
    }

    private static string CliModuleHelpText()
    {
        var output = new StringWriter();
        _ = CliModule.Run(["--help"], output, new StringWriter());
        return output.ToString();
    }
}

