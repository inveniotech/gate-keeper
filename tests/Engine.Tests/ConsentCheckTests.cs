// SPDX-License-Identifier: Apache-2.0
using InvenioTech.GateKeeper.Middleware;

namespace InvenioTech.GateKeeper.Engine.Tests;

public sealed class ConsentCheckTests
{
    [Fact]
    public async Task Consent_Should_ReturnUnknownWithSafeDiagnostics_When_TheProviderTimesOut()
    {
        var events = new List<ConsentDiagnostic>();
        var check = new ConsentCheck(new Provider(() => throw new TaskCanceledException("synthetic-sensitive-marker")), events.Add);
        (await check.CheckAsync("synthetic-subject", "a.b")).Should().Be(ConsentState.Unknown);
        (await check.IsGrantedAsync("synthetic-subject", "a.b")).Should().BeFalse();
        events.Should().Equal(new ConsentDiagnostic("a.b", "TaskCanceledException"), new ConsentDiagnostic("a.b", "TaskCanceledException"));
    }

    [Fact]
    public async Task Consent_Should_PropagateCancellationWithoutDiagnostics_When_TheCallerCancelsInsideTheProvider()
    {
        using var cancellation = new CancellationTokenSource();
        var events = new List<ConsentDiagnostic>();
        var check = new ConsentCheck(new Provider(() => { cancellation.Cancel(); throw new TaskCanceledException(); }), events.Add);
        Func<Task> run = async () => await check.CheckAsync("synthetic-subject", "a.b", cancellation.Token);
        await run.Should().ThrowAsync<OperationCanceledException>();
        events.Should().BeEmpty();
    }

    [Fact]
    public async Task Consent_Should_NameTheSubjectArgument_When_ItContainsInvalidUnicode()
    {
        var check = new ConsentCheck(null, _ => { });
        Func<Task> run = async () => await check.CheckAsync("\ud800", "a.b");
        (await run.Should().ThrowExactlyAsync<ArgumentException>()).Which.ParamName.Should().Be("subjectId");
    }

    [Theory]
    [InlineData(ConsentState.Granted, ConsentState.Granted, true)]
    [InlineData(ConsentState.Denied, ConsentState.Denied, false)]
    [InlineData(ConsentState.Unknown, ConsentState.Unknown, false)]
    [InlineData((ConsentState)999, ConsentState.Unknown, false)]
    public async Task Consent_Should_MapOnlyGrantedToTrue_When_TheProviderReturnsAState(ConsentState supplied, ConsentState expected, bool granted)
    {
        var calls = 0;
        var provider = new Provider(() => { calls++; return supplied; });
        var events = new List<ConsentDiagnostic>();
        var check = new ConsentCheck(provider, events.Add);
        (await check.CheckAsync("synthetic-subject", "acme.marketing-sms")).Should().Be(expected);
        (await check.IsGrantedAsync("synthetic-subject", "acme.marketing-sms")).Should().Be(granted);
        calls.Should().Be(2, "the adapter must not cache consent answers");
        events.Should().BeEmpty();
    }

    [Fact]
    public async Task Consent_Should_ReturnUnknownAndEmitOneStartUpWarning_When_NoProviderExists()
    {
        var events = new List<ConsentDiagnostic>();
        var check = new ConsentCheck(null, events.Add);
        (await check.CheckAsync("synthetic-subject", "acme.marketing-sms")).Should().Be(ConsentState.Unknown);
        (await check.IsGrantedAsync("synthetic-subject", "acme.marketing-sms")).Should().BeFalse();
        events.Should().Equal(new ConsentDiagnostic(null, "NoConsentProvider"));
    }

    [Fact]
    public async Task Consent_Should_LogOnlyPurposeAndType_When_BothEntryPointsEncounterProviderFailures()
    {
        var events = new List<ConsentDiagnostic>();
        var check = new ConsentCheck(new Provider(() => throw new InvalidOperationException("synthetic-sensitive-marker")), events.Add);
        (await check.CheckAsync("synthetic-subject", "acme.marketing-sms")).Should().Be(ConsentState.Unknown);
        (await check.IsGrantedAsync("synthetic-subject", "acme.marketing-sms")).Should().BeFalse();
        events.Should().Equal(new ConsentDiagnostic("acme.marketing-sms", "InvalidOperationException"),
            new ConsentDiagnostic("acme.marketing-sms", "InvalidOperationException"));
    }

    [Fact]
    public async Task Consent_Should_PropagateCancellationWithoutProviderCallsOrDiagnostics_When_TheCallerCancels()
    {
        var calls = 0;
        var events = new List<ConsentDiagnostic>();
        var check = new ConsentCheck(new Provider(() => { calls++; return ConsentState.Granted; }), events.Add);
        using var source = new CancellationTokenSource(); source.Cancel();
        Func<Task> run = async () => await check.IsGrantedAsync("synthetic-subject", "acme.marketing-sms", source.Token);
        await run.Should().ThrowExactlyAsync<OperationCanceledException>();
        calls.Should().Be(0); events.Should().BeEmpty();
    }

    [Theory]
    [InlineData("")]
    [InlineData("single")]
    [InlineData("Acme.marketing")]
    [InlineData("acme.marketing\n")]
    [InlineData("acme..marketing")]
    public async Task Consent_Should_RejectInvalidPurposesWithoutCallingTheProvider_When_ArgumentsAreInvalid(string purpose)
    {
        var calls = 0;
        var check = new ConsentCheck(new Provider(() => { calls++; return ConsentState.Granted; }), _ => { });
        Func<Task> run = async () => await check.CheckAsync("synthetic-subject", purpose);
        await run.Should().ThrowExactlyAsync<ArgumentException>(); calls.Should().Be(0);
    }

    [Fact]
    public async Task Consent_Should_UseOneUnambiguousPurposeAndSubjectBoundary_When_ArgumentsReachTheLimit()
    {
        var check = new ConsentCheck(new Provider(() => ConsentState.Denied), _ => { });
        var subject = string.Concat(Enumerable.Repeat("😀", 1024));
        var purpose = string.Join('.', Enumerable.Repeat(new string('a', 32), 5));
        purpose.Length.Should().Be(164);
        (await check.CheckAsync(subject, purpose)).Should().Be(ConsentState.Denied);
        Func<Task> longSubject = async () => await check.CheckAsync(subject + "x", purpose);
        await longSubject.Should().ThrowExactlyAsync<ArgumentException>();
        Func<Task> longPurpose = async () => await check.CheckAsync(subject, purpose + "x");
        await longPurpose.Should().ThrowExactlyAsync<ArgumentException>();
    }

    private sealed class Provider(Func<ConsentState> answer) : IConsentProvider
    {
        public ValueTask<ConsentState> GetStateAsync(string subjectId, string purpose, CancellationToken cancellationToken) => ValueTask.FromResult(answer());
    }
}
