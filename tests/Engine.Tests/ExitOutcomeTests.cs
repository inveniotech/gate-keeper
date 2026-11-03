// SPDX-License-Identifier: Apache-2.0

using InvenioTech.GateKeeper.Engine;

namespace InvenioTech.GateKeeper.Engine.Tests;

public sealed class ExitOutcomeTests
{
    [Fact]
    public void ExitOutcome_Should_MapToZeroOneAndTwo_When_CastToIntegers()
    {
        ((int)ExitOutcome.Pass).Should().Be(0);
        ((int)ExitOutcome.Violations).Should().Be(1);
        ((int)ExitOutcome.ToolError).Should().Be(2);
    }

    [Fact]
    public void ExitOutcome_Should_NameOnlyTheThreeCodes_When_Enumerated()
    {
        var names = Enum.GetNames<ExitOutcome>();

        names.Should().Equal("Pass", "Violations", "ToolError");
    }
}
