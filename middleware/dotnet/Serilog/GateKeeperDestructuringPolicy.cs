// SPDX-License-Identifier: Apache-2.0

using System.Collections;
using System.Diagnostics.CodeAnalysis;
using InvenioTech.GateKeeper.Middleware;
using global::Serilog.Core;
using global::Serilog.Events;

namespace InvenioTech.GateKeeper.Serilog;

/// <summary>
/// Redacts the string members of structured values that Serilog captures with
/// <c>{@...}</c>. A policy is consulted before Serilog's built-in conversion, so
/// dictionaries and string sequences it claims are built here: string keys and
/// string members pass through the verified <see cref="RuntimeMatcher"/>, and
/// nested dictionaries recurse. Types Serilog destructures by reflection
/// (anonymous and record types) are outside the policy: the adapter adds no
/// reflection, which is what keeps it trim and ahead-of-time safe. The policy
/// never logs the original value.
/// </summary>
public sealed class GateKeeperDestructuringPolicy : IDestructuringPolicy
{
    private readonly RuntimeMatcher runtime;

    /// <summary>Creates the policy over one verified runtime.</summary>
    /// <param name="runtime">The shared runtime matcher built by <c>AddGateKeeper</c>.</param>
    /// <exception cref="ArgumentNullException">The runtime is missing.</exception>
    public GateKeeperDestructuringPolicy(RuntimeMatcher runtime)
    {
        this.runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
    }

    /// <summary>Destructures dictionaries and string sequences with redacted string members.</summary>
    /// <param name="value">The captured value.</param>
    /// <param name="propertyValueFactory">The factory for nested member values.</param>
    /// <param name="result">The redacted structure, when the value is supported.</param>
    /// <returns><see langword="true"/> when the policy built the structure.</returns>
    public bool TryDestructure(object value, ILogEventPropertyValueFactory propertyValueFactory, [NotNullWhen(true)] out LogEventPropertyValue? result)
    {
        ArgumentNullException.ThrowIfNull(value);
        ArgumentNullException.ThrowIfNull(propertyValueFactory);
        if (value is IDictionary dictionary)
        {
            result = DestructureDictionary(dictionary, propertyValueFactory);
            return true;
        }

        if (value is IEnumerable<string> strings)
        {
            var elements = strings.Select(text => (LogEventPropertyValue)new ScalarValue(runtime.Redact(text))).ToArray();
            result = new SequenceValue(elements);
            return true;
        }

        result = null;
        return false;
    }

    /// <summary>Builds the dictionary: keys and string members are redacted, other members go to the factory.</summary>
    private DictionaryValue DestructureDictionary(IDictionary dictionary, ILogEventPropertyValueFactory propertyValueFactory)
    {
        var elements = new List<KeyValuePair<ScalarValue, LogEventPropertyValue>>(dictionary.Count);
        foreach (DictionaryEntry entry in dictionary)
        {
            elements.Add(new KeyValuePair<ScalarValue, LogEventPropertyValue>(
                new ScalarValue(entry.Key is string key ? runtime.Redact(key) : entry.Key),
                entry.Value is string text
                    ? new ScalarValue(runtime.Redact(text))
                    : propertyValueFactory.CreatePropertyValue(entry.Value, destructureObjects: true)));
        }

        return new DictionaryValue(elements);
    }
}
