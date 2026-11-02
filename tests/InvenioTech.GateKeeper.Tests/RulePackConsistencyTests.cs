// SPDX-License-Identifier: Apache-2.0

namespace InvenioTech.GateKeeper.Tests;

public sealed class RulePackConsistencyTests
{
    [Fact]
    public void Correction_with_coverage_date_after_issued_date_is_rejected_by_the_consistency_check()
    {
        var instance = RulePackTestSupport.ReadOutputFile("examples/rule-pack/invalid-correction.json");

        // The document is schema-valid; only the consistency check rejects it.
        var summary = RulePackTestSupport.Validate(instance);
        Assert.True(summary.IsValid, string.Join(Environment.NewLine, summary.Errors));

        var violations = RulePackTestSupport.CheckPackLineConsistency(instance);
        Assert.Contains(violations, violation => violation.Contains("coverageDate"));
    }

    [Fact]
    public void Consistent_correction_passes_the_consistency_check()
    {
        var instance = RulePackTestSupport.ReadOutputFile("examples/rule-pack/full.json");

        var violations = RulePackTestSupport.CheckPackLineConsistency(instance);

        Assert.Empty(violations);
    }

    [Fact]
    public void Correction_with_a_baseVersion_whose_year_or_month_does_not_match_is_rejected()
    {
        var violations = RulePackTestSupport.CheckPackLineConsistency(PackWithBaseVersion("2027.1.1", "2027.2.0"));

        Assert.Contains(violations, violation => violation.Contains("baseVersion"));
    }

    [Fact]
    public void Correction_with_a_baseVersion_that_does_not_end_in_patch_zero_is_rejected()
    {
        var violations = RulePackTestSupport.CheckPackLineConsistency(PackWithBaseVersion("2027.1.1", "2027.1.1"));

        Assert.Contains(violations, violation => violation.Contains("patch 0"));
    }

    private static string PackWithBaseVersion(string version, string baseVersion)
    {
        return $$"""
        {
          "schemaVersion": 1,
          "id": "invenio.ng.example.correction",
          "version": "{{version}}",
          "provenance": "bundled",
          "minEngineVersion": "1.0.0",
          "coverageDate": "2027-01-01",
          "contentIssuedAt": "2027-01-15",
          "baseVersion": "{{baseVersion}}",
          "rules": [
            {
              "id": "pii.bvn",
              "severity": "error",
              "category": "bvn",
              "citation": { "title": "Placeholder citation — approved NDPA wording arrives in P2-02" },
              "sinceVersion": "2027.1.0",
              "matcher": {
                "pattern": {
                  "shape": [ { "min": 11, "max": 11, "class": "digit", "emit": true } ],
                  "boundary": "digit"
                }
              }
            }
          ]
        }
        """;
    }
}
