// SPDX-License-Identifier: Apache-2.0

namespace InvenioTech.GateKeeper.Signing;

/// <summary>
/// The one place a caller obtains an <see cref="ISignatureVerifier"/> from (card P1-04). Swapping the signature
/// package means changing this type alone; no caller names the implementation or the package.
/// </summary>
internal static class SignatureVerifiers
{
    /// <summary>
    /// Gets the verifier every caller receives: pure-mode Ed25519 with a 32-byte public key and a 64-byte
    /// signature, as the licence-token specification requires (section 2 of that draft).
    /// </summary>
    public static ISignatureVerifier Default { get; } = new BouncyCastleEd25519Verifier();
}
