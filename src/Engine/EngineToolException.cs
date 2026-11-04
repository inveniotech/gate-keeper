// SPDX-License-Identifier: Apache-2.0

namespace InvenioTech.GateKeeper.Engine;

/// <summary>
/// The one exception an engine component raises when the user's input (a rule
/// pack detail, a configuration value, a command flag or a repository state)
/// is invalid in a way only a human can repair. The scan orchestrator turns
/// it into exit code 2 with the message on standard error (rule 13); every
/// message is a complete en-GB sentence that says what is wrong and what to
/// do next (rule 23).
/// </summary>
public sealed class EngineToolException : Exception
{
    /// <summary>
    /// Creates the exception for a completed error message.
    /// </summary>
    /// <param name="message">The user-facing message, naming the offending value.</param>
    public EngineToolException(string message)
        : base(message)
    {
    }
}
