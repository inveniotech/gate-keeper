// SPDX-License-Identifier: Apache-2.0

using InvenioTech.GateKeeper.Engine;

namespace InvenioTech.GateKeeper.Engine.Tests;

public sealed class LocationTests
{
    [Fact]
    public void Location_Should_HoldThePathLineAndColumn_When_ConstructedFromValidValues()
    {
        var location = new Location("src/engine/Finder.cs", 12, 7);

        location.Path.Should().Be("src/engine/Finder.cs");
        location.Line.Should().Be(12);
        location.Column.Should().Be(7);
    }

    [Fact]
    public void Location_Should_RejectTheLine_When_TheLineIsBelowOne()
    {
        Action act = () => _ = new Location("src/engine/Finder.cs", 0, 1);

        act.Should().Throw<ArgumentOutOfRangeException>().WithParameterName("line");
    }

    [Fact]
    public void Location_Should_RejectTheColumn_When_TheColumnIsBelowOne()
    {
        Action act = () => _ = new Location("src/engine/Finder.cs", 12, 0);

        act.Should().Throw<ArgumentOutOfRangeException>().WithParameterName("column");
    }

    [Fact]
    public void Location_Should_RejectThePath_When_ThePathIsEmpty()
    {
        Action act = () => _ = new Location(string.Empty, 12, 7);

        act.Should().Throw<ArgumentException>().WithParameterName("path");
    }

    [Fact]
    public void Location_Should_CompareByValue_When_ComparedToAnEqualLocation()
    {
        var first = new Location("src/engine/Finder.cs", 12, 7);
        var second = new Location("src/engine/Finder.cs", 12, 7);

        first.Should().Be(second);
    }
}
