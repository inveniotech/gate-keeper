// SPDX-License-Identifier: Apache-2.0

using InvenioTech.GateKeeper.Cli;

namespace InvenioTech.GateKeeper.Cli.Tests;

public sealed class DoctorCliTests
{
    [Fact]
    public async Task Doctor_Should_CheckInstallationWithoutScanningSource_When_ConfigurationAndLocalPackAreValid()
    {
        using var repo = new CliRepository();
        File.WriteAllText(Path.Combine(repo.Root, "source.txt"), "marker");
        var result = await repo.Run("doctor", "--evaluated-on", "2026-11-01");
        result.Code.Should().Be(0, result.Errors);
        result.Output.Should().Contain("configuration: valid").And.Contain("local.example: unsigned local pack; valid")
            .And.Contain("output: writable").And.Contain("licence: not checked");
        result.Output.Should().NotContain("source.txt").And.NotContain("local.token").And.NotContain("marker");
        Directory.EnumerateFiles(Path.Combine(repo.Root, ".gatekeeper"), "*.tmp").Should().BeEmpty();
    }

    [Fact]
    public async Task Doctor_Should_ContinueIndependentChecks_When_ConfigurationIsInvalid()
    {
        using var repo = new CliRepository();
        File.WriteAllText(Path.Combine(repo.Root, ".gatekeeper", "gatekeeper.toml"), "schemaVersion = 9\n");
        var result = await repo.Run("doctor");
        result.Code.Should().Be(2);
        result.Output.Should().Contain("configuration: invalid").And.Contain("git:").And.Contain("output: writable");
        result.Errors.Should().Contain("schemaVersion");
    }

    [Fact]
    public async Task Doctor_Should_ReportAnUnwritableOutput_When_ThePathIsAFile()
    {
        using var repo = new CliRepository();
        File.WriteAllText(Path.Combine(repo.Root, "output.txt"), "synthetic");
        var result = await repo.Run("doctor", "--output", "output.txt");
        result.Code.Should().Be(2);
        result.Errors.Should().Contain("output").And.Contain("permissions");
    }

    [Theory]
    [InlineData("../outside")]
    [InlineData("/outside")]
    public async Task Doctor_Should_RejectOutputOutsideTheRepository_When_TraversalIsRequested(string path)
    {
        using var repo = new CliRepository();
        var result = await repo.Run("doctor", "--output", path);
        result.Code.Should().Be(2);
        result.Errors.Should().Contain("repository-relative");
    }
}
