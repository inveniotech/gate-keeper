// SPDX-License-Identifier: Apache-2.0

using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using InvenioTech.GateKeeper.Signing;

namespace InvenioTech.GateKeeper.Engine;

/// <summary>
/// One candidate pack the loader reads before the merge (IQ-24).
/// </summary>
/// <param name="Name">The input name used in every error.</param>
/// <param name="Id">The pack identifier the content carries.</param>
/// <param name="VersionText">The pack version text.</param>
/// <param name="Version">The parsed version the merge compares by.</param>
/// <param name="Provenance">The provenance the content declares.</param>
/// <param name="ContentDigest">The lowercase-hex SHA-256 of the content bytes (IQ-80).</param>
/// <param name="Rules">The rules the content carries, in document order.</param>
/// <param name="CoverageDate">The coverage date retained for evidence stamps.</param>
internal sealed record PackCandidate(
    string Name,
    string Id,
    string VersionText,
    PackVersionValue Version,
    PackProvenance Provenance,
    string ContentDigest,
    IReadOnlyList<Rule> Rules,
    string? CoverageDate);

/// <summary>
/// The one entry point that turns pack bytes into the merged rule set the
/// matcher engine scans with (card P1-03, IQ-24, IQ-25, IQ-36). A load is
/// loud: any pack that does not verify or does not parse raises
/// <see cref="EngineToolException"/> with a complete en-GB sentence naming the
/// pack and the reason, and the scan orchestrator turns that into exit code 2
/// — a tampered pack is never the quiet degradation a licence problem is.
///
/// The order the checks run is the order the pack format sells them in
/// (section 2): the signature over the exact manifest payload bytes, then the
/// content digests, then the content parse and validation, then the pack-line
/// facts, then the entitlement, then the merge. Nothing is re-serialised on
/// the way (IQ-07).
/// </summary>
public static class PackLoader
{
    /// <summary>
    /// Loads the packs of one scan: the signed packs the installation embeds
    /// and the local packs the repository configuration lists (IQ-25), then
    /// merges them under IQ-24.
    /// </summary>
    /// <param name="signedPacks">The signed Invenio packs to load, in load order.</param>
    /// <param name="localPacks">The unsigned local packs to load, in load order.</param>
    /// <param name="keyCatalogue">The embedded keys the signatures verify against.</param>
    /// <param name="entitlementPolicy">The admission policy the embedded packs pass (IQ-10).</param>
    /// <returns>The merged rule set, with every pack's provenance recorded.</returns>
    public static MergedRules Load(
        IReadOnlyList<SignedPackInput> signedPacks,
        IReadOnlyList<LocalPackInput> localPacks,
        IPackKeyCatalogue keyCatalogue,
        IPackEntitlementPolicy entitlementPolicy)
    {
        var candidates = new List<PackCandidate>();
        foreach (var signed in signedPacks)
        {
            candidates.Add(LoadSigned(signed, keyCatalogue, entitlementPolicy));
        }

        foreach (var local in localPacks)
        {
            candidates.Add(LoadLocal(local));
        }

        return Merge(candidates);
    }

    /// <summary>
    /// Loads one signed pack: armour, manifest, signature, digests, content,
    /// the pack-line facts and the entitlement policy (IQ-10).
    /// </summary>
    private static PackCandidate LoadSigned(
        SignedPackInput input,
        IPackKeyCatalogue keyCatalogue,
        IPackEntitlementPolicy entitlementPolicy)
    {
        var (headerKeyId, manifestPayload) = PackArmourReader.Split(input.Name, input.ManifestArmour, watermark: false);
        var manifest = PackArmourReader.ParseManifest(input.Name, manifestPayload);
        if (!headerKeyId.Equals(manifest.KeyId, StringComparison.Ordinal))
        {
            throw new EngineToolException(
                $"The manifest of the signed pack '{input.Name}' names the key '{headerKeyId}' in its armour header " +
                $"and '{manifest.KeyId}' in its payload line. The two key identifiers must agree before anything " +
                "verifies (IQ-07): a pack whose armour and payload disagree is either corrupt or forged, so nothing " +
                "loads it. Rebuild the pack so both lines name the same key.");
        }

        if (!keyCatalogue.TryGetPackSigningKey(manifest.KeyId, out var publicKey))
        {
            throw new EngineToolException(
                $"The manifest of the signed pack '{input.Name}' names the key identifier '{manifest.KeyId}', which " +
                "this installation's key catalogue does not carry. A pack signed by a key this build never approved " +
                "cannot be trusted: remove the pack, or use release media that carries packages signed by the keys " +
                "this build knows.");
        }

        if (input.ManifestSignature.Length != 64)
        {
            throw new EngineToolException(
                $"The signature of the pack '{input.Name}' is {input.ManifestSignature.Length} bytes, but an Ed25519 " +
                "signature is 64. Nothing re-encodes before verifying (IQ-07); re-export the pack's signature.");
        }

        if (!SignatureVerifiers.Default.Verify(publicKey.AsSpan(), manifestPayload.AsSpan(), input.ManifestSignature.AsSpan()))
        {
            throw new EngineToolException(
                $"The manifest of the signed pack '{input.Name}' does not verify: its signature is not what the key " +
                $"'{manifest.KeyId}' created over the exact payload line. The pack is tampered or forged, so nothing " +
                "trusts its rules; reinstall the pack from the release media.");
        }

        var sha256Hex = Convert.ToHexStringLower(SHA256.HashData(input.ContentBytes));
        var sha512Hex = Convert.ToHexStringLower(SHA512.HashData(input.ContentBytes));
        if (!sha256Hex.Equals(manifest.Sha256Hex, StringComparison.Ordinal) || !sha512Hex.Equals(manifest.Sha512Hex, StringComparison.Ordinal))
        {
            throw new EngineToolException(
                $"The signed pack '{input.Name}' cannot be loaded: the SHA-256 digest of its content bytes is " +
                $"'{sha256Hex}', which does not match the '{manifest.Sha256Hex}' its manifest records (and the " +
                "SHA-512 agrees with neither). One of the two copies was altered after signing: reinstall the pack " +
                "from the release media it came from.");
        }

        var content = PackContentReader.ReadSigned(input.Name, input.ContentBytes);
        var rulePack = content.Pack;

        if (!manifest.PackId.Equals(rulePack.Id, StringComparison.Ordinal))
        {
            throw new EngineToolException(
                $"The manifest of the signed pack '{input.Name}' names the pack '{manifest.PackId}' but its content " +
                $"document declares '{rulePack.Id}'. The manifest speaks for the content, so the two identifiers must " +
                "be equal (section 2.2).");
        }

        if (!manifest.VersionText.Equals(rulePack.Version, StringComparison.Ordinal))
        {
            throw new EngineToolException(
                $"The manifest of the signed pack '{input.Name}' names the version '{manifest.VersionText}' but its " +
                $"content document declares '{rulePack.Version}'. The manifest speaks for the content, so the two " +
                "versions must be equal (section 2.2).");
        }

        if ((input.WatermarkArmour is null) != (input.WatermarkSignature is null))
        {
            throw new EngineToolException(
                $"The pack '{input.Name}' arrives with its watermark armour and its signature as a pair, and one half " +
                "is missing. A watermark is checked whole (section 2.3); give the loader both or neither.");
        }

        if (input.WatermarkArmour is not null && input.WatermarkSignature is not null)
        {
            var (watermarkHeaderKeyId, watermarkPayload) = PackArmourReader.Split(input.Name, input.WatermarkArmour, watermark: true);
            var watermark = PackArmourReader.ParseWatermark(input.Name, watermarkPayload);
            if (!watermarkHeaderKeyId.Equals(watermark.KeyId, StringComparison.Ordinal))
            {
                throw new EngineToolException(
                    $"The watermark of the pack '{input.Name}' names the key '{watermarkHeaderKeyId}' in its armour " +
                    $"header and '{watermark.KeyId}' in its payload line. The two key identifiers must agree before " +
                    "anything verifies (IQ-07); regenerate the copy.");
            }

            if (!keyCatalogue.TryGetWatermarkKey(watermark.KeyId, out var watermarkKey))
            {
                throw new EngineToolException(
                    $"The watermark of the pack '{input.Name}' names the key identifier '{watermark.KeyId}', which " +
                    "this installation's key catalogue does not carry. A watermark signed by a key this build never " +
                    "approved cannot be trusted; remove the copy or use the media this build trusts.");
            }

            if (input.WatermarkSignature.Length != 64)
            {
                throw new EngineToolException(
                    $"The watermark signature of the pack '{input.Name}' is {input.WatermarkSignature.Length} bytes, " +
                    "but an Ed25519 signature is 64 (section 2.3). Re-export the copy's signature.");
            }

            if (!SignatureVerifiers.Default.Verify(watermarkKey.AsSpan(), watermarkPayload.AsSpan(), input.WatermarkSignature.AsSpan()))
            {
                throw new EngineToolException(
                    $"The watermark of the pack '{input.Name}' does not verify: its signature is not what the key " +
                    $"'{watermark.KeyId}' created over the exact payload line. The copy is tampered or belongs to an " +
                    "impostor, so nothing trusts it; re-download the pack from the release media.");
            }

            if (!watermark.ContentDigest.Equals(manifest.Sha256Hex, StringComparison.Ordinal))
            {
                throw new EngineToolException(
                    $"The watermark of the pack '{input.Name}' carries the content digest '{watermark.ContentDigest}', " +
                    $"which does not match the manifest's digest '{manifest.Sha256Hex}'. The copy was produced around " +
                    "different content, so it is inconsistent with what the pack key attested (section 2.3); discard " +
                    "the copy and re-download the pack.");
            }
        }

        CheckEngineVersion(input.Name, rulePack.MinEngineVersion);
        CheckPackLineFacts(input.Name, content);

        var entitlement = entitlementPolicy.Inspect(rulePack.Id, rulePack.Provenance);
        if (!entitlement.Admitted)
        {
            throw new EngineToolException(entitlement.RefusalMessage);
        }

        return new PackCandidate(
            Name: input.Name,
            Id: rulePack.Id,
            VersionText: rulePack.Version,
            Version: content.PackVersion,
            Provenance: rulePack.Provenance,
            ContentDigest: sha256Hex,
            Rules: rulePack.Rules,
            CoverageDate: rulePack.CoverageDate);
    }

    /// <summary>
    /// Loads one unsigned local pack (IQ-25): the same content checks as the
    /// signed path, minus the signature, manifest and entitlement stages.
    /// </summary>
    private static PackCandidate LoadLocal(LocalPackInput input)
    {
        var content = PackContentReader.ReadLocal(input.Name, input.ContentBytes);
        var rulePack = content.Pack;
        CheckPackLineFacts(input.Name, content);

        return new PackCandidate(
            Name: input.Name,
            Id: rulePack.Id,
            VersionText: rulePack.Version,
            Version: content.PackVersion,
            Provenance: rulePack.Provenance,
            ContentDigest: Convert.ToHexStringLower(SHA256.HashData(input.ContentBytes)),
            Rules: rulePack.Rules,
            CoverageDate: rulePack.CoverageDate);
    }

    /// <summary>Refuses a pack whose minimum engine version is newer than this build (IQ-24).</summary>
    private static void CheckEngineVersion(string packName, string minEngineVersion)
    {
        if (!TryParseSemanticVersion(EngineProduct.EngineVersion, out var engineVersion)
            || !TryParseSemanticVersion(minEngineVersion, out var packMin))
        {
            throw new InvalidOperationException("An engine version or a pack's minEngineVersion is not a semantic version.");
        }

        if (packMin.Major > engineVersion.Major
            || (packMin.Major == engineVersion.Major && packMin.Minor > engineVersion.Minor)
            || (packMin.Major == engineVersion.Major && packMin.Minor == engineVersion.Minor && packMin.Patch > engineVersion.Patch))
        {
            throw new EngineToolException(
                $"The rule pack '{packName}' needs an engine of version {minEngineVersion} or newer, and this build " +
                $"is {EngineProduct.EngineVersion}. Upgrade the CLI to a build at least as new as the pack, then rescan.");
        }
    }

    /// <summary>Parses the semantic version the minEngineVersion field and the engine version carry: three numeric components, with no leading zero beyond a plain 0.</summary>
    private static bool TryParseSemanticVersion(string text, out (int Major, int Minor, int Patch) version)
    {
        version = default;
        var parts = text.Split('.');
        if (parts.Length != 3)
        {
            return false;
        }

        var values = new int[3];
        for (var index = 0; index < parts.Length; index++)
        {
            var part = parts[index];
            if (part.Length == 0 || (part.Length > 1 && part[0] == '0')
                || !int.TryParse(part, NumberStyles.None, CultureInfo.InvariantCulture, out values[index]))
            {
                return false;
            }
        }

        version = (values[0], values[1], values[2]);
        return true;
    }

    /// <summary>Checks the pack-line facts are consistent (rule-pack format, section 9, IQ-36).</summary>
    private static void CheckPackLineFacts(string packName, PackContentLoaded content)
    {
        var rulePack = content.Pack;
        if (content.PackVersion.Calendar is { } calendar)
        {
            var patch = calendar.Patch;
            if (patch == 0 && rulePack.BaseVersion is { } openerBaseVersion)
            {
                throw new EngineToolException(
                    $"The rule pack '{packName}' opens its pack line (patch 0) and also carries baseVersion " +
                    $"'{openerBaseVersion}'. A line opener carries no base version: the claim belongs to a correction " +
                    "(section 9). Remove baseVersion or set the pack's patch above 0.");
            }

            if (patch > 0)
            {
                if (rulePack.BaseVersion is not { } baseVersionText)
                {
                    throw new EngineToolException(
                        $"The rule pack '{packName}' is a correction (version '{rulePack.Version}') but carries no " +
                        "baseVersion. A correction records the line it corrects, so the claim travels with the bytes " +
                        "(section 9): set baseVersion to the line's patch-0 version.");
                }

                if (!PackContentReader.TryParseCalendarVersion(baseVersionText, out var baseVersion)
                    || baseVersion.Year != calendar.Year
                    || baseVersion.Month != calendar.Month)
                {
                    throw new EngineToolException(
                        $"The rule pack '{packName}' sets baseVersion '{baseVersionText}' off the year and month of " +
                        $"its version '{rulePack.Version}'. A correction corrects a line of its own calendar (section 9): " +
                        "correct the base version.");
                }
            }

            if (!PackContentReader.TryParseCivilDate(rulePack.CoverageDate ?? string.Empty, out var coverageDate)
                || !PackContentReader.TryParseCivilDate(rulePack.ContentIssuedAt ?? string.Empty, out var issuedDate))
            {
                return;
            }

            if (patch == 0 && coverageDate != issuedDate)
            {
                throw new EngineToolException(
                    $"The rule pack '{packName}' opens its line with coverageDate '{rulePack.CoverageDate}' and " +
                    $"contentIssuedAt '{rulePack.ContentIssuedAt}', which differ. A line opener states the same " +
                    "civil date in both (section 9): make them equal.");
            }

            if (patch > 0 && coverageDate > issuedDate)
            {
                throw new EngineToolException(
                    $"The rule pack '{packName}' sets coverageDate '{rulePack.CoverageDate}' after contentIssuedAt " +
                    $"'{rulePack.ContentIssuedAt}' on its correction line. Coverage does not run ahead of the issue " +
                    "of the content it covers (section 9): correct one of the two dates.");
            }
        }
        else if (rulePack.BaseVersion is { } localBaseVersion)
        {
            throw new EngineToolException(
                $"The rule pack '{packName}' carries baseVersion '{localBaseVersion}', but a local pack carries " +
                "no baseVersion: it is not a pack line (IQ-36). Remove the field.");
        }
    }

    /// <summary>
    /// Compares two candidate versions for the merge (IQ-24): the calendar
    /// triple, compared as integers because a string sort would put 2027.9
    /// after 2027.10; or the local positive integer. Both candidates of one
    /// pack identifier carry the same version shape, so a mixed case cannot reach here.
    /// </summary>
    private static int CompareVersions(PackVersionValue left, PackVersionValue right)
    {
        if (left.Calendar is { } leftCalendar && right.Calendar is { } rightCalendar)
        {
            var yearCompare = leftCalendar.Year.CompareTo(rightCalendar.Year);
            if (yearCompare != 0)
            {
                return yearCompare;
            }

            var monthCompare = leftCalendar.Month.CompareTo(rightCalendar.Month);
            if (monthCompare != 0)
            {
                return monthCompare;
            }

            return leftCalendar.Patch.CompareTo(rightCalendar.Patch);
        }

        return left.LocalNumber.GetValueOrDefault().CompareTo(right.LocalNumber.GetValueOrDefault());
    }

    /// <summary>
    /// Merges the candidates under IQ-24: one pack identifier survives with its
    /// greatest version, as a whole; identical versions must carry identical
    /// content; a rule identifier two different packs claim is a load error.
    /// </summary>
    private static MergedRules Merge(IReadOnlyList<PackCandidate> candidates)
    {
        var winners = new Dictionary<string, PackCandidate>(StringComparer.Ordinal);
        foreach (var candidate in candidates)
        {
            if (winners.TryGetValue(candidate.Id, out var existing))
            {
                if (existing.Provenance is PackProvenance.Local != candidate.Provenance is PackProvenance.Local)
                {
                    var localName = existing.Provenance is PackProvenance.Local ? existing.Name : candidate.Name;
                    var signedName = existing.Provenance is PackProvenance.Local ? candidate.Name : existing.Name;
                    throw new EngineToolException(
                        $"The local pack '{localName}' uses the pack identifier '{candidate.Id}', which the Invenio " +
                        $"pack '{signedName}' already carries. A local pack cannot impersonate an Invenio pack (IQ-25): " +
                        "rename the local pack to an identifier of your own.");
                }

                var compare = CompareVersions(existing.Version, candidate.Version);
                if (compare < 0)
                {
                    winners[candidate.Id] = candidate;
                }
                else if (compare == 0 && !existing.ContentDigest.Equals(candidate.ContentDigest, StringComparison.Ordinal))
                {
                    throw new EngineToolException(
                        $"Two copies of the pack '{candidate.Id}' both carry version '{existing.VersionText}' but their " +
                        $"content digests differ ('{existing.ContentDigest}' and '{candidate.ContentDigest}'). Two copies " +
                        "of one release share one content digest (rule-pack format, section 2): check which copy is " +
                        "genuine and load only that one.");
                }
            }
            else
            {
                winners.Add(candidate.Id, candidate);
            }
        }

        var packsInOrder = winners.Values.OrderBy(pack => pack.Id, StringComparer.Ordinal).ToList();
        var packRecords = new Dictionary<string, LoadedPack>(StringComparer.Ordinal);
        foreach (var pack in packsInOrder)
        {
            packRecords.Add(pack.Id, new LoadedPack(pack.Id, pack.VersionText, pack.Provenance, pack.ContentDigest) { CoverageDate = pack.CoverageDate });
        }

        var rulesInOrder = new Dictionary<string, LoadedRule>(StringComparer.Ordinal);
        foreach (var pack in packsInOrder)
        {
            var packRecord = packRecords[pack.Id];
            foreach (var rule in pack.Rules)
            {
                if (rulesInOrder.TryGetValue(rule.Id, out var existingRule))
                {
                    throw new EngineToolException(
                        $"The rule identifier '{rule.Id}' appears in the packs '{existingRule.Pack.Id}' and " +
                        $"'{pack.Id}'. Different packs may not share a rule identifier (IQ-24): remove one of the " +
                        "packs, or give the rule of one of them its own identifier.");
                }

                rulesInOrder.Add(rule.Id, new LoadedRule(rule, packRecord));
            }
        }

        var packs = packRecords.Values.OrderBy(pack => pack.Id, StringComparer.Ordinal).ToList();
        var rules = rulesInOrder.Values.OrderBy(rule => rule.Rule.Id, StringComparer.Ordinal).ToList();
        return new MergedRules(Packs: packs, Rules: rules);
    }
}
