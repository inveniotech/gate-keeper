// SPDX-License-Identifier: Apache-2.0
using System.Text.Json.Nodes;
using InvenioTech.GateKeeper.Cli;

namespace InvenioTech.GateKeeper.Cli.Tests;

public sealed class SampleScanTests
{
    [Theory]
    [InlineData("aspnetcore.cs")]
    [InlineData("express.mjs")]
    [InlineData("nestjs.mjs")]
    [InlineData("spring-boot.java")]
    public async Task Cli_Should_ReportTheIntentionalSyntheticValue_When_ItScansAnMvpSample(string sample)
    {
        var root = Path.Combine(Path.GetTempPath(), "gate-keeper-sample-scan", Guid.NewGuid().ToString("N"));
        var config = Path.Combine(root, ".gatekeeper");
        Directory.CreateDirectory(config);
        try
        {
            File.Copy(Path.Combine(AppContext.BaseDirectory, "samples", sample), Path.Combine(root, sample));
            var source = JsonNode.Parse(await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "samples", "bvn.pack.json")))!;
            var pack = new JsonObject
            {
                ["schemaVersion"] = 1,
                ["id"] = "local.sample",
                ["version"] = 1,
                ["provenance"] = "local",
                ["minEngineVersion"] = "0.1.0",
                ["rules"] = source["rules"]!.DeepClone()
            };
            foreach (var rule in pack["rules"]!.AsArray())
            {
                rule!["id"] = "local." + rule["id"]!.GetValue<string>();
                rule["sinceVersion"] = 1;
            }
            await File.WriteAllTextAsync(Path.Combine(config, "sample.json"), pack.ToJsonString());
            await File.WriteAllTextAsync(Path.Combine(config, "gatekeeper.toml"), "schemaVersion = 1\n[packs]\nlocal = [\".gatekeeper/sample.json\"]\n");
            var output = new StringWriter();
            var errors = new StringWriter();

            (await CliModule.RunAsync(["scan", "--json", "--evaluated-on", "2026-11-01"], output, errors, root)).Should().Be(1, errors.ToString());

            var findings = JsonNode.Parse(output.ToString())!["findings"]!.AsArray();
            findings.Should().Contain(finding => finding!["path"]!.GetValue<string>() == sample && finding["ruleId"]!.GetValue<string>() == "local.pii.bvn");
            output.ToString().Should().NotContain("32826314843");
        }
        finally { Directory.Delete(root, true); }
    }
}
