// SPDX-License-Identifier: Apache-2.0

using InvenioTech.GateKeeper.Cli;

namespace InvenioTech.GateKeeper.Cli.Tests;

public sealed class CliCommandParserTests
{
    [Fact]
    public void No_arguments_names_the_scan_command()
    {
        var parsed = CliCommandParser.TryParse([], out var invocation, out var error);

        Assert.True(parsed);
        Assert.Null(error);
        Assert.Equal(CliCommandKind.Scan, invocation!.Command);
    }

    [Fact]
    public void Names_the_same_command_whatever_the_wording_the_card_gives_it()
    {
        var examples = new Dictionary<string[], CliCommandKind>
        {
            { ["scan"], CliCommandKind.Scan },
            { ["init"], CliCommandKind.Init },
            { ["baseline"], CliCommandKind.Baseline },
            { ["scaffold", "consent"], CliCommandKind.ScaffoldConsent },
            { ["doctor"], CliCommandKind.Doctor },
            { ["support-bundle"], CliCommandKind.SupportBundle },
            { ["version"], CliCommandKind.Version },
        };

        foreach (var (args, expectedCommand) in examples)
        {
            var parsed = CliCommandParser.TryParse(args, out var invocation, out var error);

            Assert.True(parsed, $"Parsing '{string.Join(' ', args)}' should succeed.");
            Assert.Null(error);
            Assert.Equal(expectedCommand, invocation!.Command);
        }
    }

    [Fact]
    public void An_unknown_word_is_a_failure_that_says_what_to_do_next()
    {
        var parsed = CliCommandParser.TryParse(["wat"], out var invocation, out var error);

        Assert.False(parsed);
        Assert.Null(invocation);
        Assert.Contains("'wat' is not a command this build knows", error, StringComparison.Ordinal);
        Assert.Contains("--help", error, StringComparison.Ordinal);
    }

    [Fact]
    public void An_unknown_flag_is_a_failure_that_says_what_to_do_next()
    {
        var parsed = CliCommandParser.TryParse(["--verbose"], out var invocation, out var error);

        Assert.False(parsed);
        Assert.Null(invocation);
        Assert.Contains("'--verbose' is not a flag this build knows", error, StringComparison.Ordinal);
    }

    [Fact]
    public void Scaffold_without_its_subcommand_asks_for_consent()
    {
        var parsed = CliCommandParser.TryParse(["scaffold"], out var invocation, out var error);

        Assert.False(parsed);
        Assert.Null(invocation);
        Assert.Contains("The 'scaffold' command needs its subcommand", error, StringComparison.Ordinal);
        Assert.Contains("scaffold consent", error, StringComparison.Ordinal);
    }

    [Fact]
    public void Scaffold_with_an_unknown_subcommand_names_the_subcommand()
    {
        var parsed = CliCommandParser.TryParse(["scaffold", "licence"], out var invocation, out var error);

        Assert.False(parsed);
        Assert.Null(invocation);
        Assert.Contains("'scaffold' command does not know 'licence'", error, StringComparison.Ordinal);
    }

    [Fact]
    public void A_command_with_an_unexpected_argument_is_a_failure_naming_both()
    {
        var parsed = CliCommandParser.TryParse(["init", "extra"], out var invocation, out var error);

        Assert.False(parsed);
        Assert.Null(invocation);
        Assert.Contains("The 'init' command takes no other argument in this build", error, StringComparison.Ordinal);
        Assert.Contains("'extra'", error, StringComparison.Ordinal);
    }

    [Fact]
    public void The_version_flag_with_an_extra_argument_is_a_failure()
    {
        var parsed = CliCommandParser.TryParse(["--version", "scan"], out var invocation, out var error);

        Assert.False(parsed);
        Assert.Null(invocation);
        Assert.Contains("The --version flag takes no other argument", error, StringComparison.Ordinal);
    }
}
