// SPDX-License-Identifier: Apache-2.0

using InvenioTech.GateKeeper.Engine;
using InvenioTech.GateKeeper.Middleware;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace InvenioTech.GateKeeper.AspNetCore;

/// <summary>
/// The dependency-injection registration for the .NET adapter. One verified
/// <see cref="RuntimeMatcher"/> is registered, so classification, redaction, the
/// response hooks, the compliance redactor and the Serilog integrations all
/// share one immutable load.
/// </summary>
public static class GateKeeperServiceCollectionExtensions
{
    /// <summary>
    /// Registers the runtime over a verified pack load, then the response hooks,
    /// the consent check and the start-up diagnostics.
    /// </summary>
    /// <param name="services">The application service collection.</param>
    /// <param name="verified">A successful signed-only load from <see cref="RuntimePacks"/>.</param>
    /// <param name="configure">Optional rules, protected paths and the consent provider.</param>
    /// <returns>The same collection, so registrations chain.</returns>
    /// <exception cref="ArgumentException">The load is empty, local, or disables an invalid rule.</exception>
    public static IServiceCollection AddGateKeeper(
        this IServiceCollection services,
        MergedRules verified,
        Action<GateKeeperOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(verified);
        var options = BuildOptions(configure);
        var diagnostics = new GateKeeperDiagnosticLog();
        var matcher = new RuntimeMatcher(verified, options.DisabledRules, diagnostics.AddInformation);
        return AddGateKeeper(services, matcher, options, diagnostics);
    }

    /// <summary>
    /// Registers the runtime from a matcher the application already built, for
    /// hosts that need the same matcher outside dependency injection, such as
    /// the Serilog integration.
    /// </summary>
    /// <param name="services">The application service collection.</param>
    /// <param name="matcher">The verified runtime the application owns.</param>
    /// <param name="configure">Optional protected paths and the consent provider.</param>
    /// <returns>The same collection, so registrations chain.</returns>
    /// <exception cref="ArgumentException">The options name disabled rules, which the supplied matcher already applied.</exception>
    public static IServiceCollection AddGateKeeper(
        this IServiceCollection services,
        RuntimeMatcher matcher,
        Action<GateKeeperOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(matcher);
        var options = BuildOptions(configure);
        if (options.DisabledRules.Count > 0)
        {
            throw new ArgumentException(
                "Disabled rules are applied when the runtime is built. Pass them to the RuntimeMatcher constructor " +
                "instead of the options for this overload.", nameof(configure));
        }

        return AddGateKeeper(services, matcher, options, new GateKeeperDiagnosticLog());
    }

    private static IServiceCollection AddGateKeeper(
        IServiceCollection services,
        RuntimeMatcher matcher,
        GateKeeperOptions options,
        GateKeeperDiagnosticLog diagnostics)
    {
        var filter = new ResponseFilter(matcher, options.ProtectedJsonPointers);
        var consent = new ConsentCheck(options.ConsentProvider, diagnostic =>
        {
            if (diagnostic.Purpose is null)
            {
                diagnostics.AddWarning(
                    "No consent provider is registered, so consent checks return unknown. Register an " +
                    "IConsentProvider that answers from the application's own records.");
            }
            else
            {
                diagnostics.AddWarning(
                    $"The consent provider could not answer for purpose '{diagnostic.Purpose}' and the check " +
                    $"returned unknown. The provider reported {diagnostic.ErrorType}.");
            }
        });
        services.AddSingleton(matcher);
        services.AddSingleton(options);
        services.AddSingleton(filter);
        services.AddSingleton(consent);
        services.AddSingleton(diagnostics);
        services.AddSingleton<GateKeeperMiddleware>();
        services.AddTransient<GateKeeperEndpointFilter>();
        services.AddTransient<GateKeeperResultFilter>();
        services.AddSingleton<IConfigureOptions<MvcOptions>, GateKeeperMvcOptionsSetup>();
        services.AddHostedService<GateKeeperStartupService>();
        return services;
    }

    private static GateKeeperOptions BuildOptions(Action<GateKeeperOptions>? configure)
    {
        var options = new GateKeeperOptions();
        configure?.Invoke(options);
        return new GateKeeperOptions
        {
            DisabledRules = options.DisabledRules?.ToArray() ?? [],
            ProtectedJsonPointers = options.ProtectedJsonPointers?.ToArray() ?? [],
            ConsentProvider = options.ConsentProvider,
        };
    }
}

/// <summary>The application-builder registration for the response middleware.</summary>
public static class GateKeeperApplicationBuilderExtensions
{
    /// <summary>
    /// Installs the buffering middleware. Call it after exception handling and
    /// before the endpoints, so an exception response is filtered too.
    /// </summary>
    /// <param name="app">The application builder.</param>
    /// <returns>The same builder, so calls chain.</returns>
    /// <exception cref="InvalidOperationException"><see cref="GateKeeperServiceCollectionExtensions"/> was not called.</exception>
    public static IApplicationBuilder UseGateKeeper(this IApplicationBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);
        if (app.ApplicationServices.GetService<GateKeeperOptions>() is null)
        {
            throw new InvalidOperationException(
                "Call AddGateKeeper on the service collection before UseGateKeeper, so the middleware has a " +
                "verified runtime and its protected paths.");
        }

        return app.UseMiddleware<GateKeeperMiddleware>();
    }
}

/// <summary>Writes the buffered start-up diagnostics through the host's logger.</summary>
internal sealed class GateKeeperStartupService : IHostedService
{
    private readonly GateKeeperDiagnosticLog diagnostics;
    private readonly ILoggerFactory loggers;

    /// <summary>Creates the service with the buffered diagnostics and the host's logger factory.</summary>
    /// <param name="diagnostics">The buffered start-up messages.</param>
    /// <param name="loggers">The host's logger factory.</param>
    public GateKeeperStartupService(GateKeeperDiagnosticLog diagnostics, ILoggerFactory loggers)
    {
        this.diagnostics = diagnostics;
        this.loggers = loggers;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        diagnostics.Attach(loggers.CreateLogger("InvenioTech.GateKeeper"));
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
