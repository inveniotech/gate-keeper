// SPDX-License-Identifier: Apache-2.0

using InvenioTech.GateKeeper.Engine;

namespace InvenioTech.GateKeeper.Engine.Tests;

public sealed class LocationTests
{
    [Fact]
    public void Location_Should_HoldThePathLineAndColumn_When_ConstructedFromValidValues()
    {
        var location = new Location("src/engine/Finder.cs", 12, 7);

        Assert.Equal("src/engine/Finder.cs", location.Path);
        Assert.Equal(12, location.Line);
        Assert.Equal(7, location.Column);
    }

    [Fact]
    public void Location_Should_RejectTheLine_When_TheLineIsBelowOne()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => _ = new Location("src/engine/Finder.cs", 0, 1));
    }

    [Fact]
    public void Location_Should_RejectTheColumn_When_TheColumnIsBelowOne()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => _ = new Location("src/engine/Finder.cs", 12, 0));
    }

    [Fact]
    public void Location_Should_RejectThePath_When_ThePathIsEmpty()
    {
        Assert.Throws<ArgumentException>(() => _ = new Location(string.Empty, 12, 7));
    }

    [Fact]
    public void Location_Should_CompareByValue_When_ComparedToAnEqualLocation()
    {
        var first = new Location("src/engine/Finder.cs", 12, 7);
        var second = new Location("src/engine/Finder.cs", 12, 7);

        Assert.Equal(first, second);
    }
}

