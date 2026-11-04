// SPDX-License-Identifier: Apache-2.0

namespace InvenioTech.GateKeeper.Engine;

/// <summary>
/// The inputs one load carries (card P1-03). A signed pack is the
/// specification's byte sequences — the content bytes, the manifest armour with
/// its signature over the exact payload line and, when the product requires
/// one, the detached watermark — so the loader never re-serialises anything it
/// checks (IQ-07). A local pack is the unsigned content bytes the repository
/// configuration names (IQ-25). The released-pack container that wraps the
/// sequences into one file arrives with the pack build step (P2-03), so these
/// records are the container-free contract.
/// </summary>
/// <param name="Name">The name to use in every load error, for example the path the pack came from.</param>
/// <param name="ContentBytes">The exact content bytes as shipped.</param>
/// <param name="ManifestArmour">The armoured manifest text.</param>
/// <param name="ManifestSignature">The 64-byte Ed25519 signature over the exact manifest payload line.</param>
/// <param name="WatermarkArmour">The detached watermark armour, or null when the product requires none.</param>
/// <param name="WatermarkSignature">The 64-byte signature over the watermark payload line, or null.</param>
public sealed record SignedPackInput(
    string Name,
    byte[] ContentBytes,
    string ManifestArmour,
    byte[] ManifestSignature,
    string? WatermarkArmour = null,
    byte[]? WatermarkSignature = null);

/// <summary>
/// One unsigned local pack the repository configuration lists (IQ-25). The
/// loader validates its content exactly like a signed pack's, minus the
/// signature stage, and refuses anything that claims a provenance other than
/// <c>local</c>.
/// </summary>
/// <param name="Name">The name to use in every load error, usually the configuration path.</param>
/// <param name="ContentBytes">The exact content bytes of the local pack.</param>
public sealed record LocalPackInput(string Name, byte[] ContentBytes);
