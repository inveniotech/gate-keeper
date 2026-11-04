// SPDX-License-Identifier: Apache-2.0

using System.Text.Json.Serialization;

namespace InvenioTech.GateKeeper.Engine;

/// <summary>
/// The boundary an every pattern matcher applies around its shape (rule-pack
/// format, section 5.1). The boundary is a field the engine applies around the
/// shape, not a look-around inside one: the shape language has no place to
/// write a zero-width assertion.
/// </summary>
public enum MatchBoundary
{
    /// <summary>The shape may touch any neighbouring scalar value.</summary>
    [JsonStringEnumMemberName("none")]
    None = 0,

    /// <summary>A digit neighbour or the very end (or start) of the input limits the shape.</summary>
    [JsonStringEnumMemberName("digit")]
    Digit = 1,

    /// <summary>A letter-or-digit neighbour or the very end (or start) of the input limits the shape.</summary>
    [JsonStringEnumMemberName("alnum")]
    Alnum = 2,
}

/// <summary>
/// The atom classes the shape language carries (rule-pack format, section 7.1).
/// A new class is an engine capability (rule 9), so the set is closed here and
/// it changes only with a schema change and <c>minEngineVersion</c>.
/// </summary>
public enum ShapeClass
{
    /// <summary>One ASCII digit, U+0030 to U+0039.</summary>
    [JsonStringEnumMemberName("digit")]
    Digit = 0,

    /// <summary>One ASCII letter or digit.</summary>
    [JsonStringEnumMemberName("alnum")]
    Alnum = 1,
}

/// <summary>
/// The closed set of named exclusions a pattern matcher carries as data
/// (rule-pack format, section 8.2). One new name is a data set change in this
/// enum, never engine-side special cases: the exclusions implementations are
/// the engine's, the choice is the pack's.
/// </summary>
public enum RuleExclusion
{
    /// <summary>Every scalar value in the span is one of a small digit set repeated.</summary>
    [JsonStringEnumMemberName("repeated-digits")]
    RepeatedDigits = 0,

    /// <summary>The digits run up or down in order.</summary>
    [JsonStringEnumMemberName("sequential-digits")]
    SequentialDigits = 1,

    /// <summary>The span reads as a hexadecimal fragment.</summary>
    [JsonStringEnumMemberName("hex-fragment")]
    HexFragment = 2,

    /// <summary>The span reads as a fragment of a UUID.</summary>
    [JsonStringEnumMemberName("uuid-fragment")]
    UuidFragment = 3,

    /// <summary>The span reads as a software version string.</summary>
    [JsonStringEnumMemberName("version-string")]
    VersionString = 4,

    /// <summary>The span reads as a date or a timestamp.</summary>
    [JsonStringEnumMemberName("date-or-timestamp")]
    DateOrTimestamp = 5,
}

/// <summary>
/// The closed set of named validators a pattern matcher may name (rule-pack
/// format, section 8.1). A new name is an engine capability (rule 9) and it
/// needs a schema change, conformance vectors and <c>minEngineVersion</c>.
/// </summary>
public enum RuleValidatorName
{
    /// <summary>The span must carry exactly <c>digits-length</c>'s <c>length</c> digits.</summary>
    [JsonStringEnumMemberName("digits-length")]
    DigitsLength = 0,

    /// <summary>The span must satisfy the NUBAN check-digit algorithm.</summary>
    [JsonStringEnumMemberName("nuban")]
    Nuban = 1,

    /// <summary>The span must open with one of <c>ng-phone-prefix</c>'s prefixes, which are pack data.</summary>
    [JsonStringEnumMemberName("ng-phone-prefix")]
    NgPhonePrefix = 2,

    /// <summary>The span must be an email address not at the example domain.</summary>
    [JsonStringEnumMemberName("email-not-example-domain")]
    EmailNotExampleDomain = 3,
}

/// <summary>
/// One inclusive range of ASCII scalar values, written as the two-character
/// JSON pair <c>["low", "high"]</c> (rule-pack format, section 7.1).
/// </summary>
/// <param name="Low">The low end of the range, included.</param>
/// <param name="High">The high end of the range, included.</param>
public sealed record RuleCharRange(char Low, char High);

/// <summary>
/// One part of a structured shape: exactly one atom — a literal, a class, an
/// explicit range list or an alternation — with the repetition bounds
/// <c>min</c> and <c>max</c> (rule-pack format, section 7.1). An alternation
/// part never repeats, so its <c>max</c> is 1. At most one part in a shape sets
/// <see cref="Emit"/>, and that part's span is the finding span used for
/// redaction.
/// </summary>
/// <param name="Min">The minimum repetitions, 0 to 64.</param>
/// <param name="Max">The maximum repetitions, 1 to 64.</param>
/// <param name="Literal">The literal atom, or null.</param>
/// <param name="Class">The class atom, or null.</param>
/// <param name="Ranges">The explicit range list as the atom, or null.</param>
/// <param name="OneOf">The alternation atom, or null.</param>
/// <param name="Emit">Whether this part's span is the finding span.</param>
public sealed record RuleShapePart(
    [property: JsonPropertyName("min"), JsonPropertyOrder(0)]
    int Min,
    [property: JsonPropertyName("max"), JsonPropertyOrder(1)]
    int Max,
    [property: JsonPropertyName("literal"), JsonPropertyOrder(2)]
    string? Literal,
    [property: JsonPropertyName("class"), JsonPropertyOrder(3)]
    ShapeClass? Class,
    [property: JsonPropertyName("ranges"), JsonPropertyOrder(4)]
    IReadOnlyList<RuleCharRange>? Ranges,
    [property: JsonPropertyName("oneOf"), JsonPropertyOrder(5)]
    IReadOnlyList<string>? OneOf,
    [property: JsonPropertyName("emit"), JsonPropertyOrder(6)]
    bool Emit = false)
{
    /// <summary>
    /// Gets the width of the atom in scalar values, the width a fixed-bound
    /// scan may consume per repetition: 1 for a class or a range, the literal
    /// length for a literal and the longest alternative for an alternation.
    /// </summary>
    internal int AtomWidth => Class is not null || Ranges is not null
        ? 1
        : Math.Max(Literal?.Length ?? 0, OneOf is null ? 0 : OneOf.Max(alternative => alternative.Length));
}

/// <summary>
/// The location restriction a pattern matcher carries (rule-pack format,
/// section 5.3): the closed kind inventory from the results format and the
/// closed path-glob subset the enumeration compares against.
/// </summary>
/// <param name="Kind">The closed location kind.</param>
/// <param name="Paths">The path globs, relative to the repository root and forward-slash only, or null for every path.</param>
public sealed record RuleLocation(
    [property: JsonPropertyName("kind"), JsonPropertyOrder(0)]
    LocationKind Kind,
    [property: JsonPropertyName("paths"), JsonPropertyOrder(1)]
    IReadOnlyList<string>? Paths);

/// <summary>
/// The named validator of a pattern matcher with its pack data (rule-pack
/// format, section 8.1). Only the parameters the validator needs may travel
/// alongside it, so <see cref="RuleValidatorName.DigitsLength"/> carries <see cref="Length"/>
/// and <see cref="RuleValidatorName.NgPhonePrefix"/> carries <see cref="Prefixes"/>.
/// </summary>
/// <param name="Name">The validator's closed name.</param>
/// <param name="Length">The exact digit count, for <see cref="RuleValidatorName.DigitsLength"/>.</param>
/// <param name="Prefixes">The allowed leading prefixes, for <see cref="RuleValidatorName.NgPhonePrefix"/>.</param>
public sealed record RuleValidator(
    [property: JsonPropertyName("name"), JsonPropertyOrder(0)]
    RuleValidatorName Name,
    [property: JsonPropertyName("length"), JsonPropertyOrder(1)]
    int? Length,
    [property: JsonPropertyName("prefixes"), JsonPropertyOrder(2)]
    IReadOnlyList<string>? Prefixes);

/// <summary>
/// The exact-entry matcher for processor catalogues: packages and hostnames
/// held as pack data (rule-pack format, section 5.2). At least one of the two
/// lists must be present. A shorter entry never matches inside a longer token;
/// that rule reads on the matcher that searches, not here.
/// </summary>
/// <param name="Packages">The exact package names, or null.</param>
/// <param name="Hostnames">The exact hostnames, or null.</param>
public sealed record RuleListMatcher(
    [property: JsonPropertyName("package"), JsonPropertyOrder(0)]
    IReadOnlyList<string>? Packages,
    [property: JsonPropertyName("hostname"), JsonPropertyOrder(1)]
    IReadOnlyList<string>? Hostnames);

/// <summary>
/// The matcher of one rule: exactly one kind, a structured pattern or a list
/// (rule-pack format, section 5). The loader refuses any rule whose matcher
/// carries both, and the matcher engine reads exactly the one present.
/// </summary>
/// <param name="Pattern">The structured pattern, or null when the rule is a list rule.</param>
/// <param name="List">The list matcher, or null when the rule is a pattern rule.</param>
public sealed record RuleMatcher(
    [property: JsonPropertyName("pattern"), JsonPropertyOrder(0)]
    RulePatternMatcher? Pattern,
    [property: JsonPropertyName("list"), JsonPropertyOrder(1)]
    RuleListMatcher? List);
