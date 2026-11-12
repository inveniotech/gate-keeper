// SPDX-License-Identifier: Apache-2.0

using System.Text;
using System.Text.Json;
using InvenioTech.GateKeeper.Engine;

namespace InvenioTech.GateKeeper.Sample.AspNetCore;

/// <summary>
/// Reads the signed pack sequences an installation embeds and the installation's
/// public key catalogue. The catalogue must carry public keys only: a catalogue
/// that carries private key material is refused (rule 14).
/// </summary>
public static class SampleInstallation
{
    /// <summary>Reads the one signed pack the directory carries.</summary>
    /// <param name="packDirectory">The directory with the pack's release media files.</param>
    /// <param name="error">Receives the plain failure sentence.</param>
    /// <returns>The signed pack input, or null when the media is absent.</returns>
    public static SignedPackInput? ReadPack(string packDirectory, TextWriter error)
    {
        if (!Directory.Exists(packDirectory))
        {
            error.WriteLine(
                $"The pack directory '{packDirectory}' does not exist. Point the sample at the directory that " +
                "carries the signed pack's release media, then start it again.");
            return null;
        }

        var contentPath = Directory.GetFiles(packDirectory, "*.pack.json", SearchOption.TopDirectoryOnly)
            .OrderBy(path => path, StringComparer.Ordinal)
            .FirstOrDefault();
        if (contentPath is null)
        {
            error.WriteLine(
                "The pack directory carries no signed pack. Install the release media that carries a signed pack, " +
                "then start the sample again.");
            return null;
        }

        var stem = Path.GetFileName(contentPath)[..^".pack.json".Length];
        var manifestPath = Path.Combine(packDirectory, stem + ".manifest.armour");
        var signaturePath = Path.Combine(packDirectory, stem + ".signature.hex");
        if (!File.Exists(manifestPath) || !File.Exists(signaturePath))
        {
            error.WriteLine(
                $"The signed pack '{stem}' is incomplete: it needs '{stem}.manifest.armour' and " +
                $"'{stem}.signature.hex' beside its content file. Install the pack from its release media, then " +
                "start the sample again.");
            return null;
        }

        try
        {
            return new SignedPackInput(
                Name: stem,
                ContentBytes: File.ReadAllBytes(contentPath),
                ManifestArmour: File.ReadAllText(manifestPath),
                ManifestSignature: Convert.FromHexString(File.ReadAllText(signaturePath).Trim()));
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException or FormatException)
        {
            error.WriteLine(
                $"The signed pack '{stem}' could not be read. Check that the files are the release media's exact " +
                "bytes and that the process may read them, then start the sample again.");
            return null;
        }
    }

    /// <summary>Reads the installation's public-only key catalogue.</summary>
    /// <param name="keyFile">The catalogue file the installation owns.</param>
    /// <param name="error">Receives the plain failure sentence.</param>
    /// <returns>The key catalogue, or null when the file is absent or not public-only.</returns>
    public static IPackKeyCatalogue? ReadKeyCatalogue(string keyFile, TextWriter error)
    {
        if (!File.Exists(keyFile))
        {
            error.WriteLine(
                $"The key catalogue '{keyFile}' does not exist. Give the sample the installation's public key " +
                "catalogue, then start it again.");
            return null;
        }

        byte[] catalogueBytes;
        try
        {
            catalogueBytes = File.ReadAllBytes(keyFile);
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
        {
            error.WriteLine(
                $"The key catalogue '{keyFile}' could not be read. Check that the process may read it, then start " +
                "the sample again.");
            return null;
        }

        var packKeys = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        var watermarkKeys = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        try
        {
            using var document = JsonDocument.Parse(catalogueBytes);
            foreach (var key in document.RootElement.GetProperty("keys").EnumerateArray())
            {
                if (key.TryGetProperty("seedHex", out _))
                {
                    error.WriteLine(
                        $"The key catalogue '{keyFile}' carries private key material. An installation embeds public " +
                        "keys only; remove the catalogue entry that carries a seed, then start the sample again.");
                    return null;
                }

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
        }
        catch (Exception failure) when (failure is JsonException or KeyNotFoundException or InvalidOperationException or FormatException)
        {
            error.WriteLine(
                $"The key catalogue '{keyFile}' is not the installation's public key catalogue. Restore the file " +
                "the installation owns, then start the sample again.");
            return null;
        }

        if (packKeys.Count == 0)
        {
            error.WriteLine(
                $"The key catalogue '{keyFile}' carries no pack-signing key. An installation embeds the public " +
                "keys its packs verify against; add the pack-signing key, then start the sample again.");
            return null;
        }

        return new SampleKeyCatalogue(packKeys, watermarkKeys);
    }

    private sealed class SampleKeyCatalogue(
        IReadOnlyDictionary<string, byte[]> packKeys,
        IReadOnlyDictionary<string, byte[]> watermarkKeys) : IPackKeyCatalogue
    {
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
