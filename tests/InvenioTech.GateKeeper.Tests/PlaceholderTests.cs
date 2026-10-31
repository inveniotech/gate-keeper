// SPDX-License-Identifier: Apache-2.0

using InvenioTech.GateKeeper.Cli;
using InvenioTech.GateKeeper.Engine;

namespace InvenioTech.GateKeeper.Tests;

public sealed class PlaceholderTests
{
    [Fact]
    public void Engine_module_reports_not_started()
    {
        Assert.Equal("not-started", EngineModule.Status);
    }

    [Fact]
    public void Cli_module_reports_not_started()
    {
        Assert.Equal("not-started", CliModule.Status);
    }

    [Fact]
    public void Cli_placeholder_exits_without_a_tool_error()
    {
        Assert.Equal(0, CliModule.Run());
    }
}
