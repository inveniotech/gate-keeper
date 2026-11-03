// SPDX-License-Identifier: Apache-2.0

using InvenioTech.GateKeeper.Engine;

namespace InvenioTech.GateKeeper.Engine.Tests;

/// <summary>
/// The <c>--changed-since</c> work-tree limit of <see cref="FileEnumerator"/>
/// (section 6.1 of the configuration specification): the tracked paths whose
/// content differs from a reference, with a tool error for a reference that
/// does not resolve and for the flag outside git.
/// </summary>
public sealed class FileEnumeratorChangedSinceTests
{
    private const string BeforeContent = "Sensor 0001 synthevalues fixture\n";
    private const string AfterContent = "Sensor 0002 synthetic fixture\n";

    private static GitTestRepository BuildTwoCommitRepository()
    {
        var repository = GitTestRepository.Create();
        repository.WriteFile("aa.txt", BeforeContent);
        repository.WriteFile("bb.txt", "unchanged bb fixture\n");
        repository.WriteFile("cc.txt", "unchanged cc fixture\n");
        repository.Stage("aa.txt", "bb.txt", "cc.txt");
        repository.Commit("one: add the three-state fixture");
        repository.WriteFile("aa.txt", AfterContent);
        repository.WriteFile("dd.txt", "new dd fixture\n");
        repository.Stage("aa.txt", "dd.txt");
        repository.Commit("two: change aa and add dd");
        return repository;
    }

    [Fact]
    public async Task Enumerator_Should_LimitTheScanToChangedPaths_When_ChangedSinceIsGiven()
    {
        var repository = BuildTwoCommitRepository();

        var result = await new FileEnumerator().EnumerateAsync(
            repository.Options(o => o.ChangedSince = "HEAD~1"),
            CancellationToken.None);

        result.Files.Should().Equal(
            new EnumeratedFile("aa.txt", AfterContent),
            new EnumeratedFile("dd.txt", "new dd fixture\n"));
        result.Notes.Should().BeEmpty();
    }

    [Fact]
    public async Task Enumerator_Should_KnowTheFullTree_When_ChangedSinceIsOmitted()
    {
        var repository = BuildTwoCommitRepository();

        var result = await new FileEnumerator().EnumerateAsync(repository.Options(), CancellationToken.None);

        result.Files.Should().Equal(
            new EnumeratedFile("aa.txt", AfterContent),
            new EnumeratedFile("bb.txt", "unchanged bb fixture\n"),
            new EnumeratedFile("cc.txt", "unchanged cc fixture\n"),
            new EnumeratedFile("dd.txt", "new dd fixture\n"));
    }

    [Fact]
    public async Task Enumerator_Should_RejectAReferenceThatDoesNotResolve_When_ChangedSinceNamesOne()
    {
        var repository = BuildTwoCommitRepository();

        var options = repository.Options(o => o.ChangedSince = "no-such-ref");

        var act = async () => await new FileEnumerator().EnumerateAsync(options, CancellationToken.None);

        await act.Should().ThrowAsync<EngineToolException>().WithMessage(
            "The reference 'no-such-ref' used with --changed-since does not resolve in this repository. " +
            "Check the reference name, for example a branch, a tag or a commit, and run the scan again.");
    }

    [Fact]
    public async Task Enumerator_Should_RejectChangedSinceOutsideGit_When_TheDirectoryIsNotAWorkTree()
    {
        var directory = GitTestRepository.CreatePlainDirectory();
        directory.WriteFile("plain.txt", "plain directory fixture\n");

        var options = directory.Options(o => o.ChangedSince = "HEAD");

        var act = async () => await new FileEnumerator().EnumerateAsync(options, CancellationToken.None);

        await act.Should().ThrowAsync<EngineToolException>().WithMessage(
            $"The --changed-since flag works only inside a git work tree. " +
            $"'{directory.RootPath}' is not a git work tree, so remove the flag and run the scan again.");
    }
}
