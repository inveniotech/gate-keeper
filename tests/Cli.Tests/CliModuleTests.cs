// SPDX-License-Identifier: Apache-2.0

using InvenioTech.GateKeeper.Cli;
using InvenioTech.GateKeeper.Engine;

namespace InvenioTech.GateKeeper.Cli.Tests;

public sealed class CliModuleTests
{
    [Fact]
    public void Version_prints_the_product_version_and_addresses_the_pack_version_to_standard_output()
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
    public void The_version_flag_behaves_like_the_version_command()
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
    public void The_bare_invocation_is_the_scan_command()
    {
        var output = new StringWriter();
        var errors = new StringWriter();

        var exitCode = CliModule.Run([], output, errors);

        Assert.Equal((int)ExitOutcome.ToolError, exitCode);
        Assert.Contains("The scan (the default) command is not in this build yet", errors.ToString(), StringComparison.Ordinal);
        Assert.Equal(string.Empty, output.ToString());
    }

    [Fact]
    public void Every_card_command_except_version_and_help_exits_with_a_tool_error_naming_its_command()
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
    public void Scaffold_without_its_subcommand_asks_for_consent()
    {
        var errors = new StringWriter();

        var exitCode = CliModule.Run(["scaffold"], new StringWriter(), errors);

        Assert.Equal((int)ExitOutcome.ToolError, exitCode);
        Assert.Contains("The 'scaffold' command needs its subcommand", errors.ToString(), StringComparison.Ordinal);
        Assert.Contains("scaffold consent", errors.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Scaffold_with_an_unknown_subcommand_names_the_subcommand()
    {
        var errors = new StringWriter();

        var exitCode = CliModule.Run(["scaffold", "licence"], new StringWriter(), errors);

        Assert.Equal((int)ExitOutcome.ToolError, exitCode);
        Assert.Contains("The 'scaffold' command does not know 'licence'", errors.ToString(), StringComparison.Ordinal);
        Assert.Contains("--help", errors.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void A_command_with_an_unexpected_argument_is_a_failure_naming_both()
    {
        var errors = new StringWriter();

        var exitCode = CliModule.Run(["init", "extra"], new StringWriter(), errors);

        Assert.Equal((int)ExitOutcome.ToolError, exitCode);
        Assert.Contains("The 'init' command takes no other argument in this build", errors.ToString(), StringComparison.Ordinal);
        Assert.Contains("'extra'", errors.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void The_version_flag_with_an_extra_argument_is_a_failure()
    {
        var errors = new StringWriter();

        var exitCode = CliModule.Run(["--version", "scan"], new StringWriter(), errors);

        Assert.Equal((int)ExitOutcome.ToolError, exitCode);
        Assert.Contains("The --version flag takes no other argument", errors.ToString(), StringComparison.Ordinal);
        Assert.Contains("'scan'", errors.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void An_unknown_word_is_a_failure_that_says_what_to_do_next()
    {
        var errors = new StringWriter();

        var exitCode = CliModule.Run(["wat"], new StringWriter(), errors);

        Assert.Equal((int)ExitOutcome.ToolError, exitCode);
        Assert.Contains("'wat' is not a command or flag this build knows", errors.ToString(), StringComparison.Ordinal);
        Assert.Contains("--help", errors.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void An_unknown_flag_is_a_failure_that_says_what_to_do_next()
    {
        var errors = new StringWriter();

        var exitCode = CliModule.Run(["--verbose"], new StringWriter(), errors);

        Assert.Equal((int)ExitOutcome.ToolError, exitCode);
        Assert.Contains("'--verbose' is not a command or flag this build knows", errors.ToString(), StringComparison.Ordinal);
        Assert.Contains("--help", errors.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Help_prints_the_product_purpose_the_commands_and_the_version_flag_to_standard_output()
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
    public void The_short_help_flag_behaves_like_the_long_one()
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
