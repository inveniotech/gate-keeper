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
            root.GetProperty("keyId").GetString().Should().Be(LicenceVectorSupport.DevelopmentKeyId);
            root.GetProperty("label").GetString().Should().Be("development-only");
            root.GetProperty("seedHex").GetString().Should().Be(LicenceVectorSupport.DevelopmentSeedHex);
            root.GetProperty("publicKeyHex").GetString().Should().Be(LicenceVectorSupport.DevelopmentPublicKeyHex);
        }

        var derived = Convert.ToHexString(LicenceVectorSupport.PublicKeyFromSeed()).ToLowerInvariant();
        derived.Should().Be(LicenceVectorSupport.DevelopmentPublicKeyHex);
    }

    [Fact]
    public void Token_with_both_public_dates_verifies_and_exposes_the_claim_set()
    {
        var token = LicenceVectorSupport.ReadVector("claims-with-updates.gklic");
        var payload = LicenceVectorSupport.PayloadLine(token);

        LicenceVectorSupport.Inspect(token).Should().Be(LicenceVectorSupport.Outcome.Valid);
        Encoding.UTF8.GetByteCount(payload).Should().BeLessThanOrEqualTo(4096);
        payload.Should().Contain("\"purchasedOn\":\"2026-11-01\"");
        payload.Should().Contain("\"updatesUntil\":\"2027-11-01\"");
        SignatureHex(token).Should().Be(Convert.ToHexString(LicenceVectorSupport.Sign(payload)).ToLowerInvariant());
    }

    [Fact]
    public void One_time_token_verifies_without_an_updates_until_claim()
    {
        var token = LicenceVectorSupport.ReadVector("claims-one-time.gklic");
        var payload = LicenceVectorSupport.PayloadLine(token);

        LicenceVectorSupport.Inspect(token).Should().Be(LicenceVectorSupport.Outcome.Valid);
        payload.Should().NotContain("updatesUntil");
        SignatureHex(token).Should().Be(Convert.ToHexString(LicenceVectorSupport.Sign(payload)).ToLowerInvariant());
    }

    [Fact]
    public void Carriage_return_on_each_armour_line_still_verifies()
    {
        var crlf = LicenceVectorSupport.ReadVector("claims-with-updates.gklic").Replace("\n", "\r\n", StringComparison.Ordinal);

        LicenceVectorSupport.Inspect(crlf).Should().Be(LicenceVectorSupport.Outcome.Valid);
    }

    [Fact]
    public void Tampered_payload_does_not_verify()
    {
        LicenceVectorSupport.Inspect(LicenceVectorSupport.ReadVector("tampered-payload.gklic")).Should().Be(LicenceVectorSupport.Outcome.BadSignature);
    }

    [Fact]
    public void Tampered_signature_does_not_verify()
    {
        LicenceVectorSupport.Inspect(LicenceVectorSupport.ReadVector("tampered-signature.gklic")).Should().Be(LicenceVectorSupport.Outcome.BadSignature);
    }

    [Fact]
    public void Unknown_key_id_is_rejected_before_the_claims_are_accepted()
    {
        LicenceVectorSupport.Inspect(LicenceVectorSupport.ReadVector("unknown-key.gklic")).Should().Be(LicenceVectorSupport.Outcome.UnknownKey);
    }

    [Fact]
    public void Malformed_armour_is_rejected()
    {
        LicenceVectorSupport.Inspect(LicenceVectorSupport.ReadVector("malformed-armour.gklic")).Should().Be(LicenceVectorSupport.Outcome.Malformed);
    }

    [Fact]
    public void Mismatched_key_id_is_rejected_after_the_signature_verifies()
    {
        var token = LicenceVectorSupport.ReadVector("key-id-mismatch.gklic");

        LicenceVectorSupport.Inspect(token).Should().Be(LicenceVectorSupport.Outcome.KeyIdMismatch);
        SignatureHex(token).Should().Be(Convert.ToHexString(LicenceVectorSupport.Sign(LicenceVectorSupport.PayloadLine(token))).ToLowerInvariant());
    }

    [Fact]
    public void Updates_until_before_purchased_on_is_rejected_after_the_signature_verifies()
    {
        var token = LicenceVectorSupport.ReadVector("claims-dates-out-of-order.gklic");

        LicenceVectorSupport.Inspect(token).Should().Be(LicenceVectorSupport.Outcome.RejectedClaims);
        SignatureHex(token).Should().Be(Convert.ToHexString(LicenceVectorSupport.Sign(LicenceVectorSupport.PayloadLine(token))).ToLowerInvariant());
    }

    [Fact]
    public void Missing_licence_file_stops_at_the_flag()
    {
        var discovery = DiscoveryCase("missing-file");

        discovery.Source.Should().Be("flag");
        discovery.Kind.Should().Be("missing-file");
    }

    [Fact]
    public void Mounted_file_is_used_when_no_earlier_source_is_set()
    {
        var discovery = DiscoveryCase("later-source-when-nothing-earlier-is-set");

        discovery.Source.Should().Be("mounted-file");
        discovery.Kind.Should().Be("present");
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
