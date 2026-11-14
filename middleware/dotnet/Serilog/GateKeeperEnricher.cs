// SPDX-License-Identifier: Apache-2.0

using InvenioTech.GateKeeper.Middleware;
using global::Serilog.Core;
using global::Serilog.Events;

namespace InvenioTech.GateKeeper.Serilog;

/// <summary>
/// Replaces every scalar string property of a Serilog event with its redacted
/// form through <see cref="LogEvent.AddOrUpdateProperty"/>, before any sink
/// renders the event. Non-string scalars, structured values and exception
/// messages are left alone; the enricher never logs the original value.
/// </summary>
public sealed class GateKeeperEnricher : ILogEventEnricher
{
    private readonly RuntimeMatcher runtime;

    /// <summary>Creates the enricher over one verified runtime.</summary>
    /// <param name="runtime">The shared runtime matcher built by <c>AddGateKeeper</c>.</param>
    /// <exception cref="ArgumentNullException">The runtime is missing.</exception>
    public GateKeeperEnricher(RuntimeMatcher runtime)
    {
        this.runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
    }

    /// <summary>Redacts the event's scalar string properties in place.</summary>
    /// <param name="logEvent">The captured event.</param>
    /// <param name="propertyFactory">Unused; the redactor builds properties directly.</param>
    /// <exception cref="InvenioTech.GateKeeper.Middleware.InputLimitException">A property exceeds 65,536 Unicode scalars.</exception>
    public void Enrich(LogEvent logEvent, ILogEventPropertyFactory propertyFactory)
    {
        ArgumentNullException.ThrowIfNull(logEvent);
        var changes = new List<LogEventProperty>();
        foreach (var pair in logEvent.Properties)
        {
            if (pair.Value is ScalarValue { Value: string text })
            {
                var redacted = runtime.Redact(text);
                if (!string.Equals(redacted, text, StringComparison.Ordinal))
                {
                    changes.Add(new LogEventProperty(pair.Key, new ScalarValue(redacted)));
                }
            }
        }

        foreach (var change in changes)
        {
            logEvent.AddOrUpdateProperty(change);
        }
    }
}
