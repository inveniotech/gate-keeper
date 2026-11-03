// SPDX-License-Identifier: Apache-2.0

using InvenioTech.GateKeeper.Engine;

namespace InvenioTech.GateKeeper.Engine.Tests;

/// <summary>
/// How <see cref="FileEnumerator"/> treats submodule (gitlink) entries: skip
/// them by default, enumerate checked-out ones on request, and warn when an
/// included submodule is not checked out (section 6.1 of the configuration
/// specification). The fixtures build the gitlink, the checkout and the
/// <c>.gitmodules</c> file by hand, so every byte of the fixture is
/// deterministic on every machine (rule 26).
/// </summary>
public sealed class FileEnumeratorSubmoduleTests
{
    private const string SubmoduleModuleContent = "module-side synthetic sensor fixture.\n";
    private const string SuperContent = "super-side synthetic fixture.\n";
    private const string GitmodulesContent =
        "[submodule \"modules/inner\"]\n" +
        "\tpath = modules/inner\n" +
        "\turl = ../../fixture-child\n";

    [Fact]
    public async Task Enumerator_Should_SkipGitlinkEntriesByDefault_When_SubmodulesAreNotRequested()
    {
        var parent = BuildSubmoduleFixture();

        var result = await new FileEnumerator().EnumerateAsync(parent.Options(), CancellationToken.None);

        result.Files.Should().Equal(
            new EnumeratedFile(".gitmodules", GitmodulesContent),
            new EnumeratedFile("super.txt", SuperContent));
        result.Notes.Should().BeEmpty();
    }

    [Fact]
    public async Task Enumerator_Should_EnumerateTheCheckedOutSubmoduleWithPrefixedPaths_When_SubmodulesAreIncluded()
    {
        var parent = BuildSubmoduleFixture();

        var result = await new FileEnumerator().EnumerateAsync(
            parent.Options(o => o.IncludeSubmodules = true),
            CancellationToken.None);

        result.Files.Should().Equal(
            new EnumeratedFile(".gitmodules", GitmodulesContent),
            new EnumeratedFile("modules/inner/module.cs", SubmoduleModuleContent),
            new EnumeratedFile("super.txt", SuperContent));
        result.Notes.Should().BeEmpty();
    }

    [Fact]
    public async Task Enumerator_Should_WarnOnTheUnCheckedOutSubmodule_When_SubmodulesAreIncludedAndTheDirectoryIsMissing()
    {
        var parent = BuildSubmoduleFixture();
        parent.Git("submodule", "deinit", "--force", "modules/inner");

        var result = await new FileEnumerator().EnumerateAsync(
            parent.Options(o => o.IncludeSubmodules = true),
            CancellationToken.None);

        result.Files.Should().Equal(
            new EnumeratedFile(".gitmodules", GitmodulesContent),
            new EnumeratedFile("super.txt", SuperContent));
        result.Warnings.Should().ContainSingle().Which.Should().Be(
            new ScanWarning(
                "submodule.not-checked-out",
                "The submodule at modules/inner is not checked out, so its files were not scanned."));
    }

    private static GitTestRepository BuildSubmoduleFixture()
    {
        var child = GitTestRepository.Create();
        child.WriteFile("module.cs", SubmoduleModuleContent);
        child.Stage("module.cs");
        var childCommit = child.Commit("child: add the module fixture");

        var parent = GitTestRepository.Create();
        parent.WriteFile("super.txt", SuperContent);
        parent.Stage("super.txt");
        _ = parent.Commit("super: add the super fixture");

        // The checkout of the submodule is a plain local-path clone, which
        // avoids the git file protocol entirely, and the clone lands the
        // child's fixed commit, so the checkout is deterministic too.
        var innerPath = Path.Combine(parent.RootPath, "modules", "inner");
        parent.Git("clone", child.RootPath, innerPath);

        parent.Git(
            "update-index",
            "--add",
            "--cacheinfo",
            "160000," + childCommit + ",modules/inner");
        parent.WriteFile(".gitmodules", GitmodulesContent);
        parent.Stage(".gitmodules", "modules/inner");
        _ = parent.Commit("super: wire the submodule");
        return parent;
    }
}
