// SPDX-License-Identifier: Apache-2.0

using Microsoft.Extensions.Compliance.Classification;
using Microsoft.Extensions.Compliance.Redaction;
using Microsoft.Extensions.DependencyInjection;

namespace InvenioTech.GateKeeper.Compliance.Redaction;

/// <summary>
/// Registers the Gate-Keeper redactor with the compliance redaction engine.
/// Call it after <c>AddGateKeeper</c>, because the redactor resolves the
/// verified <see cref="InvenioTech.GateKeeper.Middleware.RuntimeMatcher"/>.
/// </summary>
public static class ComplianceRedactionServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="GateKeeperRedactor"/> for the Gate-Keeper
    /// classification and as the fallback, so any redaction request goes through
    /// the verified rules. Redaction never changes classification behaviour.
    /// </summary>
    /// <param name="services">The application service collection.</param>
    /// <returns>The same collection, so registrations chain.</returns>
    public static IServiceCollection AddGateKeeperRedaction(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddTransient<GateKeeperRedactor>();
        services.AddRedaction(builder =>
        {
            builder.SetRedactor<GateKeeperRedactor>(GateKeeperClassifications.Pii);
            builder.SetFallbackRedactor<GateKeeperRedactor>();
        });
        return services;
    }
}
