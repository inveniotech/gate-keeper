// SPDX-License-Identifier: Apache-2.0

using InvenioTech.GateKeeper.Middleware;

namespace InvenioTech.GateKeeper.Middleware.Tests;

public sealed class ConsentScaffoldTests
{
    private sealed class FixedClock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(2026, 11, 14, 0, 0, 0, TimeSpan.Zero);
    }

    [Fact]
    public async Task ScaffoldProvider_Should_UseLatestEvent_When_ConsentIsGrantedWithdrawnAndGrantedAgain()
    {
        var clock = new FixedClock();
        var provider = new ConsentScaffold.ConsentProvider(clock);
        var check = new ConsentCheck(provider, _ => throw new InvalidOperationException("Unexpected diagnostic."));
        (await check.CheckAsync("subject-1", "marketing.email")).Should().Be(ConsentState.Denied);
        await provider.RecordAsync("subject-1", "marketing.email", true, "settings", "v1");
        var first = provider.History();
        (await check.IsGrantedAsync("subject-1", "marketing.email")).Should().BeTrue();
        (await check.IsGrantedAsync("subject-2", "marketing.email")).Should().BeFalse();
        (await check.IsGrantedAsync("subject-1", "marketing.sms")).Should().BeFalse();
        await provider.RecordAsync("subject-1", "marketing.email", false, "settings", "v1");
        (await check.IsGrantedAsync("subject-1", "marketing.email")).Should().BeFalse();
        await provider.RecordAsync("subject-1", "marketing.email", true, "settings", "v2");
        (await check.IsGrantedAsync("subject-1", "marketing.email")).Should().BeTrue();
        first.Should().HaveCount(1);
        provider.History().Select(e => e.Kind).Should().Equal("granted", "withdrawn", "granted");
        provider.History()[2].Should().Be(new ConsentScaffold.ConsentEvent("subject-1", "marketing.email", "granted", clock.GetUtcNow(), "settings", "v2"));
    }

    [Fact]
    public async Task ScaffoldProvider_Should_LeaveHistoryUnchanged_When_AnEventIsInvalidOrCancelled()
    {
        var provider = new ConsentScaffold.ConsentProvider(new FixedClock());
        var invalid = async () => await provider.RecordAsync("subject-1", "invalid", true, "settings", "v1");
        await invalid.Should().ThrowAsync<ArgumentException>();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var cancelled = async () => await provider.RecordAsync("subject-1", "marketing.email", true, "settings", "v1", cancellation.Token);
        await cancelled.Should().ThrowAsync<OperationCanceledException>();
        provider.History().Should().BeEmpty();
    }
}
