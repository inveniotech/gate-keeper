// SPDX-License-Identifier: Apache-2.0

using InvenioTech.GateKeeper.Engine;

namespace InvenioTech.GateKeeper.Engine.Tests;

public sealed class LocationTests
{
    [Fact]
    public void Holds_the_repository_relative_path_and_the_one_based_line_and_column()
    {
        var location = new Location("src/engine/Finder.cs", 12, 7);

        Assert.Equal("src/engine/Finder.cs", location.Path);
        Assert.Equal(12, location.Line);
        Assert.Equal(7, location.Column);
    }

    [Fact]
    public void Rejects_a_line_below_one()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => _ = new Location("src/engine/Finder.cs", 0, 1));
    }

    [Fact]
    public void Rejects_a_column_below_one()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => _ = new Location("src/engine/Finder.cs", 12, 0));
    }

    [Fact]
    public void Rejects_an_empty_path()
    {
        Assert.Throws<ArgumentException>(() => _ = new Location(string.Empty, 12, 7));
    }

    [Fact]
    public void A_location_compares_by_value_so_two_equal_locations_are_equal()
    {
        var first = new Location("src/engine/Finder.cs", 12, 7);
        var second = new Location("src/engine/Finder.cs", 12, 7);

        Assert.Equal(first, second);
    }
}
