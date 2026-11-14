// SPDX-License-Identifier: Apache-2.0

using System.Text.Json.Serialization;

namespace InvenioTech.GateKeeper.Engine;

/// <summary>
/// One reported finding on the work tree (the results format, section 5.3) or
/// in a history scan. The matched value is never a property of a finding:
/// outputs carry the rule identifier and the location only (rule 10).
/// </summary>
/// <param name="RuleId">The rule identifier that produced the finding.</param>
/// <param name="Severity">The severity after overrides and after a severity-bearing exception.</param>
/// <param name="Category">The rule's category.</param>
/// <param name="Path">The repository-relative path with forward-slash separators.</param>
/// <param name="Line">The 1-based line.</param>
/// <param name="Column">The 1-based column.</param>
/// <param name="LocationKind">The location kind from section 6 of the results format.</param>
/// <param name="Fingerprint">The <c>gatekeeper/v1</c> fingerprint as lowercase hexadecimal text, or null when no fingerprint key was available (IQ-86).</param>
/// <param name="BaselineState">Whether the baseline held the finding back.</param>
/// <param name="Citation">The citation of the rule that produced the finding.</param>
public sealed record Finding(
    [property: JsonPropertyName("ruleId"), JsonPropertyOrder(0)]
    string RuleId,
    [property: JsonPropertyName("severity"), JsonPropertyOrder(1)]
    Severity Severity,
    [property: JsonPropertyName("category"), JsonPropertyOrder(2)]
    string Category,
    [property: JsonPropertyName("path"), JsonPropertyOrder(3)]
    string Path,
    [property: JsonPropertyName("line"), JsonPropertyOrder(4)]
    int Line,
    [property: JsonPropertyName("column"), JsonPropertyOrder(5)]
    int Column,
    [property: JsonPropertyName("locationKind"), JsonPropertyOrder(6)]
    LocationKind LocationKind,
    [property: JsonPropertyName("fingerprint"), JsonPropertyOrder(7)]
    string? Fingerprint,
    [property: JsonPropertyName("baselineState"), JsonPropertyOrder(8)]
    BaselineState BaselineState,
    [property: JsonPropertyName("citation"), JsonPropertyOrder(9)]
    Citation Citation)
{
    /// <summary>Gets the effective rule catalogue metadata, without matched content.</summary>
    [System.Text.Json.Serialization.JsonPropertyName("processor"), System.Text.Json.Serialization.JsonPropertyOrder(10)]
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public ProcessorInfo? Processor { get; init; }
}
