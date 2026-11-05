// SPDX-License-Identifier: Apache-2.0

namespace InvenioTech.GateKeeper.Tests;

public sealed class RulePackSchemaTests
{
    [Fact]
    public void Minimal_example_is_valid()
    {
        var instance = RulePackTestSupport.ReadOutputFile("examples/rule-pack/minimal.json");

        var summary = RulePackTestSupport.Validate(instance);

        summary.IsValid.Should().BeTrue(string.Join(Environment.NewLine, summary.Errors));
    }

    [Fact]
    public void Full_example_is_valid()
    {
        var instance = RulePackTestSupport.ReadOutputFile("examples/rule-pack/full.json");

        var summary = RulePackTestSupport.Validate(instance);

        summary.IsValid.Should().BeTrue(string.Join(Environment.NewLine, summary.Errors));
    }

    [Fact]
    public void Invalid_example_is_rejected_with_a_message_that_names_the_failure_and_its_location()
    {
        var instance = RulePackTestSupport.ReadOutputFile("examples/rule-pack/invalid.json");

        var summary = RulePackTestSupport.Validate(instance);

        summary.IsValid.Should().BeFalse();
        summary.Errors.Should().NotBeEmpty();
        var message = string.Join(Environment.NewLine, summary.Errors);

        // The offending field is the shape part whose max is 65, so the message must point there and name the bound.
        message.Should().Contain("/rules/0/matcher/pattern/shape/0");
        message.Should().Contain("64");
    }

    [Fact]
    public void Shape_part_with_max_above_64_is_rejected()
    {
        var summary = RulePackTestSupport.Validate(PackWithShape("""[ { "min": 1, "max": 65, "class": "digit" } ]"""));

        summary.IsValid.Should().BeFalse();
    }

    [Fact]
    public void Shape_with_a_second_emit_part_is_rejected()
    {
        var summary = RulePackTestSupport.Validate(
            PackWithShape(
                """
                [
                  { "min": 11, "max": 11, "class": "digit", "emit": true },
                  { "min": 2, "max": 2, "class": "alnum", "emit": true }
                ]
                """));

        summary.IsValid.Should().BeFalse();
    }

    [Fact]
    public void Shape_with_a_repeated_oneOf_part_is_rejected()
    {
        var summary = RulePackTestSupport.Validate(PackWithShape("""[ { "min": 1, "max": 2, "oneOf": ["A", "B"] } ]"""));

        summary.IsValid.Should().BeFalse();
    }

    [Fact]
    public void Shape_with_no_emit_part_is_valid()
    {
        var summary = RulePackTestSupport.Validate(PackWithShape("""[ { "min": 11, "max": 11, "class": "digit" } ]"""));

        summary.IsValid.Should().BeTrue(string.Join(Environment.NewLine, summary.Errors));
    }

    /// <summary>Builds a minimal, otherwise-valid Invenio pack whose single rule uses the supplied shape.</summary>
    private static string PackWithShape(string shapeJson)
    {
        return $$"""
        {
          "schemaVersion": 1,
          "id": "invenio.ng.example.bound",
          "version": "2027.1.0",
          "provenance": "bundled",
          "minEngineVersion": "1.0.0",
          "coverageDate": "2027-01-01",
          "contentIssuedAt": "2027-01-01",
          "rules": [
            {
              "id": "pii.bvn",
              "severity": "error",
              "category": "bvn",
              "citation": { "title": "Placeholder citation — approved NDPA wording arrives in P2-02" },
              "sinceVersion": "2027.1.0",
              "matcher": {
                "pattern": {
                  "shape": {{shapeJson}},
                  "boundary": "digit"
                }
              }
            }
          ]
        }
        """;
    }
}
