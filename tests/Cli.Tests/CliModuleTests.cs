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

        exitCode.Should().Be((int)ExitOutcome.Pass);
        var text = output.ToString();
        text.Should().Contain("product: gatekeeper");
        text.Should().Contain($"product version: {EngineProduct.EngineVersion}");
        // The bundled pack arrives with the pack build and signing step, so the
        // absence is printed rather than an invented number. The same section
        // prints the version once the build carries a pack.
        text.Should().Contain("bundled pack version");
        errors.ToString().Should().BeEmpty();
    }

    [Fact]
    public void VersionFlag_Should_MatchTheVersionCommand_When_Used()
    {
        var output = new StringWriter();
        var errors = new StringWriter();

        var fromFlag = CliModule.Run(["--version"], output, errors);
        output.GetStringBuilder().Clear();
        var fromCommand = CliModule.Run(["version"], output, errors);

        fromCommand.Should().Be(fromFlag);
        output.ToString().Should().Be(CliModuleVersionText());
    }

    [Fact]
    public void BareInvocation_Should_MeanTheScanCommand_When_NoArgumentsAreGiven()
    {
        var output = new StringWriter();
        var errors = new StringWriter();

        var exitCode = CliModule.Run([], output, errors);

        exitCode.Should().Be((int)ExitOutcome.ToolError);
        errors.ToString().Should().Contain("No rule pack is available for scan");
        output.ToString().Should().BeEmpty();
    }

    [Fact]
    public void UnimplementedCommands_Should_ExitWithAToolErrorNamingTheCommand_When_Ran()
    {
        var commands = new Dictionary<string[], string>
        {
            { ["init"], "init" },
            { ["doctor"], "doctor" },
            { ["support-bundle"], "support-bundle" },
        };

        foreach (var (args, name) in commands)
        {
            var errors = new StringWriter();

            var exitCode = CliModule.Run(args, new StringWriter(), errors);

            exitCode.Should().Be((int)ExitOutcome.ToolError);
            errors.ToString().Should().Contain($"The {name} command is not in this build yet");
            errors.ToString().Should().Contain("Run the binary with --help");
        }
    }

    [Fact]
    public void Scaffold_Should_AskForConsent_When_CalledWithoutItsSubcommand()
    {
        var errors = new StringWriter();

        var exitCode = CliModule.Run(["scaffold"], new StringWriter(), errors);

        exitCode.Should().Be((int)ExitOutcome.ToolError);
        errors.ToString().Should().Contain("The 'scaffold' command needs its subcommand");
        errors.ToString().Should().Contain("scaffold consent");
    }

    [Fact]
    public void Scaffold_Should_NameTheUnknownSubcommand_When_CalledWithOneItDoesNotKnow()
    {
        var errors = new StringWriter();

        var exitCode = CliModule.Run(["scaffold", "licence"], new StringWriter(), errors);

        exitCode.Should().Be((int)ExitOutcome.ToolError);
        errors.ToString().Should().Contain("The 'scaffold' command does not know 'licence'");
        errors.ToString().Should().Contain("--help");
    }

    [Fact]
    public void LeafCommands_Should_TakeNoOtherArgument_When_ExtraTokensArePassed()
    {
        var errors = new StringWriter();

        var exitCode = CliModule.Run(["init", "extra"], new StringWriter(), errors);

        exitCode.Should().Be((int)ExitOutcome.ToolError);
        errors.ToString().Should().Contain("The 'init' command takes no other argument in this build");
        errors.ToString().Should().Contain("'extra'");
    }

    [Fact]
    public void VersionFlag_Should_TakeNoOtherArgument_When_AnyOtherTokenIsPassed()
    {
        var errors = new StringWriter();

        var exitCode = CliModule.Run(["--version", "scan"], new StringWriter(), errors);

        exitCode.Should().Be((int)ExitOutcome.ToolError);
        errors.ToString().Should().Contain("The --version flag takes no other argument");
        errors.ToString().Should().Contain("'scan'");
    }

    [Fact]
    public void CliModule_Should_SayWhatToDoNext_When_AnUnknownWordIsGiven()
    {
        var errors = new StringWriter();

        var exitCode = CliModule.Run(["wat"], new StringWriter(), errors);

        exitCode.Should().Be((int)ExitOutcome.ToolError);
        errors.ToString().Should().Contain("'wat' is not a command or flag this build knows");
        errors.ToString().Should().Contain("--help");
    }

    [Fact]
    public void CliModule_Should_SayWhatToDoNext_When_AnUnknownFlagIsGiven()
    {
        var errors = new StringWriter();

        var exitCode = CliModule.Run(["--verbose"], new StringWriter(), errors);

        exitCode.Should().Be((int)ExitOutcome.ToolError);
        errors.ToString().Should().Contain("'--verbose' is not a command or flag this build knows");
        errors.ToString().Should().Contain("--help");
    }

    [Fact]
    public void CliModule_Should_PrintTheProductPurposeCommandsAndVersionFlag_When_HelpIsRequested()
    {
        var output = new StringWriter();
        var errors = new StringWriter();

        var exitCode = CliModule.Run(["--help"], output, errors);

        exitCode.Should().Be((int)ExitOutcome.Pass);
        var text = output.ToString();
        text.Should().Contain("Nigeria Data Protection Act 2023");
        text.Should().Contain("no arguments runs the scan");
        text.Should().Contain("--version");
        text.Should().Contain("-h");
        errors.ToString().Should().BeEmpty();
    }

    [Fact]
    public void CliModule_Should_MatchTheLongHelpOutput_When_HelpIsRequestedWithTheShortFlag()
    {
        var output = new StringWriter();
        var errors = new StringWriter();

        var exitCode = CliModule.Run(["-h"], output, errors);

        exitCode.Should().Be((int)ExitOutcome.Pass);
        output.ToString().Should().Be(CliModuleHelpText());
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
