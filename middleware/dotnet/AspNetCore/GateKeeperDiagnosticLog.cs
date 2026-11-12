// SPDX-License-Identifier: Apache-2.0

using Microsoft.Extensions.Logging;

namespace InvenioTech.GateKeeper.AspNetCore;

/// <summary>
/// Buffers start-up diagnostics until the host's logger exists, then writes each
/// one exactly once. Runtime consent faults arrive after start-up and are
/// written immediately. Messages contain metadata only: rule identifiers,
/// purposes and exception type names, never source text or subjects.
/// </summary>
internal sealed class GateKeeperDiagnosticLog
{
    private readonly object gate = new();
    private readonly List<(LogLevel Level, string Message)> pending = [];
    private ILogger? logger;

    internal void AddInformation(string message) => Add(LogLevel.Information, message);

    internal void AddWarning(string message) => Add(LogLevel.Warning, message);

    internal void Attach(ILogger target)
    {
        lock (gate)
        {
            logger = target;
            foreach (var entry in pending)
            {
                target.Log(entry.Level, "{GateKeeperDiagnostic}", entry.Message);
            }

            pending.Clear();
        }
    }

    private void Add(LogLevel level, string message)
    {
        lock (gate)
        {
            if (logger is null)
            {
                pending.Add((level, message));
                return;
            }

            logger.Log(level, "{GateKeeperDiagnostic}", message);
        }
    }
}
