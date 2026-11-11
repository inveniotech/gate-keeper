// SPDX-License-Identifier: Apache-2.0

namespace InvenioTech.GateKeeper.Engine;

/// <summary>
/// The outcome of loading <c>.gatekeeper/gatekeeper.toml</c>. Errors are tool
/// errors: what happened, why it matters and what to do next, naming the
/// settings file (rule 23). Warnings such as an unknown key never fail the
/// scan (IQ-17) and each names the file or key it refers to. When
/// <see cref="Settings"/> is null the scan cannot run.
/// </summary>
/// <param name="Settings">The effective settings, or null when a tool error stopped the load.</param>
/// <param name="Warnings">Warnings, sorted by the setting key they name, in ordinal order.</param>
/// <param name="Errors">Tool-error sentences, in the order the loader met them.</param>
public sealed record ConfigurationLoadResult(
    ScanSettings? Settings,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<string> Errors);
