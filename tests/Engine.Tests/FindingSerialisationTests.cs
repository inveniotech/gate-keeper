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
    public void A_finding_serialises_with_the_camel_case_names_and_the_spec_strings()
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

        Assert.Equal(expected.ReplaceLineEndings(), json.ReplaceLineEndings());
    }

    [Fact]
    public void Serialising_the_same_finding_twice_gives_the_same_bytes()
    {
        var first = JsonSerializer.Serialize(Finding, EngineJsonSerializerContext.Default.Finding);
        var second = JsonSerializer.Serialize(Finding, EngineJsonSerializerContext.Default.Finding);

        Assert.Equal(first, second);
    }

    [Fact]
    public void A_finding_without_a_fingerprint_omits_the_fingerprint_key()
    {
        var withoutFingerprint = Finding with { Fingerprint = null };
        var json = JsonSerializer.Serialize(
            withoutFingerprint,
            EngineJsonSerializerContext.Default.Finding);

        Assert.DoesNotContain("\"fingerprint\"", json, StringComparison.Ordinal);
        Assert.Contains("\"ruleId\": \"pii.bvn.log\"", json, StringComparison.Ordinal);
    }

    [Fact]
    public void A_finding_serialisation_carries_the_citation_sections_when_they_exist()
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

        Assert.Contains("\"ndpa\": \"placeholder\"", json, StringComparison.Ordinal);
        Assert.Contains("\"gaid\": \"placeholder\"", json, StringComparison.Ordinal);
    }
}
