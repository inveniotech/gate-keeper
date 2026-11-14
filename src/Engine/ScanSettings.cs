// SPDX-License-Identifier: Apache-2.0

namespace InvenioTech.GateKeeper.Engine;

/// <summary>
/// The effective settings of one scan, resolved from
/// <c>.gatekeeper/gatekeeper.toml</c> (configuration specification, section 4).
/// The record carries every key the specification lists, with the
/// profile-dependent defaults (IQ-23) and the
/// <c>scan</c>/<c>baseline</c> floor and ceiling rules already applied. Flags
/// for a single run (<c>--profile</c> and the rest) override this record's
/// values in the scan orchestrator; the file itself is never rewritten.
///
/// The record never carries a matched value and never carries the fingerprint
/// key (rule 10).
/// </summary>
/// <param name="Profile">The effective profile from <c>scan.profile</c>, defaulting to <see cref="ScanProfile.Default"/>.</param>
/// <param name="ScanHistory">Whether history is scanned: the explicit <c>scan.history</c> key when present, otherwise the profile's default (IQ-83).</param>
/// <param name="MaxFileBytes">The file-size limit: the explicit <c>scan.maxFileBytes</c> key when present, otherwise the profile's default, in bytes.</param>
/// <param name="Exclude">The additional <c>scan.exclude</c> globs. Built-in exclusions still apply; this list adds to them and can never remove them.</param>
/// <param name="IncludeSubmodules">Whether <c>scan.includeSubmodules</c> enumerates checked-out submodules.</param>
/// <param name="RepositoryId">The <c>repository.id</c> value, or null when the key is omitted and the scan must derive the identity from the git remote (IQ-29).</param>
/// <param name="BaselineMaxAgeDays">The <c>baseline.maxAgeDays</c> age cap in civil days; 0 means a baseline entry does not expire by age.</param>
/// <param name="FailOn">The <c>fail-on</c> threshold: the lowest severity that fails the scan.</param>
/// <param name="SeverityOverrides">The <c>severity.overrides</c> table, mapping a rule identifier to the severity it reports at.</param>
/// <param name="DisabledRules">The <c>rules.disabled</c> entries, each carrying the written reason IQ-24 requires.</param>
/// <param name="LocalPacks">The <c>packs.local</c> paths to unsigned local packs, relative to the repository root.</param>
public sealed record ScanSettings(
    ScanProfile Profile,
    bool ScanHistory,
    int MaxFileBytes,
    IReadOnlyList<string> Exclude,
    bool IncludeSubmodules,
    string? RepositoryId,
    int BaselineMaxAgeDays,
    Severity FailOn,
    IReadOnlyDictionary<string, Severity> SeverityOverrides,
    IReadOnlyList<DisabledRule> DisabledRules,
    IReadOnlyList<string> LocalPacks);
