// SPDX-License-Identifier: Apache-2.0

using System.Text.Json.Serialization;
using Tomlyn.Serialization;

namespace InvenioTech.GateKeeper.Spikes.Aot.TomlSarif;

/// <summary>
/// A representative model of the <c>.ndpa-check/ndpa-check.toml</c> file, reduced
/// to the shapes the source generator must handle: nested tables, arrays and a
/// dictionary of overrides. The real schema is defined by work package P0-05;
/// this spike only proves that Tomlyn can parse a TOML file of this shape under
/// NativeAOT.
/// </summary>
public sealed class NdpachConfig
{
    public string Repository { get; set; } = string.Empty;

    public ScanSection Scan { get; set; } = new();

    public FailOnSection FailOn { get; set; } = new();

    public Dictionary<string, string> SeverityOverrides { get; set; } = new();

    public PacksSection Packs { get; set; } = new();
}

/// <summary>Scan-scope settings.</summary>
public sealed class ScanSection
{
    public string Profile { get; set; } = "default";

    public int MaxFileSizeKb { get; set; } = 1024;

    public bool IncludeSubmodules { get; set; }

    public List<string> Exclusions { get; set; } = new();
}

/// <summary>The failure threshold.</summary>
public sealed class FailOnSection
{
    public string Level { get; set; } = "error";
}

/// <summary>Pack loading settings.</summary>
public sealed class PacksSection
{
    public List<string> Local { get; set; } = new();
}

/// <summary>
/// The source-generated serialiser context. The <c>[TomlSerializable]</c>
/// attribute lists the root type, and the generator produces the metadata that
/// replaces reflection entirely.
/// </summary>
[TomlSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.KebabCaseLower)]
[TomlSerializable(typeof(NdpachConfig))]
internal partial class NdpachTomlContext : TomlSerializerContext
{
}
