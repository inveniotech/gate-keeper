// SPDX-License-Identifier: Apache-2.0

using InvenioTech.GateKeeper.Engine;

namespace InvenioTech.GateKeeper.Engine.Tests;

/// <summary>
/// The glob subset that <c>scan.exclude</c> entries use (section 6.3 of the
/// configuration specification): only <c>*</c> and <c>**</c>, ordinal and
/// case-sensitive matching against the forward-slash paths the enumerator
/// returns, and a tool error for every candidate outside the subset.
/// </summary>
public sealed class PathGlobTests
{
    [Theory]
    [InlineData("*.cs", "readme.cs", true)]
    [InlineData("*.cs", "src/a.cs", false)] // A glob with no slash matches the repository root only.
    [InlineData("**", "src/a.cs", true)]
    [InlineData("**", "a.cs", true)]
    [InlineData("**/*.cs", "a.cs", true)] // ** also matches zero segments.
    [InlineData("**/*.cs", "src/a.cs", true)]
    [InlineData("src/**", "src", true)]
    [InlineData("src/**/*.ts", "src/x/y.ts", true)]
    [InlineData("a/**/b.txt", "a/b.txt", true)]
    [InlineData("a/**/b.txt", "a/one/b.txt", true)]
    [InlineData("a/**/b.txt", "a/one/two/b.txt", true)]
    [InlineData("scratch/**", "other/scratch/nested/deep.md", false)] // Globs are relative to the root.
    [InlineData("src/*.cs", "src/engine/Finder.cs", false)] // * does not cross a segment.
    [InlineData("src/*/x.cs", "src/engine/x.cs", true)]
    [InlineData("*.cs", "READ.CS", false)] // Matching is case-sensitive.
    [InlineData("a?c.txt", "abc.txt", false)] // ? is not in the subset: it never compiles when used.
    public void Glob_Should_MatchTheDocumentedSubset_When_ThePatternAndPathVary(string pattern, string path, bool expected)
    {
        if (pattern.Contains('?'))
        {
            var act = () => PathGlob.Compile(pattern).IsMatch(path);
            act.Should().Throw<EngineToolException>();
            return;
        }

        PathGlob.Compile(pattern).IsMatch(path).Should().Be(expected);
    }

    [Fact]
    public void Glob_Should_ReportTheQuestionMark_When_ThePatternCarriesOne()
    {
        var act = () => PathGlob.Compile("a?c.txt");

        act.Should().Throw<EngineToolException>().WithMessage(
            "The 'scan.exclude' glob 'a?c.txt' is not in the supported pattern subset. " +
            "The '?' character is not supported. " +
            "The only metacharacters are '*' inside one path segment and '**' across segments.");
    }

    [Fact]
    public void Glob_Should_ReportTheBracketClass_When_ThePatternCarriesOne()
    {
        var act = () => PathGlob.Compile("[ab].txt");

        act.Should().Throw<EngineToolException>().WithMessage(
            "The 'scan.exclude' glob '[ab].txt' is not in the supported pattern subset. " +
            "Character classes are not supported. " +
            "The only metacharacters are '*' inside one path segment and '**' across segments.");
    }

    [Fact]
    public void Glob_Should_ReportTheBraceExpansion_When_ThePatternCarriesOne()
    {
        var act = () => PathGlob.Compile("{a,b}.txt");

        act.Should().Throw<EngineToolException>().WithMessage(
            "The 'scan.exclude' glob '{a,b}.txt' is not in the supported pattern subset. " +
            "Brace expansion is not supported. " +
            "The only metacharacters are '*' inside one path segment and '**' across segments.");
    }

    [Fact]
    public void Glob_Should_ReportTheLeadingSlash_When_ThePatternCarriesOne()
    {
        var act = () => PathGlob.Compile("/src/*.cs");

        act.Should().Throw<EngineToolException>().WithMessage(
            "The 'scan.exclude' glob '/src/*.cs' is not in the supported pattern subset. " +
            "A leading '/' is not valid: globs are relative to the repository root. " +
            "The only metacharacters are '*' inside one path segment and '**' across segments.");
    }

    [Fact]
    public void Glob_Should_ReportTheBackslash_When_ThePatternCarriesOne()
    {
        var act = () => PathGlob.Compile("a\\b.txt");

        act.Should().Throw<EngineToolException>().WithMessage(
            "The 'scan.exclude' glob 'a\\b.txt' is not in the supported pattern subset. " +
            "A backslash is not valid: use '/' only. " +
            "The only metacharacters are '*' inside one path segment and '**' across segments.");
    }

    [Fact]
    public void Glob_Should_ReportTheParentSegment_When_ThePatternCarriesOne()
    {
        var act = () => PathGlob.Compile("x/../*.txt");

        act.Should().Throw<EngineToolException>().WithMessage(
            "The 'scan.exclude' glob 'x/../*.txt' is not in the supported pattern subset. " +
            "The '..' segment is not valid: globs cannot name a path outside the repository root. " +
            "The only metacharacters are '*' inside one path segment and '**' across segments.");
    }
}
