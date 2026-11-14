// SPDX-License-Identifier: Apache-2.0

using InvenioTech.GateKeeper.Cli;

namespace InvenioTech.GateKeeper.Cli.Tests;

public sealed class BaselineCliTests
{
    [Fact]
    public async Task Baseline_Should_ReportMissingPacks_When_NoRulesAreAvailable()
    {
        var root = Path.Combine(Path.GetTempPath(), "gate-keeper-baseline-cli", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var output = new StringWriter();
            var errors = new StringWriter();
            var code = await CliModule.RunAsync(["baseline", "--evaluated-on", "2026-11-01"], output, errors, root);
            code.Should().Be(2);
            errors.ToString().Should().Contain("No rule pack").And.Contain("packs.local");
            output.ToString().Should().BeEmpty();
            File.Exists(Path.Combine(root, ".gatekeeper", "baseline.json")).Should().BeFalse();
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task Baseline_Should_WriteAKeyAndBaseline_When_ALocalPackIsConfigured()
    {
        var root = Path.Combine(Path.GetTempPath(), "gate-keeper-baseline-cli", Guid.NewGuid().ToString("N"));
        var folder = Path.Combine(root, ".gatekeeper");
        Directory.CreateDirectory(folder);
        try
        {
            await File.WriteAllTextAsync(Path.Combine(folder, "gatekeeper.toml"), "schemaVersion = 1\n[packs]\nlocal = [\".gatekeeper/local.json\"]\n");
            await File.WriteAllTextAsync(Path.Combine(folder, "local.json"), """
                {"schemaVersion":1,"id":"local.example","version":1,"minEngineVersion":"0.1.0","provenance":"local","rules":[
                {"id":"local.marker","severity":"error","category":"synthetic","citation":{"title":"Synthetic test citation."},"sinceVersion":1,"matcher":{"list":{"package":["marker"]}}}]}
                """);
            await File.WriteAllTextAsync(Path.Combine(root, "sample.txt"), "marker");
            var output = new StringWriter();
            var errors = new StringWriter();
            (await CliModule.RunAsync(["baseline", "--evaluated-on", "2026-11-01"], output, errors, root)).Should().Be(0, errors.ToString());
            errors.ToString().Should().BeEmpty();
            var key = await File.ReadAllTextAsync(Path.Combine(folder, "fingerprint.key"));
            output.ToString().Should().NotContain(key.Trim()).And.NotContain("marker");
            (await File.ReadAllTextAsync(Path.Combine(folder, "baseline.json"))).Should().Contain("2026-11-01").And.NotContain("marker");
        }
        finally { Directory.Delete(root, true); }
    }
}
