// SPDX-License-Identifier: Apache-2.0

using System.Text.Json.Serialization;
using Tomlyn.Serialization;

namespace InvenioTech.GateKeeper.Engine;

/// <summary>
/// The typed model of <c>.gatekeeper/gatekeeper.toml</c>, the settings file of
/// the configuration specification (section 4). Every TOML key maps to a
/// property here through the camelCase naming policy, so the property names
/// follow the keys the specification fixes: a key such as
/// <c>scan.maxFileBytes</c> reads as <c>ScanSection.MaxFileBytes</c>, and so
/// on. The one key the policy cannot spell is <c>fail-on</c>, which carries
/// <see cref="JsonPropertyNameAttribute"/> explicitly.
///
/// Properties are nullable so the loader can tell an omitted key from an
/// explicit value: the profile-dependent defaults (IQ-23) apply only when the
/// key is omitted. The model is day-data only: dates are strings, and a TOML
/// date value is a tool error.
/// </summary>
public sealed class ScanSettingsFile
{
    /// <summary>The <c>schemaVersion</c> integer; required to be 1 when the file exists. An absent file behaves as version 1 of the configuration specification.</summary>
    public int? SchemaVersion { get; set; }

    /// <summary>The <c>repository</c> table.</summary>
    public RepositorySectionFile? Repository { get; set; }

    /// <summary>The <c>scan</c> table.</summary>
    public ScanSectionFile? Scan { get; set; }

    /// <summary>The <c>baseline</c> table.</summary>
    public BaselineSectionFile? Baseline { get; set; }

    /// <summary>The <c>fail-on</c> threshold word.</summary>
    [JsonPropertyName("fail-on")]
    public string? FailOn { get; set; }

    /// <summary>The <c>severity</c> table, carrying the <c>overrides</c> sub-table.</summary>
    public SeveritySectionFile? Severity { get; set; }

    /// <summary>The <c>rules</c> table, carrying the <c>disabled</c> table array.</summary>
    public RulesSectionFile? Rules { get; set; }

    /// <summary>The <c>packs</c> table.</summary>
    public PacksSectionFile? Packs { get; set; }
}

/// <summary>The <c>repository</c> table of the settings file.</summary>
public sealed class RepositorySectionFile
{
    /// <summary>The <c>id</c> value, when the repository sets its identity explicitly (IQ-29).</summary>
    public string? Id { get; set; }
}

/// <summary>The <c>scan</c> table of the settings file.</summary>
public sealed class ScanSectionFile
{
    /// <summary>The <c>profile</c> word: <c>default</c> or <c>deep</c> (IQ-23).</summary>
    public string? Profile { get; set; }

    /// <summary>The <c>history</c> switch (IQ-83); omitted means the profile's default.</summary>
    public bool? History { get; set; }

    /// <summary>The <c>maxFileBytes</c> limit in bytes; omitted means the profile's default.</summary>
    public int? MaxFileBytes { get; set; }

    /// <summary>The <c>exclude</c> globs.</summary>
    public List<string>? Exclude { get; set; }

    /// <summary>The <c>includeSubmodules</c> switch.</summary>
    public bool? IncludeSubmodules { get; set; }
}

/// <summary>The <c>baseline</c> table of the settings file.</summary>
public sealed class BaselineSectionFile
{
    /// <summary>The <c>maxAgeDays</c> age cap in civil days; omitted, or 0, means entries never age out.</summary>
    public int? MaxAgeDays { get; set; }
}

/// <summary>The <c>severity</c> table of the settings file.</summary>
public sealed class SeveritySectionFile
{
    /// <summary>
    /// The <c>overrides</c> table: quoted rule identifiers to severity words.
    /// An unquoted rule identifier reads as a nested TOML table, not a rule
    /// identifier, and is a tool error.
    /// </summary>
    public Dictionary<string, string>? Overrides { get; set; }
}

/// <summary>The <c>rules</c> table of the settings file.</summary>
public sealed class RulesSectionFile
{
    /// <summary>The <c>disabled</c> tables, one per disabled rule identifier (IQ-24).</summary>
    public List<DisabledRuleFile>? Disabled { get; set; }
}

/// <summary>One entry of the <c>rules.disabled</c> table array.</summary>
public sealed class DisabledRuleFile
{
    /// <summary>The disabled rule identifier.</summary>
    public string? Id { get; set; }

    /// <summary>The written reason IQ-24 requires.</summary>
    public string? Reason { get; set; }
}

/// <summary>The <c>packs</c> table of the settings file.</summary>
public sealed class PacksSectionFile
{
    /// <summary>The <c>local</c> paths of unsigned local packs, relative to the repository root (IQ-25).</summary>
    public List<string>? Local { get; set; }
}

/// <summary>
/// The Tomlyn source-generated serialiser context for the settings-file
/// model. Reflection stays disabled under NativeAOT (rule 7), so every
/// mapping decision is made by the generated metadata. The generated mapping
/// ignores keys the model does not carry; the loader warns about those
/// separately instead of failing the scan (IQ-17).
/// </summary>
[TomlSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[TomlSerializable(typeof(ScanSettingsFile))]
internal sealed partial class ScanSettingsTomlContext : TomlSerializerContext;
