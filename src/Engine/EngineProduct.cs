// SPDX-License-Identifier: Apache-2.0

namespace InvenioTech.GateKeeper.Engine;

/// <summary>
/// The facts every artefact stamps (NFR-031) while Phase 1 has no release
/// pipeline. The version numbers are constants rather than assembly metadata so
/// that a stamp is the same bytes everywhere it is written without reflection.
/// </summary>
public static class EngineProduct
{
    /// <summary>
    /// Gets the product name written into evidence files. The results format
    /// fixes this value as <c>gatekeeper</c>.
    /// </summary>
    public const string Product = "gatekeeper";

    /// <summary>
    /// Gets the engine version as a semantic version <c>major.minor.patch</c>,
    /// which is the form the results format fixes for the engine version. New
    /// features land only in a new major (rule 13), and before the first
    /// release every capability lands as a minor of the 0 line: 0.1.0 was
    /// Phase 1 and 0.2.0 carries the email match boundary (IQ-91).
    /// </summary>
    public const string EngineVersion = "0.2.0";

    /// <summary>
    /// Gets the version of the rule pack bundled into this build, or null when
    /// the build does not bundle a pack. Bundling arrives with the pack build
    /// and signing step, so the version command states the absence rather than
    /// inventing a number. This is a field rather than a constant so callers
    /// can branch on it without the compiler treating it as a constant null
    /// and flagging the other branch as unreachable.
    /// </summary>
    public static readonly string? BundledPackVersion = null;
}
