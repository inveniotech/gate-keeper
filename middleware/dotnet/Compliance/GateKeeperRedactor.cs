// SPDX-License-Identifier: Apache-2.0

using InvenioTech.GateKeeper.Middleware;
using Microsoft.Extensions.Compliance.Redaction;

namespace InvenioTech.GateKeeper.Compliance.Redaction;

/// <summary>
/// The compliance redactor: a <see cref="Redactor"/> that delegates to the
/// verified <see cref="RuntimeMatcher"/>. The mask is longer than the matched
/// span, so <see cref="GetRedactedLength"/> reports the mask length after
/// redaction. Argument and limit failures propagate; the redactor never
/// returns the original text.
/// </summary>
public sealed class GateKeeperRedactor : Redactor
{
    private readonly RuntimeMatcher runtime;

    /// <summary>Creates the redactor over one verified runtime.</summary>
    /// <param name="runtime">The shared runtime matcher built by <c>AddGateKeeper</c>.</param>
    /// <exception cref="ArgumentNullException">The runtime is missing.</exception>
    public GateKeeperRedactor(RuntimeMatcher runtime)
    {
        this.runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
    }

    /// <summary>Returns the mask length after redaction, because masks are longer than the matched span.</summary>
    /// <param name="input">The text to measure.</param>
    /// <exception cref="ArgumentException">The input is not valid Unicode text.</exception>
    /// <exception cref="InputLimitException">The input exceeds 65,536 Unicode scalars.</exception>
    public override int GetRedactedLength(ReadOnlySpan<char> input) => runtime.Redact(input.ToString()).Length;

    /// <summary>Copies the redacted text into <paramref name="destination"/> and returns its length.</summary>
    /// <param name="source">The text to redact.</param>
    /// <param name="destination">The buffer that receives the masked text.</param>
    /// <exception cref="ArgumentException">The destination is too small or the input is invalid.</exception>
    /// <exception cref="InputLimitException">The input or output exceeds 65,536 Unicode scalars.</exception>
    public override int Redact(ReadOnlySpan<char> source, Span<char> destination)
    {
        var redacted = runtime.Redact(source.ToString());
        if (redacted.Length > destination.Length)
        {
            throw new ArgumentException(
                "The redacted text is longer than the destination buffer. Size the buffer with GetRedactedLength.",
                nameof(destination));
        }

        redacted.CopyTo(destination);
        return redacted.Length;
    }
}
