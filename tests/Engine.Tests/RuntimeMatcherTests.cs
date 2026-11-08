// SPDX-License-Identifier: Apache-2.0
using InvenioTech.GateKeeper.Engine;
using InvenioTech.GateKeeper.Middleware;

namespace InvenioTech.GateKeeper.Engine.Tests;

public sealed class RuntimeMatcherTests
{
    [Fact]
    public void Runtime_Should_UseEmitScalarSpansAndIgnorePathScopes_When_RedactingAndClassifying()
    {
        var runtime = Create([Rule("local.synthetic", "marker", Severity.Warning)]);
        runtime.Redact("😀 label:marker!").Should().Be("😀 label:[REDACTED:local.synthetic]!");
        var finding = runtime.Classify("😀 label:marker!", new RuntimeContext("response", "phone"))!;
        finding.RuleId.Should().Be("local.synthetic");
        finding.Start.Should().Be(8);
        finding.Length.Should().Be(6);
        finding.Severity.Should().Be(Severity.Warning);
        runtime.Classify("😀 label:marker!", new RuntimeContext("log")).Should().Be(finding);
    }

    [Fact]
    public void Runtime_Should_ReserveLiteralMasksAndBeIdempotent_When_AnUnknownRuleMaskIsPresent()
    {
        var runtime = Create([Rule("local.synthetic", "marker", Severity.Note)]);
        const string input = "[REDACTED:local.previous] label:marker";
        var output = runtime.Redact(input);
        output.Should().Be("[REDACTED:local.previous] label:[REDACTED:local.synthetic]");
        runtime.Redact(output).Should().Be(output);
        runtime.Classify(output, new RuntimeContext("value")).Should().BeNull();
    }

    [Fact]
    public void Runtime_Should_ChooseEarliestThenLongestThenOrdinalRule_When_RulesOverlap()
    {
        var runtime = Create([Rule("local.z", "mark", Severity.Error), Rule("local.b", "marker", Severity.Note), Rule("local.a", "marker", Severity.Warning)]);
        runtime.Redact("label:marker").Should().Be("label:[REDACTED:local.a]");
        runtime.Classify("label:marker", new RuntimeContext("value"))!.RuleId.Should().Be("local.a");
    }

    [Fact]
    public void Runtime_Should_CountScalarsInsteadOfBytesOrCodeUnits_When_EnforcingTheCap()
    {
        var runtime = Create([Rule("local.synthetic", "marker", Severity.Error)]);
        var limit = string.Concat(Enumerable.Repeat("😀", 65_536));
        runtime.Redact(limit).Should().Be(limit);
        Action tooLarge = () => runtime.Redact(limit + "x");
        tooLarge.Should().ThrowExactly<InputLimitException>();
        Action invalid = () => runtime.Redact("\ud800");
        invalid.Should().ThrowExactly<ArgumentException>();
    }

    [Fact]
    public void Runtime_Should_ValidateContextBeforeEmptyInput_When_Classifying()
    {
        var runtime = Create([Rule("local.synthetic", "marker", Severity.Error)]);
        runtime.Classify("", new RuntimeContext("value")).Should().BeNull();
        Action badSurface = () => runtime.Classify("", new RuntimeContext("migration"));
        badSurface.Should().ThrowExactly<ArgumentException>();
        Action badField = () => runtime.Classify("", new RuntimeContext("value", ""));
        badField.Should().ThrowExactly<ArgumentException>();
    }

    [Fact]
    public void Runtime_Should_FailBeforeReturningAnOversizedResult_When_MasksExpandTheInput()
    {
        var runtime = Create([Rule("local.synthetic", "marker", Severity.Note)]);
        Action expand = () => runtime.Redact(string.Concat(Enumerable.Repeat("label:marker ", 3000)));
        expand.Should().ThrowExactly<InputLimitException>();
    }

    [Fact]
    public void Runtime_Should_AllowOnlyOptionalDisablement_When_Constructed()
    {
        var optional = Rule("local.optional", "marker", Severity.Note) with { Redaction = new RedactionOptions(true) };
        var diagnostics = new List<string>();
        var runtime = Create([optional], [optional.Id], diagnostics.Add);
        runtime.Redact("label:marker").Should().Be("label:marker");
        diagnostics.Should().ContainSingle().Which.Should().Contain(optional.Id);
        Action mandatory = () => Create([Rule("local.required", "marker", Severity.Note)], ["local.required"], diagnostics.Add);
        mandatory.Should().ThrowExactly<ArgumentException>();
        Action unknown = () => Create([optional], ["local.unknown"], diagnostics.Add);
        unknown.Should().ThrowExactly<ArgumentException>();
    }

    private static RuntimeMatcher Create(IReadOnlyList<Rule> rules, IReadOnlyList<string>? disabled = null, Action<string>? diagnostic = null)
    {
        var pack = new LoadedPack("synthetic.pack", "2026.11.0", PackProvenance.Bundled, new string('a', 64));
        return new RuntimeMatcher(new MergedRules([pack], rules.Select(rule => new LoadedRule(rule, pack)).ToArray()),
            disabled, diagnostic, new ShapeRegexCache(Timeout.InfiniteTimeSpan));
    }

    private static Rule Rule(string id, string literal, Severity severity) => new(id, severity, "synthetic",
        new Citation("Synthetic runtime conformance.", null, null), "2026.11.0", null, null, null, null,
        new RuleMatcher(new RulePatternMatcher(
        [new RuleShapePart(1, 1, "label:", null, null, null), new RuleShapePart(1, 1, literal, null, null, null, true)],
        MatchBoundary.None, null, null, null, new RuleLocation(LocationKind.Log, ["not/a/runtime/path"]), null, null, null), null));
}
