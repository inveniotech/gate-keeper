// SPDX-License-Identifier: Apache-2.0

using System.Text.Json.Serialization;

namespace InvenioTech.GateKeeper.Engine;

/// <summary>
/// The closed set of location kinds (the P0-04 results format, section 6).
/// The kind says which kind of place a finding was reported in, and the
/// results inventory groups findings by it.
/// </summary>
public enum LocationKind
{
    /// <summary>A log statement or a log file line.</summary>
    [JsonStringEnumMemberName("log")]
    Log = 0,

    /// <summary>A database migration.</summary>
    [JsonStringEnumMemberName("migration")]
    Migration = 1,

    /// <summary>An application programming interface model.</summary>
    [JsonStringEnumMemberName("api-model")]
    ApiModel = 2,

    /// <summary>A configuration file.</summary>
    [JsonStringEnumMemberName("configuration")]
    Configuration = 3,

    /// <summary>A test fixture.</summary>
    [JsonStringEnumMemberName("fixture")]
    Fixture = 4,

    /// <summary>Source code, the default when a rule names no location kind.</summary>
    [JsonStringEnumMemberName("source")]
    Source = 5,
}
