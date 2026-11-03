// SPDX-License-Identifier: Apache-2.0

using InvenioTech.GateKeeper.Engine;

namespace InvenioTech.GateKeeper.Engine.Tests;

public sealed class ExitOutcomeTests
{
    [Fact]
    public void ExitOutcome_Should_MapToZeroOneAndTwo_When_CastToIntegers()
    {
        Assert.Equal(0, (int)ExitOutcome.Pass);
        Assert.Equal(1, (int)ExitOutcome.Violations);
        Assert.Equal(2, (int)ExitOutcome.ToolError);
    }

    [Fact]
    public void ExitOutcome_Should_NameOnlyTheThreeCodes_When_Enumerated()
    {
        var names = Enum.GetNames<ExitOutcome>();

        Assert.Equal(["Pass", "Violations", "ToolError"], names);
    }
}

