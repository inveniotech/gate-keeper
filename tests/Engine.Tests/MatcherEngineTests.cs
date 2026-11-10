// SPDX-License-Identifier: Apache-2.0

using InvenioTech.GateKeeper.Engine;

namespace InvenioTech.GateKeeper.Engine.Tests;

/// <summary>
/// The matcher engine (P1-06): keyword windows, location scopes, suppression
/// hints, the list matcher, the resume rule, and the exclusions a pattern
/// match has to honour before it becomes a match.
/// </summary>
public sealed class MatcherEngineTests
{
    private static readonly Citation Citation = new("Synthetic citation for the matcher tests.", null, null);

    [Fact]
    public void MatcherEngine_Should_AcceptAKeywordInsideTheWindow_When_TheLabelSitsBesideTheValue()
    {
        var text = "Identity BVN 32826314843.";
        var matches = Scan(text, Digits(keywords: ["bvn"], window: 16, boundary: MatchBoundary.Digit));

        matches.Should().ContainSingle();
        matches[0].Column.Should().Be(ColumnOf(text, "32826314843"));
        matches[0].Line.Should().Be(1);
        matches[0].LocationKind.Should().Be(LocationKind.Source);
    }

    [Fact]
    public void MatcherEngine_Should_AcceptAKeywordInsideAnIdentifier_When_TheKeywordIsASubstring()
    {
        var text = "customerBvn=32826314843";
        var matches = Scan(text, Digits(keywords: ["bvn"], window: 8, boundary: MatchBoundary.Digit));

        matches.Should().ContainSingle();
        matches[0].Column.Should().Be(ColumnOf(text, "32826314843"));
    }

    [Fact]
    public void MatcherEngine_Should_FoldTheKeyword_When_TheLabelUsesADifferentAsciiCase()
    {
        var text = "bvn 32826314843";
        var matches = Scan(text, Digits(keywords: ["BVN"], window: 4, boundary: MatchBoundary.Digit));

        matches.Should().ContainSingle();
    }

    [Fact]
    public void MatcherEngine_Should_RejectTheMatch_When_TheKeywordSitsOutsideTheWindow()
    {
        var text = "bvn" + new string(' ', 20) + "32826314843";
        var matches = Scan(text, Digits(keywords: ["bvn"], window: 8, boundary: MatchBoundary.Digit));

        matches.Should().BeEmpty();
    }

    [Fact]
    public void MatcherEngine_Should_AcceptAKeywordAfterTheValue_When_ItSitsInsideTheWindow()
    {
        var text = "32826314843 BVN";
        var matches = Scan(text, Digits(keywords: ["bvn"], window: 4, boundary: MatchBoundary.Digit));

        matches.Should().ContainSingle();
        matches[0].Column.Should().Be(1);
    }

    [Fact]
    public void MatcherEngine_Should_RejectTheMatch_When_TheFileHasNoKeyword()
    {
        var matches = Scan("32826314843", Digits(keywords: ["bvn"], window: 32, boundary: MatchBoundary.Digit));

        matches.Should().BeEmpty();
    }

    [Fact]
    public void MatcherEngine_Should_ReportNothing_When_ADigitBoundaryRejectsAnElevenDigitRunInsideTwelve()
    {
        var matches = Scan("123456789012", Digits(boundary: MatchBoundary.Digit));

        matches.Should().BeEmpty();
    }

    [Fact]
    public void MatcherEngine_Should_ReportTheBoundedRun_When_ElevenDigitsStandAlone()
    {
        var text = "x12345678901y";
        var matches = Scan(text, Digits(boundary: MatchBoundary.Digit));

        matches.Should().ContainSingle();
        matches[0].Column.Should().Be(ColumnOf(text, "12345678901"));
    }

    [Fact]
    public void MatcherEngine_Should_RejectALetterNeighbour_When_TheBoundaryIsAlnum()
    {
        var matches = Scan("a12345678901", Digits(boundary: MatchBoundary.Alnum));

        matches.Should().BeEmpty();
    }

    [Fact]
    public void MatcherEngine_Should_MatchAnAddressAtTheTextEdges_When_TheBoundaryIsEmail()
    {
        // The address is the whole text, so both ends of the text count as boundaries.
        var text = "ada@ledgerops.ng";
        var matches = Scan(text, EmailRule(MatchBoundary.Email));

        matches.Should().ContainSingle();
        matches[0].Column.Should().Be(1);
    }

    [Fact]
    public void MatcherEngine_Should_AcceptASentenceDotTail_When_TheBoundaryIsEmail()
    {
        // The trailing neighbour set leaves out '.', so a sentence dot after the domain does not reject the match.
        var text = "to ada@ledgerops.ng.";
        var matches = Scan(text, EmailRule(MatchBoundary.Email));

        matches.Should().ContainSingle();
        matches[0].Column.Should().Be(4);
    }

    [Fact]
    public void MatcherEngine_Should_MatchADottedLocalPart_When_TheBoundaryIsEmail()
    {
        var text = "to dipo.ada@ledgerops.ng";
        var matches = Scan(text, DottedEmailRule(MatchBoundary.Email));

        matches.Should().ContainSingle();
        matches[0].Column.Should().Be(4);
    }

    [Fact]
    public void MatcherEngine_Should_RejectAnInternalDotStart_When_TheBoundaryIsEmail()
    {
        // A longer local part lets the shape align after an internal dot; the
        // leading neighbour set keeps '.', so that shortened alignment is rejected.
        var matches = Scan("to dipo.ada.bola@ledgerops.ng", DottedEmailRule(MatchBoundary.Email));

        matches.Should().BeEmpty();
    }

    [Fact]
    public void MatcherEngine_Should_RejectATrailingLabelNeighbour_When_TheBoundaryIsEmail()
    {
        // The shape ends inside a longer label run, so the trailing neighbour '-' rejects the span.
        var matches = Scan("to ada@ledgerops.ng-uk", EmailRule(MatchBoundary.Email));

        matches.Should().BeEmpty();
    }

    [Fact]
    public void MatcherEngine_Should_ResumeOneScalarLater_When_TheFirstAlignmentIsExcluded()
    {
        // Eleven ones are excluded. The alignment one scalar later is a different span and stands.
        var text = "111111111112";
        var rule = Digits(
            boundary: MatchBoundary.None,
            excludeValues: ["11111111111"]);
        var matches = Scan(text, rule);

        matches.Should().ContainSingle();
        matches[0].Column.Should().Be(2);
        matches[0].SpanLength.Should().Be(11);
    }

    [Fact]
    public void MatcherEngine_Should_ReportBothRuns_When_TwoValuesAreSeparated()
    {
        var text = "32826314843 and 59217260258";
        var matches = Scan(text, Digits(boundary: MatchBoundary.Digit));

        matches.Should().HaveCount(2);
        matches[0].Column.Should().Be(1);
        matches[1].Column.Should().Be(ColumnOf(text, "59217260258"));
    }

    [Fact]
    public void MatcherEngine_Should_CountTheSecondLine_When_TheValueFollowsACrlfBreak()
    {
        var text = "head\r\n08079012062";
        var matches = Scan(text, Phone());

        matches.Should().ContainSingle();
        matches[0].Line.Should().Be(2);
        matches[0].Column.Should().Be(1);
    }

    [Theory]
    [InlineData(LocationKind.Log)]
    [InlineData(LocationKind.Migration)]
    [InlineData(LocationKind.ApiModel)]
    [InlineData(LocationKind.Configuration)]
    [InlineData(LocationKind.Fixture)]
    [InlineData(LocationKind.Source)]
    public void MatcherEngine_Should_StampTheRuleLocationKind_When_TheRuleNamesOne(LocationKind kind)
    {
        var rule = Digits(boundary: MatchBoundary.None, kind: kind);
        var matches = Scan("32826314843", rule);

        matches.Should().ContainSingle();
        matches[0].LocationKind.Should().Be(kind);
    }

    [Fact]
    public void MatcherEngine_Should_MatchOnlyTheNamedPaths_When_TheLocationCarriesGlobs()
    {
        var rule = Digits(
            boundary: MatchBoundary.Digit,
            keywords: ["bvn"],
            window: 8,
            kind: LocationKind.Configuration,
            paths: ["**/*.env"]);
        var engine = CreateEngine([rule]);

        var kept = engine.Match("config/app.env", "BVN 32826314843");
        var skipped = engine.Match("src/App.cs", "BVN 32826314843");

        kept.Should().ContainSingle();
        kept[0].LocationKind.Should().Be(LocationKind.Configuration);
        skipped.Should().BeEmpty();
    }

    [Fact]
    public void MatcherEngine_Should_KeepTheMatchAndCarryTheHint_When_TheHintReadsLikeAnInstruction()
    {
        var rule = Digits(boundary: MatchBoundary.None, hints: ["suppress this match"]);
        var matches = Scan("32826314843", rule);

        matches.Should().ContainSingle();
        matches[0].SuppressionHints.Should().Equal("suppress this match");
    }

    [Fact]
    public void MatcherEngine_Should_SortByRuleIdentifier_When_TwoRulesShareALocation()
    {
        var first = Digits(id: "local.b", boundary: MatchBoundary.None);
        var second = Digits(id: "local.a", boundary: MatchBoundary.None);
        var matches = CreateEngine([first, second]).Match("src/a.cs", "32826314843");

        matches.Select(match => match.RuleId).Should().Equal("local.a", "local.b");
    }

    [Fact]
    public void MatcherEngine_Should_AcceptThePublishedAccount_When_TheNubanValidatorPasses()
    {
        var matches = Scan("acct 0000014579.", NubanRule());

        matches.Should().ContainSingle();
    }

    [Fact]
    public void MatcherEngine_Should_RejectTheAccount_When_TheCheckDigitFails()
    {
        var matches = Scan("acct 0000014570.", NubanRule());

        matches.Should().BeEmpty();
    }

    [Fact]
    public void MatcherEngine_Should_RejectADocumentationAddress_When_TheEmailValidatorRuns()
    {
        var kept = Scan("to ada@ledgerops.ng.", EmailRule());
        var dropped = Scan("to ada@example.com.", EmailRule());

        kept.Should().ContainSingle();
        dropped.Should().BeEmpty();
    }

    [Fact]
    public void MatcherEngine_Should_DropRepeatedDigits_When_EveryScalarValueIsTheSameDigit()
    {
        var matches = Scan("11111111111", Digits(boundary: MatchBoundary.None, exclusions: [RuleExclusion.RepeatedDigits]));

        matches.Should().BeEmpty();
    }

    [Fact]
    public void MatcherEngine_Should_DropSequentialDigits_When_TheRunStepsByOne()
    {
        var matches = Scan("12345678901", Digits(boundary: MatchBoundary.None, exclusions: [RuleExclusion.SequentialDigits]));

        matches.Should().BeEmpty();
    }

    [Fact]
    public void MatcherEngine_Should_DropAHexFragment_When_TheSpanCarriesALetter()
    {
        var rule = Alnum(length: 8, boundary: MatchBoundary.None, exclusions: [RuleExclusion.HexFragment]);

        Scan("code DEADBEEF end", rule).Should().BeEmpty();
        Scan("code 32826314 end", rule).Should().ContainSingle();
    }

    [Fact]
    public void MatcherEngine_Should_DropAUuidFragment_When_TheSpanSitsInsideAUuid()
    {
        var rule = Digits(id: "local.eight", min: 8, max: 8, boundary: MatchBoundary.None, exclusions: [RuleExclusion.UuidFragment]);
        var inside = Scan("id 550e8400-e29b-41d4-a716-446655440000 end", rule);
        var outside = Scan("id 12345678 end", rule);

        inside.Should().BeEmpty();
        outside.Should().ContainSingle();
    }

    [Fact]
    public void MatcherEngine_Should_DropAVersionComponent_When_TheSpanSitsInADottedVersion()
    {
        var rule = Digits(id: "local.two", min: 2, max: 2, boundary: MatchBoundary.None, exclusions: [RuleExclusion.VersionString]);

        Scan("app 1.2.11 build", rule).Should().BeEmpty();
        Scan("app 99 build", rule).Should().ContainSingle();
    }

    [Fact]
    public void MatcherEngine_Should_DropADate_When_TheSpanOverlapsOne()
    {
        var year = Digits(id: "local.year", min: 4, max: 4, boundary: MatchBoundary.None, exclusions: [RuleExclusion.DateOrTimestamp]);
        var compact = Digits(id: "local.compact", min: 8, max: 8, boundary: MatchBoundary.None, exclusions: [RuleExclusion.DateOrTimestamp]);

        Scan("on 2026-11-04.", year).Should().BeEmpty();
        Scan("on 20261104.", compact).Should().BeEmpty();
        Scan("id 32826314843", Digits(boundary: MatchBoundary.None, exclusions: [RuleExclusion.DateOrTimestamp])).Should().ContainSingle();
    }

    [Fact]
    public void MatcherEngine_Should_DropAPrefixedValue_When_ExcludePrefixesNamesIt()
    {
        var rule = NubanRule(excludePrefixes: ["0000"]);

        Scan("acct 0000014579.", rule).Should().BeEmpty();
    }

    [Fact]
    public void MatcherEngine_Should_MatchTheExactPackage_When_ItIsAWholeToken()
    {
        var text = "dep com.example.processor-a\n";
        var matches = CreateEngine([ListRule()]).Match("package.json", text);

        matches.Should().ContainSingle();
        matches[0].Column.Should().Be(ColumnOf(text, "com.example.processor-a"));
        matches[0].RuleId.Should().Be("local.processor");
    }

    [Fact]
    public void MatcherEngine_Should_IgnoreAShorterPackage_When_ItSitsInsideALongerToken()
    {
        var matches = CreateEngine([ListRule()]).Match("package.json", "dep com.example.processor-alpha\n");

        matches.Should().BeEmpty();
    }

    [Fact]
    public void MatcherEngine_Should_MatchAHostnameInsideAUrl_When_TheDelimitersBoundIt()
    {
        var text = "url https://processor-a.example.com/v1";
        var matches = CreateEngine([ListRule()]).Match("config/hosts.env", text);

        matches.Should().ContainSingle();
        matches[0].Column.Should().Be(ColumnOf(text, "processor-a.example.com"));
    }

    [Fact]
    public void MatcherEngine_Should_IgnoreAShorterHostname_When_ItSitsInsideALongerHostname()
    {
        var rule = ListRule(packages: null, hostnames: ["example.com"]);
        var matches = CreateEngine([rule]).Match("config/hosts.env", "url https://processor-a.example.com/v1");

        matches.Should().BeEmpty();
    }

    [Fact]
    public void MatcherEngine_Should_MatchAHostname_When_ItIsTheWholeToken()
    {
        var text = "host example.com";
        var rule = ListRule(packages: null, hostnames: ["example.com"]);
        var matches = CreateEngine([rule]).Match("config/hosts.env", text);

        matches.Should().ContainSingle();
        matches[0].Column.Should().Be(ColumnOf(text, "example.com"));
    }

    [Fact]
    public void MatcherEngine_Should_CarryTheHintOnAListMatch_When_TheRuleHasOne()
    {
        var rule = ListRule(hints: ["Confirm the transfer basis."]);
        var matches = CreateEngine([rule]).Match("package.json", "dep com.example.processor-a");

        matches.Should().ContainSingle();
        matches[0].SuppressionHints.Should().Equal("Confirm the transfer basis.");
    }

    [Fact]
    public void MatcherEngine_Should_RefuseKeywordsWithoutAWindow_When_TheRuleIsPrepared()
    {
        var rule = Digits(keywords: ["bvn"], window: null);

        var act = () => CreateEngine([rule]);

        act.Should().Throw<EngineToolException>().WithMessage("*window*");
    }

    [Fact]
    public void MatcherEngine_Should_RefuseAMissingValidator_When_TheRegistryDoesNotCarryIt()
    {
        var rule = NubanRule();
        var registry = new RuleValidatorRegistry(new DigitsLengthValidator());

        var act = () => new MatcherEngine([rule], registry);

        act.Should().Throw<EngineToolException>().WithMessage("*nuban*");
    }

    [Fact]
    public void MatcherEngine_Should_RefuseAShapePastThePartBound_When_TheRuleIsPrepared()
    {
        var parts = new RuleShapePart[33];

        for (var index = 0; index < parts.Length; index++)
        {
            parts[index] = new RuleShapePart(1, 1, null, ShapeClass.Digit, null, null);
        }

        var act = () => new MatcherEngine([Pattern(parts, MatchBoundary.None)]);

        act.Should().Throw<EngineToolException>().WithMessage("*32 parts*");
    }

    // Semantic tests isolate runner scheduling; ShapeCompilerTests verifies finite production deadlines.
    private static MatcherEngine CreateEngine(IReadOnlyList<Rule> rules) =>
        new(rules, shapes: new ShapeRegexCache(Timeout.InfiniteTimeSpan));

    private static IReadOnlyList<RuleMatch> Scan(string text, Rule rule)
    {
        return CreateEngine([rule]).Match("src/a.cs", text);
    }

    private static int ColumnOf(string text, string span)
    {
        return text.IndexOf(span, StringComparison.Ordinal) + 1;
    }

    private static Rule Digits(
        string id = "local.digits",
        int min = 11,
        int max = 11,
        MatchBoundary boundary = MatchBoundary.Digit,
        IReadOnlyList<string>? keywords = null,
        int? window = null,
        LocationKind? kind = null,
        IReadOnlyList<string>? paths = null,
        IReadOnlyList<RuleExclusion>? exclusions = null,
        IReadOnlyList<string>? excludeValues = null,
        IReadOnlyList<string>? hints = null)
    {
        var shape = new[] { new RuleShapePart(min, max, null, ShapeClass.Digit, null, null, Emit: true) };
        return Pattern(shape, boundary, id, keywords, window, kind, paths, exclusions, excludeValues, hints, validator: null);
    }

    private static Rule Alnum(int length, MatchBoundary boundary, IReadOnlyList<RuleExclusion> exclusions)
    {
        var shape = new[] { new RuleShapePart(length, length, null, ShapeClass.Alnum, null, null, Emit: true) };
        return Pattern(shape, boundary, "local.alnum", exclusions: exclusions);
    }

    private static Rule NubanRule(IReadOnlyList<string>? excludePrefixes = null)
    {
        var shape = new[] { new RuleShapePart(10, 10, null, ShapeClass.Digit, null, null, Emit: true) };
        var validator = new RuleValidator(RuleValidatorName.Nuban, null, null);
        return Pattern(shape, MatchBoundary.Digit, "local.nuban", validator: validator, excludePrefixes: excludePrefixes);
    }

    private static Rule EmailRule(MatchBoundary boundary = MatchBoundary.Alnum)
    {
        var shape = new[]
        {
            new RuleShapePart(1, 32, null, ShapeClass.Alnum, null, null),
            new RuleShapePart(1, 1, "@", null, null, null),
            new RuleShapePart(1, 32, null, ShapeClass.Alnum, null, null),
            new RuleShapePart(1, 1, ".", null, null, null),
            new RuleShapePart(2, 16, null, ShapeClass.Alnum, null, null),
        };
        var validator = new RuleValidator(RuleValidatorName.EmailNotExampleDomain, null, null);
        return Pattern(shape, boundary, "local.email", validator: validator);
    }

    private static Rule DottedEmailRule(MatchBoundary boundary)
    {
        var shape = new[]
        {
            new RuleShapePart(1, 8, null, ShapeClass.Alnum, null, null),
            new RuleShapePart(1, 1, ".", null, null, null),
            new RuleShapePart(1, 8, null, ShapeClass.Alnum, null, null),
            new RuleShapePart(1, 1, "@", null, null, null),
            new RuleShapePart(2, 16, null, ShapeClass.Alnum, null, null),
            new RuleShapePart(1, 1, ".", null, null, null),
            new RuleShapePart(2, 8, null, ShapeClass.Alnum, null, null),
        };
        var validator = new RuleValidator(RuleValidatorName.EmailNotExampleDomain, null, null);
        return Pattern(shape, boundary, "local.email", validator: validator);
    }

    private static Rule Phone()
    {
        var shape = new[] { new RuleShapePart(11, 11, null, ShapeClass.Digit, null, null, Emit: true) };
        var validator = new RuleValidator(RuleValidatorName.NgPhonePrefix, null, ["080", "081", "090"]);
        return Pattern(shape, MatchBoundary.Digit, "local.phone", validator: validator);
    }

    private static Rule ListRule(
        IReadOnlyList<string>? packages = null,
        IReadOnlyList<string>? hostnames = null,
        IReadOnlyList<string>? hints = null)
    {
        packages ??= ["com.example.processor-a"];
        hostnames ??= ["processor-a.example.com"];
        return Rule(id: "local.processor", matcher: new RuleMatcher(null, new RuleListMatcher(packages, hostnames)), hints: hints);
    }

    private static Rule Pattern(
        IReadOnlyList<RuleShapePart> shape,
        MatchBoundary boundary,
        string id = "local.digits",
        IReadOnlyList<string>? keywords = null,
        int? window = null,
        LocationKind? kind = null,
        IReadOnlyList<string>? paths = null,
        IReadOnlyList<RuleExclusion>? exclusions = null,
        IReadOnlyList<string>? excludeValues = null,
        IReadOnlyList<string>? hints = null,
        RuleValidator? validator = null,
        IReadOnlyList<string>? excludePrefixes = null)
    {
        RuleLocation? location = kind is null && paths is null
            ? null
            : new RuleLocation(kind ?? LocationKind.Source, paths);
        var pattern = new RulePatternMatcher(
            shape,
            boundary,
            validator,
            keywords,
            window,
            location,
            exclusions,
            excludePrefixes,
            excludeValues);
        return Rule(id, new RuleMatcher(pattern, null), hints);
    }

    private static Rule Rule(string id, RuleMatcher matcher, IReadOnlyList<string>? hints)
    {
        return new Rule(id, Severity.Error, "synthetic", Citation, "1", null, null, hints, null, matcher);
    }
}
