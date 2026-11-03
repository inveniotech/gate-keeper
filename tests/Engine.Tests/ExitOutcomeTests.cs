// SPDX-License-Identifier: Apache-2.0

using InvenioTech.GateKeeper.Engine;

namespace InvenioTech.GateKeeper.Engine.Tests;

public sealed class ExitOutcomeTests
{
    [Fact]
    public void Rules_exit_codes_are_exactly_zero_one_and_two()
    {
        Assert.Equal(0, (int)ExitOutcome.Pass);
        Assert.Equal(1, (int)ExitOutcome.Violations);
        Assert.Equal(2, (int)ExitOutcome.ToolError);
    }

    [Fact]
    public void The_outcome_set_names_only_the_three_codes_by_name()
    {
        var names = Enum.GetNames<ExitOutcome>();

        Assert.Equal(["Pass", "Violations", "ToolError"], names);
    }
}
