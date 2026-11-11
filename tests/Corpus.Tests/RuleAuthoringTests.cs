// SPDX-License-Identifier: Apache-2.0
using System.Text.Json;
using InvenioTech.GateKeeper.Cli;

namespace InvenioTech.GateKeeper.Corpus.Tests;

public sealed class RuleAuthoringTests
{
    [Theory]
    [InlineData("gatekeeper-authoring-probe", 1, 1)]
    [InlineData("prefix-gatekeeper-authoring-probe-suffix", 0, 0)]
    public async Task AuthoringTemplate_Should_ReportOnlyTheExactToken_When_RunThroughTheCli(string text, int code, int count)
    {
        var root = Path.Combine(Path.GetTempPath(), "gate-keeper-authoring", Guid.NewGuid().ToString("N"));
        var settings = Path.Combine(root, ".gatekeeper");
        Directory.CreateDirectory(settings);
        try
        {
            var repository = Path.GetDirectoryName(Path.GetDirectoryName(CorpusPaths.Root))!;
            File.Copy(Path.Combine(repository, "rule-packs", "templates", "local.pack.json"), Path.Combine(settings, "local.json"));
            File.WriteAllText(Path.Combine(settings, "gatekeeper.toml"), "schemaVersion = 1\n[packs]\nlocal = [\".gatekeeper/local.json\"]\n");
            File.WriteAllText(Path.Combine(root, "example.txt"), text + "\n");
            var output = new StringWriter();
            var errors = new StringWriter();
            var actual = await CliModule.RunAsync(["scan", "--json", "--evaluated-on", "2026-11-05"], output, errors, root);
            actual.Should().Be(code, errors.ToString());
            using var document = JsonDocument.Parse(output.ToString());
            var findings = document.RootElement.GetProperty("findings");
            findings.GetArrayLength().Should().Be(count);
            if (count == 1)
            {
                findings[0].GetProperty("ruleId").GetString().Should().Be("local.authoring.marker");
                findings[0].GetProperty("path").GetString().Should().Be("example.txt");
                findings[0].GetProperty("line").GetInt32().Should().Be(1);
                findings[0].GetProperty("column").GetInt32().Should().Be(1);
            }
            output.ToString().Should().NotContain("gatekeeper-authoring-probe");
        }
        finally { Directory.Delete(root, true); }
    }
}
