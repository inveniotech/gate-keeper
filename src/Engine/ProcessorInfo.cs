// SPDX-License-Identifier: Apache-2.0

using System.Text.Json.Serialization;

namespace InvenioTech.GateKeeper.Engine;

/// <summary>
/// The processor record a catalogue rule carries (IQ-82). A rule with a
/// processor block produces note-severity catalogue findings, which fill the
/// processor index without failing a scan.
/// </summary>
/// <param name="DataCategories">The data categories the processor handles.</param>
/// <param name="UsualRegion">The region where the processor usually operates.</param>
public sealed record ProcessorInfo(
    [property: JsonPropertyName("dataCategories"), JsonPropertyOrder(0)]
    IReadOnlyList<string> DataCategories,
    [property: JsonPropertyName("usualRegion"), JsonPropertyOrder(1)]
    string UsualRegion);
