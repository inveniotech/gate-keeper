// SPDX-License-Identifier: Apache-2.0

using InvenioTech.GateKeeper.Engine;

namespace InvenioTech.GateKeeper.Engine.Tests;

/// <summary>
/// History scanning by <see cref="FileEnumerator"/> (IQ-83, section 7 of the
/// configuration specification): the blobs reachable from <c>HEAD</c>, the
/// skipping of blobs the work tree already covers, the size, binary and LFS
/// rules for historical bytes, and the deterministic ordering by commit
/// identifier then path. A history entry's commit identifier is the newest
/// commit reachable from <c>HEAD</c> whose tree still contains the blob.
/// </summary>
public sealed class FileEnumeratorHistoryTests
{
    private static readonly string PointerContent =
        "version https://git-lfs.github.com/spec/v1\noid sha256:" + new string('0', 64) + "\nsize 12\n";

    [Fact]
    public async Task Enumerator_Should_ListHistoricalBlobsNotCoveredByTheWorkTree_When_HistoryIsRequested()
    {
        var repository = BuildReworkRepository();
        var seedCommit = repository.Git("rev-parse", "HEAD~1").TrimEnd('\n');

        var result = await new FileEnumerator().EnumerateAsync(
            repository.Options(o => o.ScanHistory = true),
            CancellationToken.None);

        result.IsInsideGitWorkTree.Should().BeTrue();
        result.History.Should().Equal(
            new HistoryCandidate(CommitId: seedCommit, Path: "a.txt", Text: "alpha-0001\n"),
            new HistoryCandidate(CommitId: seedCommit, Path: "old.txt", Text: "omega-0031\n"));
        result.Warnings.Should().BeEmpty();
        result.Notes.Should().BeEmpty();
    }

    [Fact]
    public async Task Enumerator_Should_ProduceTheSameHistory_When_RunAgainstTheSameRepositoryTwice()
    {
        var repository = BuildReworkRepository();

        var enumerator = new FileEnumerator();
        var first = await enumerator.EnumerateAsync(
            repository.Options(o => o.ScanHistory = true),
            CancellationToken.None);
        var second = await enumerator.EnumerateAsync(
            repository.Options(o => o.ScanHistory = true),
            CancellationToken.None);

        second.History.Should().Equal(first.History);
    }

    [Fact]
    public async Task Enumerator_Should_ReturnAnEmptyHistoryWithoutWarnings_When_HistoryIsDisabled()
    {
        var repository = BuildReworkRepository();

        var result = await new FileEnumerator().EnumerateAsync(repository.Options(), CancellationToken.None);

        result.History.Should().BeEmpty();
        result.Warnings.Should().BeEmpty();
    }

    [Fact]
    public async Task Enumerator_Should_OrderTheHistoryByCommitThenPath_When_BlobsSitInDifferentCommits()
    {
        var repository = GitTestRepository.Create();
        repository.WriteFile("x.txt", "x-0001\n");
        repository.Stage("x.txt");
        repository.Commit("one: only the history holds x");
        File.Delete(repository.FullPath("x.txt"));
        repository.Stage("x.txt");
        repository.WriteFile("y.txt", "y-0002\n");
        repository.Stage("y.txt");
        repository.Commit("two: only the history holds y");
        File.Delete(repository.FullPath("y.txt"));
        repository.Stage("y.txt");
        repository.WriteFile("z.txt", "z-0003\n");
        repository.Stage("z.txt");
        repository.Commit("three: only the work tree holds z");

        var result = await new FileEnumerator().EnumerateAsync(
            repository.Options(o => o.ScanHistory = true),
            CancellationToken.None);

        result.History.Select(entry => entry.Path).Should().Equal("x.txt", "y.txt");
        result.History.Select(entry => entry.Text).Should().Equal("x-0001\n", "y-0002\n");
    }

    [Fact]
    public async Task Enumerator_Should_LeaveUnreachableBranchesOut_When_ASideBranchAddsABlob()
    {
        var repository = BuildReworkRepository();
        repository.Git("checkout", "-b", "side");
        repository.WriteFile("side-only.txt", "side-branch blob\n");
        repository.Stage("side-only.txt");
        repository.Commit("side: the side branch blob");
        repository.Git("checkout", "master");

        var result = await new FileEnumerator().EnumerateAsync(
            repository.Options(o => o.ScanHistory = true),
            CancellationToken.None);

        result.History.Where(entry => entry.Path == "side-only.txt").Should().BeEmpty();
    }

    [Fact]
    public async Task Enumerator_Should_ApplyTheScanScopeRulesToHistoricalBytes_When_TheRulesSkipThem()
    {
        var repository = GitTestRepository.Create();
        repository.WriteFile("pointer.bin", PointerContent);
        repository.WriteBytes("weird.bin", [0x50, 0x49, 0x00, 0x4E]);
        repository.WriteFile("engine.ini", "value=pretty-large-setting");
        repository.Stage("pointer.bin", "weird.bin", "engine.ini");
        repository.Commit("one: seed the skipped fixtures");
        File.Delete(repository.FullPath("pointer.bin"));
        File.Delete(repository.FullPath("weird.bin"));
        File.Delete(repository.FullPath("engine.ini"));
        repository.Stage("pointer.bin", "weird.bin", "engine.ini");
        repository.Commit("two: the work tree is now empty");

        var options = repository.Options(o =>
        {
            o.ScanHistory = true;
            o.MaxFileBytes = 16;
        });

        var result = await new FileEnumerator().EnumerateAsync(options, CancellationToken.None);

        result.Files.Should().BeEmpty();
        result.History.Should().BeEmpty();
        result.Notes.Should().Equal(
            "Skipped 'engine.ini': the file is 26 bytes, and the limit is 16 bytes.",
            "Skipped 'pointer.bin': the file is 127 bytes, and the limit is 16 bytes.",
            "Skipped 'weird.bin': the file is binary.");
    }

    [Fact]
    public async Task Enumerator_Should_DecodeHistoricalUtf16BytesWithTheMark_When_TheHistoryReachesThem()
    {
        var repository = GitTestRepository.Create();
        repository.WriteBytes("sensor.log", [0xFF, 0xFE, 0x61, 0x00, 0x62, 0x00, 0x0A, 0x00]);
        repository.Stage("sensor.log");
        repository.Commit("one: the historical UTF-16 file");
        File.Delete(repository.FullPath("sensor.log"));
        repository.Stage("sensor.log");
        repository.Commit("two: delete the historical file");

        var result = await new FileEnumerator().EnumerateAsync(
            repository.Options(o => o.ScanHistory = true),
            CancellationToken.None);

        result.History.Should().ContainSingle().Which.Text.Should().Be("ab\n");
    }

    [Fact]
    public async Task Enumerator_Should_WarnAboutTheShallowClone_When_HistoryRunsOnAShallowClone()
    {
        var repository = BuildReworkRepository();
        var shallow = GitTestRepository.ShallowClone(repository);

        var result = await new FileEnumerator().EnumerateAsync(
            shallow.Options(o => o.ScanHistory = true),
            CancellationToken.None);

        result.IsInsideGitWorkTree.Should().BeTrue();
        result.History.Should().BeEmpty();
        result.Warnings.Should().Equal(
            new ScanWarning(
                "scan.shallow-history",
                "History scanning covered only the commits in this shallow clone."));
    }

    private static GitTestRepository BuildReworkRepository()
    {
        var repository = GitTestRepository.Create();
        repository.WriteFile("a.txt", "alpha-0001\n");
        repository.WriteFile("b.txt", "beta-0002\n");
        repository.WriteFile("old.txt", "omega-0031\n");
        repository.Stage("a.txt", "b.txt", "old.txt");
        repository.Commit("one: seed the fixture");
        repository.WriteFile("a.txt", "alpha-0009\n");
        repository.WriteFile("c.txt", "gamma-0003\n");
        File.Delete(repository.FullPath("old.txt"));
        repository.Stage("a.txt", "c.txt", "old.txt");
        repository.Commit("two: rework the fixture");
        return repository;
    }
}


