// SPDX-License-Identifier: Apache-2.0

using System.Text;
using System.Text.Json;

namespace InvenioTech.GateKeeper.Tests;

public sealed class LicenceVectorTests
{
    [Fact]
    public void Development_public_key_matches_the_documented_seed()
    {
        var documented = JsonDocument.Parse(LicenceVectorSupport.ReadVector("development-public-key.json"));
        using (documented)
        {
            var root = documented.RootElement;
            Assert.Equal(LicenceVectorSupport.DevelopmentKeyId, root.GetProperty("keyId").GetString());
            Assert.Equal("development-only", root.GetProperty("label").GetString());
            Assert.Equal(LicenceVectorSupport.DevelopmentSeedHex, root.GetProperty("seedHex").GetString());
            Assert.Equal(LicenceVectorSupport.DevelopmentPublicKeyHex, root.GetProperty("publicKeyHex").GetString());
        }

        var derived = Convert.ToHexString(LicenceVectorSupport.PublicKeyFromSeed()).ToLowerInvariant();
        Assert.Equal(LicenceVectorSupport.DevelopmentPublicKeyHex, derived);
    }

    [Fact]
    public void Token_with_both_public_dates_verifies_and_exposes_the_claim_set()
    {
        var token = LicenceVectorSupport.ReadVector("claims-with-updates.gklic");
        var payload = LicenceVectorSupport.PayloadLine(token);

        Assert.Equal(LicenceVectorSupport.Outcome.Valid, LicenceVectorSupport.Inspect(token));
        Assert.True(Encoding.UTF8.GetByteCount(payload) <= 4096);
        Assert.Contains("\"purchasedOn\":\"2026-11-01\"", payload, StringComparison.Ordinal);
        Assert.Contains("\"updatesUntil\":\"2027-11-01\"", payload, StringComparison.Ordinal);
        Assert.Equal(
            Convert.ToHexString(LicenceVectorSupport.Sign(payload)).ToLowerInvariant(),
            SignatureHex(token));
    }

    [Fact]
    public void One_time_token_verifies_without_an_updates_until_claim()
    {
        var token = LicenceVectorSupport.ReadVector("claims-one-time.gklic");
        var payload = LicenceVectorSupport.PayloadLine(token);

        Assert.Equal(LicenceVectorSupport.Outcome.Valid, LicenceVectorSupport.Inspect(token));
        Assert.DoesNotContain("updatesUntil", payload, StringComparison.Ordinal);
        Assert.Equal(
            Convert.ToHexString(LicenceVectorSupport.Sign(payload)).ToLowerInvariant(),
            SignatureHex(token));
    }

    [Fact]
    public void Carriage_return_on_each_armour_line_still_verifies()
    {
        var crlf = LicenceVectorSupport.ReadVector("claims-with-updates.gklic").Replace("\n", "\r\n", StringComparison.Ordinal);

        Assert.Equal(LicenceVectorSupport.Outcome.Valid, LicenceVectorSupport.Inspect(crlf));
    }

    [Fact]
    public void Tampered_payload_does_not_verify()
    {
        Assert.Equal(
            LicenceVectorSupport.Outcome.BadSignature,
            LicenceVectorSupport.Inspect(LicenceVectorSupport.ReadVector("tampered-payload.gklic")));
    }

    [Fact]
    public void Tampered_signature_does_not_verify()
    {
        Assert.Equal(
            LicenceVectorSupport.Outcome.BadSignature,
            LicenceVectorSupport.Inspect(LicenceVectorSupport.ReadVector("tampered-signature.gklic")));
    }

    [Fact]
    public void Unknown_key_id_is_rejected_before_the_claims_are_accepted()
    {
        Assert.Equal(
            LicenceVectorSupport.Outcome.UnknownKey,
            LicenceVectorSupport.Inspect(LicenceVectorSupport.ReadVector("unknown-key.gklic")));
    }

    [Fact]
    public void Malformed_armour_is_rejected()
    {
        Assert.Equal(
            LicenceVectorSupport.Outcome.Malformed,
            LicenceVectorSupport.Inspect(LicenceVectorSupport.ReadVector("malformed-armour.gklic")));
    }

    [Fact]
    public void Mismatched_key_id_is_rejected_after_the_signature_verifies()
    {
        var token = LicenceVectorSupport.ReadVector("key-id-mismatch.gklic");

        Assert.Equal(LicenceVectorSupport.Outcome.KeyIdMismatch, LicenceVectorSupport.Inspect(token));
        Assert.Equal(
            Convert.ToHexString(LicenceVectorSupport.Sign(LicenceVectorSupport.PayloadLine(token))).ToLowerInvariant(),
            SignatureHex(token));
    }

    [Fact]
    public void Updates_until_before_purchased_on_is_rejected_after_the_signature_verifies()
    {
        var token = LicenceVectorSupport.ReadVector("claims-dates-out-of-order.gklic");

        Assert.Equal(LicenceVectorSupport.Outcome.RejectedClaims, LicenceVectorSupport.Inspect(token));
        Assert.Equal(
            Convert.ToHexString(LicenceVectorSupport.Sign(LicenceVectorSupport.PayloadLine(token))).ToLowerInvariant(),
            SignatureHex(token));
    }

    [Fact]
    public void Missing_licence_file_stops_at_the_flag()
    {
        var discovery = DiscoveryCase("missing-file");

        Assert.Equal("flag", discovery.Source);
        Assert.Equal("missing-file", discovery.Kind);
    }

    [Fact]
    public void Mounted_file_is_used_when_no_earlier_source_is_set()
    {
        var discovery = DiscoveryCase("later-source-when-nothing-earlier-is-set");

        Assert.Equal("mounted-file", discovery.Source);
        Assert.Equal("present", discovery.Kind);
    }

    private static LicenceVectorSupport.DiscoveryResult DiscoveryCase(string name)
    {
        using var document = JsonDocument.Parse(LicenceVectorSupport.ReadVector("discovery.json"));
        var match = document.RootElement.GetProperty("cases").EnumerateArray()
            .Single(item => item.GetProperty("name").GetString() == name);

        return LicenceVectorSupport.Discover(new LicenceVectorSupport.DiscoveryRequest(
            match.GetProperty("flagSet").GetBoolean(),
            match.GetProperty("flagExists").GetBoolean(),
            match.GetProperty("tokenTextSet").GetBoolean(),
            match.GetProperty("tokenFileSet").GetBoolean(),
            match.GetProperty("tokenFileExists").GetBoolean(),
            match.GetProperty("configFileExists").GetBoolean(),
            match.GetProperty("mountedFileExists").GetBoolean()));
    }

    private static string SignatureHex(string token)
    {
        var line = token.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Single(item => item.StartsWith("Signature: ", StringComparison.Ordinal));
        return line["Signature: ".Length..];
    }
}
