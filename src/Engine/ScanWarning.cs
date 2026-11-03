// SPDX-License-Identifier: Apache-2.0

namespace InvenioTech.GateKeeper.Engine;

/// <summary>
/// One warning of the closed set the evidence file carries (section 5.6 of
/// the results format). The enumeration raises four of the codes; the other
/// codes belong to the configuration, the pack and the baseline stages.
/// </summary>
/// <param name="Code">The closed-set warning code.</param>
/// <param name="Message">The complete en-GB sentence, with the input values in place of the placeholders.</param>
public sealed record ScanWarning(string Code, string Message);
