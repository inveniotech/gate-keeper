// SPDX-License-Identifier: Apache-2.0

using InvenioTech.GateKeeper.Signing;

namespace InvenioTech.GateKeeper.Signing.Tests;

/// <summary>
/// The Ed25519 verification contract callers rely on: byte-exact RFC 8032
/// behaviour, wrong lengths reported as a failed verification and never a
/// thrown error, and the signature material treated as hostile input
/// (card P1-04, NFR-002, rules 14 and 28 of AGENTS.md).
/// </summary>
public sealed class SignatureVerifierTests
{
    [Theory]
    [MemberData(nameof(Ed25519Vectors))]
    public void Verifier_Should_VerifyTheVectorSignature_When_TheVectorKeyAndMessageAreGiven(Ed25519Vector vector)
    {
        SignatureVerifiers.Default.Verify(vector.PublicKey, vector.Message, vector.Signature)
            .Should().BeTrue($"RFC 8032 {vector.Name} is a passing vector");
    }

    [Fact]
    public void Verifier_Should_RejectTheSignature_When_TheMessageIsNotTheOneThatWasSigned()
    {
        var signed = Rfc8032Vectors.All[1];
        var wrong = Rfc8032Vectors.All[2];

        SignatureVerifiers.Default.Verify(signed.PublicKey, wrong.Message, signed.Signature)
            .Should().BeFalse();
    }

    [Fact]
    public void Verifier_Should_RejectTheSignature_When_TheKeyIsNotTheSigner()
    {
        var signed = Rfc8032Vectors.All[1];
        var other = Rfc8032Vectors.All[0];

        SignatureVerifiers.Default.Verify(other.PublicKey, signed.Message, signed.Signature)
            .Should().BeFalse();
    }

    [Theory]
    [MemberData(nameof(Ed25519Vectors))]
    public void Verifier_Should_RejectASignatureWithOneFlippedBit_When_TheVectorSignatureIsTampered(Ed25519Vector vector)
    {
        var tampered = TamperLastByte(vector.Signature);

        SignatureVerifiers.Default.Verify(vector.PublicKey, vector.Message, tampered)
            .Should().BeFalse();
    }

    [Fact]
    public void Verifier_Should_RejectTheSignature_When_TheSignatureHalvesAreSwapped()
    {
        var vector = Rfc8032Vectors.All[0];
        var swapped = new byte[vector.Signature.Length];
        vector.Signature.AsSpan(32, 32).CopyTo(swapped);
        vector.Signature.AsSpan(0, 32).CopyTo(swapped[32..]);

        SignatureVerifiers.Default.Verify(vector.PublicKey, vector.Message, swapped)
            .Should().BeFalse();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(31)]
    [InlineData(33)]
    public void Verifier_Should_ReturnFalseWithoutThrowing_When_ThePublicKeyIsNot32Bytes(int length)
    {
        var vector = Rfc8032Vectors.All[0];

        Func<bool> act = () => SignatureVerifiers.Default.Verify(
            new byte[length],
            vector.Message,
            vector.Signature);

        act.Should().NotThrow().Which.Should().BeFalse();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(63)]
    [InlineData(65)]
    public void Verifier_Should_ReturnFalseWithoutThrowing_When_TheSignatureIsNot64Bytes(int length)
    {
        var vector = Rfc8032Vectors.All[0];

        Func<bool> act = () => SignatureVerifiers.Default.Verify(
            vector.PublicKey,
            vector.Message,
            new byte[length]);

        act.Should().NotThrow().Which.Should().BeFalse();
    }

    public static IEnumerable<object[]> Ed25519Vectors() =>
        Rfc8032Vectors.All.Select(vector => new object[] { vector });

    private static byte[] TamperLastByte(byte[] signature)
    {
        var tampered = (byte[])signature.Clone();
        tampered[^1] ^= 0x01;
        return tampered;
    }
}
