// SPDX-License-Identifier: Apache-2.0

namespace InvenioTech.GateKeeper.Engine;

/// <summary>
/// Parts of a rule pack the loader asserts as load outcomes: the pack (with its
/// provenance and content digest) and the rules it still owns after merging
/// (IQ-24). Evidence stamps these later, so a record keeps bytes out of the
/// result: the digest is the only byte-derived fact the results format needs.
/// </summary>
/// <param name="Id">The pack identifier the content carries.</param>
/// <param name="Version">The pack version text: the calendar version for an Invenio pack, the positive integer's text for a local pack.</param>
/// <param name="Provenance">The provenance the content declares (IQ-25).</param>
/// <param name="ContentDigest">The lowercase-hex SHA-256 digest of the content bytes (IQ-80).</param>
public sealed record LoadedPack(
    string Id,
    string Version,
    PackProvenance Provenance,
    string ContentDigest)
{
    /// <summary>The coverage date of an Invenio pack, omitted for local packs (IQ-36).</summary>
    public string? CoverageDate { get; init; }

    /// <summary>The content issue date used for the deterministic age warning, omitted for local packs (IQ-26).</summary>
    public string? ContentIssuedAt { get; init; }
}

/// <summary>
/// One merged rule with the pack it came from, so the matcher engine and the
/// provenance column of the evidence file both read one source of truth.
/// </summary>
/// <param name="Rule">The rule as the content document carried it.</param>
/// <param name="Pack">The pack the rule belongs to.</param>
public sealed record LoadedRule(Rule Rule, LoadedPack Pack);

/// <summary>
/// The merged load outcome: every pack that survived the IQ-24 merge, in pack
/// identifier order, and every rule they own, in rule-identifier order. Both
/// orders are ordinal, so two identical loads give the same list in the same
/// order (rule 6), and nothing is left to insertion order.
/// </summary>
/// <param name="Packs">The packs, sorted by identifier.</param>
/// <param name="Rules">The rules, sorted by identifier, each with its pack.</param>
public sealed record MergedRules(
    IReadOnlyList<LoadedPack> Packs,
    IReadOnlyList<LoadedRule> Rules);
