// SPDX-License-Identifier: Apache-2.0

namespace InvenioTech.GateKeeper.Engine;

/// <summary>
/// One key catalogue the pack loader consults for the public keys the
/// installation embeds (card P1-03). The catalogue maps a manifest or watermark
/// <c>keyId</c> to its 32-byte Ed25519 public key; an unknown key never verifies
/// a pack. Keys travel as bytes and are never logged or printed (rule 14); the
/// verifier interface stays the one in the signing module, and the catalogue is
/// the only place keys reach the loader from.
/// </summary>
public interface IPackKeyCatalogue
{
    /// <summary>
    /// Looks up the pack-signing public key for <paramref name="keyId"/>.
    /// </summary>
    /// <param name="keyId">The key identifier the manifest carries.</param>
    /// <param name="publicKey">The 32-byte Ed25519 public key, or empty when unknown.</param>
    /// <returns><see langword="true"/> when the catalogue carries the key.</returns>
    bool TryGetPackSigningKey(string keyId, out byte[] publicKey);

    /// <summary>
    /// Looks up the watermark public key for <paramref name="keyId"/>. The
    /// watermark block is checked with a different embedded public key to the
    /// pack-signing key (rule-pack format, section 2.3).
    /// </summary>
    /// <param name="keyId">The key identifier the watermark block carries.</param>
    /// <param name="publicKey">The 32-byte Ed25519 public key, or empty when unknown.</param>
    /// <returns><see langword="true"/> when the catalogue carries the key.</returns>
    bool TryGetWatermarkKey(string keyId, out byte[] publicKey);
}

/// <summary>
/// The decision an <see cref="IPackEntitlementPolicy"/> reaches for one pack:
/// whether the build may load it, and, when it may not, the complete en-GB
/// refusal sentence to show (IQ-10). The decision is data, so the Phase 5
/// licence-based policy plugs in without changing the loader.
/// </summary>
/// <param name="Admitted">Whether the pack may enter the load.</param>
/// <param name="RefusalMessage">The refusal sentence when <see cref="Admitted"/> is <see langword="false"/>.</param>
public readonly record struct PackEntitlementDecision(bool Admitted, string RefusalMessage)
{
    /// <summary>Creates the decision that admits the pack.</summary>
    public static PackEntitlementDecision Admit() => new(true, string.Empty);

    /// <summary>Creates the refusal decision with the sentence to show.</summary>
    /// <param name="refusalMessage">The complete en-GB refusal sentence.</param>
    public static PackEntitlementDecision Refuse(string refusalMessage) => new(false, refusalMessage);
}

/// <summary>
/// Decides whether a pack of a provenance may load (IQ-10). The owner-defined
/// decision keeps admission a product policy decision the loader cannot override:
/// local packs are the customer's own and never pass here, so the MVP
/// implementation admits every embedded pack whose content is bundled and
/// refuses the plan-gated ones the later licence-based implementation admits.
/// </summary>
public interface IPackEntitlementPolicy
{
    /// <summary>
    /// Inspects one pack that has verified as Invenio content.
    /// </summary>
    /// <param name="packId">The pack identifier the content declares.</param>
    /// <param name="provenance">The provenance the content declares.</param>
    /// <returns>The decision for the pack.</returns>
    PackEntitlementDecision Inspect(string packId, PackProvenance provenance);
}

/// <summary>
/// The MVP entitlement policy: bundled packs only (IQ-10). A plan-gated pack
/// needs a licence, and no verifier in this build covers it, so the refusal is
/// loud and says exactly what happens next; Phase 5 replaces this
/// implementation without changing the loader.
/// </summary>
public sealed class BundledPacksOnlyPolicy : IPackEntitlementPolicy
{
    /// <summary>Gets the policy instance every caller shares.</summary>
    public static BundledPacksOnlyPolicy Instance { get; } = new();

    private BundledPacksOnlyPolicy()
    {
    }

    /// <summary>Admits bundled content and refuses plan-gated content.</summary>
    public PackEntitlementDecision Inspect(string packId, PackProvenance provenance)
    {
        return provenance == PackProvenance.PlanGated
            ? PackEntitlementDecision.Refuse(
                $"The pack '{packId}' is plan-gated, and this build admits bundled packs only: loading a " +
                "plan-gated pack needs the licence verifier a later release carries (IQ-10). If your plan covers " +
                "this pack, run a build from the paid installation media; otherwise remove the pack from the media.")
            : PackEntitlementDecision.Admit();
    }
}
