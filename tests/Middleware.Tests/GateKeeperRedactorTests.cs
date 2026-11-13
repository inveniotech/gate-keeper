// SPDX-License-Identifier: Apache-2.0

using InvenioTech.GateKeeper.AspNetCore;
using InvenioTech.GateKeeper.Compliance.Redaction;
using InvenioTech.GateKeeper.Middleware;
using Microsoft.Extensions.Compliance.Classification;
using Microsoft.Extensions.Compliance.Redaction;
using Microsoft.Extensions.DependencyInjection;

namespace InvenioTech.GateKeeper.Middleware.Tests;

public sealed class GateKeeperRedactorTests
{
    [Fact]
    public void Redactor_Should_MarkTheMatchedSpan_When_TheBaseStringRedactionRuns()
    {
        var redactor = new GateKeeperRedactor(TestRuntimes.Matcher());

        redactor.Redact("BVN 32826314843").Should().Be("BVN [REDACTED:pii.bvn]");
    }

    [Fact]
    public void Redactor_Should_ReportTheMaskLength_When_GetRedactedLengthRuns()
    {
        var redactor = new GateKeeperRedactor(TestRuntimes.Matcher());
        const string input = "BVN 32826314843";
        var expected = redactor.Redact(input);

        redactor.GetRedactedLength(input.AsSpan()).Should().Be(expected.Length);
    }

    [Fact]
    public void Redactor_Should_CopyTheMaskedText_When_TheSpanDestinationHasRoom()
    {
        var redactor = new GateKeeperRedactor(TestRuntimes.Matcher());
        var destination = new char[64];

        var written = redactor.Redact("BVN 32826314843".AsSpan(), destination);

        new string(destination.AsSpan(0, written)).Should().Be("BVN [REDACTED:pii.bvn]");
    }

    [Fact]
    public void Redactor_Should_LeaveUnmatchedTextUnchanged_When_NothingMatches()
    {
        var redactor = new GateKeeperRedactor(TestRuntimes.Matcher());

        redactor.Redact("ordinary synthetic text").Should().Be("ordinary synthetic text");
    }

    [Fact]
    public void Redactor_Should_PropagateTheArgumentError_When_TheInputHasInvalidUnicode()
    {
        var redactor = new GateKeeperRedactor(TestRuntimes.Matcher());

        var redact = () => redactor.Redact("\ud800");

        redact.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Redactor_Should_PropagateTheLimitError_When_TheInputExceedsTheCap()
    {
        var redactor = new GateKeeperRedactor(TestRuntimes.Matcher());

        var redact = () => redactor.Redact(new string('a', 65_537));

        redact.Should().Throw<InputLimitException>();
    }

    [Fact]
    public void Redactor_Should_PropagateTheDestinationError_When_ItIsTooSmall()
    {
        var redactor = new GateKeeperRedactor(TestRuntimes.Matcher());
        var tooSmall = new char[1];

        var redact = () => redactor.Redact("BVN 32826314843".AsSpan(), tooSmall);

        redact.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Redactor_Should_ThrowArgumentNullException_When_TheRuntimeIsMissing()
    {
        var create = () => new GateKeeperRedactor(null!);

        create.Should().Throw<ArgumentNullException>().WithParameterName("runtime");
    }

    [Fact]
    public void AddGateKeeperRedaction_Should_MapTheClassification_When_TheRuntimeIsRegistered()
    {
        var services = new ServiceCollection();
        services.AddGateKeeper(TestRuntimes.Merged());
        services.AddGateKeeperRedaction();
        using var provider = services.BuildServiceProvider();

        var redaction = provider.GetRequiredService<IRedactorProvider>();
        var redactor = redaction.GetRedactor(new DataClassificationSet(GateKeeperClassifications.Pii));

        redactor.Should().BeOfType<GateKeeperRedactor>();
        redactor.Redact("BVN 32826314843").Should().Be("BVN [REDACTED:pii.bvn]");
    }

    [Fact]
    public void AddGateKeeperRedaction_Should_ReturnTheFallback_When_AnUnmappedClassificationIsRequested()
    {
        var services = new ServiceCollection();
        services.AddGateKeeper(TestRuntimes.Merged());
        services.AddGateKeeperRedaction();
        using var provider = services.BuildServiceProvider();

        var redaction = provider.GetRequiredService<IRedactorProvider>();
        var redactor = redaction.GetRedactor(new DataClassificationSet(new DataClassification("Other.Taxonomy", "Value")));

        redactor.Should().BeOfType<GateKeeperRedactor>();
        redactor.Redact("BVN 32826314843").Should().Be("BVN [REDACTED:pii.bvn]");
    }
}
