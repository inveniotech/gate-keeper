// SPDX-License-Identifier: Apache-2.0

using InvenioTech.GateKeeper.Engine;

namespace InvenioTech.GateKeeper.Engine.Tests;

public sealed class FingerprintTests
{
    [Fact]
    public void Fingerprint_Should_HoldTheHexCharacters_When_Given64LowercaseHexCharacters()
    {
        var hex = new string('a', 64);

        var fingerprint = new Fingerprint(hex);

        Assert.Equal(hex, fingerprint.Value);
    }

    [Theory]
    [InlineData("")]
    [InlineData("abc")]
    [InlineData("00cca3a4746d65803db2fa8f18dfa1d9f05ca1c3c382ca92b2078b31a0e9c3a")] // 63 characters
    [InlineData("00cca3a4746d65803db2fa8f18dfa1d9f05ca1c3c382ca92b2078b31a0e9c3a77")] // 65 characters
    [InlineData("00CCA3A4746D65803DB2FA8F18DFA1D9F05CA1C3C382CA92B2078B31A0E9C3A7")] // upper case
    [InlineData("00cca3a4746d65803db2ga8f18dfa1d9f05ca1c3c382ca92b2078b31a0e9c3a7")] // letter g is not hex
    [InlineData("00cca3a4746d65803db2fa8f18dfa1d9f05ca1c3c382ca92b2078b31a0e9c3a7 ")] // padding space
    public void Fingerprint_Should_RejectTheValue_When_TheTextIsNot64LowercaseHexCharacters(string bad)
    {
        Assert.Throws<ArgumentException>(() => _ = new Fingerprint(bad));
    }
}

