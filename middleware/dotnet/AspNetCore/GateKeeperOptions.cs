// SPDX-License-Identifier: Apache-2.0

namespace InvenioTech.GateKeeper.AspNetCore;

/// <summary>
/// The start-up settings <see cref="GateKeeperServiceCollectionExtensions"/>
/// applies. Everything is optional: no disabled rules, no protected response
/// paths and no consent provider leave redaction and classification unchanged.
/// </summary>
public sealed class GateKeeperOptions
{
    /// <summary>
    /// Gets or sets the optional rule identifiers to switch off at start-up.
    /// Only rules the pack marks <c>redaction.optional</c> may be named; an
    /// unknown or mandatory identifier fails start-up.
    /// </summary>
    public IReadOnlyList<string> DisabledRules { get; set; } = [];

    /// <summary>
    /// Gets or sets the RFC 6901 JSON Pointers whose string leaves the response
    /// hooks redact. A complete <c>*</c> segment selects every array element and
    /// <c>~2</c> addresses a literal star key. An empty list leaves every
    /// response untouched.
    /// </summary>
    public IReadOnlyList<string> ProtectedJsonPointers { get; set; } = [];

    /// <summary>
    /// Gets or sets the application-owned consent provider. Its absence makes
    /// every check return <see cref="InvenioTech.GateKeeper.Middleware.ConsentState.Unknown"/>
    /// with one start-up warning; it never changes redaction.
    /// </summary>
    public InvenioTech.GateKeeper.Middleware.IConsentProvider? ConsentProvider { get; set; }
}
