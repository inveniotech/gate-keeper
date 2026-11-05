// SPDX-License-Identifier: Apache-2.0

using System.Text.Json;
using InvenioTech.GateKeeper.Cli;

namespace InvenioTech.GateKeeper.Cli.Tests;

internal sealed class CliRepository : IDisposable
{
    internal string Root { get; } = Path.Combine(Path.GetTempPath(), "gate-keeper-cli", Guid.NewGuid().ToString("N"));

    internal CliRepository()
    {
        var folder = Path.Combine(Root, ".ndpa-check");
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "ndpa-check.toml"), "schemaVersion = 1\n[repository]\nid = \"synthetic/console\"\n[packs]\nlocal = [\".ndpa-check/local.json\"]\n");
        File.WriteAllText(Path.Combine(folder, "local.json"), """
            {"schemaVersion":1,"id":"local.example","version":1,"minEngineVersion":"0.1.0","provenance":"local","rules":[
            {"id":"local.token","severity":"error","category":"synthetic","citation":{"title":"Synthetic test citation."},"sinceVersion":1,"matcher":{"list":{"package":["marker"]}}}]}
            """);
    }

    internal async Task<(int Code, string Output, string Errors)> Run(params string[] args)
    {
        var output = new StringWriter();
        var errors = new StringWriter();
        var code = await CliModule.RunAsync(args, output, errors, Root);
        return (code, output.ToString(), errors.ToString());
    }

    public void Dispose() => Directory.Delete(Root, true);
}

public sealed class ScanCliTests
{
    [Fact]
    public async Task Scan_Should_GroupFindingsByFile_When_ConsoleOutputIsRequested()
    {
        using var repo = new CliRepository();
        File.WriteAllText(Path.Combine(repo.Root, "z.txt"), "marker");
        File.WriteAllText(Path.Combine(repo.Root, "a.txt"), "\nmarker");
        var result = await repo.Run("scan", "--evaluated-on", "2026-11-01");
        result.Code.Should().Be(1, result.Errors);
        result.Output.Replace("\r\n", "\n").Should().Be("""
            Gate-Keeper 0.1.0
            Rule pack local.example: 1
            a.txt
              2:1 error local.token (new)
                Synthetic test citation.
            z.txt
              1:1 error local.token (new)
                Synthetic test citation.
            Findings: 2 work tree, 0 history. Exit code: 1.
            A clean result does not mean git history was scanned.

            """);
        result.Errors.Should().Contain("No fingerprint key").And.Contain("not a git work tree");
        result.Output.Should().NotContain("\u001b").And.NotContain("marker");
    }

    [Fact]
    public async Task Scan_Should_WriteDeterministicEvidence_When_JsonIsRequested()
    {
        using var repo = new CliRepository();
        File.WriteAllText(Path.Combine(repo.Root, "sample.txt"), "marker");
        var first = await repo.Run("--json", "--evaluated-on", "2026-11-01");
        var second = await repo.Run("scan", "--json", "--evaluated-on", "2026-11-01");
        first.Code.Should().Be(1, first.Errors);
        first.Output.Should().Be(second.Output).And.NotContain("marker");
        using var json = JsonDocument.Parse(first.Output);
        json.RootElement.GetProperty("findings")[0].GetRawText().Replace("\r\n", "\n").Should().Be("""
            {
                  "ruleId": "local.token",
                  "severity": "error",
                  "category": "synthetic",
                  "path": "sample.txt",
                  "line": 1,
                  "column": 1,
                  "locationKind": "source",
                  "baselineState": "new",
                  "citation": {
                    "title": "Synthetic test citation."
                  }
                }
            """);
        json.RootElement.GetProperty("evaluatedOn").GetString().Should().Be("2026-11-01");
        json.RootElement.GetProperty("historyScanned").GetBoolean().Should().BeFalse();
        first.Errors.Should().Contain("No fingerprint key");
    }

    [Fact]
    public async Task Scan_Should_ReturnPass_When_ThereAreNoFindings()
    {
        using var repo = new CliRepository();
        var result = await repo.Run("--evaluated-on", "2026-11-01");
        result.Code.Should().Be(0, result.Errors);
        result.Output.Should().Contain("Findings: 0 work tree, 0 history. Exit code: 0.");
    }

    [Fact]
    public async Task Scan_Should_WriteOnlyDiagnostics_When_ConfigurationIsInvalid()
    {
        using var repo = new CliRepository();
        File.WriteAllText(Path.Combine(repo.Root, ".ndpa-check", "ndpa-check.toml"), "schemaVersion = 9\n");
        var result = await repo.Run("scan", "--json");
        result.Code.Should().Be(2);
        result.Output.Should().BeEmpty();
        result.Errors.Should().Contain("schemaVersion");
    }
}
