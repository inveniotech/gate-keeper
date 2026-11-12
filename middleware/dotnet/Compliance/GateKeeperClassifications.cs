// SPDX-License-Identifier: Apache-2.0

using Microsoft.Extensions.Compliance.Classification;

namespace InvenioTech.GateKeeper.Compliance.Redaction;

/// <summary>
/// The Gate-Keeper data classifications the compliance redactor answers for.
/// Applications that log with data classification annotate their values with
/// these and the <see cref="Microsoft.Extensions.Compliance.Redaction.IRedactorProvider"/>
/// supplies the Gate-Keeper redactor.
/// </summary>
public static class GateKeeperClassifications
{
    /// <summary>The taxonomy every Gate-Keeper classification belongs to.</summary>
    public const string TaxonomyName = "InvenioTech.GateKeeper";

    /// <summary>Personal data the loaded rules classify.</summary>
    public static readonly DataClassification Pii = new(TaxonomyName, "PII");
}
