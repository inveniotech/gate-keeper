// SPDX-License-Identifier: Apache-2.0

using System.Text.Json.Serialization;

namespace InvenioTech.GateKeeper.Engine;

/// <summary>
/// The three severities the rule-pack format and the results format fix
/// (IQ-22). A rule carries its severity, and a finding carries the severity it
/// ended up with after overrides.
/// </summary>
public enum Severity
{
    /// <summary>
    /// A finding that fails a scan under the default failure threshold, where
    /// <c>fail-on = "error"</c>.
    /// </summary>
    [JsonStringEnumMemberName("error")]
    Error = 0,

    /// <summary>
    /// A finding that fails a scan only when the failure threshold includes it.
    /// </summary>
    [JsonStringEnumMemberName("warning")]
    Warning = 1,

    /// <summary>
    /// A finding that is reported for the record and fails only when the
    /// failure threshold is note (configuration section 8, IQ-22).
    /// </summary>
    [JsonStringEnumMemberName("note")]
    Note = 2,
}
