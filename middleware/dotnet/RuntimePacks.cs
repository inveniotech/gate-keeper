// SPDX-License-Identifier: Apache-2.0

using System.Globalization;
using InvenioTech.GateKeeper.Engine;

namespace InvenioTech.GateKeeper.Middleware;

/// <summary>
/// Raised when the middleware cannot obtain a verified signed pack load, so it
/// must not start. The message names the pack and the recovery action and never
/// contains content bytes or matched values.
/// </summary>
public sealed class PackLoadException : Exception
{
    /// <summary>Creates a pack-load error with the complete recovery sentence.</summary>
    /// <param name="message">The complete en-GB sentence naming the pack and the recovery action.</param>
    public PackLoadException(string message) : base(message)
    {
    }

    /// <summary>Creates a pack-load error that retains the engine's content-free cause.</summary>
    /// <param name="message">The complete en-GB sentence naming the pack and the recovery action.</param>
    /// <param name="innerException">The engine failure that refused the load.</param>
    public PackLoadException(string message, Exception innerException) : base(message, innerException)
    {
    }
}

/// <summary>
/// The middleware's verified pack load (middleware API, section 9). It calls the
/// existing engine loader with an empty local-pack list and an admission policy
/// that admits bundled and plan-gated content without consulting a licence. The
/// engine keeps the signature, digest, schema and watermark checks; this type
/// only applies the replacement rules the engine merge does not implement.
/// </summary>
public static class RuntimePacks
{
    /// <summary>
    /// Loads the embedded packs and, when supplied, the signed replacement
    /// candidates. A candidate that does not verify is left out and the already
    /// verified embedded load stays active; an older or equal version, or a
    /// different pack identifier, refuses start-up.
    /// </summary>
    /// <param name="embedded">The signed packs the installation embeds, in load order.</param>
    /// <param name="keyCatalogue">The installation's embedded public keys.</param>
    /// <param name="replacements">The signed replacement candidates, or null for none.</param>
    /// <param name="diagnostic">Receives one metadata-only sentence for each rejected candidate.</param>
    /// <returns>The merged verified load, sorted by pack identifier.</returns>
    /// <exception cref="PackLoadException">No embedded pack verified, or a candidate breaks the replacement rules.</exception>
    public static MergedRules Load(
        IReadOnlyList<SignedPackInput> embedded,
        IPackKeyCatalogue keyCatalogue,
        IReadOnlyList<SignedPackInput>? replacements = null,
        Action<string>? diagnostic = null)
    {
        ArgumentNullException.ThrowIfNull(embedded);
        ArgumentNullException.ThrowIfNull(keyCatalogue);
        if (embedded.Count == 0)
        {
            throw new PackLoadException(
                "The middleware cannot start because no signed rule pack was supplied. Install the release media " +
                "that carries a signed pack, then start the application again.");
        }

        if (embedded.Any(item => item is null))
        {
            throw new ArgumentException("Every embedded pack must be a signed pack input.", nameof(embedded));
        }

        var policy = MiddlewarePackPolicy.Instance;
        MergedRules verified;
        try
        {
            verified = PackLoader.Load(embedded, [], keyCatalogue, policy);
        }
        catch (EngineToolException error)
        {
            throw new PackLoadException(
                "The middleware cannot start because a signed embedded rule pack did not verify. " + error.Message,
                error);
        }

        var accepted = new List<SignedPackInput>();
        foreach (var candidate in replacements ?? [])
        {
            ArgumentNullException.ThrowIfNull(candidate, nameof(replacements));
            MergedRules loaded;
            try
            {
                loaded = PackLoader.Load([candidate], [], keyCatalogue, policy);
            }
            catch (EngineToolException error)
            {
                diagnostic?.Invoke(
                    $"The signed replacement pack '{candidate.Name}' did not verify and was not loaded. {error.Message}");
                continue;
            }

            var pack = loaded.Packs.Single();
            var current = verified.Packs.FirstOrDefault(item => item.Id == pack.Id);
            if (current is null)
            {
                throw new PackLoadException(
                    $"The signed replacement pack '{candidate.Name}' carries the pack identifier '{pack.Id}', which " +
                    "is not one of the embedded identifiers. A replacement must carry the same identifier as the " +
                    "embedded pack it replaces; remove it, or install it through the release media that owns its " +
                    "identifier.");
            }

            var candidateVersion = ParseCalendarVersion(pack.Version, candidate.Name);
            var currentVersion = ParseCalendarVersion(current.Version, current.Id);
            if (Compare(candidateVersion, currentVersion) <= 0)
            {
                throw new PackLoadException(
                    $"The signed replacement pack '{candidate.Name}' carries version '{pack.Version}', which is not " +
                    $"newer than the embedded pack '{current.Id}' version '{current.Version}'. A replacement must " +
                    "carry a strictly greater calendar version; remove it, or install the newer release media.");
            }

            accepted.Add(candidate);
        }

        if (accepted.Count == 0)
        {
            return verified;
        }

        try
        {
            return PackLoader.Load([.. embedded, .. accepted], [], keyCatalogue, policy);
        }
        catch (EngineToolException error)
        {
            throw new PackLoadException(
                "The middleware cannot start because the verified replacement packs do not merge with the embedded " +
                "packs. " + error.Message,
                error);
        }
    }

    /// <summary>
    /// Reads the signed replacement sequences from one directory and applies the
    /// same rules as <see cref="Load"/>. Each <c>*.pack.json</c> file pairs with
    /// <c>*.manifest.armour</c> and <c>*.signature.hex</c>, plus the optional
    /// <c>*.watermark.armour</c> and <c>*.watermark.signature.hex</c> pair. No
    /// new container is invented: the files are the sequences the engine reads.
    /// </summary>
    /// <param name="embedded">The signed packs the installation embeds, in load order.</param>
    /// <param name="keyCatalogue">The installation's embedded public keys.</param>
    /// <param name="replacementDirectory">The directory that carries the signed replacement sequences.</param>
    /// <param name="diagnostic">Receives one metadata-only sentence for each rejected candidate.</param>
    /// <returns>The merged verified load, sorted by pack identifier.</returns>
    /// <exception cref="PackLoadException">The directory is absent or no embedded pack verified.</exception>
    public static MergedRules LoadFromDirectory(
        IReadOnlyList<SignedPackInput> embedded,
        IPackKeyCatalogue keyCatalogue,
        string replacementDirectory,
        Action<string>? diagnostic = null)
    {
        ArgumentNullException.ThrowIfNull(replacementDirectory);
        if (!Directory.Exists(replacementDirectory))
        {
            throw new PackLoadException(
                $"The replacement pack directory '{replacementDirectory}' does not exist. Create it, or remove the " +
                "replacement pack setting, then start the application again.");
        }

        var candidates = new List<SignedPackInput>();
        foreach (var contentPath in Directory.GetFiles(replacementDirectory, "*.pack.json", SearchOption.TopDirectoryOnly)
            .OrderBy(path => path, StringComparer.Ordinal))
        {
            var stem = Path.GetFileName(contentPath)[..^".pack.json".Length];
            try
            {
                candidates.Add(ReadSequence(replacementDirectory, stem));
            }
            catch (PackLoadException error)
            {
                diagnostic?.Invoke(error.Message);
            }
        }

        return Load(embedded, keyCatalogue, candidates, diagnostic);
    }

    private static SignedPackInput ReadSequence(string directory, string stem)
    {
        var manifestPath = Path.Combine(directory, stem + ".manifest.armour");
        var signaturePath = Path.Combine(directory, stem + ".signature.hex");
        if (!File.Exists(manifestPath) || !File.Exists(signaturePath))
        {
            throw new PackLoadException(
                $"The signed pack '{stem}' is incomplete: it needs '{Path.GetFileName(manifestPath)}' and " +
                $"'{Path.GetFileName(signaturePath)}' beside its content file. Install the pack from its release " +
                "media, or remove it.");
        }

        var watermarkArmourPath = Path.Combine(directory, stem + ".watermark.armour");
        var watermarkSignaturePath = Path.Combine(directory, stem + ".watermark.signature.hex");
        if (File.Exists(watermarkArmourPath) != File.Exists(watermarkSignaturePath))
        {
            throw new PackLoadException(
                $"The signed pack '{stem}' carries only one half of its detached watermark. Supply both " +
                $"'{Path.GetFileName(watermarkArmourPath)}' and '{Path.GetFileName(watermarkSignaturePath)}', or " +
                "remove both.");
        }

        try
        {
            var signature = Convert.FromHexString(File.ReadAllText(signaturePath).Trim());
            var watermarkSignature = File.Exists(watermarkSignaturePath)
                ? Convert.FromHexString(File.ReadAllText(watermarkSignaturePath).Trim())
                : null;
            return new SignedPackInput(
                Name: stem,
                ContentBytes: File.ReadAllBytes(Path.Combine(directory, stem + ".pack.json")),
                ManifestArmour: File.ReadAllText(manifestPath),
                ManifestSignature: signature,
                WatermarkArmour: File.Exists(watermarkArmourPath) ? File.ReadAllText(watermarkArmourPath) : null,
                WatermarkSignature: watermarkSignature);
        }
        catch (FormatException)
        {
            throw new PackLoadException(
                $"The signature of the signed pack '{stem}' is not hexadecimal text. Reinstall the pack from its " +
                "release media, or remove it.");
        }
        catch (IOException error)
        {
            throw new PackLoadException(
                $"The signed pack '{stem}' could not be read from '{directory}'. Check that the process may read the " +
                "pack files, then start the application again.", error);
        }
        catch (UnauthorizedAccessException error)
        {
            throw new PackLoadException(
                $"The signed pack '{stem}' could not be read from '{directory}'. Check that the process may read the " +
                "pack files, then start the application again.", error);
        }
    }

    private static (int Year, int Month, int Patch) ParseCalendarVersion(string version, string packName)
    {
        var parts = version.Split('.');
        if (parts.Length == 3
            && int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var year)
            && int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var month)
            && int.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out var patch))
        {
            return (year, month, patch);
        }

        throw new PackLoadException(
            $"The signed pack '{packName}' carries version '{version}', which is not a YYYY.MM.P calendar triple. " +
            "Reinstall the pack from its release media, then start the application again.");
    }

    private static int Compare((int Year, int Month, int Patch) left, (int Year, int Month, int Patch) right)
    {
        var year = left.Year.CompareTo(right.Year);
        if (year != 0)
        {
            return year;
        }

        var month = left.Month.CompareTo(right.Month);
        return month != 0 ? month : left.Patch.CompareTo(right.Patch);
    }

    /// <summary>
    /// Admits embedded content of either signed provenance and never consults a
    /// licence (middleware API, section 9). A local pack cannot reach this path
    /// through the engine's signed reader, and is refused if it ever does.
    /// </summary>
    private sealed class MiddlewarePackPolicy : IPackEntitlementPolicy
    {
        internal static MiddlewarePackPolicy Instance { get; } = new();

        private MiddlewarePackPolicy()
        {
        }

        public PackEntitlementDecision Inspect(string packId, PackProvenance provenance) =>
            provenance is PackProvenance.Bundled or PackProvenance.PlanGated
                ? PackEntitlementDecision.Admit()
                : PackEntitlementDecision.Refuse(
                    $"The pack '{packId}' declares the provenance '{provenance}', which the middleware never loads " +
                    "from a signed pack. Remove the pack, or install it through the release media that owns it.");
    }
}
