// SPDX-License-Identifier: Apache-2.0

using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Crypto.Signers;

namespace InvenioTech.GateKeeper.Signing;

/// <summary>
/// The Ed25519 verifier behind <see cref="SignatureVerifiers"/>, provided by the BouncyCastle.Cryptography package.
/// The NativeAOT feasibility spike (P0-16, IQ-11) verified that the package signs and verifies every pure-Ed25519
/// RFC 8032 vector under NativeAOT with zero trim and AOT warnings, which is why the pre-approved fallback
/// (Rebex.Elliptic.Ed25519) is not referenced. Any change here requires a full test-vector run (rule 14).
/// </summary>
internal sealed class BouncyCastleEd25519Verifier : ISignatureVerifier
{
    private const int PublicKeyByteLength = 32;
    private const int SignatureByteLength = 64;

    public bool Verify(ReadOnlySpan<byte> publicKey, ReadOnlySpan<byte> message, ReadOnlySpan<byte> signature)
    {
        if (publicKey.Length != PublicKeyByteLength || signature.Length != SignatureByteLength)
        {
            return false;
        }

        try
        {
            // BouncyCastle reads every input from a byte array, so the spans are copied once. The cost is three
            // short allocations and one at most per licence token, never in a per-file hot path, so it stays far
            // inside the 10 ms NFR-002 budget. The pre-write length checks above keep the rest of the failure
            // surface inside the caught block.
            var verifier = new Ed25519Signer();
            verifier.Init(
                forSigning: false,
                new Ed25519PublicKeyParameters(publicKey.ToArray(), 0));
            verifier.BlockUpdate(message.ToArray(), 0, message.Length);
            return verifier.VerifySignature(signature.ToArray());
        }
        catch (Exception exception) when (exception is not (OutOfMemoryException or OperationCanceledException))
        {
            // Malformed signature material is input an attacker chose, so the outcome is a failed verification and
            // the offsetting exception type stays an implementation detail of the package. This is the documented
            // result of the interface, not a silent swallow (rules 23 and 28 of AGENTS.md).
            return false;
        }
    }
}
