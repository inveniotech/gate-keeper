// SPDX-License-Identifier: Apache-2.0

using InvenioTech.GateKeeper.Spikes.Aot.Ed25519;

int failures = 0;

foreach (Ed25519Vector vector in Rfc8032Vectors.All)
{
    failures += RunVector(vector, "BouncyCastle", BouncyCastleDrivers.DerivePublicKey, BouncyCastleDrivers.Sign);
    failures += RunVector(vector, "Rebex", RebexDrivers.DerivePublicKey, RebexDrivers.Sign);
}

// A sign-then-verify round trip exercises the verification path with a fixed
// seed so the output stays deterministic.
byte[] roundTripSeed = Rfc8032Vectors.All[0].SecretKey;
byte[] roundTripMessage = System.Text.Encoding.UTF8.GetBytes("gatekeeper-ndpa-check signature round trip");

failures += RunRoundTrip("BouncyCastle", BouncyCastleDrivers.RoundTrip(roundTripSeed, roundTripMessage));
failures += RunRoundTrip("Rebex", RebexDrivers.RoundTrip(roundTripSeed, roundTripMessage));

Console.WriteLine(failures == 0 ? "RESULT: PASS" : $"RESULT: FAIL ({failures})");
return failures == 0 ? 0 : 1;

static int RunVector(
    Ed25519Vector vector,
    string library,
    Func<byte[], byte[]> derivePublicKey,
    Func<byte[], byte[], byte[]> sign)
{
    byte[] derivedPublicKey = derivePublicKey(vector.SecretKey);
    if (!derivedPublicKey.AsSpan().SequenceEqual(vector.PublicKey))
    {
        Console.WriteLine($"{library}: {vector.Name}: FAIL (derived public key does not match the vector)");
        return 1;
    }

    byte[] signature = sign(vector.SecretKey, vector.Message);
    if (!signature.AsSpan().SequenceEqual(vector.Signature))
    {
        Console.WriteLine($"{library}: {vector.Name}: FAIL (signature does not match the vector)");
        return 1;
    }

    Console.WriteLine($"{library}: {vector.Name}: PASS");
    return 0;
}

static int RunRoundTrip(string library, bool passed)
{
    Console.WriteLine($"{library}: round-trip sign and verify: {(passed ? "PASS" : "FAIL")}");
    return passed ? 0 : 1;
}

/// <summary>Drivers for <c>Org.BouncyCastle.Cryptography</c>.</summary>
internal static class BouncyCastleDrivers
{
    public static byte[] DerivePublicKey(byte[] seed) =>
        new Org.BouncyCastle.Crypto.Parameters.Ed25519PrivateKeyParameters(seed, 0).GeneratePublicKey().GetEncoded();

    public static byte[] Sign(byte[] seed, byte[] message)
    {
        Org.BouncyCastle.Crypto.Parameters.Ed25519PrivateKeyParameters privateKey = new(seed, 0);
        var signer = new Org.BouncyCastle.Crypto.Signers.Ed25519Signer();
        signer.Init(true, privateKey);
        signer.BlockUpdate(message, 0, message.Length);
        return signer.GenerateSignature();
    }

    public static bool RoundTrip(byte[] seed, byte[] message)
    {
        Org.BouncyCastle.Crypto.Parameters.Ed25519PrivateKeyParameters privateKey = new(seed, 0);
        Org.BouncyCastle.Crypto.Parameters.Ed25519PublicKeyParameters publicKey = privateKey.GeneratePublicKey();

        var signer = new Org.BouncyCastle.Crypto.Signers.Ed25519Signer();
        signer.Init(true, privateKey);
        signer.BlockUpdate(message, 0, message.Length);
        byte[] signature = signer.GenerateSignature();

        var verifier = new Org.BouncyCastle.Crypto.Signers.Ed25519Signer();
        verifier.Init(false, publicKey);
        verifier.BlockUpdate(message, 0, message.Length);
        return verifier.VerifySignature(signature);
    }
}

/// <summary>Drivers for <c>Rebex.Elliptic.Ed25519</c>.</summary>
internal static class RebexDrivers
{
    public static byte[] DerivePublicKey(byte[] seed)
    {
        var key = new Rebex.Security.Cryptography.Ed25519();
        key.FromSeed(seed);
        return key.GetPublicKey();
    }

    public static byte[] Sign(byte[] seed, byte[] message)
    {
        var key = new Rebex.Security.Cryptography.Ed25519();
        key.FromSeed(seed);
        return key.SignMessage(message);
    }

    public static bool RoundTrip(byte[] seed, byte[] message)
    {
        var signer = new Rebex.Security.Cryptography.Ed25519();
        signer.FromSeed(seed);
        byte[] signature = signer.SignMessage(message);

        var verifier = new Rebex.Security.Cryptography.Ed25519();
        verifier.FromPublicKey(signer.GetPublicKey());
        return verifier.VerifyMessage(message, signature);
    }
}
