// SPDX-License-Identifier: Apache-2.0

using InvenioTech.GateKeeper.Engine;

namespace InvenioTech.GateKeeper.Corpus.Tests;

/// <summary>
/// The per-case scan runs: the engine's enumeration reaches every location
/// the case's expectations name, and no finding appears that the case does
/// not declare. Adding a case only grows the theory members; the assertions
/// stay the same for every case.
/// </summary>
public sealed class CorpusEnumerationTests
{
    [Theory]
    [MemberData(nameof(CorpusCaseSource.Cases), MemberType = typeof(CorpusCaseSource))]
    public async Task Corpus_Case_Should_RevealItsExpectationLocations_When_TheEngineEnumeratesTheRepository(CorpusCase item)
    {
        var runner = new CorpusScanRunner(item);
        var scan = await runner.RunAsync().ConfigureAwait(true);

        using (new CorpusScanScavenger(scan))
        {
            foreach (var entry in item.Expected)
            {
                var file = GetFile(scan, entry.Path, item.Name);
                var textLines = file.Text.Split('\n');

                textLines.Should().HaveCountGreaterThanOrEqualTo(
                    entry.Line,
                    $"case '{item.Name}' declares line {entry.Line} of {entry.Path}");
                var lineText = textLines[entry.Line - 1];

                lineText.ToCharArray().Length.Should().BeGreaterThanOrEqualTo(
                    entry.Column,
                    $"case '{item.Name}' declares column {entry.Column} on line {entry.Line} of {entry.Path}");
            }
        }
    }

    [Theory]
    [MemberData(nameof(CorpusCaseSource.Cases), MemberType = typeof(CorpusCaseSource))]
    public async Task Corpus_Case_Should_ProduceNoUndeclaredFinding_When_TheEngineScanCompletes(CorpusCase item)
    {
        var runner = new CorpusScanRunner(item);
        var scan = await runner.RunAsync().ConfigureAwait(true);

        using (new CorpusScanScavenger(scan))
        {
            var diff = CorpusMeasurements.Score(item.Expected, scan.Findings);

            diff.Unexpected.Should().BeEmpty(
                $"the engine produced findings case '{item.Name}' does not declare; " +
                "every produced finding needs an expected entry");
        }
    }

    private static EnumeratedFile GetFile(CorpusScan scan, string path, string caseName)
    {
        return scan.Files.Should()
            .Contain(
                file => file.Path == path,
                because: $"case '{caseName}' names '{path}' as an expected finding's location")
            .Subject;
    }
}
