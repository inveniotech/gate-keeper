// SPDX-License-Identifier: Apache-2.0

namespace InvenioTech.GateKeeper.Tests;

public sealed class ResultsSchemaTests
{
    [Fact]
    public void Minimal_evidence_example_is_valid()
    {
        var summary = ResultsTestSupport.ValidateEvidence(ResultsTestSupport.ReadOutputFile("examples/results/minimal.gk.json"));

        summary.IsValid.Should().BeTrue(string.Join(Environment.NewLine, summary.Errors));
    }

    [Fact]
    public void Full_evidence_example_is_valid()
    {
        var summary = ResultsTestSupport.ValidateEvidence(ResultsTestSupport.ReadOutputFile("examples/results/full.gk.json"));

        summary.IsValid.Should().BeTrue(string.Join(Environment.NewLine, summary.Errors));
    }

    [Fact]
    public void Evidence_example_with_a_matched_value_is_rejected_and_names_the_property()
    {
        var summary = ResultsTestSupport.ValidateEvidence(ResultsTestSupport.ReadOutputFile("examples/results/invalid.gk.json"));

        summary.IsValid.Should().BeFalse();
        var message = string.Join(Environment.NewLine, summary.Errors);
        message.Should().Contain("/findings/0/match");
    }

    [Fact]
    public void Minimal_sarif_example_is_valid()
    {
        var summary = ResultsTestSupport.ValidateSarif(ResultsTestSupport.ReadOutputFile("examples/results/minimal.sarif.json"));

        summary.IsValid.Should().BeTrue(string.Join(Environment.NewLine, summary.Errors));
    }

    [Fact]
    public void Full_sarif_example_is_valid()
    {
        var summary = ResultsTestSupport.ValidateSarif(ResultsTestSupport.ReadOutputFile("examples/results/full.sarif.json"));

        summary.IsValid.Should().BeTrue(string.Join(Environment.NewLine, summary.Errors));
    }

    [Fact]
    public void Sarif_example_with_a_snippet_is_rejected_and_names_the_snippet()
    {
        var summary = ResultsTestSupport.ValidateSarif(ResultsTestSupport.ReadOutputFile("examples/results/invalid.sarif.json"));

        summary.IsValid.Should().BeFalse();
        var message = string.Join(Environment.NewLine, summary.Errors);
        message.Should().Contain("snippet");
    }
}
