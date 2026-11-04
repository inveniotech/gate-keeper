// SPDX-License-Identifier: Apache-2.0

using System.Text;
using System.Text.Json;
using InvenioTech.GateKeeper.Engine;

namespace InvenioTech.GateKeeper.Engine.Tests;

/// <summary>
/// Shared support for the pack-loader tests (card P1-03). It reads the
/// development-pack vectors under <c>vectors/pack</c>, which the vector script
/// <c>tests/vectors/pack/New-PackVectors.mjs</c> generates from two clearly
/// labelled development-only keys (rule 14): one signs the manifest, one signs
/// the detached watermark.
/// </summary>
internal static class PackTestSupport
{
    internal const string PackSigningKeyId = "gkpk-dev-1";
    internal const string WatermarkKeyId = "gkwm-dev-1";

    /// <summary>The key catalogue the tests use: the development keys only.</summary>
    internal static readonly DevelopmentPackKeyCatalogue Catalogue = DevelopmentPackKeyCatalogue.Load();

    /// <summary>Reads one content sequence as the exact bytes the script wrote.</summary>
    internal static byte[] ReadContent(string stem)
    {
        return File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "vectors", "pack", $"{stem}.pack.json"));
    }

    /// <summary>Reads the manifest armour text for one stem.</summary>
    internal static string ReadManifestArmour(string stem)
    {
        return File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "vectors", "pack", $"{stem}.manifest.armour"));
    }

    /// <summary>Reads one hex signature file as the 64 signature bytes.</summary>
    internal static byte[] ReadSignature(string stem)
    {
        var text = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "vectors", "pack", $"{stem}.signature.hex"));
        return Convert.FromHexString(text.Trim());
    }

    /// <summary>
    /// Builds the <see cref="SignedPackInput"/> the loader receives for one stem.
    /// </summary>
    /// <param name="stem">The vector stem, for example <c>pack</c> or <c>next-line</c>.</param>
    /// <param name="watermarkStem">A watermark stem to attach, or null for none.</param>
    internal static SignedPackInput SignedPack(string stem, string? watermarkStem = null)
    {
        return new SignedPackInput(
            Name: stem,
            ContentBytes: ReadContent(stem),
            ManifestArmour: ReadManifestArmour(stem),
            ManifestSignature: ReadSignature(stem),
            WatermarkArmour: watermarkStem is null ? null : ReadManifestArmour(watermarkStem),
            WatermarkSignature: watermarkStem is null ? null : ReadSignature(watermarkStem));
    }

    /// <summary>
    /// Builds the bytes of one unsigned local pack document from a jacket of
    /// rules. Every object the helpers write carries its keys in a fixed order,
    /// and the identifiers are synthetic (rule 10).
    /// </summary>
    /// <param name="id">The pack identifier.</param>
    /// <param name="version">The pack version as JSON text, for example <c>2</c>.</param>
    /// <param name="rules">Rule objects as JSON text.</param>
    internal static byte[] LocalPack(string id, string version, params string[] rules)
    {
        var builder = new StringBuilder();
        builder.Append('{');
        builder.Append("\"id\": \"").Append(id).Append("\", ");
        builder.Append("\"minEngineVersion\": \"0.1.0\", ");
        builder.Append("\"provenance\": \"local\", ");
        builder.Append("\"rules\": [");
        for (var index = 0; index < rules.Length; index++)
        {
            builder.Append(rules[index]);
            if (index < rules.Length - 1)
            {
                builder.Append(", ");
            }
        }
        builder.Append("], ");
        builder.Append("\"schemaVersion\": 1, ");
        builder.Append("\"version\": ").Append(version);
        builder.Append("}\n");
        return Encoding.UTF8.GetBytes(builder.ToString());
    }

    /// <summary>The standard rule body as JSON text, with placeholders for the variants.</summary>
    internal static string LocalRule(string id, string matcherJson, string severity = "error")
    {
        return """
            {
              "id": "RULE_ID",
              "severity": "SEVERITY",
              "category": "synthetic-code",
              "citation": { "title": "Placeholder citation — approved NDPA wording arrives in P2-02" },
              "sinceVersion": 2,
              "matcher": MATCHER
            }
            """.Replace("RULE_ID", id).Replace("SEVERITY", severity).Replace("MATCHER", matcherJson);
    }

    /// <summary>The matcher JSON the valid local rule carries.</summary>
    internal const string DigitMatcherJson =
        """{ "pattern": { "shape": [ { "min": 11, "max": 11, "class": "digit", "emit": true } ], "boundary": "digit" } }""";
}

/// <summary>
/// The one key catalogue the loader tests use: it hands out the public keys of
/// the development-only pack and watermark keys from the committed vector file.
/// It is a test implementation of the engine interface; production catalogues
/// arrive with the packaging and licensing cards.
/// </summary>
internal sealed class DevelopmentPackKeyCatalogue : IPackKeyCatalogue
{
    private readonly IReadOnlyDictionary<string, byte[]> _packKeys;
    private readonly IReadOnlyDictionary<string, byte[]> _watermarkKeys;

    private DevelopmentPackKeyCatalogue(IReadOnlyDictionary<string, byte[]> packKeys, IReadOnlyDictionary<string, byte[]> watermarkKeys)
    {
        _packKeys = packKeys;
        _watermarkKeys = watermarkKeys;
    }

    internal static readonly string PackSigningKeyId = PackTestSupport.PackSigningKeyId;

    /// <summary>Loads the catalogue from <c>development-public-key.json</c>.</summary>
    internal static DevelopmentPackKeyCatalogue Load()
    {
        var text = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "vectors", "pack", "development-public-key.json"));
        using var document = JsonDocument.Parse(text);
        var packKeys = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        var watermarkKeys = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        foreach (var key in document.RootElement.GetProperty("keys").EnumerateArray())
        {
            var keyId = key.GetProperty("keyId").GetString()!;
            var publicKey = Convert.FromHexString(key.GetProperty("publicKeyHex").GetString()!);
            if (key.GetProperty("purpose").GetString() == "pack-signing")
            {
                packKeys.Add(keyId, publicKey);
            }
            else
            {
                watermarkKeys.Add(keyId, publicKey);
            }
        }

        return new DevelopmentPackKeyCatalogue(packKeys, watermarkKeys);
    }

    /// <summary>True when the key identifier is the development pack key.</summary>
    public bool TryGetPackSigningKey(string keyId, out byte[] publicKey)
    {
        return TryGet(_packKeys, keyId, out publicKey);
    }

    /// <summary>True when the key identifier is the development watermark key.</summary>
    public bool TryGetWatermarkKey(string keyId, out byte[] publicKey)
    {
        return TryGet(_watermarkKeys, keyId, out publicKey);
    }

    private static bool TryGet(IReadOnlyDictionary<string, byte[]> keys, string keyId, out byte[] publicKey)
    {
        if (keys.TryGetValue(keyId, out var found))
        {
            publicKey = [.. found];
            return true;
        }

        publicKey = [];
        return false;
    }
}
