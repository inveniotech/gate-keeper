// SPDX-License-Identifier: Apache-2.0

using System.Diagnostics;
using System.IO.Compression;
using System.Text.Json.Nodes;

namespace InvenioTech.GateKeeper.Cli.Tests;

public sealed class ProcessorOutputCliTests
{
    [Fact]
    public async Task Scan_Should_DescribeProcessorMetadataForHistoryAndWorkTree_When_ConsoleOutputIsRequested()
    {
        using var repo = new CliRepository();
        ConfigureProcessor(repo);
        File.AppendAllText(Path.Combine(repo.Root, ".gatekeeper", "gatekeeper.toml"), "\n[scan]\nhistory = true\n");
        await Git(repo, "init", "--initial-branch=main");
        await Git(repo, "config", "core.autocrlf", "false");
        await Git(repo, "config", "user.name", "Synthetic Author");
        await Git(repo, "config", "user.email", "synthetic@example.invalid");
        File.WriteAllText(Path.Combine(repo.Root, "old.txt"), "marker old\n");
        await Git(repo, "add", "old.txt");
        await Git(repo, "commit", "-m", "synthetic history");
        File.WriteAllText(Path.Combine(repo.Root, "old.txt"), "clean\n");
        File.WriteAllText(Path.Combine(repo.Root, "current.txt"), "marker\n");
        await Git(repo, "add", "old.txt", "current.txt");
        await Git(repo, "commit", "-m", "synthetic work tree");
        var result = await repo.Run("scan", "--evaluated-on", "2026-11-13");
        result.Code.Should().Be(0, result.Errors);
        result.Output.Should().Contain("Findings: 1 work tree, 1 history. Exit code: 0.");
        const string detail = "    Data categories: second category, first category\n    Synthetic region caveat.\n    Integration reference detected. Confirm the categories of data actually sent, the service region and the applicable transfer basis for your deployment.\n";
        result.Output.Split(detail, StringSplitOptions.None).Should().HaveCount(3);
        result.Output.Should().Contain("History ").And.NotContain("marker");
        result.Errors.Should().NotContain("marker");
        var repeat = await repo.Run("scan", "--evaluated-on", "2026-11-13");
        repeat.Output.Should().Be(result.Output);
        repeat.Errors.Should().Be(result.Errors);
    }

    [Fact]
    public async Task SupportBundle_Should_OmitMatchedProcessorInput_When_CatalogueMetadataIsReported()
    {
        using var repo = new CliRepository();
        ConfigureProcessor(repo);
        File.WriteAllText(Path.Combine(repo.Root, "source.txt"), "marker");
        var json = await repo.Run("scan", "--json", "--evaluated-on", "2026-11-13");
        json.Code.Should().Be(0, json.Errors);
        json.Output.Should().Contain("\"processor\"").And.NotContain("marker");
        json.Errors.Should().NotContain("marker");
        var bundle = await repo.Run("support-bundle", "--evaluated-on", "2026-11-13");
        bundle.Code.Should().Be(0, bundle.Errors);
        bundle.Output.Should().NotContain("marker");
        bundle.Errors.Should().NotContain("marker");
        using var archive = ZipFile.OpenRead(Path.Combine(repo.Root, ".gatekeeper", "support-bundle.zip"));
        foreach (var entry in archive.Entries)
        {
            using var reader = new StreamReader(entry.Open());
            (await reader.ReadToEndAsync()).Should().NotContain("marker").And.NotContain("source.txt");
        }
    }

    private static void ConfigureProcessor(CliRepository repo)
    {
        var path = Path.Combine(repo.Root, ".gatekeeper", "local.json");
        var pack = JsonNode.Parse(File.ReadAllText(path))!;
        pack["rules"]![0]!["severity"] = "note";
        pack["rules"]![0]!["processor"] = JsonNode.Parse("""
            {"dataCategories":["second category","first category","second category"],"usualRegion":"Synthetic region caveat."}
            """);
        File.WriteAllText(path, pack.ToJsonString());
    }

    private static async Task Git(CliRepository repo, params string[] args)
    {
        var start = new ProcessStartInfo("git") { WorkingDirectory = repo.Root, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        foreach (var arg in args) start.ArgumentList.Add(arg);
        start.Environment["GIT_AUTHOR_DATE"] = "2026-11-01T00:00:00Z";
        start.Environment["GIT_COMMITTER_DATE"] = "2026-11-01T00:00:00Z";
        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEndAsync();
        var errors = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        process.ExitCode.Should().Be(0, await errors);
        await output;
    }
}
