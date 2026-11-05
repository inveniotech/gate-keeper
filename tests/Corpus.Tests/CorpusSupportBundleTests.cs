// SPDX-License-Identifier: Apache-2.0

using System.IO.Compression;
using System.Text.Json;
using InvenioTech.GateKeeper.Cli;

namespace InvenioTech.GateKeeper.Corpus.Tests;

public sealed class CorpusSupportBundleTests
{
    [Fact]
    public async Task SupportBundle_Should_ExcludeEveryGeneratedIdentifier_When_ScanningEveryCorpusRepository()
    {
        using var inventory = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "synthetic-values.json")));
        var identifiers = inventory.RootElement.EnumerateArray().Select(v => v.GetString()!).ToArray();
        identifiers.Length.Should().BeGreaterThan(30);
        var cases = CorpusCase.LoadAll(CorpusPaths.Root);
        cases.Count.Should().BeGreaterThan(30);
        foreach (var item in cases)
        {
            var scan = await new CorpusScanRunner(item).RunAsync();
            using var cleanup = new CorpusScanScavenger(scan);
            var folder = Path.Combine(scan.CopyRoot, ".ndpa-check");
            Directory.CreateDirectory(folder);
            await File.WriteAllTextAsync(Path.Combine(folder, "ndpa-check.toml"), $"schemaVersion = 1\n[repository]\nid = \"synthetic/{identifiers[0]}\"\n[scan]\nexclude = [\"private/{identifiers[1]}/**\"]\n[packs]\nlocal = [\".ndpa-check/privacy.json\"]\n");
            File.Copy(Path.Combine(AppContext.BaseDirectory, "support-probe.pack.json"), Path.Combine(folder, "privacy.json"));
            await File.WriteAllTextAsync(Path.Combine(folder, "fingerprint.key"), new string('a', 64) + "\n");
            await File.WriteAllTextAsync(Path.Combine(scan.CopyRoot, "privacy-probe.txt"), "gatekeeper-corpus-probe\n");
            var errors = new StringWriter();
            (await CliModule.RunAsync(["support-bundle", "--evaluated-on", "2026-11-01"], new StringWriter(), errors, scan.CopyRoot)).Should().Be(0, $"corpus case {item.Name} must produce a safe bundle: {errors}");
            using var archive = ZipFile.OpenRead(Path.Combine(folder, "support-bundle.zip"));
            archive.Entries.Select(e => e.FullName).Should().Equal("configuration.json", "findings.json", "versions.json");
            foreach (var entry in archive.Entries)
            {
                using var reader = new StreamReader(entry.Open());
                var text = await reader.ReadToEndAsync();
                foreach (var identifier in identifiers)
                    text.Contains(identifier, StringComparison.Ordinal).Should().BeFalse($"{item.Name}/{entry.FullName} must exclude synthetic source identifiers");
                text.Contains(new string('a', 64), StringComparison.Ordinal).Should().BeFalse("the secret key must never enter a bundle");
                if (entry.FullName == "findings.json")
                {
                    using var document = JsonDocument.Parse(text);
                    document.RootElement.GetProperty("workTreeCount").GetInt32().Should().BeGreaterThan(0, "the real scanner must report the mandatory probe");
                }
            }
        }
    }
}
