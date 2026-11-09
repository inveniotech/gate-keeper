// SPDX-License-Identifier: Apache-2.0

using System.IO.Compression;
using System.Text.Json;
using InvenioTech.GateKeeper.Engine;

namespace InvenioTech.GateKeeper.Cli.Tests;

public sealed class SupportBundleCliTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public async Task SupportBundle_Should_PreserveTheArchiveAndReturnToolError_When_EitherMatcherFails(int failingStage)
    {
        using var repo = new CliRepository();
        (await repo.Run("support-bundle", "--evaluated-on", "2026-11-01")).Code.Should().Be(0);
        var path = Path.Combine(repo.Root, ".ndpa-check", "support-bundle.zip");
        var previous = await File.ReadAllBytesAsync(path);
        var errors = new StringWriter(); var calls = 0;
        var result = await CliModule.RunWithMatcherFactoryAsync(["support-bundle", "--evaluated-on", "2026-11-01"],
            new StringWriter(), errors, repo.Root, rules =>
            {
                if (++calls == failingStage) throw new EngineToolException("Synthetic matcher deadline failure.");
                return new MatcherEngine(rules);
            });
        result.Should().Be(2);
        calls.Should().Be(failingStage);
        errors.ToString().Should().Contain("Synthetic matcher deadline failure.");
        (await File.ReadAllBytesAsync(path)).Should().Equal(previous);
        Directory.EnumerateFiles(Path.GetDirectoryName(path)!, "*.tmp").Should().BeEmpty();
    }

    [Fact]
    public async Task SupportBundle_Should_ContainOnlyMaskedMetadataAndCounts_When_TheScanHasFindings()
    {
        using var repo = new CliRepository();
        File.WriteAllText(Path.Combine(repo.Root, "source.txt"), "marker");
        File.WriteAllText(Path.Combine(repo.Root, ".ndpa-check", "fingerprint.key"), new string('a', 64) + "\n");
        var result = await repo.Run("support-bundle", "--evaluated-on", "2026-11-01");
        result.Code.Should().Be(0, result.Errors);
        var path = Path.Combine(repo.Root, ".ndpa-check", "support-bundle.zip");
        using var archive = ZipFile.OpenRead(path);
        archive.Entries.Select(e => e.FullName).Should().Equal("configuration.json", "findings.json", "versions.json");
        foreach (var entry in archive.Entries)
        {
            entry.LastWriteTime.Year.Should().Be(1980);
            using var reader = new StreamReader(entry.Open());
            var text = await reader.ReadToEndAsync();
            text.Contains("marker", StringComparison.Ordinal).Should().BeFalse();
            text.Contains("source.txt", StringComparison.Ordinal).Should().BeFalse();
            text.Contains(new string('a', 64), StringComparison.Ordinal).Should().BeFalse();
            text.Contains("synthetic/console", StringComparison.Ordinal).Should().BeFalse();
        }
        using var findings = JsonDocument.Parse(await ReadEntry(archive, "findings.json"));
        findings.RootElement.GetProperty("workTreeCount").GetInt32().Should().Be(1);
        findings.RootElement.GetProperty("rules")[0].GetProperty("ruleId").GetString().Should().Be("local.token");
        using var versions = JsonDocument.Parse(await ReadEntry(archive, "versions.json"));
        versions.RootElement.GetProperty("engine").GetProperty("product").GetString().Should().Be("gatekeeper");
    }

    [Fact]
    public async Task SupportBundle_Should_ProduceIdenticalBytes_When_InputsAndEvaluationDateAreUnchanged()
    {
        using var repo = new CliRepository();
        (await repo.Run("support-bundle", "--evaluated-on", "2026-11-01")).Code.Should().Be(0);
        var path = Path.Combine(repo.Root, ".ndpa-check", "support-bundle.zip");
        var first = await File.ReadAllBytesAsync(path);
        (await repo.Run("support-bundle", "--evaluated-on", "2026-11-01")).Code.Should().Be(0);
        (await File.ReadAllBytesAsync(path)).Should().Equal(first);
    }

    [Fact]
    public async Task SupportBundle_Should_PreserveThePreviousArchive_When_ConfigurationIsInvalid()
    {
        using var repo = new CliRepository();
        var path = Path.Combine(repo.Root, ".ndpa-check", "support-bundle.zip");
        (await repo.Run("support-bundle", "--evaluated-on", "2026-11-01")).Code.Should().Be(0);
        var previous = await File.ReadAllBytesAsync(path);
        await File.WriteAllTextAsync(Path.Combine(repo.Root, ".ndpa-check", "ndpa-check.toml"), "schemaVersion = 9\n");
        var result = await repo.Run("support-bundle");
        result.Code.Should().Be(2);
        (await File.ReadAllBytesAsync(path)).Should().Equal(previous);
        Directory.EnumerateFiles(Path.GetDirectoryName(path)!, "*.tmp").Should().BeEmpty();
    }

    [Fact]
    public async Task SupportBundle_Should_FailClosed_When_AFinalDocumentMatchesALoadedRule()
    {
        using var repo = new CliRepository();
        var pack = Path.Combine(repo.Root, ".ndpa-check", "local.json");
        await File.WriteAllTextAsync(pack, (await File.ReadAllTextAsync(pack)).Replace("[\"marker\"]", "[\"schemaVersion\"]", StringComparison.Ordinal));
        var result = await repo.Run("support-bundle", "--evaluated-on", "2026-11-01");
        result.Code.Should().Be(2);
        result.Errors.Should().Contain("privacy check");
        File.Exists(Path.Combine(repo.Root, ".ndpa-check", "support-bundle.zip")).Should().BeFalse();
    }

    [Fact]
    public async Task SupportBundle_Should_RejectTraversal_When_AnExternalOutputIsRequested()
    {
        using var repo = new CliRepository();
        var result = await repo.Run("support-bundle", "--output", "../bundle.zip");
        result.Code.Should().Be(2);
        result.Errors.Should().Contain("repository-relative");
    }

    [Fact]
    public async Task SupportBundle_Should_MaskRetainedIdentifiers_When_TheSourceRuleHasPathAndKeywordRestrictions()
    {
        using var repo = new CliRepository();
        await File.WriteAllTextAsync(Path.Combine(repo.Root, ".ndpa-check", "local.json"), """
            {"schemaVersion":1,"id":"local.marker","version":1,"minEngineVersion":"0.1.0","provenance":"local","rules":[
            {"id":"local.context","severity":"error","category":"synthetic","citation":{"title":"Synthetic test citation."},"sinceVersion":1,
            "matcher":{"pattern":{"shape":[{"literal":"marker","min":1,"max":1}],"boundary":"none","keywords":["context"],"window":10,"location":{"kind":"source","paths":["src/**"]}}}}]}
            """);
        Directory.CreateDirectory(Path.Combine(repo.Root, "src"));
        await File.WriteAllTextAsync(Path.Combine(repo.Root, "src", "sample.txt"), "context marker");
        var result = await repo.Run("support-bundle", "--evaluated-on", "2026-11-01");
        result.Code.Should().Be(0, result.Errors);
        using var archive = ZipFile.OpenRead(Path.Combine(repo.Root, ".ndpa-check", "support-bundle.zip"));
        using var versions = JsonDocument.Parse(await ReadEntry(archive, "versions.json"));
        versions.RootElement.GetProperty("packs")[0].GetProperty("packId").GetString().Should().Be("[masked]");
    }

    private static async Task<string> ReadEntry(ZipArchive archive, string name)
    {
        using var reader = new StreamReader(archive.GetEntry(name)!.Open());
        return await reader.ReadToEndAsync();
    }
}
