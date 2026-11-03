// SPDX-License-Identifier: Apache-2.0

using InvenioTech.GateKeeper.Engine;

namespace InvenioTech.GateKeeper.Engine.Tests;

/// <summary>
/// The file-level scan-scope decisions of <see cref="FileEnumerator"/> inside
/// a git work tree: the size limit, the binary and encoding classification,
/// the Git LFS pointer skip, the built-in exclusions and the
/// <c>scan.exclude</c> globs (sections 6.3 and 6.4 of the configuration
/// specification).
/// </summary>
public sealed class FileEnumeratorScanScopeTests
{
    private const string PlainContent = "A synthetic plain fixture. Sensor 0001\n";

    [Fact]
    public async Task Enumerator_Should_SkipTheOversizedFileWithANote_When_TheBytesExceedTheMaxFileBytes()
    {
        var repository = GitTestRepository.Create();
        repository.WriteFile("engine.ini", "value=pretty-large-setting");

        var options = repository.Options(o => o.MaxFileBytes = 16);

        var result = await new FileEnumerator().EnumerateAsync(options, CancellationToken.None);

        result.Files.Should().BeEmpty();
        result.Notes.Should().Equal(
            "Skipped 'engine.ini': the file is 26 bytes, and the limit is 16 bytes.");
    }

    [Fact]
    public async Task Enumerator_Should_KeepTheSmallFile_When_OtherFilesWereSkippedForTheirSize()
    {
        var repository = GitTestRepository.Create();
        repository.WriteFile("engine.ini", "value=pretty-large-setting");
        repository.WriteFile("small.txt", PlainContent);

        var options = repository.Options(o => o.MaxFileBytes = 16);

        var result = await new FileEnumerator().EnumerateAsync(options, CancellationToken.None);

        result.Files.Should().Equal(new EnumeratedFile("small.txt", PlainContent));
    }

    [Fact]
    public async Task Enumerator_Should_SkipTheBinaryFileWithANote_When_TheFirstBytesCarryANullCharacter()
    {
        var repository = GitTestRepository.Create();
        repository.WriteBytes("weird.txt", [0x50, 0x49, 0x00, 0x4E, 0xFF, 0xFE, 0x00, 0x01]);

        var result = await new FileEnumerator().EnumerateAsync(repository.Options(), CancellationToken.None);

        result.Files.Should().BeEmpty();
        result.Notes.Should().Equal("Skipped 'weird.txt': the file is binary.");
    }

    [Fact]
    public async Task Enumerator_Should_DecodeUtf16LeBytes_When_TheFileStartsWithTheLittleEndianByteOrderMark()
    {
        var repository = GitTestRepository.Create();
        repository.WriteBytes("sensor.log", [0xFF, 0xFE, 0x61, 0x00, 0x62, 0x00, 0x0A, 0x00]);

        var result = await new FileEnumerator().EnumerateAsync(repository.Options(), CancellationToken.None);

        result.Files.Should().Equal(new EnumeratedFile("sensor.log", "ab\n"));
        result.Notes.Should().BeEmpty();
    }

    [Fact]
    public async Task Enumerator_Should_DecodeUtf16BeBytes_When_TheFileStartsWithTheBigEndianByteOrderMark()
    {
        var repository = GitTestRepository.Create();
        repository.WriteBytes("sensor.log", [0xFE, 0xFF, 0x61, 0x00, 0x62, 0x00, 0x0A, 0x00]);

        var result = await new FileEnumerator().EnumerateAsync(repository.Options(), CancellationToken.None);

        result.Files.Should().Equal(new EnumeratedFile("sensor.log", "ab\n"));
    }

    [Fact]
    public async Task Enumerator_Should_TreatUtf16WithoutAByteOrderMarkAsBinary_When_TheBytesCarryNullBytes()
    {
        var repository = GitTestRepository.Create();
        repository.WriteBytes("sensor.log", [0x61, 0x00, 0x62, 0x00, 0x0A, 0x00]);

        var result = await new FileEnumerator().EnumerateAsync(repository.Options(), CancellationToken.None);

        result.Files.Should().BeEmpty();
        result.Notes.Should().Equal("Skipped 'sensor.log': the file is binary.");
    }

    [Fact]
    public async Task Enumerator_Should_SkipTheInvalidUtf8FileWithANote_When_NoByteOrderMarkNamesTheEncoding()
    {
        var repository = GitTestRepository.Create();
        repository.WriteBytes("odd.md", [0x48, 0xC3, 0x28, 0x4C, 0x0A]);

        var result = await new FileEnumerator().EnumerateAsync(repository.Options(), CancellationToken.None);

        result.Files.Should().BeEmpty();
        result.Notes.Should().Equal(
            "Skipped 'odd.md': the bytes are not valid UTF-8 and the file carries no byte-order mark.");
    }

    [Fact]
    public async Task Enumerator_Should_DropTheByteOrderMark_When_TheUtf8FileStartsWithIt()
    {
        var repository = GitTestRepository.Create();
        repository.WriteBytes("marked.md", [0xEF, 0xBB, 0xBF, 0x48, 0x4C, 0x0A]);

        var result = await new FileEnumerator().EnumerateAsync(repository.Options(), CancellationToken.None);

        result.Files.Should().Equal(new EnumeratedFile("marked.md", "HL\n"));
        result.Notes.Should().BeEmpty();
    }

    [Fact]
    public async Task Enumerator_Should_SkipTheLfsPointerWithANote_When_TheBytesBeginWithThePointerBanner()
    {
        var repository = GitTestRepository.Create();
        repository.WriteFile(
            "assets/pointer.bin",
            "version https://git-lfs.github.com/spec/v1\noid sha256:" + new string('0', 64) + "\nsize 12\n");

        var result = await new FileEnumerator().EnumerateAsync(repository.Options(), CancellationToken.None);

        result.Files.Should().BeEmpty();
        result.Notes.Should().Equal(
            "Skipped 'assets/pointer.bin': the file is a Git LFS pointer. The scan does not fetch Git LFS objects.");
    }

    [Fact]
    public async Task Enumerator_Should_ExcludeTrackedPathsUnderBuiltInNames_When_TheSegmentEqualsAnExclusion()
    {
        var repository = GitTestRepository.Create();
        repository.WriteFile("node_modules/dep/index.js", "// synthetic\n");
        repository.WriteFile("bin/machine/a.txt", "a\n");
        repository.WriteFile("obj/machine/a.txt", "a\n");
        repository.WriteFile("dist/machine/a.txt", "a\n");
        repository.WriteFile("build/machine/a.txt", "a\n");
        repository.WriteFile("vendor/machine/a.txt", "a\n");
        repository.WriteFile(".ndpa-check/pack.json", "{}\n");
        repository.Stage(
            "node_modules/dep/index.js",
            "bin/machine/a.txt",
            "obj/machine/a.txt",
            "dist/machine/a.txt",
            "build/machine/a.txt",
            "vendor/machine/a.txt",
            ".ndpa-check/pack.json");
        repository.Commit("one: add the built-in exclusion fixtures");

        var result = await new FileEnumerator().EnumerateAsync(repository.Options(), CancellationToken.None);

        result.Files.Should().BeEmpty();
        result.Notes.Should().BeEmpty();
    }

    [Fact]
    public async Task Enumerator_Should_KeepTrackedPathsThatOnlyLookSimilar_When_TheSegmentIsNotAnExactExclusion()
    {
        var repository = GitTestRepository.Create();
        repository.WriteFile("location.ndpa-check/reader.md", "Synthetic reader fixture.\n");
        repository.WriteFile("distributor/outer/a.md", "Synthetic outer fixture.\n");
        repository.Stage("location.ndpa-check/reader.md", "distributor/outer/a.md");
        repository.Commit("one: add the look-alike fixtures");

        var result = await new FileEnumerator().EnumerateAsync(repository.Options(), CancellationToken.None);

        result.Files.Should().Equal(
            new EnumeratedFile("distributor/outer/a.md", "Synthetic outer fixture.\n"),
            new EnumeratedFile("location.ndpa-check/reader.md", "Synthetic reader fixture.\n"));
    }

    [Fact]
    public async Task Enumerator_Should_ExcludeTrackedPathsUnderBuiltInNamesRegardlessOfCase_When_TheCaseDiffers()
    {
        var repository = GitTestRepository.Create();
        repository.WriteFile("BIN/machine/a.txt", "a\n");
        repository.WriteFile("Build/machine/a.txt", "a\n");
        repository.Stage("BIN/machine/a.txt", "Build/machine/a.txt");
        repository.Commit("one: add the case-insensitive exclusion fixtures");

        var result = await new FileEnumerator().EnumerateAsync(repository.Options(), CancellationToken.None);

        result.Files.Should().BeEmpty();
    }

    [Fact]
    public async Task Enumerator_Should_ExcludeTrackedLockFiles_When_TheFileNameMatchesTheClosedSet()
    {
        var repository = GitTestRepository.Create();
        repository.WriteFile("packages.lock.json", "{}\n");
        repository.WriteFile("yarn.lock", "{}\n");
        repository.WriteFile("Cargo.lock", "{}\n");
        repository.WriteFile("poetry.lock", "{}\n");
        repository.Stage("packages.lock.json", "yarn.lock", "Cargo.lock", "poetry.lock");
        repository.Commit("one: add the lock-file fixtures");

        var result = await new FileEnumerator().EnumerateAsync(repository.Options(), CancellationToken.None);

        result.Files.Should().BeEmpty();
    }

    [Fact]
    public async Task Enumerator_Should_KeepTrackedFilesWhoseNamesAreNotLockNames_When_TheNameIsOnlySimilar()
    {
        var repository = GitTestRepository.Create();
        repository.WriteFile("my.lock", "{}\n");
        repository.WriteFile("lock.cargo", "{}\n");
        repository.Stage("my.lock", "lock.cargo");
        repository.Commit("one: add the lock look-alike fixtures");

        var result = await new FileEnumerator().EnumerateAsync(repository.Options(), CancellationToken.None);

        result.Files.Should().Equal(
            new EnumeratedFile("lock.cargo", "{}\n"),
            new EnumeratedFile("my.lock", "{}\n"));
    }

    [Fact]
    public async Task Enumerator_Should_ExcludeTrackedMinifiedFiles_When_TheNameEndsWithTheMinifiedSuffix()
    {
        var repository = GitTestRepository.Create();
        repository.WriteFile("assets/app.min.js", "var a=1;var b=2;\n");
        repository.WriteFile("assets/style.MIN.CSS", "body{margin:0}\n");
        repository.Stage("assets/app.min.js", "assets/style.MIN.CSS");
        repository.Commit("one: add the minified fixtures");

        var result = await new FileEnumerator().EnumerateAsync(repository.Options(), CancellationToken.None);

        result.Files.Should().BeEmpty();
    }

    [Fact]
    public async Task Enumerator_Should_KeepTrackedFilesWhoseNamesOnlyResembleMinifiedNames_When_TheSuffixIsLonger()
    {
        var repository = GitTestRepository.Create();
        repository.WriteFile("assets/app.min.js.txt", "var a=1;\n");
        repository.WriteFile("assets/app.minparser.js", "var a=1;\n");
        repository.Stage("assets/app.min.js.txt", "assets/app.minparser.js");
        repository.Commit("one: add the minified look-alike fixtures");

        var result = await new FileEnumerator().EnumerateAsync(repository.Options(), CancellationToken.None);

        result.Files.Should().Equal(
            new EnumeratedFile("assets/app.min.js.txt", "var a=1;\n"),
            new EnumeratedFile("assets/app.minparser.js", "var a=1;\n"));
    }

    [Fact]
    public async Task Enumerator_Should_ExcludeTrackedFilesByGlob_When_ScanExcludeIsSet()
    {
        var repository = GitTestRepository.Create();
        repository.WriteFile("scratch/nested/deep.md", "scratch fixture\n");
        repository.WriteFile("root.txt", "root fixture\n");
        repository.WriteFile("other/scratch/nested/deep.md", "kept other fixture\n");
        repository.WriteFile("src/plain.cs", "kept src fixture\n");
        repository.Stage(
            "scratch/nested/deep.md",
            "root.txt",
            "other/scratch/nested/deep.md",
            "src/plain.cs");
        repository.Commit("one: add the glob exclusion fixtures");

        var options = repository.Options(o => o.ExcludeGlobs = ["scratch/**", "root.txt"]);

        var result = await new FileEnumerator().EnumerateAsync(options, CancellationToken.None);

        result.Files.Should().Equal(
            new EnumeratedFile("other/scratch/nested/deep.md", "kept other fixture\n"),
            new EnumeratedFile("src/plain.cs", "kept src fixture\n"));
        result.Notes.Should().BeEmpty();
    }

    [Fact]
    public async Task Enumerator_Should_NoteTheDeletedTrackedFile_When_GitTracksAPathThatNoLongerExists()
    {
        var repository = GitTestRepository.Create();
        repository.WriteFile("gone.txt", "tracked then deleted\n");
        repository.WriteFile("kept.txt", PlainContent);
        repository.Stage("gone.txt", "kept.txt");
        repository.Commit("one: add the deletion fixture");
        File.Delete(repository.FullPath("gone.txt"));

        var result = await new FileEnumerator().EnumerateAsync(repository.Options(), CancellationToken.None);

        result.Files.Should().Equal(new EnumeratedFile("kept.txt", PlainContent));
        result.Notes.Should().Equal(
            "Skipped 'gone.txt': the file is tracked by git but no longer exists on disk.");
    }
}
