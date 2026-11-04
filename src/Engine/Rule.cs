// SPDX-License-Identifier: Apache-2.0

using System.Text.Json.Serialization;

namespace InvenioTech.GateKeeper.Engine;

/// <summary>
/// One detection rule as the rule-pack content document defines it (section 4),
/// with the matcher the pack loader (P1-03) parses it into. The matcher engine
/// (P1-06) executes the matcher; the loader never runs one, so a malformed
/// matcher still fails at load time, before any scan.
/// </summary>
/// <param name="Id">The dotted lowercase rule identifier, for example <c>pii.bvn.log</c>.</param>
/// <param name="Severity">The severity the pack gives the rule (IQ-22).</param>
/// <param name="Category">The free category string, so a new category stays data (NFR-027).</param>
/// <param name="Citation">The legal citation the rule enforces.</param>
/// <param name="SinceVersion">The pack version from which the rule takes effect.</param>
/// <param name="Redaction">The redaction behaviour, or null when the pack says nothing.</param>
/// <param name="Profiles">The profiles the rule runs in, or null for both <c>default</c> and <c>deep</c></param>
/// <param name="SuppressionHints">The plain help-text strings, or null when the pack carries none.</param>
/// <param name="Processor">The processor block, or null for rules that do not describe a processor.</param>
/// <param name="Matcher">The matcher the pack loader (P1-03) reads from the pack JSON: exactly one of a structured pattern or an exact list (rule-pack format, section 5).</param>
public sealed record Rule(
    [property: JsonPropertyName("id"), JsonPropertyOrder(0)]
    string Id,
    [property: JsonPropertyName("severity"), JsonPropertyOrder(1)]
    Severity Severity,
    [property: JsonPropertyName("category"), JsonPropertyOrder(2)]
    string Category,
    [property: JsonPropertyName("citation"), JsonPropertyOrder(3)]
    Citation Citation,
    [property: JsonPropertyName("sinceVersion"), JsonPropertyOrder(4)]
    string SinceVersion,
    [property: JsonPropertyName("redaction"), JsonPropertyOrder(5)]
    RedactionOptions? Redaction,
    [property: JsonPropertyName("profiles"), JsonPropertyOrder(6)]
    IReadOnlyList<string>? Profiles,
    [property: JsonPropertyName("suppressionHints"), JsonPropertyOrder(7)]
    IReadOnlyList<string>? SuppressionHints,
    [property: JsonPropertyName("processor"), JsonPropertyOrder(8)]
    ProcessorInfo? Processor,
    [property: JsonPropertyName("matcher"), JsonPropertyOrder(9)]
    RuleMatcher Matcher);
