// SPDX-License-Identifier: Apache-2.0

using System.Globalization;
using System.Text;
using System.Text.Json;

namespace InvenioTech.GateKeeper.Engine;

/// <summary>The parsed pack manifest payload the loader verifies against (rule-pack format, section 2.2).</summary>
/// <param name="KeyId">The key identifier the manifest carries.</param>
/// <param name="PackId">The pack identifier the manifest carries.</param>
/// <param name="VersionText">The version as text, so equality reads against the content document's version text.</param>
/// <param name="Sha256Hex">The lowercase-hex SHA-256 of the content bytes (IQ-80).</param>
/// <param name="Sha512Hex">The lowercase-hex SHA-512 of the content bytes (IQ-35).</param>
internal sealed record PackManifest(string KeyId, string PackId, string VersionText, string Sha256Hex, string Sha512Hex);

/// <summary>The parsed detached watermark block (rule-pack format, section 2.3).</summary>
/// <param name="KeyId">The key identifier the block carries.</param>
/// <param name="ContentDigest">The copied content digest.</param>
/// <param name="OrganisationName">The organisation the copy was generated for.</param>
/// <param name="LicenceId">The licence identifier the block names.</param>
/// <param name="GeneratedAt">The UTC instant the copy was produced.</param>
internal sealed record PackWatermark(string KeyId, string ContentDigest, string OrganisationName, string LicenceId, string GeneratedAt);

/// <summary>
/// Splits and parses the ASCII armour of a pack (rule-pack format, sections 2.2
/// and 2.3). The armour is exactly four lines: the begin label, the
/// <c>Key-Id:</c> header, the single payload line that the signature covers
/// byte-exactly, and the end label. A trailing carriage return per line is
/// stripped before anything is verified (IQ-07), which keeps the signed bytes
/// the same whether the file travelled with LF or CRLF endings.
/// </summary>
internal static class PackArmourReader
{
    private const string ManifestBeginLabel = "-----BEGIN GATE-KEEPER RULE PACK MANIFEST-----";
    private const string ManifestEndLabel = "-----END GATE-KEEPER RULE PACK MANIFEST-----";
    private const string WatermarkBeginLabel = "-----BEGIN GATE-KEEPER RULE PACK WATERMARK-----";
    private const string WatermarkEndLabel = "-----END GATE-KEEPER RULE PACK WATERMARK-----";
    private const int ManifestValueDigits = 64;
    private const int ManifestDigestDigits = 128;
    private const int WatermarkDigestDigits = 64;
    private const string Manifest = "manifest";
    private const string Watermark = "watermark";

    /// <summary>The keys the manifest payload carries (section 2.2).</summary>
    private static readonly IReadOnlySet<string> ManifestKeys = new HashSet<string>(StringComparer.Ordinal)
    {
        "keyId", "packId", "version", "contentHashes",
    };

    /// <summary>The keys the watermark payload carries (section 2.3).</summary>
    private static readonly IReadOnlySet<string> WatermarkKeys = new HashSet<string>(StringComparer.Ordinal)
    {
        "keyId", "contentDigest", "organisationName", "licenceId", "generatedAt",
    };

    /// <summary>
    /// Splits one armour block into its header key identifier and the exact
    /// payload bytes the signature covers. Every failure is a loud tool error
    /// naming the pack and the block.
    /// </summary>
    /// <param name="packName">The name used in every error.</param>
    /// <param name="armour">The armoured text.</param>
    /// <param name="watermark"><see langword="true"/> for the detached watermark block, <see langword="false"/> for the manifest.</param>
    internal static (string HeaderKeyId, byte[] PayloadBytes) Split(string packName, string armour, bool watermark)
    {
        var (kind, beginLabel, endLabel) = watermark
            ? (Watermark, WatermarkBeginLabel, WatermarkEndLabel)
            : (Manifest, ManifestBeginLabel, ManifestEndLabel);

        var lines = armour.Replace("\r\n", "\n").Split('\n');
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

        if (lines.Length != 4
            || lines[0] != beginLabel
            || lines[3] != endLabel
            || !lines[1].StartsWith("Key-Id: ", StringComparison.Ordinal))
        {
            throw new EngineToolException(
                $"The {kind} of the pack '{packName}' is not the four-line armour the rule-pack format fixes " +
                "(sections 2.2 and 2.3): it needs the begin label, one 'Key-Id:' header, the single payload line " +
                "that the signature covers, and the end label. Rebuild the pack; nothing reads and nothing verifies " +
                "an armour that does not match the format.");
        }

        var headerKeyId = lines[1]["Key-Id: ".Length..];
        if (headerKeyId.Length == 0)
        {
            throw new EngineToolException(
                $"The {kind} of the pack '{packName}' carries an empty 'Key-Id:' header. The header is what the " +
                "key catalogue looks the verifying key up by (IQ-07).");
        }

        if (!lines[2].StartsWith('{'))
        {
            throw new EngineToolException(
                $"The {kind} payload of the pack '{packName}' is not a JSON line: it does not start with '{{'. " +
                "The payload line is what the signature covers, so nothing reads one that is not the form the " +
                "format fixes.");
        }

        return (headerKeyId, Encoding.UTF8.GetBytes(lines[2]));
    }

    /// <summary>
    /// Parses the manifest payload (1 to 4,096 bytes, per section 2.2) into its
    /// facts. Unknown keys refuse, per the manifest schema's
    /// additionalProperties rule. The content hashes must be the lowercase hex
    /// digests the schema fixes.
    /// </summary>
    internal static PackManifest ParseManifest(string packName, byte[] payload)
    {
        if (payload.Length is 0 or > 4_096)
        {
            throw new EngineToolException(
                $"The manifest of the pack '{packName}' is {payload.Length} bytes, but a manifest is 1 to 4,096 bytes " +
                "(section 2.2), because the signing service signs at most that length. Rebuild the pack with a " +
                "manifest inside the limit.");
        }

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(payload);
        }
        catch (JsonException exception)
        {
            throw new EngineToolException(
                $"The manifest of the pack '{packName}' is not valid JSON: the parser reports {exception.Message}. " +
                "The manifest is the single-line JSON payload the signature covers (section 2.2); rebuild the pack.");
        }

        using (document)
        {
            var properties = Properties(packName, Manifest, document.RootElement, ManifestKeys);
            var contentHashes = properties.TryGetValue("contentHashes", out var contentHashesElement)
                && contentHashesElement.ValueKind is JsonValueKind.Object
                ? contentHashesElement
                : Fail<JsonElement>(packName, Manifest, "It does not set 'contentHashes' to a JSON object, which the " +
                    "manifest carries with the sha256 and sha512 digests of one content byte sequence (section 2.2).");

            var hashProperties = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
            foreach (var property in contentHashes.EnumerateObject())
            {
                if (!hashProperties.TryAdd(property.Name, property.Value))
                {
                    Fail(packName, Manifest, $"Its 'contentHashes' carries the key '{property.Name}' twice.");
                }
            }

            foreach (var name in hashProperties.Keys)
            {
                if (name is not ("sha256" or "sha512"))
                {
                    Fail(packName, Manifest, $"Its 'contentHashes' carries the key '{name}', which the schema does not " +
                        "list for the digests.");
                }
            }

            return new PackManifest(
                KeyId: ReadNonEmptyString(packName, Manifest, properties, "keyId"),
                PackId: ReadNonEmptyString(packName, Manifest, properties, "packId"),
                VersionText: ReadVersionText(packName, Manifest, properties),
                Sha256Hex: ReadHexDigest(packName, Manifest, hashProperties, "sha256", ManifestValueDigits),
                Sha512Hex: ReadHexDigest(packName, Manifest, hashProperties, "sha512", ManifestDigestDigits));
        }
    }

    /// <summary>Parses the watermark payload into its fields (section 2.3).</summary>
    internal static PackWatermark ParseWatermark(string packName, byte[] payload)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(payload);
        }
        catch (JsonException exception)
        {
            throw new EngineToolException(
                $"The watermark of the pack '{packName}' is not valid JSON: the parser reports {exception.Message}. " +
                "The watermark is the single-line JSON payload its signature covers (section 2.3); regenerate the copy.");
        }

        using (document)
        {
            var properties = Properties(packName, Watermark, document.RootElement, WatermarkKeys);
            var generatedAt = ReadNonEmptyString(packName, Watermark, properties, "generatedAt");
            if (generatedAt.Length != 20
                || !DateTimeOffset.TryParseExact(
                    generatedAt,
                    "yyyy'-'MM'-'dd'T'HH':'mm':'ss'Z'",
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                    out _))
            {
                Fail(packName, Watermark, "It sets generatedAt to an instant that is not UTC YYYY-MM-DDThh:mm:ssZ, " +
                    "so nothing can read when the copy was produced (section 2.3).");
            }

            return new PackWatermark(
                KeyId: ReadNonEmptyString(packName, Watermark, properties, "keyId"),
                ContentDigest: ReadHexDigest(packName, Watermark, properties, "contentDigest", WatermarkDigestDigits),
                OrganisationName: ReadNonEmptyString(packName, Watermark, properties, "organisationName"),
                LicenceId: ReadNonEmptyString(packName, Watermark, properties, "licenceId"),
                GeneratedAt: generatedAt);
        }
    }

    private static Dictionary<string, JsonElement> Properties(string packName, string kind, JsonElement root, IReadOnlySet<string> allowedKeys)
    {
        if (root.ValueKind is not JsonValueKind.Object)
        {
            Fail(packName, kind, $"It is not a JSON object, which the {kind} payload is (sections 2.2 and 2.3).");
        }

        var properties = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (var property in root.EnumerateObject())
        {
            if (!properties.TryAdd(property.Name, property.Value))
            {
                Fail(packName, kind, $"It carries the key '{property.Name}' twice.");
            }
        }

        foreach (var name in properties.Keys)
        {
            if (!allowedKeys.Contains(name))
            {
                Fail(packName, kind, $"It carries the key '{name}', which the schema does not list for the {kind}. " +
                    "Every additional property is refused, so the keys are exactly the ones the format fixes.");
            }
        }

        return properties;
    }

    private static string ReadNonEmptyString(string packName, string kind, Dictionary<string, JsonElement> properties, string name)
    {
        if (!properties.TryGetValue(name, out var element)
            || element.ValueKind is not JsonValueKind.String
            || element.GetString() is not { } text
            || text.Length == 0)
        {
            Fail(packName, kind, $"It does not set '{name}' to a non-empty string, which the schema requires.");
        }

        return element.GetString()!;
    }

    private static string ReadHexDigest(string packName, string kind, Dictionary<string, JsonElement> properties, string name, int digitCount)
    {
        if (!properties.TryGetValue(name, out var element)
            || element.ValueKind is not JsonValueKind.String
            || element.GetString() is not { } text
            || text.Length != digitCount
            || !text.All(IsLowerHex))
        {
            Fail(packName, kind, $"It sets '{name}' to something that is not {digitCount} lowercase hexadecimal " +
                "characters, which the schema fixes for the digest.");
        }

        return element.GetString()!;
    }

    private static string ReadVersionText(string packName, string kind, Dictionary<string, JsonElement> properties)
    {
        if (!properties.TryGetValue("version", out var element)
            || element.ValueKind is not (JsonValueKind.Number or JsonValueKind.String))
        {
            Fail(packName, kind, "It does not set 'version' to a string or an integer, which the manifest carries (section 2.2).");
        }

        return element.ValueKind is JsonValueKind.Number ? element.ToString() : element.GetString()!;
    }

    private static bool IsLowerHex(char character) => character is >= '0' and <= '9' or >= 'a' and <= 'f';

    private static void Fail(string packName, string kind, string detail)
    {
        throw new EngineToolException($"The {kind} of the pack '{packName}' cannot be read. {detail}");
    }

    private static T Fail<T>(string packName, string kind, string detail)
    {
        throw new EngineToolException($"The {kind} of the pack '{packName}' cannot be read. {detail}");
    }
}
