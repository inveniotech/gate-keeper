// SPDX-License-Identifier: Apache-2.0

namespace InvenioTech.GateKeeper.Engine;

/// <summary>
/// A work-tree finding's fingerprint: the keyed hash (IQ-20) computed over the
/// rule identifier, the repository-relative path, the matched span and the
/// occurrence index, so a finding can be recognised across scans without ever
/// exposing the matched value. A fingerprint is never a plain hash of a
/// matched value (rule 10).
/// </summary>
/// <param name="value">The fingerprint as 64 lowercase hexadecimal characters, the text form of the HMAC-SHA-256 output.</param>
/// <exception cref="ArgumentException">The text is not exactly 64 lowercase hexadecimal characters.</exception>
public readonly record struct Fingerprint(string value)
{
    /// <summary>Gets the fingerprint as 64 lowercase hexadecimal characters.</summary>
    public string Value { get; } = Fingerprint.IsHexString64(value)
        ? value
        : throw new ArgumentException(
            "A fingerprint is 64 lowercase hexadecimal characters, the text form of an HMAC-SHA-256 output.",
            nameof(value));

    private static bool IsHexString64(string text)
    {
        if (text.Length != 64)
        {
            return false;
        }

        foreach (char character in text)
        {
            var isLowerHex = (character >= '0' && character <= '9') || (character >= 'a' && character <= 'f');
            if (!isLowerHex)
            {
                return false;
            }
        }

        return true;
    }
}
