// SPDX-License-Identifier: Apache-2.0

using System.Text.Json.Serialization;

namespace InvenioTech.GateKeeper.Engine;

/// <summary>
/// Where a rule pack came from (IQ-25). The provenance decides which date
/// fields the pack carries and which versions are legal.
/// </summary>
public enum PackProvenance
{
    /// <summary>The pack is bundled with the installation, free of licence checks.</summary>
    [JsonStringEnumMemberName("bundled")]
    Bundled = 0,

    /// <summary>The pack is plan-gated and issued to one organisation.</summary>
    [JsonStringEnumMemberName("plan-gated")]
    PlanGated = 1,

    /// <summary>The pack is an unsigned local pack listed in the repository configuration.</summary>
    [JsonStringEnumMemberName("local")]
    Local = 2,
}
