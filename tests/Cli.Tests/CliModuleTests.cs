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
            { ["scan"], "scan (the default)" },
            { ["init"], "init" },
            { ["baseline"], "baseline" },
            { ["scaffold", "consent"], "scaffold consent" },
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
    public void Help_prints_the_command_list_and_the_flags_to_standard_output()
    {
        var output = new StringWriter();
        var errors = new StringWriter();

        var exitCode = CliModule.Run(["--help"], output, errors);

        Assert.Equal((int)ExitOutcome.Pass, exitCode);
        var text = output.ToString();
        Assert.Contains("scan (the default)", text, StringComparison.Ordinal);
        Assert.Contains("scaffold consent", text, StringComparison.Ordinal);
        Assert.Contains("version", text, StringComparison.Ordinal);
        Assert.Contains("--version", text, StringComparison.Ordinal);
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
