// SPDX-License-Identifier: Apache-2.0

using InvenioTech.GateKeeper.Engine;

namespace InvenioTech.GateKeeper.Engine.Tests;

public sealed class ScanOrchestratorTests
{
    [Fact]
    public async Task ScanOrchestrator_Should_ReturnIdenticalSortedFindings_When_EnumerationIsShuffled()
    {
        var scanner = Scanner();
        EnumeratedFile[] files = [new("z.cs", "marker"), new("A.cs", "marker\n marker marker"), new("a.cs", "clean")];
        var expected = new[] { "A.cs:1:1:local.a", "A.cs:1:1:local.z", "A.cs:2:2:local.a", "A.cs:2:2:local.z", "A.cs:2:9:local.a", "A.cs:2:9:local.z", "z.cs:1:1:local.a", "z.cs:1:1:local.z" };
        var first = await scanner.ScanAsync(Input(files));
        var second = await scanner.ScanAsync(Input(files.Reverse().ToArray()));
        first.Findings.Select(f => $"{f.Path}:{f.Line}:{f.Column}:{f.RuleId}").Should().Equal(expected);
        second.Findings.Should().Equal(first.Findings);
        Bytes(second).Should().Equal(Bytes(first));
    }

    [Fact]
    public async Task ScanOrchestrator_Should_KeepHistorySeparate_When_ACommitContainsMatches()
    {
        var input = new FileEnumerationResult([new("current.cs", "clean")],
            [new(new string('b', 40), "old.cs", "marker marker"), new(new string('a', 40), "old.cs", "marker")], [], [], true);
        var result = await Scanner().ScanAsync(input);
        result.Findings.Should().BeEmpty();
        result.History.Select(f => f.CommitId).Should().Equal(new string('a', 40), new string('a', 40), new string('b', 40), new string('b', 40), new string('b', 40), new string('b', 40));
        result.History.Select(f => f.Match.RuleId).Should().Equal("local.a", "local.z", "local.a", "local.a", "local.z", "local.z");
        result.History.Select(f => f.Match.Column).Should().Equal(1, 1, 1, 8, 1, 8);
    }

    [Fact]
    public async Task ScanOrchestrator_Should_CancelWithoutReturningResults_When_AlreadyCancelled()
    {
        using var source = new CancellationTokenSource();
        source.Cancel();
        Func<Task> scan = async () => await Scanner().ScanAsync(Input([]), source.Token);
        await scan.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task ScanOrchestrator_Should_KeepRunsIndependent_When_InvokedConcurrently()
    {
        var scanner = Scanner();
        var runs = await Task.WhenAll(Enumerable.Range(0, 8).Select(index => scanner.ScanAsync(Input([new($"{index}.cs", "marker")]))));
        for (var index = 0; index < runs.Length; index++)
        {
            runs[index].Findings.Should().HaveCount(2).And.OnlyContain(f => f.Path == $"{index}.cs");
        }
    }

    private static FileEnumerationResult Input(IReadOnlyList<EnumeratedFile> files) => new(files, [], [], [], false);

    private static byte[] Bytes(ScanMatchResult result) => System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(
        new ScanResult(result.Findings.Select(f => new Finding(f.RuleId, f.Severity, f.Category, f.Path, f.Line, f.Column,
            f.LocationKind, null, BaselineState.New, f.Citation)).ToArray()), EngineJsonSerializerContext.Default.ScanResult);

    [Fact]
    public async Task ScanOrchestrator_Should_ReadAllEligibleFiles_When_ScanningADirectory()
    {
        var root = Path.Combine(Path.GetTempPath(), "gate-keeper-scan", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            for (var index = 0; index < 128; index++)
            {
                await File.WriteAllTextAsync(Path.Combine(root, $"file-{index:D3}.txt"), "marker");
            }

            await File.WriteAllBytesAsync(Path.Combine(root, "binary.dat"), [0, 1, 2]);
            var result = await Scanner().ScanAsync(new FileEnumerationOptions { Root = root });
            result.Findings.Should().HaveCount(256);
            result.Enumeration.Files.Should().HaveCount(128);
            result.Enumeration.Notes.Should().ContainSingle(note => note.Contains("binary.dat", StringComparison.Ordinal));
            var again = await Scanner().ScanAsync(new FileEnumerationOptions { Root = root });
            Bytes(again).Should().Equal(Bytes(result));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static ScanOrchestrator Scanner() => new(new MatcherEngine([Rule("local.z"), Rule("local.a")]));

    private static Rule Rule(string id) => new(id, Severity.Error, "synthetic", new Citation("Synthetic test citation.", null, null), "1", null, null, null, null,
        new RuleMatcher(null, new RuleListMatcher(["marker"], [])));
}
