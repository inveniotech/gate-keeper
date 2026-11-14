// SPDX-License-Identifier: Apache-2.0
using System.Text.Json;
using InvenioTech.GateKeeper.Engine;

namespace InvenioTech.GateKeeper.Engine.Tests;

public sealed class DomainSnapshotTests
{
    private static Rule PatternRule() => new("local.snapshot.pattern", Severity.Error, "synthetic",
        new Citation("Synthetic snapshot; no legal claim.", "synthetic", "synthetic"), "2026.11.0",
        new RedactionOptions(false), ["default", "deep"], ["Review the synthetic fixture."], null,
        new RuleMatcher(new RulePatternMatcher(
        [
            new RuleShapePart(1, 1, null, null, null, ["identity", "bvn"]),
            new RuleShapePart(1, 1, "=", null, null, null),
            new RuleShapePart(1, 1, null, ShapeClass.Alnum, null, null),
            new RuleShapePart(11, 11, null, null, [new RuleCharRange('0', '9')], null, true),
        ], MatchBoundary.Digit, new RuleValidator(RuleValidatorName.DigitsLength, 11, null),
        ["identity"], 32, new RuleLocation(LocationKind.Log, ["src/**"]),
        [RuleExclusion.RepeatedDigits, RuleExclusion.SequentialDigits], ["000"], ["12345678901"]), null));

    [Fact]
    public void RuleSnapshot_Should_PreserveMatchingAndOmission_When_RoundTrippedWithoutReflection()
    {
        var original = PatternRule();
        var bytes = JsonSerializer.SerializeToUtf8Bytes(original, EngineJsonSerializerContext.Default.Rule);
        using var document = JsonDocument.Parse(bytes);
        var node = document.RootElement;
        node.GetProperty("id").GetString().Should().Be("local.snapshot.pattern");
        node.GetProperty("severity").GetString().Should().Be("error");
        node.TryGetProperty("processor", out _).Should().BeFalse();
        node.GetProperty("matcher").TryGetProperty("list", out _).Should().BeFalse();
        var pattern = node.GetProperty("matcher").GetProperty("pattern");
        pattern.GetProperty("shape")[3].GetProperty("emit").GetBoolean().Should().BeTrue();
        pattern.GetProperty("validator").GetProperty("name").GetString().Should().Be("digits-length");
        pattern.GetProperty("location").GetProperty("kind").GetString().Should().Be("log");

        var restored = JsonSerializer.Deserialize(bytes, EngineJsonSerializerContext.Default.Rule)!;
        restored.Should().BeEquivalentTo(original, options => options.WithStrictOrdering());
        var matcher = new MatcherEngine([restored], shapes: new ShapeRegexCache(Timeout.InfiniteTimeSpan));
        var match = matcher.Match("src/synthetic.txt", "identity=B32826314843").Should().ContainSingle().Subject;
        match.RuleId.Should().Be(original.Id);
        match.SpanStart.Should().Be(10);
        match.SpanLength.Should().Be(11);
        match.LocationKind.Should().Be(LocationKind.Log);
        matcher.Match("docs/synthetic.txt", "identity=B32826314843").Should().BeEmpty();
        matcher.Match("src/synthetic.txt", "identity=B12345678901").Should().BeEmpty();
    }

    [Fact]
    public void PackSnapshot_Should_PreserveRuleMetadataAndProvenance_When_OptionalAndListRulesAreMixed()
    {
        var list = new Rule("local.snapshot.processor", Severity.Note, "synthetic",
            new Citation("Synthetic processor metadata.", null, null), "2026.11.0", new RedactionOptions(true),
            null, null, new ProcessorInfo(["synthetic transactions", "synthetic contact"], "Confirm deployment region."),
            new RuleMatcher(null, new RuleListMatcher(["synthetic-sdk"], ["api.example.invalid"])));
        var minimal = PatternRule() with
        {
            Id = "local.snapshot.minimal",
            Redaction = null,
            Profiles = null,
            SuppressionHints = null,
            Matcher = new RuleMatcher(new RulePatternMatcher(
                [new RuleShapePart(1, 1, "marker", null, null, null)], MatchBoundary.None, null, null, null, null, null, null, null), null),
        };
        var pack = new RulePack(1, "local.snapshot", "2026.11.1", PackProvenance.Bundled,
            "0.1.0", [PatternRule(), list, minimal], "2026-11-01", "2026-11-02", "2026.11.0");
        var bytes = JsonSerializer.SerializeToUtf8Bytes(pack, EngineJsonSerializerContext.Default.RulePack);
        var restored = JsonSerializer.Deserialize(bytes, EngineJsonSerializerContext.Default.RulePack)!;
        restored.Should().BeEquivalentTo(pack, options => options.WithStrictOrdering());
        JsonSerializer.SerializeToUtf8Bytes(restored, EngineJsonSerializerContext.Default.RulePack).Should().Equal(bytes);
        var matcher = new MatcherEngine([restored.Rules[1]]);
        matcher.Match("src/integration.ts", "synthetic-sdk api.example.invalid").Should().HaveCount(2)
            .And.OnlyContain(match => match.Severity == Severity.Note);
        matcher.Match("src/integration.ts", "longer-synthetic-sdk api.example.invalid.suffix").Should().BeEmpty();

        var local = pack with { Provenance = PackProvenance.Local, CoverageDate = null, ContentIssuedAt = null, BaseVersion = null };
        using var localDocument = JsonDocument.Parse(JsonSerializer.Serialize(local, EngineJsonSerializerContext.Default.RulePack));
        localDocument.RootElement.GetProperty("provenance").GetString().Should().Be("local");
        localDocument.RootElement.TryGetProperty("coverageDate", out _).Should().BeFalse();
        localDocument.RootElement.TryGetProperty("baseVersion", out _).Should().BeFalse();
    }

    [Fact]
    public void MergedSnapshot_Should_RetainSelectedPackIdentity_When_RoundTripped()
    {
        var pack = new LoadedPack("synthetic.pack", "2026.11.1", PackProvenance.PlanGated, new string('a', 64))
        {
            CoverageDate = "2026-11-01",
            ContentIssuedAt = "2026-11-02",
        };
        var merged = new MergedRules([pack], [new LoadedRule(PatternRule(), pack)]);
        var bytes = JsonSerializer.SerializeToUtf8Bytes(merged, EngineJsonSerializerContext.Default.MergedRules);
        var restored = JsonSerializer.Deserialize(bytes, EngineJsonSerializerContext.Default.MergedRules)!;
        restored.Should().BeEquivalentTo(merged, options => options.WithStrictOrdering());
        restored.Rules[0].Pack.ContentDigest.Should().Be(new string('a', 64));
        restored.Rules[0].Pack.Provenance.Should().Be(PackProvenance.PlanGated);
        restored.Rules[0].Pack.Version.Should().Be("2026.11.1");
        JsonSerializer.SerializeToUtf8Bytes(restored, EngineJsonSerializerContext.Default.MergedRules).Should().Equal(bytes);
    }

    [Fact]
    public void ValueSnapshots_Should_RestoreValidatedCoordinatesAndFingerprint_When_UsingGeneratedMetadata()
    {
        var location = new Location("src/synthetic.cs", 12, 7);
        var locationBytes = JsonSerializer.SerializeToUtf8Bytes(location, EngineJsonSerializerContext.Default.Location);
        JsonSerializer.Deserialize(locationBytes, EngineJsonSerializerContext.Default.Location).Should().Be(location);
        var fingerprint = new Fingerprint(new string('b', 64));
        var fingerprintBytes = JsonSerializer.SerializeToUtf8Bytes(fingerprint, EngineJsonSerializerContext.Default.Fingerprint);
        JsonSerializer.Deserialize(fingerprintBytes, EngineJsonSerializerContext.Default.Fingerprint).Should().Be(fingerprint);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("null")]
    [InlineData("{\"path\":\"src/synthetic.cs\",\"line\":\"one\",\"column\":1}")]
    [InlineData("{\"path\":\"src/synthetic.cs\",\"line\":0,\"column\":1}")]
    [InlineData("{\"path\":\"\",\"line\":1,\"column\":1}")]
    public void LocationSnapshot_Should_RejectMissingOrInvalidCoordinates_When_Reading(string json)
    {
        Action read = () => JsonSerializer.Deserialize(json, EngineJsonSerializerContext.Default.Location);
        read.Should().ThrowExactly<JsonException>();
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("null")]
    [InlineData("{\"value\":null}")]
    [InlineData("{\"value\":\"not-a-fingerprint\"}")]
    public void FingerprintSnapshot_Should_RejectInvalidValues_When_Reading(string json)
    {
        Action read = () => JsonSerializer.Deserialize(json, EngineJsonSerializerContext.Default.Fingerprint);
        read.Should().ThrowExactly<JsonException>();
    }
}
