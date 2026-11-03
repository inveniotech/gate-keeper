// SPDX-License-Identifier: Apache-2.0

using System.Text.Json;
using InvenioTech.GateKeeper.Engine;

namespace InvenioTech.GateKeeper.Engine.Tests;

public sealed class FindingSerialisationTests
{
    private static readonly Finding Finding = new(
        RuleId: "pii.bvn.log",
        Severity: Severity.Error,
        Category: "bank-verification-number",
        Path: "src/example/Processor.cs",
        Line: 14,
        Column: 9,
        LocationKind: LocationKind.Source,
        Fingerprint: new Fingerprint(new string('a', 64)).Value,
        BaselineState: BaselineState.New,
        Citation: new Citation(
            Title: "Placeholder citation - approved NDPA wording arrives in P2-02",
            NdpaSection: null,
            GaidSection: null));

    [Fact]
    public void Finding_Should_SerialiseWithCamelCaseNamesAndTheSpecStrings_When_WrittenThroughTheEngineContext()
    {
        var json = JsonSerializer.Serialize(
            Finding,
            EngineJsonSerializerContext.Default.Finding);

        const string expected = """
            {
              "ruleId": "pii.bvn.log",
              "severity": "error",
              "category": "bank-verification-number",
              "path": "src/example/Processor.cs",
              "line": 14,
              "column": 9,
              "locationKind": "source",
              "fingerprint": "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
              "baselineState": "new",
              "citation": {
                "title": "Placeholder citation - approved NDPA wording arrives in P2-02"
              }
            }
            """;

        json.Should().Be(expected.ReplaceLineEndings());
    }

    [Fact]
    public void Finding_Should_ProduceTheSameBytes_When_SerialisedTwice()
    {
        var first = JsonSerializer.Serialize(Finding, EngineJsonSerializerContext.Default.Finding);
        var second = JsonSerializer.Serialize(Finding, EngineJsonSerializerContext.Default.Finding);

        first.Should().Be(second);
    }

    [Fact]
    public void Finding_Should_OmitTheFingerprintKey_When_NoFingerprintWasComputed()
    {
        var withoutFingerprint = Finding with { Fingerprint = null };
        var json = JsonSerializer.Serialize(
            withoutFingerprint,
            EngineJsonSerializerContext.Default.Finding);

        json.Should().NotContain("\"fingerprint\"");
        json.Should().Contain("\"ruleId\": \"pii.bvn.log\"");
    }

    [Fact]
    public void Finding_Should_CarryTheCitationSections_When_ThePackRecordsThem()
    {
        var withSections = Finding with
        {
            Citation = Finding.Citation with
            {
                NdpaSection = "placeholder",
                GaidSection = "placeholder",
            },
        };
        var json = JsonSerializer.Serialize(
            withSections,
            EngineJsonSerializerContext.Default.Finding);

        json.Should().Contain("\"ndpa\": \"placeholder\"");
        json.Should().Contain("\"gaid\": \"placeholder\"");
    }
}
