// SPDX-License-Identifier: Apache-2.0

using System.Text.Json.Serialization;

namespace InvenioTech.GateKeeper.Engine;

/// <summary>
/// The result of one scan: the findings reported from the work tree, in the
/// deterministic order the scan stage produced them (path, line, column, rule
/// identifier, ordinal). History findings travel separately because they are
/// not fingerprinted and not baselined (IQ-83); their type and list land with
/// the scan orchestrator's history scan.
/// </summary>
/// <param name="Findings">The reported work-tree findings, sorted deterministically.</param>
public sealed record ScanResult(
    [property: JsonPropertyName("findings"), JsonPropertyOrder(0)]
    IReadOnlyList<Finding> Findings);
