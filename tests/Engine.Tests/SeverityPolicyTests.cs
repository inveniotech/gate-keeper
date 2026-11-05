// SPDX-License-Identifier: Apache-2.0

using System.Text.Json;
using InvenioTech.GateKeeper.Engine;

namespace InvenioTech.GateKeeper.Engine.Tests;

public sealed class SeverityPolicyTests
{
    public static IEnumerable<object[]> Cases()
    {
        foreach (var severity in Enum.GetValues<Severity>())
            foreach (var threshold in Enum.GetValues<Severity>())
                foreach (var baselined in new[] { false, true })
                    foreach (var suppressed in new[] { false, true })
                        foreach (var history in new[] { false, true })
                            foreach (var toolError in new[] { false, true })
                                yield return [severity, threshold, baselined, suppressed, history, toolError];
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public async Task SeverityPolicy_Should_ReturnTheSpecifiedCode_When_TheDispositionCombinationIsEvaluated(
        Severity severity, Severity threshold, bool baselined, bool suppressed, bool history, bool toolError)
    {
        var result = Result(severity, baselined, suppressed, history);
        var decision = await SeverityPolicy.RunAsync(_ => toolError
            ? throw new EngineToolException("The synthetic pack is invalid. Correct the pack and run the scan again.")
            : Task.FromResult(result), threshold);
        var meetsThreshold = threshold switch
        {
            Severity.Error => severity == Severity.Error,
            Severity.Warning => severity is Severity.Error or Severity.Warning,
            Severity.Note => true,
            _ => throw new InvalidOperationException(),
        };
        var expected = toolError ? ExitOutcome.ToolError
            : !suppressed && (history || !baselined) && meetsThreshold ? ExitOutcome.Violations : ExitOutcome.Pass;
        decision.Outcome.Should().Be(expected);
        if (toolError)
        {
            decision.Result.Should().BeNull();
            decision.Error.Should().Contain("Correct the pack");
        }
        else
        {
            decision.Result.Should().BeSameAs(result);
            decision.Error.Should().BeNull();
            decision.Outcome.Should().NotBe(ExitOutcome.ToolError);
        }
    }

    [Fact]
    public async Task SeverityPolicy_Should_ReportAToolError_When_TheRepositoryCannotBeRead()
    {
        var decision = await SeverityPolicy.RunAsync(_ => throw new IOException("Synthetic read failure."), Severity.Error);
        decision.Outcome.Should().Be(ExitOutcome.ToolError);
        decision.Error.Should().Contain("repository").And.Contain("Check");
    }

    [Fact]
    public async Task SeverityPolicy_Should_PropagateCancellation_When_TheCallerCancels()
    {
        using var source = new CancellationTokenSource();
        source.Cancel();
        Func<Task> run = async () => await SeverityPolicy.RunAsync(_ => Task.FromResult(Result(Severity.Error, false, false, false)), Severity.Error, source.Token);
        await run.Should().ThrowAsync<OperationCanceledException>();
    }

    [Theory]
    [InlineData(Severity.Error, 1)]
    [InlineData(Severity.Warning, 0)]
    [InlineData(Severity.Note, 0)]
    public void SeverityPolicy_Should_AgreeWithBothOutputs_When_AnErrorThresholdIsUsed(Severity severity, int code)
    {
        using var repo = new PolicyRepository();
        var settings = ConfigurationLoader.Load(repo.Root).Settings!;
        var policy = Result(severity, false, false, false);
        var outcome = SeverityPolicy.Evaluate(policy, settings.FailOn);
        var documents = ResultsWriter.Write(new ResultsInput(policy, settings, [], "", "", false, outcome));
        using var evidence = JsonDocument.Parse(documents.Evidence);
        using var sarif = JsonDocument.Parse(documents.Sarif);
        evidence.RootElement.GetProperty("exitCode").GetInt32().Should().Be(code);
        sarif.RootElement.GetProperty("runs")[0].GetProperty("invocations")[0].GetProperty("exitCode").GetInt32().Should().Be(code);
        sarif.RootElement.GetProperty("runs")[0].GetProperty("invocations")[0].GetProperty("executionSuccessful").GetBoolean().Should().BeTrue();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void SeverityPolicy_Should_RejectUnknownConfiguredIdentifiers_When_AnOverrideOrDisableHasNoLoadedRule(bool overrideSeverity)
    {
        using var repo = new PolicyRepository();
        var settings = ConfigurationLoader.Load(repo.Root).Settings!;
        settings = overrideSeverity ? settings with { SeverityOverrides = new Dictionary<string, Severity> { ["local.unknown"] = Severity.Note } }
            : settings with { DisabledRules = [new("local.unknown", "Synthetic test reason.")] };
        Action validate = () => SeverityPolicy.ValidateSettings(settings, [PolicyRepository.Rule]);
        validate.Should().Throw<EngineToolException>().WithMessage("*local.unknown*");
    }

    [Fact]
    public async Task SeverityPolicy_Should_RejectAnInvalidThreshold_When_TheValueIsOutsideTheContract()
    {
        var decision = await SeverityPolicy.RunAsync(_ => Task.FromResult(Result(Severity.Error, false, false, false)), (Severity)99);
        decision.Outcome.Should().Be(ExitOutcome.ToolError);
        decision.Error.Should().Contain("threshold");
    }

    [Theory]
    [InlineData(false, false, 0)]
    [InlineData(true, false, 1)]
    [InlineData(true, true, 0)]
    public void SeverityPolicy_Should_UseTheEffectiveSeverity_When_OverridesExceptionsAndDisablesApply(bool exceptionOverride, bool disabled, int expected)
    {
        using var repo = new PolicyRepository();
        var settings = ConfigurationLoader.Load(repo.Root).Settings! with
        {
            SeverityOverrides = new Dictionary<string, Severity> { ["local.test"] = Severity.Note },
            DisabledRules = disabled ? [new("local.test", "Synthetic test reason.")] : [],
        };
        ExceptionEntry[] exceptions = exceptionOverride
            ? [new("exc-test", null, "synthetic", [PathGlob.Compile("src/**")], new(2026, 11, 1), Severity.Error, new string('a', 64))] : [];
        var policy = FindingPolicy.Apply(PolicyRepository.Scan(), settings, new(exceptions, new Dictionary<string, DateOnly>(), null, []), new(2026, 11, 1));
        ((int)SeverityPolicy.Evaluate(policy, settings.FailOn)).Should().Be(expected);
        if (disabled) policy.Exceptions.Should().BeEmpty();
    }

    private static PolicyResult Result(Severity severity, bool baselined, bool suppressed, bool history)
    {
        var citation = new Citation("Synthetic test citation.", null, null);
        var finding = new Finding("local.test", severity, "synthetic", "sample.cs", 1, 1, LocationKind.Source, null,
            baselined ? BaselineState.Unchanged : BaselineState.New, citation);
        var historical = new HistoryMatch(new string('a', 40), new("local.test", severity, "synthetic", "old.cs", 1, 1, LocationKind.Source, citation, [], 0, 1));
        return new PolicyResult(new(2026, 11, 1), history || suppressed ? [] : [finding], history && !suppressed ? [historical] : [], [], new(0, 0), []);
    }
}
