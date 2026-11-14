// SPDX-License-Identifier: Apache-2.0

using InvenioTech.GateKeeper.Engine;

namespace InvenioTech.GateKeeper.Engine.Tests;

public sealed class RepositoryGitStateTests
{
    [Fact]
    public async Task RepositoryGitState_Should_IdentifyATrackedKey_When_ItIsInTheIndex()
    {
        var repo = GitTestRepository.Create();
        try
        {
            repo.WriteFile(".gatekeeper/fingerprint.key", new string('a', 64) + "\n");
            repo.Stage(".gatekeeper/fingerprint.key");
            var state = await RepositoryGitState.ReadAsync(repo.RootPath);
            state.Available.Should().BeTrue();
            state.InsideWorkTree.Should().BeTrue();
            state.FingerprintKeyTracked.Should().BeTrue();
        }
        finally
        {
            foreach (var path in Directory.EnumerateFiles(repo.RootPath, "*", SearchOption.AllDirectories)) File.SetAttributes(path, FileAttributes.Normal);
            Directory.Delete(repo.RootPath, true);
        }
    }

    [Fact]
    public async Task RepositoryGitState_Should_ReportNoTrackedKey_When_OutsideGit()
    {
        var repo = GitTestRepository.CreatePlainDirectory();
        try
        {
            var state = await RepositoryGitState.ReadAsync(repo.RootPath);
            state.Available.Should().BeTrue();
            state.InsideWorkTree.Should().BeFalse();
            state.FingerprintKeyTracked.Should().BeFalse();
        }
        finally
        {
            foreach (var path in Directory.EnumerateFiles(repo.RootPath, "*", SearchOption.AllDirectories)) File.SetAttributes(path, FileAttributes.Normal);
            Directory.Delete(repo.RootPath, true);
        }
    }
}
