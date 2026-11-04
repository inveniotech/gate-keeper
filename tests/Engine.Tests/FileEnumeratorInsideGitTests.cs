// SPDX-License-Identifier: Apache-2.0

using InvenioTech.GateKeeper.Engine;

namespace InvenioTech.GateKeeper.Engine.Tests;

/// <summary>
/// The behaviour of <see cref="FileEnumerator"/> inside a git work tree: track
/// only what the index tracks, name and order paths deterministically, and
/// refuse roots that are not the repository root (section 6.1 of the
/// configuration specification).
/// </summary>
public sealed class FileEnumeratorInsideGitTests
{
    private static readonly string ReadmeContent = "Inventory of synthetic sensor fixtures.\r\n";

    private static readonly string FinderContent =
        "using InvenioTech.GateKeeper.Engine;\n\nnamespace Engine.Fixtures;\n\n// Sensor file " +
        GitTestRepository.SensorLine(1) + "\n";

    private static readonly string NotesContent = "Synthetic notes for " + GitTestRepository.SensorLine(2);

    [Fact]
    public async Task Enumerator_Should_ListTrackedFilesWithText_When_TheRepositoryContainsTrackedFiles()
    {
        var repository = GitTestRepository.Create();
        repository.WriteFile("README.md", ReadmeContent);
        repository.WriteFile("src/engine/Finder.cs", FinderContent);
        repository.WriteFile("docs/notes/Summary.md", NotesContent);
        repository.Stage("README.md", "src/engine/Finder.cs", "docs/notes/Summary.md");
        repository.Commit("one: add the corpus fixture files");

        var result = await new FileEnumerator().EnumerateAsync(repository.Options(), CancellationToken.None);

        result.IsInsideGitWorkTree.Should().BeTrue();
        result.Files.Should().Equal(
            new EnumeratedFile("README.md", ReadmeContent),
            new EnumeratedFile("docs/notes/Summary.md", NotesContent),
            new EnumeratedFile("src/engine/Finder.cs", FinderContent));
    }

    [Fact]
    public async Task Enumerator_Should_LeaveUntrackedFilesOutOfTheScan_When_GitDoesNotTrackThem()
    {
        var repository = GitTestRepository.Create();
        repository.WriteFile("tracked.txt", GitTestRepository.SensorLine(1));
        repository.Stage("tracked.txt");
        repository.Commit("one: add the tracked file");
        repository.WriteFile("untracked.txt", GitTestRepository.SensorLine(2));

        var result = await new FileEnumerator().EnumerateAsync(repository.Options(), CancellationToken.None);

        result.Files.Should().ContainSingle().Which.Path.Should().Be("tracked.txt");
        result.Notes.Should().BeEmpty();
    }

    [Fact]
    public async Task Enumerator_Should_ReturnThePathsInOrdinalOrder_When_TheNamesCapitalisationVaries()
    {
        var repository = GitTestRepository.Create();
        repository.WriteFile("a.txt", GitTestRepository.SensorLine(1));
        repository.WriteFile("b/deeper.txt", GitTestRepository.SensorLine(2));
        repository.WriteFile("B2/upper.x", GitTestRepository.SensorLine(3));
        repository.Stage("a.txt", "b/deeper.txt", "B2/upper.x");
        repository.Commit("one: add the ordinal-order fixture");

        var result = await new FileEnumerator().EnumerateAsync(repository.Options(), CancellationToken.None);

        result.Files.Select(file => file.Path).Should().Equal(
            "B2/upper.x",
            "a.txt",
            "b/deeper.txt");
    }

    [Fact]
    public async Task Enumerator_Should_HaveNoNotesOrWarnings_When_TheTrackedFilesNeedNoDecisions()
    {
        var repository = GitTestRepository.Create();
        repository.WriteFile("tracked.txt", GitTestRepository.SensorLine(1));
        repository.Stage("tracked.txt");
        repository.Commit("one: add the plain tracked fixture");

        var result = await new FileEnumerator().EnumerateAsync(repository.Options(), CancellationToken.None);

        result.Notes.Should().BeEmpty();
        result.Warnings.Should().BeEmpty();
        result.History.Should().BeEmpty();
    }

    [Fact]
    public async Task Enumerator_Should_ProduceTheSamePathsAndTexts_When_RunAgainstTheSameRepositoryTwice()
    {
        var repository = GitTestRepository.Create();
        repository.WriteFile("src/first.cs", GitTestRepository.SensorLine(1));
        repository.WriteFile("src/nested/second.cs", GitTestRepository.SensorLine(2));
        repository.Stage("src/first.cs", "src/nested/second.cs");
        repository.Commit("one: add the stability fixture");

        var enumerator = new FileEnumerator();
        var first = await enumerator.EnumerateAsync(repository.Options(), CancellationToken.None);
        var second = await enumerator.EnumerateAsync(repository.Options(), CancellationToken.None);

        second.Files.Select(file => file.Path).Should().Equal(first.Files.Select(file => file.Path));
        second.Files.Select(file => file.Text).Should().Equal(first.Files.Select(file => file.Text));
    }

    [Fact]
    public async Task Enumerator_Should_RefuseTheScan_When_TheRootIsNotTheRepositoryRoot()
    {
        var repository = GitTestRepository.Create();
        repository.WriteFile("src/engine/Finder.cs", FinderContent);
        repository.Stage("src/engine/Finder.cs");
        repository.Commit("one: add the root-refusal fixture");

        var options = repository.Options(o =>
        {
            o.Root = Path.Combine(repository.RootPath, "src");
        });

        var act = async () => await new FileEnumerator().EnumerateAsync(options, CancellationToken.None);

        // The message names git's own answer for the work-tree root, which on
        // macOS is the physical path behind the symlinked temporary directory
        // ('/private/var/folders/...'). The normalisation here is the plain
        // full-path form of git's answer, not the scan's own canonicalisation.
        var toplevel = Path.GetFullPath(
            repository.Git("rev-parse", "--show-toplevel").TrimEnd('\n', '\r'));

        await act.Should().ThrowAsync<EngineToolException>().WithMessage(
            $"The scan must run on the repository root. '{options.Root}' is not the repository root; " +
            $"the repository root is '{toplevel}'.");
    }

    [SymlinkFact]
    public async Task Enumerator_Should_ScanThroughASymlinkedRoot_When_TheRepositoryRootIsNamedThroughALink()
    {
        var repository = GitTestRepository.Create();
        repository.WriteFile("tracked.txt", GitTestRepository.SensorLine(1));
        repository.Stage("tracked.txt");
        repository.Commit("one: add the symlinked-root fixture");

        var link = Path.Combine(
            Path.GetDirectoryName(repository.RootPath)!,
            $"gk-link-{Guid.NewGuid():N}");
        _ = Directory.CreateSymbolicLink(link, repository.RootPath);

        var options = repository.Options(o => o.Root = link);

        var result = await new FileEnumerator().EnumerateAsync(options, CancellationToken.None);

        result.IsInsideGitWorkTree.Should().BeTrue();
        result.Files.Should().ContainSingle().Which.Path.Should().Be("tracked.txt");
    }
}
