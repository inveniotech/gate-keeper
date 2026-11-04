// SPDX-License-Identifier: Apache-2.0

using InvenioTech.GateKeeper.Engine;
using Xunit;

namespace InvenioTech.GateKeeper.Engine.Tests;

/// <summary>
/// The walk of a plain directory by <see cref="FileEnumerator"/>: the
/// <c>.gitignore</c> semantics, the built-in exclusions, the outside-git
/// warning, and the skip of history (section 6.2 of the configuration
/// specification).
/// </summary>
public sealed class FileEnumeratorOutsideGitTests
{
    private const string OutsideGitWarning =
        "This directory is not a git work tree. The scan walked the directory and applied the built-in exclusions.";

    private const string HistoryWithoutGitWarning =
        "History scanning was requested and this directory is not a git work tree, so history was not scanned.";

    [Fact]
    public async Task Enumerator_Should_WalkTheDirectoryWithGitignoreSemantics_When_TheRootIsNotAWorkTree()
    {
        var directory = GitTestRepository.CreatePlainDirectory();
        directory.WriteFile(
            ".gitignore",
            "*.tmp\nignore-me/\n!keep.tmp\n/root-only.txt\nsub/only.txt\n");
        directory.WriteFile("logs/run.tmp", "excluded tmp fixture\n");
        directory.WriteFile("const/keep.tmp", "re-included tmp fixture\n");
        directory.WriteFile("ignore-me/inner.txt", "excluded subtree fixture\n");
        directory.WriteFile("root-only.txt", "excluded anchored fixture\n");
        directory.WriteFile("deep/root-only.txt", "kept deep look-alike fixture\n");
        directory.WriteFile("sub/only.txt", "excluded middle-slash fixture\n");
        directory.WriteFile("other/sub/only.txt", "kept other look-alike fixture\n");
        directory.WriteFile("nested/deeper/a.md", "kept deep fixture\n");
        directory.WriteFile("vendor/dep.txt", "excluded built-in fixture\n");
        directory.WriteFile("assets/app.min.js", "excluded minified fixture\n");

        var result = await new FileEnumerator().EnumerateAsync(directory.Options(), CancellationToken.None);

        result.IsInsideGitWorkTree.Should().BeFalse();
        result.Files.Should().Equal(
            new EnumeratedFile(".gitignore", "*.tmp\nignore-me/\n!keep.tmp\n/root-only.txt\nsub/only.txt\n"),
            new EnumeratedFile("const/keep.tmp", "re-included tmp fixture\n"),
            new EnumeratedFile("deep/root-only.txt", "kept deep look-alike fixture\n"),
            new EnumeratedFile("nested/deeper/a.md", "kept deep fixture\n"),
            new EnumeratedFile("other/sub/only.txt", "kept other look-alike fixture\n"));
        result.Warnings.Should().Equal(new ScanWarning("scan.outside-git", OutsideGitWarning));
        result.Notes.Should().BeEmpty();
        result.History.Should().BeEmpty();
    }

    [Fact]
    public async Task Enumerator_Should_ReturnAnEmptyScan_When_TheDirectoryCarriesNoFiles()
    {
        var directory = GitTestRepository.CreatePlainDirectory();

        var result = await new FileEnumerator().EnumerateAsync(directory.Options(), CancellationToken.None);

        result.Files.Should().BeEmpty();
        result.Warnings.Should().Equal(new ScanWarning("scan.outside-git", OutsideGitWarning));
        result.Notes.Should().BeEmpty();
    }

    [Fact]
    public async Task Enumerator_Should_LetTheNestedGitignoreOverrideTheParentRules_When_TheNestedGitignoreNegates()
    {
        var directory = GitTestRepository.CreatePlainDirectory();
        directory.WriteFile(".gitignore", "*.tmp\n");
        directory.WriteFile("docs/.gitignore", "!special.tmp\n");
        directory.WriteFile("docs/nested/special.tmp", "kept override fixture\n");
        directory.WriteFile("other/nested/special.tmp", "excluded parent-rule fixture\n");

        var result = await new FileEnumerator().EnumerateAsync(directory.Options(), CancellationToken.None);

        result.Files.Should().Equal(
            new EnumeratedFile(".gitignore", "*.tmp\n"),
            new EnumeratedFile("docs/.gitignore", "!special.tmp\n"),
            new EnumeratedFile("docs/nested/special.tmp", "kept override fixture\n"));
    }

    [Fact]
    public async Task Enumerator_Should_NotReincludeAFileInsideAnIgnoredFolder_When_TheFolderPatternWins()
    {
        var directory = GitTestRepository.CreatePlainDirectory();
        directory.WriteFile(".gitignore", "keep-me/\n!keep-me/inner.txt\n");
        directory.WriteFile("keep-me/inner.txt", "pruned subtree fixture\n");
        directory.WriteFile("outside.txt", "kept plain fixture\n");

        var result = await new FileEnumerator().EnumerateAsync(directory.Options(), CancellationToken.None);

        result.Files.Should().Equal(
            new EnumeratedFile(".gitignore", "keep-me/\n!keep-me/inner.txt\n"),
            new EnumeratedFile("outside.txt", "kept plain fixture\n"));
    }

    [Fact]
    public async Task Enumerator_Should_NeverReincludeBuiltInExclusions_When_GitignoreNegatesThem()
    {
        var directory = GitTestRepository.CreatePlainDirectory();
        directory.WriteFile(".gitignore", "node_modules/\n!node_modules/boarding.md\nvendor/\n!vendor/boarding.md\n");
        directory.WriteFile("node_modules/boarding.md", "built-in fixture stays out\n");
        directory.WriteFile("vendor/boarding.md", "built-in fixture stays out too\n");

        var result = await new FileEnumerator().EnumerateAsync(directory.Options(), CancellationToken.None);

        result.Files.Should().Equal(
            new EnumeratedFile(".gitignore", "node_modules/\n!node_modules/boarding.md\nvendor/\n!vendor/boarding.md\n"));
        result.Warnings.Should().Equal(new ScanWarning("scan.outside-git", OutsideGitWarning));
    }

    [SymlinkFact]
    public async Task Enumerator_Should_SkipSymbolicLinksWithANote_When_TheWalkMeetsThem()
    {
        var directory = GitTestRepository.CreatePlainDirectory();
        directory.WriteFile("real.txt", "real file fixture\n");
        directory.WriteFile("target.txt", "target file fixture\n");
        _ = File.CreateSymbolicLink(directory.FullPath("linked.txt"), directory.FullPath("target.txt"));

        var result = await new FileEnumerator().EnumerateAsync(directory.Options(), CancellationToken.None);

        result.Files.Should().Equal(
            new EnumeratedFile("real.txt", "real file fixture\n"),
            new EnumeratedFile("target.txt", "target file fixture\n"));
        result.Notes.Should().Equal(
            "Skipped 'linked.txt': the entry is a symbolic link, so the scan did not read it.");
    }

    [Fact]
    public async Task Enumerator_Should_ExcludeWalkedFilesByGlob_When_ScanExcludeIsSet()
    {
        var directory = GitTestRepository.CreatePlainDirectory();
        directory.WriteFile("scratch/deep.md", "globbed away fixture\n");
        directory.WriteFile("kept.md", "kept plain fixture\n");

        var options = directory.Options(o => o.ExcludeGlobs = ["scratch/**"]);

        var result = await new FileEnumerator().EnumerateAsync(options, CancellationToken.None);

        result.Files.Should().Equal(new EnumeratedFile("kept.md", "kept plain fixture\n"));
    }

    [Fact]
    public async Task Enumerator_Should_SkipHistoryWithTheWithoutGitWarning_When_HistoryIsRequestedOutsideGit()
    {
        var directory = GitTestRepository.CreatePlainDirectory();
        directory.WriteFile("plain.txt", "plain directory fixture\n");

        var options = directory.Options(o => o.ScanHistory = true);

        var result = await new FileEnumerator().EnumerateAsync(options, CancellationToken.None);

        result.IsInsideGitWorkTree.Should().BeFalse();
        result.Files.Should().Equal(new EnumeratedFile("plain.txt", "plain directory fixture\n"));
        result.History.Should().BeEmpty();
        result.Warnings.Should().Equal(
            new ScanWarning("scan.history-without-git", HistoryWithoutGitWarning),
            new ScanWarning("scan.outside-git", OutsideGitWarning));
    }

    [Fact]
    public async Task Enumerator_Should_KeepSkipDecisionsDeterministic_When_TheWalkRunsTwiceOnTheSameTree()
    {
        var directory = GitTestRepository.CreatePlainDirectory();
        directory.WriteFile("logs/run.tmp", "excluded tmp fixture\n");
        directory.WriteFile("kept.md", "kept plain fixture\n");
        directory.WriteFile("vendor/dep.txt", "excluded built-in fixture\n");

        var enumerator = new FileEnumerator();
        var first = await enumerator.EnumerateAsync(directory.Options(), CancellationToken.None);
        var second = await enumerator.EnumerateAsync(directory.Options(), CancellationToken.None);

        second.Files.Should().Equal(first.Files);
        second.Notes.Should().Equal(first.Notes);
        second.Warnings.Should().Equal(first.Warnings);
    }
}


