// SPDX-License-Identifier: Apache-2.0

using InvenioTech.GateKeeper.Engine;

namespace InvenioTech.GateKeeper.Engine.Tests;

public sealed class ScanResultTests
{
    private static readonly Citation AnyCitation = new(
        Title: "Placeholder citation - approved NDPA wording arrives in P2-02",
        NdpaSection: null,
        GaidSection: null);

    [Fact]
    public void Keeps_the_findings_in_the_order_the_scan_supplied_them()
    {
        // The scan stage owns the deterministic sort (path, line, column, rule
        // identifier); the record must not resort, so the writer in P1-11 can
        // trust the order it receives. The list here is deliberately unsorted
        // to pin down the record's job.
        var result = new ScanResult(
        [
            new Finding(
                RuleId: "pii.phone.source",
                Severity: Severity.Warning,
                Category: "phone-number",
                Path: "src/second.cs",
                Line: 3,
                Column: 1,
                LocationKind: LocationKind.Source,
                Fingerprint: null,
                BaselineState: BaselineState.New,
                Citation: AnyCitation),
            new Finding(
                RuleId: "pii.bvn.error",
                Severity: Severity.Error,
                Category: "bank-verification-number",
                Path: "src/first.cs",
                Line: 40,
                Column: 12,
                LocationKind: LocationKind.Source,
                Fingerprint: null,
                BaselineState: BaselineState.New,
                Citation: AnyCitation),
        ]);

        Assert.Equal("pii.phone.source", result.Findings[0].RuleId);
        Assert.Equal("pii.bvn.error", result.Findings[1].RuleId);
    }

    [Fact]
    public void A_clean_scan_is_a_result_with_no_findings()
    {
        var result = new ScanResult([]);

        Assert.Empty(result.Findings);
    }
}
