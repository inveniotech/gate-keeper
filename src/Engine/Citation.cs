// SPDX-License-Identifier: Apache-2.0

using System.Text.Json.Serialization;

namespace InvenioTech.GateKeeper.Engine;

/// <summary>
/// The citation a detection rule carries to the law or directive it enforces.
/// The title is required; the section strings come from the approved citation
/// register and are never invented by an agent (rule 9, IQ-57).
/// </summary>
/// <param name="Title">The human-readable citation title.</param>
/// <param name="NdpaSection">The Nigeria Data Protection Act section string, when the rule has one.</param>
/// <param name="GaidSection">The Nigeria Data Protection Act General Application and Implementation Directive section string, when the rule has one.</param>
public sealed record Citation(
    [property: JsonPropertyName("title"), JsonPropertyOrder(0)]
    string Title,
    [property: JsonPropertyName("ndpa"), JsonPropertyOrder(1)]
    string? NdpaSection,
    [property: JsonPropertyName("gaid"), JsonPropertyOrder(2)]
    string? GaidSection);
