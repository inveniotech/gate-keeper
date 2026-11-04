// SPDX-License-Identifier: Apache-2.0

namespace InvenioTech.GateKeeper.Signing;

/// <summary>
/// Verifies Ed25519 signatures over exact message bytes. The surface is internal to the product (card P1-04), so
/// that no caller references the signature package directly: every caller takes this interface through its
/// constructor and every package is swapped in <c>SignatureVerifiers</c> alone.
/// </summary>
internal interface ISignatureVerifier
{
    /// <summary>
    /// Verifies one 64-byte Ed25519 signature against one 32-byte public key over exact message bytes. Nothing is
    /// re-encoded: whatever bytes the caller passes are the signed bytes, which is the licence-token rule
    /// (IQ-07) that nothing is re-serialised before the signature is checked.
    /// </summary>
    /// <param name="publicKey">The 32-byte Ed25519 public key.</param>
    /// <param name="message">The signed message bytes.</param>
    /// <param name="signature">The 64-byte Ed25519 signature.</param>
    /// <returns>
    /// <see langword="true"/> when the signature verifies; <see langword="false"/> when a byte array has the wrong
    /// length or the signature does not verify. The method reports malformed input as a failed verification and
    /// never throws for it (rules 23 and 28 of AGENTS.md).
    /// </returns>
    bool Verify(ReadOnlySpan<byte> publicKey, ReadOnlySpan<byte> message, ReadOnlySpan<byte> signature);
}
