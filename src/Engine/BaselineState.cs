// SPDX-License-Identifier: Apache-2.0

using System.Text.Json.Serialization;

namespace InvenioTech.GateKeeper.Engine;

/// <summary>
/// Whether a reported work-tree finding was already held back by the baseline
/// (IQ-19). A new finding is one the baseline does not cover; an unchanged one
/// is covered, and is reported without failing the threshold.
/// </summary>
public enum BaselineState
{
    /// <summary>
    /// The finding is not covered by the baseline.
    /// </summary>
    [JsonStringEnumMemberName("new")]
    New = 0,

    /// <summary>
    /// The fingerprint of the finding matched a baseline entry.
    /// </summary>
    [JsonStringEnumMemberName("unchanged")]
    Unchanged = 1,
}
