// SPDX-License-Identifier: Apache-2.0

using System.Text.Json.Serialization;

namespace InvenioTech.GateKeeper.Engine;

/// <summary>
/// One structured-shape matcher with its boundary, its optional prefilter
/// keywords and window, its location restriction, its named validator and its
/// exclusions (rule-pack format, sections 5.1 and 7 to 8).
/// </summary>
/// <param name="Shape">The shape: 1 to 32 parts, at most one of which emits.</param>
/// <param name="Boundary">The boundary the engine applies around the shape.</param>
/// <param name="Validator">The named validator, or null for none.</param>
/// <param name="Keywords">The literal prefilter strings, or null to always run the shape.</param>
/// <param name="Window">The prefilter window in scalar values, at most 4,096, or null when the pack carries no keywords.</param>
/// <param name="Location">The location restriction, or null for every location.</param>
/// <param name="Exclusions">The named exclusions that void a match, or null.</param>
/// <param name="ExcludePrefixes">The exact prefixes that void a match, or null.</param>
/// <param name="ExcludeValues">The exact values that void a match, or null.</param>
public sealed record RulePatternMatcher(
    [property: JsonPropertyName("shape"), JsonPropertyOrder(0)]
    IReadOnlyList<RuleShapePart> Shape,
    [property: JsonPropertyName("boundary"), JsonPropertyOrder(1)]
    MatchBoundary Boundary,
    [property: JsonPropertyName("validator"), JsonPropertyOrder(2)]
    RuleValidator? Validator,
    [property: JsonPropertyName("keywords"), JsonPropertyOrder(3)]
    IReadOnlyList<string>? Keywords,
    [property: JsonPropertyName("window"), JsonPropertyOrder(4)]
    int? Window,
    [property: JsonPropertyName("location"), JsonPropertyOrder(5)]
    RuleLocation? Location,
    [property: JsonPropertyName("exclusions"), JsonPropertyOrder(6)]
    IReadOnlyList<RuleExclusion>? Exclusions,
    [property: JsonPropertyName("excludePrefixes"), JsonPropertyOrder(7)]
    IReadOnlyList<string>? ExcludePrefixes,
    [property: JsonPropertyName("excludeValues"), JsonPropertyOrder(8)]
    IReadOnlyList<string>? ExcludeValues);
