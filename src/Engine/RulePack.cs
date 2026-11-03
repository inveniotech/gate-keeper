// SPDX-License-Identifier: Apache-2.0

using System.Text.Json.Serialization;

namespace InvenioTech.GateKeeper.Engine;

/// <summary>
/// One rule pack: the signed content document the rule-pack format defines
/// (section 3), as loaded data. When two copies of the same pack identifier
/// are loaded, the greater version wins as a whole (IQ-24).
/// </summary>
/// <param name="SchemaVersion">The content-document schema version, which is 1 for version 1.</param>
/// <param name="Id">The pack identifier.</param>
/// <param name="Version">The version text: a calendar version for an Invenio pack, or the text of the positive integer for a local pack. The loader decides the exact forms (IQ-25).</param>
/// <param name="Provenance">Where the pack came from.</param>
/// <param name="MinEngineVersion">The oldest engine that may load this pack, as a semantic version.</param>
/// <param name="Rules">The rules the pack carries, in the order the content document lists them.</param>
/// <param name="CoverageDate">The civil date coverage checks against, for an Invenio pack only (IQ-36).</param>
/// <param name="ContentIssuedAt">The civil date the rule content was issued, for an Invenio pack only.</param>
/// <param name="BaseVersion">The correction line's version for an Invenio correction pack.</param>
public sealed record RulePack(
    [property: JsonPropertyName("schemaVersion"), JsonPropertyOrder(0)]
    int SchemaVersion,
    [property: JsonPropertyName("id"), JsonPropertyOrder(1)]
    string Id,
    [property: JsonPropertyName("version"), JsonPropertyOrder(2)]
    string Version,
    [property: JsonPropertyName("provenance"), JsonPropertyOrder(3)]
    PackProvenance Provenance,
    [property: JsonPropertyName("minEngineVersion"), JsonPropertyOrder(4)]
    string MinEngineVersion,
    [property: JsonPropertyName("rules"), JsonPropertyOrder(5)]
    IReadOnlyList<Rule> Rules,
    [property: JsonPropertyName("coverageDate"), JsonPropertyOrder(6)]
    string? CoverageDate,
    [property: JsonPropertyName("contentIssuedAt"), JsonPropertyOrder(7)]
    string? ContentIssuedAt,
    [property: JsonPropertyName("baseVersion"), JsonPropertyOrder(8)]
    string? BaseVersion)
{
    /// <summary>
    /// Gets the schema version this type models, for loaders to check against.
    /// </summary>
    public const int SupportedSchemaVersion = 1;
}
