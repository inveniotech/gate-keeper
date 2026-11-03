// SPDX-License-Identifier: Apache-2.0

using System.Text.Json.Serialization;

namespace InvenioTech.GateKeeper.Engine;

/// <summary>
/// A rule's redaction behaviour (IQ-76). Only a rule pack may mark a rule
/// optional for redaction, and only generic low-confidence rules carry that
/// mark; the named identifier rules never do.
/// </summary>
/// <param name="Optional">Whether an application may switch this rule off for redaction.</param>
public sealed record RedactionOptions(
    [property: JsonPropertyName("optional"), JsonPropertyOrder(0)]
    bool Optional);
