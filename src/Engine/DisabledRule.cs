// SPDX-License-Identifier: Apache-2.0

namespace InvenioTech.GateKeeper.Engine;

/// <summary>
/// One entry of <c>rules.disabled</c>: a rule the repository turns off, with
/// the written reason IQ-24 requires. The reason is recorded in the evidence,
/// so it is trimmed to the form section 8 of the configuration specification
/// fixes: either the entry is a tool error.
/// </summary>
/// <param name="Id">The rule identifier, as written in the entry.</param>
/// <param name="Reason">The trimmed written reason.</param>
public sealed record DisabledRule(string Id, string Reason);
