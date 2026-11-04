// SPDX-License-Identifier: Apache-2.0

using System.Text;
using InvenioTech.GateKeeper.Engine;

namespace InvenioTech.GateKeeper.Corpus.Tests;

/// <summary>
/// The edge-case fixtures the card fixes day one, each with one dedicated
/// behaviour: the UTF-16 encoding path, the very long line, the minified
/// bundle, the binary content in a text extension, the file excluded by
/// <c>.gitignore</c>, and the symbolic link.
/// </summary>
public sealed class CorpusEdgeCaseTests
{
    private const string Utf16Case = "bvn-utf16-configuration";
    private const string Utf16Path = "config/environments.toml";

    [Fact]
    public async Task Corpus_Utf16Configuration_Should_DecodeTheMarkedText_When_TheEngineEnumeratesIt()
    {
        var item = GetCase(Utf16Case);
        var runner = new CorpusScanRunner(item);
        var scan = await runner.RunAsync().ConfigureAwait(true);

        using (new CorpusScanScavenger(scan))
        {
            var file = scan.Files.Should().Contain(
                candidate => candidate.Path == Utf16Path,
                because: $"the case '{Utf16Case}' exists so the encoding path is exercised").Subject;

            // The differential decode: the test decodes the committed bytes
            // itself from the byte-order mark, so the engine's decoder and
            // the framework agree byte for byte.
            var committedBytes = File.ReadAllBytes(Path.Combine(item.RepositoryRoot, Utf16Path));
            var decoded = committedBytes[0] == 0xFF && committedBytes[1] == 0xFE
                ? Encoding.Unicode.GetString(committedBytes, 2, committedBytes.Length - 2)
                : throw new InvalidOperationException($"The fixture '{Utf16Path}' lost its UTF-16 byte-order mark.");

            file.Text.Should().Be(decoded, "the scanner must decode the UTF-16 byte-order mark the way the byte sequence says");

            scan.Files.Where(candidate => candidate.Path == Utf16Path)
                .Select(candidate => candidate.Text)
                .First()
                .Should().Contain("[identity]", because: $"the case '{Utf16Case}' carries a real TOML body past the mark");
        }
    }

    private const string LongLineCase = "bvn-very-long-line";
    private const string LongLinePath = "data/reference-indexes.txt";

    [Fact]
    public async Task Corpus_VeryLongLine_Should_ReachTheEngineAtFullLength_When_TheEngineEnumeratesIt()
    {
        var item = GetCase(LongLineCase);
        var runner = new CorpusScanRunner(item);
        var scan = await runner.RunAsync().ConfigureAwait(true);

        using (new CorpusScanScavenger(scan))
        {
            var file = scan.Files.Should().Contain(
                candidate => candidate.Path == LongLinePath,
                because: $"the case '{LongLineCase}' names '{LongLinePath}'").Subject;

            var firstLine = file.Text.Split('\n')[0];

            firstLine.ToCharArray().Length.Should().BeGreaterThanOrEqualTo(
                100_000,
                "the card fixes at least 100,000 characters on the very-long-line case");
        }
    }

    private const string MinifiedCase = "minified-javascript";
    private const string MinifiedPath = "assets/app.min.js";

    [Fact]
    public async Task Corpus_MinifiedJavaScript_Should_StayOutOfTheScanSilently_When_TheEngineEnumeratesIt()
    {
        var item = GetCase(MinifiedCase);
        var runner = new CorpusScanRunner(item);
        var scan = await runner.RunAsync().ConfigureAwait(true);

        using (new CorpusScanScavenger(scan))
        {
            scan.Files.Should().NotContain(
                candidate => candidate.Path == MinifiedPath,
                "the built-in exclusions leave the minified bundle out of the scan");

            scan.Enumeration.Notes.Should().NotContain(
                note => note.Contains(MinifiedPath, StringComparison.Ordinal),
                "the built-in exclusion is silent: no skip note explains it");
        }
    }

    private const string GitignoredCase = "gitignore-excluded";
    private const string GitignoredPath = "secrets/customer.env";
    private const string GitignoredCleanPath = "app/hosting.txt";

    [Fact]
    public async Task Corpus_GitignoredRulesFile_Should_StayOutOfTheScan_When_TheEngineEnumeratesIt()
    {
        var item = GetCase(GitignoredCase);
        var runner = new CorpusScanRunner(item);
        var scan = await runner.RunAsync().ConfigureAwait(true);

        using (new CorpusScanScavenger(scan))
        {
            scan.Files.Should().NotContain(
                candidate => candidate.Path == GitignoredPath,
                "the .gitignore rules of the case's repo keep the spool file out of the scan");

            scan.Files.Should().Contain(
                candidate => candidate.Path == GitignoredCleanPath,
                because: $"the case '{GitignoredCase}' keeps a clean file the scan must reach");
        }
    }

    private const string BinaryCase = "binary-text-extension";
    private const string BinaryPath = "exports/bulk-export.txt";

    [Fact]
    public async Task Corpus_BinaryTextExtension_Should_SkipWithANote_When_TheEngineEnumeratesIt()
    {
        var item = GetCase(BinaryCase);
        var runner = new CorpusScanRunner(item);
        var scan = await runner.RunAsync().ConfigureAwait(true);

        using (new CorpusScanScavenger(scan))
        {
            scan.Files.Should().NotContain(
                candidate => candidate.Path == BinaryPath,
                "the binary probe skips the export regardless of the text extension");

            var skipNote = $"Skipped '{BinaryPath}': the file is binary.";

            scan.Enumeration.Notes.Should().ContainSingle(
                note => note == skipNote,
                because: $"the case '{BinaryCase}' records the skip the console shows");
        }
    }

    private const string SymlinkCase = "symbolic-link";
    private const string SymlinkPath = "link-to-dispatch-notes.log";
    private const string SymlinkTargetPath = "data/dispatch-notes.log";

    [CorpusSymlinkFact]
    public async Task Corpus_SymbolicLink_Should_SkipWithANote_When_TheEngineEnumeratesIt()
    {
        var item = GetCase(SymlinkCase);
        var runner = new CorpusScanRunner(item);
        var scan = await runner.RunAsync().ConfigureAwait(true);

        using (new CorpusScanScavenger(scan))
        {
            scan.Files.Should().NotContain(
                candidate => candidate.Path == SymlinkPath,
                "the walk never reads a symbolic link");

            var skipNote = $"Skipped '{SymlinkPath}': the entry is a symbolic link, so the scan did not read it.";

            scan.Enumeration.Notes.Should().Contain(
                note => note == skipNote,
                because: $"the case '{SymlinkCase}' records the skip the console shows");

            scan.Files.Should().Contain(
                candidate => candidate.Path == SymlinkTargetPath,
                because: $"the case '{SymlinkCase}' keeps the link's target in the scan");
        }
    }

    private static CorpusCase GetCase(string name)
    {
        var cases = CorpusCase.LoadAll(CorpusPaths.Root);
        return cases.Should().Contain(item => item.Name == name).Subject;
    }
}
