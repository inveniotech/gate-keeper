// SPDX-License-Identifier: Apache-2.0

using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using InvenioTech.GateKeeper.Engine;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Crypto.Signers;

namespace InvenioTech.GateKeeper.Middleware.Tests;

/// <summary>
/// Reads the committed development vectors and signs synthetic content with the
/// labelled development-only keys. Nothing here is production key material, and
/// no signature it produces is written into <c>rule-packs</c> (rule 14).
/// </summary>
internal static class TestPacks
{
    internal const string SigningKeyId = "gkpk-dev-1";
    internal const string WatermarkKeyId = "gkwm-dev-1";

    internal static string VectorDirectory => Path.Combine(AppContext.BaseDirectory, "vectors", "pack");

    internal static string CataloguePath => Path.Combine(VectorDirectory, "development-public-key.json");

    internal static byte[] ReadContent(string stem) =>
        File.ReadAllBytes(Path.Combine(VectorDirectory, $"{stem}.pack.json"));

    internal static string ReadManifestArmour(string stem) =>
        File.ReadAllText(Path.Combine(VectorDirectory, $"{stem}.manifest.armour"));

    internal static string ReadWatermarkArmour(string stem) =>
        File.ReadAllText(Path.Combine(VectorDirectory, $"{stem}.manifest.armour"));

    internal static byte[] ReadSignature(string stem) =>
        Convert.FromHexString(File.ReadAllText(Path.Combine(VectorDirectory, $"{stem}.signature.hex")).Trim());

    internal static SignedPackInput SignedPack(string stem, string? watermarkStem = null) =>
        new(
            Name: stem,
            ContentBytes: ReadContent(stem),
            ManifestArmour: ReadManifestArmour(stem),
            ManifestSignature: ReadSignature(stem),
            WatermarkArmour: watermarkStem is null ? null : ReadWatermarkArmour(watermarkStem),
            WatermarkSignature: watermarkStem is null ? null : ReadSignature(watermarkStem));

    /// <summary>Signs one content sequence in memory with the development manifest key.</summary>
    /// <param name="content">The exact content bytes.</param>
    /// <param name="name">The name the loader reports in errors.</param>
    /// <param name="keyId">The key identifier the manifest names.</param>
    /// <param name="seedKeyId">The key whose seed signs the payload, when it differs from the named key.</param>
    internal static SignedPackInput SignInMemory(byte[] content, string name, string keyId = SigningKeyId, string? seedKeyId = null)
    {
        using var document = JsonDocument.Parse(content);
        var packId = document.RootElement.GetProperty("id").GetString()!;
        var version = document.RootElement.GetProperty("version").GetString()!;
        var sha256 = Convert.ToHexStringLower(SHA256.HashData(content));
        var sha512 = Convert.ToHexStringLower(SHA512.HashData(content));
        var payload = Encoding.UTF8.GetBytes(
            $"{{\"keyId\":\"{keyId}\",\"packId\":\"{packId}\",\"version\":\"{version}\",\"contentHashes\":{{\"sha256\":\"{sha256}\",\"sha512\":\"{sha512}\"}}}}");
        var signature = Sign(payload, seedKeyId ?? keyId);
        var armour = $"-----BEGIN GATE-KEEPER RULE PACK MANIFEST-----\nKey-Id: {keyId}\n{Encoding.UTF8.GetString(payload)}\n-----END GATE-KEEPER RULE PACK MANIFEST-----\n";
        return new SignedPackInput(name, content, armour, signature);
    }

    /// <summary>Signs a detached watermark for the given content with the development watermark key.</summary>
    internal static (string Armour, byte[] Signature) SignWatermarkInMemory(byte[] content, string keyId = WatermarkKeyId)
    {
        var digest = Convert.ToHexStringLower(SHA256.HashData(content));
        var payload = Encoding.UTF8.GetBytes(
            $"{{\"keyId\":\"{keyId}\",\"contentDigest\":\"{digest}\",\"organisationName\":\"SYNTHETIC-ORGANISATION\",\"licenceId\":\"LIC-0000000\",\"generatedAt\":\"2027-01-01T00:00:00Z\"}}");
        var signature = Sign(payload, keyId);
        var armour = $"-----BEGIN GATE-KEEPER RULE PACK WATERMARK-----\nKey-Id: {keyId}\n{Encoding.UTF8.GetString(payload)}\n-----END GATE-KEEPER RULE PACK WATERMARK-----\n";
        return (armour, signature);
    }

    /// <summary>Writes a public-only key catalogue, as an installation would embed it.</summary>
    internal static string WritePublicCatalogue(string targetPath)
    {
        using var document = JsonDocument.Parse(File.ReadAllBytes(CataloguePath));
        var builder = new StringBuilder("{\n  \"keys\": [\n");
        var first = true;
        foreach (var key in document.RootElement.GetProperty("keys").EnumerateArray())
        {
            if (!first)
            {
                builder.Append(",\n");
            }

            first = false;
            builder.Append("    {\n      \"keyId\": \"").Append(key.GetProperty("keyId").GetString())
                .Append("\",\n      \"purpose\": \"").Append(key.GetProperty("purpose").GetString())
                .Append("\",\n      \"publicKeyHex\": \"").Append(key.GetProperty("publicKeyHex").GetString())
                .Append("\"\n    }");
        }

        builder.Append("\n  ]\n}\n");
        File.WriteAllText(targetPath, builder.ToString(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        return targetPath;
    }

    internal static DevelopmentCatalogue Catalogue { get; } = DevelopmentCatalogue.Load();

    private static byte[] Sign(byte[] payload, string keyId)
    {
        var seed = ReadSeed(keyId);
        var signer = new Ed25519Signer();
        signer.Init(true, new Ed25519PrivateKeyParameters(seed, 0));
        signer.BlockUpdate(payload, 0, payload.Length);
        return signer.GenerateSignature();
    }

    private static byte[] ReadSeed(string keyId)
    {
        using var document = JsonDocument.Parse(File.ReadAllBytes(CataloguePath));
        var key = document.RootElement.GetProperty("keys").EnumerateArray()
            .Single(item => item.GetProperty("keyId").GetString() == keyId);
        key.GetProperty("label").GetString().Should().Be("development-only");
        return Convert.FromHexString(key.GetProperty("seedHex").GetString()!);
    }

    /// <summary>The development-only catalogue the tests verify against.</summary>
    internal sealed class DevelopmentCatalogue : IPackKeyCatalogue
    {
        private readonly IReadOnlyDictionary<string, byte[]> packKeys;
        private readonly IReadOnlyDictionary<string, byte[]> watermarkKeys;

        private DevelopmentCatalogue(
            IReadOnlyDictionary<string, byte[]> packKeys,
            IReadOnlyDictionary<string, byte[]> watermarkKeys)
        {
            this.packKeys = packKeys;
            this.watermarkKeys = watermarkKeys;
        }

        internal static DevelopmentCatalogue Load()
        {
            using var document = JsonDocument.Parse(File.ReadAllBytes(CataloguePath));
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

            return new DevelopmentCatalogue(packKeys, watermarkKeys);
        }

        public bool TryGetPackSigningKey(string keyId, out byte[] publicKey) =>
            TryGet(packKeys, keyId, out publicKey);

        public bool TryGetWatermarkKey(string keyId, out byte[] publicKey) =>
            TryGet(watermarkKeys, keyId, out publicKey);

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
}

/// <summary>A temporary directory that removes itself after the test.</summary>
internal sealed class TempDirectory : IDisposable
{
    internal TempDirectory()
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "gatekeeper-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path);
    }

    internal string Path { get; }

    public void Dispose()
    {
        if (Directory.Exists(Path))
        {
            Directory.Delete(Path, recursive: true);
        }
    }
}
