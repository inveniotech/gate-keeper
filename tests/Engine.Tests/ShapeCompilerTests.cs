// SPDX-License-Identifier: Apache-2.0

using System.Text.RegularExpressions;

namespace InvenioTech.GateKeeper.Engine.Tests;

/// <summary>
/// The shape compiler (P1-06, card 1): the fixed translation of the
/// structured shape into one <see cref="RegexOptions.NonBacktracking"/>
/// regular expression, the cache that shares one compiled shape per pattern
/// text, the match timeout the work package asks for, and the structural
/// bounds of section 7.1 of the rule-pack format, which are the safety
/// control against a hostile shape.
/// </summary>
public sealed class ShapeCompilerTests
{
    // Semantic tests must not depend on runner scheduling or lazy regex initialisation.
    // Dedicated timeout tests below assert the finite production configuration.
    private static readonly TimeSpan SemanticMatchTimeout = Timeout.InfiniteTimeSpan;

    private static RuleShapePart Part(
        int min,
        int max,
        string? literal = null,
        ShapeClass? shapeClass = null,
        IReadOnlyList<RuleCharRange>? ranges = null,
        IReadOnlyList<string>? oneOf = null,
        bool emit = false)
    {
        return new RuleShapePart(min, max, literal, shapeClass, ranges, oneOf, emit);
    }

    [Fact]
    public void Compiler_Should_TranslateEveryAtom_When_TheShapeCarriesOneOfEachKind()
    {
        var shape = new[]
        {
            Part(1, 1, oneOf: ["B", "A"]),
            Part(4, 4, ranges: [new RuleCharRange('0', '9'), new RuleCharRange('A', 'F')]),
            Part(1, 1, literal: "-"),
            Part(2, 2, shapeClass: ShapeClass.Alnum, emit: true),
        };

        var compiled = CompiledShape.Compile(shape, "pii.synthetic.code", ShapeRegexCache.DefaultMatchTimeout);

        // Alternatives sort longest first, then ordinal. Letters fold to a
        // two-case class (section 7.2), and the emitting part is the one
        // named group the engine reads back.
        compiled.Pattern.Should().Be("(?:[Aa]|[Bb]){1}[0-9A-F]{4}-{1}(?<emit>[0-9A-Za-z]{2})");
        compiled.HasEmitGroup.Should().BeTrue();
    }

    [Fact]
    public void Compiler_Should_EscapeRegexMetacharacters_When_ALiteralCarriesThem()
    {
        var shape = new[] { Part(1, 2, literal: "a.b(c)") };

        var compiled = CompiledShape.Compile(shape, "local.escaped", SemanticMatchTimeout);

        compiled.Regex.IsMatch("a.b(c)a.b(c)").Should().BeTrue();
        compiled.Regex.IsMatch("A.B(C)").Should().BeTrue();
        // The stand-in would match if the dot were a wildcard and the parentheses a group.
        compiled.Regex.IsMatch("an" + "bc").Should().BeFalse();
        compiled.Pattern.Should().NotContain("a.b(c)");
    }

    [Fact]
    public void Compiler_Should_RepeatTheWholeLiteral_When_AMultiCharacterLiteralIsBounded()
    {
        var compiled = CompiledShape.Compile(
            [Part(2, 2, literal: "ab")],
            "local.repeated-literal",
            SemanticMatchTimeout);

        compiled.Regex.IsMatch("abab").Should().BeTrue();
        compiled.Regex.IsMatch("abb").Should().BeFalse();
        compiled.Pattern.Should().Be("(?:[Aa][Bb]){2}");
    }

    [Fact]
    public void Compiler_Should_FoldAsciiLettersOnly_When_ALiteralCarriesALetter()
    {
        var compiled = CompiledShape.Compile(
            [Part(1, 1, literal: "BVN ")],
            "local.folded",
            SemanticMatchTimeout);

        compiled.Regex.IsMatch("bvn ").Should().BeTrue();
        compiled.Regex.IsMatch("BVN ").Should().BeTrue();
        compiled.Regex.IsMatch("bvN ").Should().BeTrue();
    }

    [Theory]
    [InlineData(0, 4, "[0-9]{0,4}")]
    [InlineData(11, 11, "[0-9]{11}")]
    [InlineData(1, 64, "[0-9A-Za-z]{1,64}")]
    public void Compiler_Should_WriteBoundedQuantifiers_When_TheBoundsVary(int min, int max, string expected)
    {
        var shapeClass = expected.StartsWith("[0-9A") ? ShapeClass.Alnum : ShapeClass.Digit;
        var compiled = CompiledShape.Compile(
            [Part(min, max, shapeClass: shapeClass)],
            "local.bounds",
            ShapeRegexCache.DefaultMatchTimeout);

        compiled.Pattern.Should().Be(expected);
    }

    [Fact]
    public void Compiler_Should_TryAlternativesLongestFirst_When_TwoAlternativesShareAStart()
    {
        // A shorter alternative listed first must not mask the longer one at
        // the same position, whatever order the pack lists them in.
        var shape = new[] { Part(1, 1, oneOf: ["processor-a", "processor-alpha"]) };

        var compiled = CompiledShape.Compile(shape, "local.alternation", SemanticMatchTimeout);
        var match = compiled.Regex.Match("uses processor-alpha here");

        match.Value.Should().Be("processor-alpha");
    }

    [Fact]
    public void Compiler_Should_CompiledWithNonBacktrackingAndTheTimeout_When_AShapeCompiles()
    {
        var timeout = TimeSpan.FromMilliseconds(250);
        var compiled = CompiledShape.Compile(
            [Part(11, 11, shapeClass: ShapeClass.Digit, emit: true)],
            "local.digits",
            timeout);

        compiled.Regex.Options.Should().HaveFlag(RegexOptions.NonBacktracking);
        compiled.Regex.Options.Should().HaveFlag(RegexOptions.CultureInvariant);
        compiled.Regex.MatchTimeout.Should().Be(timeout);
    }

    [Fact]
    public void Compiler_Should_ReportTheEmittingSpan_When_APartEmits()
    {
        var shape = new[]
        {
            Part(1, 1, literal: "bvn "),
            Part(11, 11, shapeClass: ShapeClass.Digit, emit: true),
        };

        var compiled = CompiledShape.Compile(shape, "local.emit", SemanticMatchTimeout);
        var match = compiled.Regex.Match("bvn 32826314843 end");
        var emit = match.Groups[compiled.Regex.GroupNumberFromName("emit")];

        match.Value.Should().Be("bvn 32826314843");
        emit.Value.Should().Be("32826314843");
    }

    [Fact]
    public void Compiler_Should_LeaveNoEmitGroup_When_NoPartEmits()
    {
        var compiled = CompiledShape.Compile(
            [Part(11, 11, shapeClass: ShapeClass.Digit)],
            "local.no-emit",
            ShapeRegexCache.DefaultMatchTimeout);

        compiled.HasEmitGroup.Should().BeFalse();
        compiled.Pattern.Should().NotContain("emit");
    }

    [Fact]
    public void Compiler_Should_RefuseASecondEmittingPart_When_AShapeMarksTwo()
    {
        var shape = new[]
        {
            Part(1, 1, shapeClass: ShapeClass.Digit, emit: true),
            Part(2, 2, shapeClass: ShapeClass.Digit, emit: true),
        };

        var act = () => CompiledShape.Compile(shape, "local.two-emits", ShapeRegexCache.DefaultMatchTimeout);

        act.Should().Throw<EngineToolException>().WithMessage(
            "The rule 'local.two-emits' marks 2 shape parts with emit. At most one part emits, and " +
            "that part's span is the finding span (section 7.1).*");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(33)]
    public void Compiler_Should_RefuseThePartCount_When_AShapeCarriesTooManyParts(int count)
    {
        var shape = new RuleShapePart[count];
        for (var index = 0; index < count; index++)
        {
            shape[index] = Part(1, 1, shapeClass: ShapeClass.Digit);
        }

        var act = () => CompiledShape.Compile(shape, "local.part-count", ShapeRegexCache.DefaultMatchTimeout);

        act.Should().Throw<EngineToolException>().WithMessage(
            $"The rule 'local.part-count' carries a shape of {count} parts. A shape has 1 to 32 parts (section 7.1).*");
    }

    [Fact]
    public void Compiler_Should_RefuseBoundsOutsideSectionSevenOne_When_APartCarriesThem()
    {
        var act = () => CompiledShape.Compile(
            [Part(5, 3, shapeClass: ShapeClass.Digit)],
            "local.min-above-max",
            ShapeRegexCache.DefaultMatchTimeout);

        act.Should().Throw<EngineToolException>().WithMessage(
            "A shape part of the rule 'local.min-above-max' sets min (5) above its max (3).*");
    }

    [Fact]
    public void Compiler_Should_RefuseARepeatingAlternation_When_AOneOfPartRepeats()
    {
        var act = () => CompiledShape.Compile(
            [Part(1, 2, oneOf: ["a", "b"])],
            "local.repeating-one-of",
            ShapeRegexCache.DefaultMatchTimeout);

        act.Should().Throw<EngineToolException>().WithMessage(
            "An oneOf part of the rule 'local.repeating-one-of' repeats (max 2).*");
    }

    [Fact]
    public void Compiler_Should_RefuseTheScalarSum_When_AShapeCouldConsumeTooMuch()
    {
        // 32 parts of 64 digits each: 2,048 scalar values, over the 256 bound.
        var shape = new RuleShapePart[32];
        for (var index = 0; index < shape.Length; index++)
        {
            shape[index] = Part(64, 64, shapeClass: ShapeClass.Digit);
        }

        var act = () => CompiledShape.Compile(shape, "local.wide-shape", ShapeRegexCache.DefaultMatchTimeout);

        act.Should().Throw<EngineToolException>().WithMessage(
            "The shape of the rule 'local.wide-shape' could consume 2048 scalar values, over the 256 limit*");
    }

    [Fact]
    public void Compiler_Should_RefuseAmbiguousAtoms_When_APartCarriesTwoKinds()
    {
        var act = () => CompiledShape.Compile(
            [Part(1, 1, literal: "a", shapeClass: ShapeClass.Digit)],
            "local.two-atoms",
            ShapeRegexCache.DefaultMatchTimeout);

        act.Should().Throw<EngineToolException>().WithMessage(
            "A shape part of the rule 'local.two-atoms' does not carry exactly one atom*");
    }

    [Fact]
    public void Cache_Should_ShareOneCompiledShape_When_TwoShapesTranslateToTheSamePattern()
    {
        var cache = new ShapeRegexCache();
        var byClass = cache.GetOrCompile([Part(11, 11, shapeClass: ShapeClass.Digit)], "local.by-class");
        var byRange = cache.GetOrCompile(
            [Part(11, 11, ranges: [new RuleCharRange('0', '9')])],
            "local.by-range");

        byRange.Should().BeSameAs(byClass);
    }

    [Fact]
    public void Cache_Should_CompileEachDistinctPatternOnce_When_RulesRepeatShapes()
    {
        var cache = new ShapeRegexCache();
        var first = cache.GetOrCompile([Part(11, 11, shapeClass: ShapeClass.Digit, emit: true)], "local.first");
        var second = cache.GetOrCompile([Part(11, 11, shapeClass: ShapeClass.Digit, emit: true)], "local.second");

        second.Should().BeSameAs(first);
    }

    [Fact]
    public void Cache_Should_CarryTheDefaultTimeout_When_TheCallerNamesNone()
    {
        ShapeRegexCache.DefaultMatchTimeout.Should().Be(TimeSpan.FromMilliseconds(100));
        var cache = new ShapeRegexCache();
        var compiled = cache.GetOrCompile([Part(1, 1, shapeClass: ShapeClass.Digit)], "local.timeout");

        compiled.Regex.MatchTimeout.Should().Be(ShapeRegexCache.DefaultMatchTimeout);
    }

    [Fact]
    public void Cache_Should_CarryTheNamedTimeout_When_TheCallerSetsOne()
    {
        var timeout = Timeout.InfiniteTimeSpan;
        var cache = new ShapeRegexCache(timeout);
        var compiled = cache.GetOrCompile([Part(1, 1, shapeClass: ShapeClass.Digit)], "local.infinite");

        compiled.Regex.MatchTimeout.Should().Be(timeout);
    }
}
