// SPDX-License-Identifier: Apache-2.0

using InvenioTech.GateKeeper.AspNetCore;
using InvenioTech.GateKeeper.Engine;
using InvenioTech.GateKeeper.Middleware;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace InvenioTech.GateKeeper.Middleware.Tests;

public sealed class AspNetCoreRegistrationTests
{
    [Fact]
    public void AddGateKeeper_Should_RegisterOneSharedRuntime_When_TheLoadVerifies()
    {
        var services = new ServiceCollection();
        services.AddGateKeeper(TestRuntimes.Merged(), options =>
        {
            options.ProtectedJsonPointers = ["/customer/bvn"];
            options.ConsentProvider = new DeniedProvider();
        });
        using var provider = services.BuildServiceProvider();

        var matcher = provider.GetRequiredService<RuntimeMatcher>();
        provider.GetRequiredService<RuntimeMatcher>().Should().BeSameAs(matcher);
        provider.GetRequiredService<GateKeeperOptions>().ProtectedJsonPointers.Should().Equal("/customer/bvn");
        provider.GetRequiredService<ResponseFilter>().Should().NotBeNull();
        provider.GetRequiredService<ConsentCheck>().Should().NotBeNull();
        provider.GetRequiredService<GateKeeperMiddleware>().Should().NotBeNull();
        provider.GetRequiredService<GateKeeperEndpointFilter>().Should().NotBeNull();
        provider.GetRequiredService<GateKeeperResultFilter>().Should().NotBeNull();
    }

    [Fact]
    public async Task AddGateKeeper_Should_LogDisabledRulesOnce_When_TheHostStarts()
    {
        var recording = new RecordingLogger();
        var services = new ServiceCollection();
        services.AddSingleton<ILoggerProvider>(recording);
        services.AddLogging();
        services.AddGateKeeper(TestRuntimes.Merged([TestRuntimes.OptionalRule()]), options =>
        {
            options.DisabledRules = ["local.synthetic"];
            options.ConsentProvider = new DeniedProvider();
        });
        using var provider = services.BuildServiceProvider();

        await StartHostedServices(provider);

        recording.Entries.Should().ContainSingle().Which.Should().Contain("local.synthetic");
    }

    [Fact]
    public async Task AddGateKeeper_Should_WarnAboutTheAbsentProvider_When_TheHostStarts()
    {
        var recording = new RecordingLogger();
        var services = new ServiceCollection();
        services.AddSingleton<ILoggerProvider>(recording);
        services.AddLogging();
        services.AddGateKeeper(TestRuntimes.Merged());
        using var provider = services.BuildServiceProvider();

        await StartHostedServices(provider);

        recording.Entries.Should().ContainSingle().Which.Should().Contain("No consent provider");
    }

    [Fact]
    public void AddGateKeeper_Should_ThrowArgumentException_When_TheMatcherOverloadNamesDisabledRules()
    {
        var services = new ServiceCollection();
        var add = () => services.AddGateKeeper(TestRuntimes.Matcher([TestRuntimes.OptionalRule()]),
            options => options.DisabledRules = ["local.synthetic"]);

        add.Should().Throw<ArgumentException>().WithParameterName("configure");
    }

    [Fact]
    public void UseGateKeeper_Should_ThrowInvalidOperationException_When_AddGateKeeperWasNotCalled()
    {
        var app = new ApplicationBuilder(new ServiceCollection().BuildServiceProvider());

        var use = () => app.UseGateKeeper();

        use.Should().Throw<InvalidOperationException>().Which.Message.Should().Contain("AddGateKeeper");
    }

    [Fact]
    public void UseGateKeeper_Should_InstallTheMiddleware_When_TheRuntimeIsRegistered()
    {
        var services = new ServiceCollection();
        services.AddGateKeeper(TestRuntimes.Merged(), options => options.ProtectedJsonPointers = ["/value"]);
        var provider = services.BuildServiceProvider();
        var app = new ApplicationBuilder(provider);

        var chained = app.UseGateKeeper();

        chained.Should().BeSameAs(app);
    }

    [Fact]
    public void AddGateKeeper_Should_ThrowArgumentException_When_ThePointerIsInvalid()
    {
        var services = new ServiceCollection();
        var add = () => services.AddGateKeeper(TestRuntimes.Merged(), options =>
        {
            options.ProtectedJsonPointers = ["value"];
        });

        add.Should().Throw<ArgumentException>().Which.Message.Should().Contain("JSON Pointer");
    }

    private static async Task StartHostedServices(ServiceProvider provider)
    {
        foreach (var service in provider.GetRequiredService<IEnumerable<IHostedService>>())
        {
            await service.StartAsync(CancellationToken.None);
        }
    }

    private sealed class DeniedProvider : IConsentProvider
    {
        public ValueTask<ConsentState> GetStateAsync(string subjectId, string purpose, CancellationToken cancellationToken) =>
            ValueTask.FromResult(ConsentState.Denied);
    }

    private sealed class RecordingLogger : ILoggerProvider
    {
        internal List<string> Entries { get; } = [];

        public ILogger CreateLogger(string categoryName) => new Recorder(Entries);

        public void Dispose()
        {
        }

        private sealed class Recorder(List<string> entries) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
                entries.Add(formatter(state, exception));
        }
    }
}


