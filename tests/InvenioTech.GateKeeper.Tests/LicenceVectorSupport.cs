// SPDX-License-Identifier: Apache-2.0

using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Crypto.Signers;

namespace InvenioTech.GateKeeper.Tests;

/// <summary>
/// Checks the public licence-token vectors against the draft specification. This is test-only code. The production
/// verifier is later licensing work and is not this helper.
/// </summary>
internal static partial class LicenceVectorSupport
{
    internal const string DevelopmentKeyId = "gklic-dev-1";

    internal const string DevelopmentSeedHex =
        "00112233445566778899aabbccddeeff00112233445566778899aabbccddeeff";

    internal const string DevelopmentPublicKeyHex =
        "3ccd241cffc9b3618044b97d036d8614593d8b017c340f1dee8773385517654b";

    internal enum Outcome
    {
        Valid,
        Malformed,
        UnknownKey,
        BadSignature,
        KeyIdMismatch,
        RejectedClaims,
    }

    internal readonly record struct DiscoveryRequest(
        bool FlagSet,
        bool FlagExists,
        bool TokenTextSet,
        bool TokenFileSet,
        bool TokenFileExists,
        bool ConfigFileExists,
        bool MountedFileExists);

    internal readonly record struct DiscoveryResult(string Source, string Kind);

    internal static string ReadVector(string fileName)
    {
        var fullPath = Path.Combine(AppContext.BaseDirectory, "vectors", "licence", fileName);
        return File.ReadAllText(fullPath);
    }

    internal static Outcome Inspect(string token)
    {
        if (!TryReadArmour(token, out var keyId, out var signature, out var payloadLine))
        {
            return Outcome.Malformed;
        }

        if (!string.Equals(keyId, DevelopmentKeyId, StringComparison.Ordinal))
        {
            return Outcome.UnknownKey;
        }

        var payloadBytes = Encoding.UTF8.GetBytes(payloadLine);
        if (payloadBytes.Length > 4096 || !Verify(payloadBytes, signature))
        {
            return Outcome.BadSignature;
        }

        if (!TryAcceptClaims(payloadLine, keyId, out var failure))
        {
            return failure == "key-id" ? Outcome.KeyIdMismatch : Outcome.RejectedClaims;
        }

        return Outcome.Valid;
    }

    internal static DiscoveryResult Discover(DiscoveryRequest request)
    {
        if (request.FlagSet)
        {
            return request.FlagExists
                ? new DiscoveryResult("flag", "present")
                : new DiscoveryResult("flag", "missing-file");
        }

        if (request.TokenTextSet)
        {
            return new DiscoveryResult("environment-text", "present");
        }

        if (request.TokenFileSet)
        {
            return request.TokenFileExists
                ? new DiscoveryResult("environment-file", "present")
                : new DiscoveryResult("environment-file", "missing-file");
        }

        if (request.ConfigFileExists)
        {
            return new DiscoveryResult("config-folder", "present");
        }

        if (request.MountedFileExists)
        {
            return new DiscoveryResult("mounted-file", "present");
        }

        return new DiscoveryResult("none", "absent");
    }

    internal static byte[] PublicKeyFromSeed()
    {
        var privateKey = new Ed25519PrivateKeyParameters(Convert.FromHexString(DevelopmentSeedHex), 0);
        return privateKey.GeneratePublicKey().GetEncoded();
    }

    internal static byte[] Sign(string payloadLine)
    {
        var privateKey = new Ed25519PrivateKeyParameters(Convert.FromHexString(DevelopmentSeedHex), 0);
        var signer = new Ed25519Signer();
        signer.Init(true, privateKey);
        var message = Encoding.UTF8.GetBytes(payloadLine);
        signer.BlockUpdate(message, 0, message.Length);
        return signer.GenerateSignature();
    }

    internal static string PayloadLine(string token)
    {
        if (!TryReadArmour(token, out _, out _, out var payloadLine))
        {
            throw new InvalidOperationException("The token is not armoured.");
        }

        return payloadLine;
    }

    private static bool Verify(byte[] payload, byte[] signature)
    {
        var publicKey = new Ed25519PublicKeyParameters(Convert.FromHexString(DevelopmentPublicKeyHex), 0);
        var verifier = new Ed25519Signer();
        verifier.Init(false, publicKey);
        verifier.BlockUpdate(payload, 0, payload.Length);
        return verifier.VerifySignature(signature);
    }

    private static bool TryReadArmour(string text, out string keyId, out byte[] signature, out string payload)
    {
        keyId = string.Empty;
        signature = [];
        payload = string.Empty;

        var lines = text.Split('\n');
        if (lines.Length > 0 && lines[^1].Length == 0)
        {
            lines = lines[..^1];
        }

        for (var index = 0; index < lines.Length; index++)
        {
            if (lines[index].EndsWith('\r'))
            {
                lines[index] = lines[index][..^1];
            }
        }

        if (lines.Length != 5
            || lines[0] != "-----BEGIN GATEKEEPER LICENCE-----"
            || lines[4] != "-----END GATEKEEPER LICENCE-----"
            || !lines[1].StartsWith("Key-Id: ", StringComparison.Ordinal)
            || !lines[2].StartsWith("Signature: ", StringComparison.Ordinal))
        {
            return false;
        }

        keyId = lines[1]["Key-Id: ".Length..];
        var signatureHex = lines[2]["Signature: ".Length..];
        payload = lines[3];
        if (!KeyIdPattern().IsMatch(keyId)
            || signatureHex.Length != 128
            || !signatureHex.All(IsLowerHex)
            || !payload.StartsWith('{'))
        {
            return false;
        }

        signature = Convert.FromHexString(signatureHex);
        return true;
    }

    private static bool TryAcceptClaims(string payloadLine, string headerKeyId, out string failure)
    {
        failure = "claims";
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(payloadLine);
        }
        catch (JsonException)
        {
            return false;
        }

        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            var allowed = new HashSet<string>(StringComparer.Ordinal)
            {
                "schemaVersion",
                "product",
                "keyId",
                "orgId",
                "cacRegistrationNumber",
                "purchasedOn",
                "updatesUntil",
            };

            var present = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in root.EnumerateObject())
            {
                if (!allowed.Contains(property.Name))
                {
                    return false;
                }

                present.Add(property.Name);
            }

            foreach (var required in new[] { "schemaVersion", "product", "keyId", "orgId", "cacRegistrationNumber", "purchasedOn" })
            {
                if (!present.Contains(required))
                {
                    return false;
                }
            }

            if (!root.GetProperty("schemaVersion").TryGetInt32(out var schemaVersion) || schemaVersion != 1)
            {
                return false;
            }

            if (root.GetProperty("product").GetString() != "gatekeeper")
            {
                return false;
            }

            var payloadKeyId = root.GetProperty("keyId").GetString();
            if (!KeyIdPattern().IsMatch(payloadKeyId ?? string.Empty))
            {
                return false;
            }

            if (!string.Equals(payloadKeyId, headerKeyId, StringComparison.Ordinal))
            {
                failure = "key-id";
                return false;
            }

            if (!OrgIdPattern().IsMatch(root.GetProperty("orgId").GetString() ?? string.Empty)
                || !RegistrationPattern().IsMatch(root.GetProperty("cacRegistrationNumber").GetString() ?? string.Empty)
                || !TryReadDate(root.GetProperty("purchasedOn"), out var purchasedOn))
            {
                return false;
            }

            if (!present.Contains("updatesUntil"))
            {
                return true;
            }

            var updatesUntil = root.GetProperty("updatesUntil");
            if (updatesUntil.ValueKind == JsonValueKind.Null
                || !TryReadDate(updatesUntil, out var updatesOn)
                || updatesOn < purchasedOn)
            {
                return false;
            }

            return true;
        }
    }

    private static bool TryReadDate(JsonElement element, out DateOnly date)
    {
        date = default;
        if (element.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        return DateOnly.TryParseExact(
            element.GetString(),
            "yyyy-MM-dd",
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out date);
    }

    private static bool IsLowerHex(char character) =>
        character is >= '0' and <= '9' or >= 'a' and <= 'f';

    [GeneratedRegex("^gklic-[a-z0-9][a-z0-9-]{0,31}$", RegexOptions.CultureInvariant | RegexOptions.NonBacktracking)]
    private static partial Regex KeyIdPattern();

    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9._-]{0,63}$", RegexOptions.CultureInvariant | RegexOptions.NonBacktracking)]
    private static partial Regex OrgIdPattern();

    [GeneratedRegex("^[A-Z0-9][A-Z0-9-]{0,31}$", RegexOptions.CultureInvariant | RegexOptions.NonBacktracking)]
    private static partial Regex RegistrationPattern();
}
