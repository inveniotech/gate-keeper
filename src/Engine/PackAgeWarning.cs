// SPDX-License-Identifier: Apache-2.0

using System.Globalization;

namespace InvenioTech.GateKeeper.Engine;

/// <summary>The deterministic 90-day content-age warning (P1-14, IQ-26).</summary>
public static class PackAgeWarning
{
    /// <summary>Warns about the newest dated loaded pack only when its content is more than 90 civil days old. Local packs have no issue date and are ignored.</summary>
    /// <exception cref="EngineToolException">A supplied content issue date is invalid.</exception>
    public static IReadOnlyList<ScanWarning> Evaluate(IReadOnlyList<LoadedPack> packs, DateOnly evaluatedOn)
    {
        LoadedPack? newest = null;
        DateOnly newestDate = default;
        foreach (var pack in packs.OrderBy(p => p.Id, StringComparer.Ordinal))
        {
            if (pack.ContentIssuedAt is null) continue;
            var issued = EvaluationDate.Parse(pack.ContentIssuedAt, "contentIssuedAt");
            if (newest is null || issued > newestDate) { newest = pack; newestDate = issued; }
        }
        var age = evaluatedOn.DayNumber - newestDate.DayNumber;
        return newest is not null && age > 90
            ? [new("pack.stale", string.Create(CultureInfo.InvariantCulture, $"Rule pack {newest.Id} was issued {age} days before the evaluation date, which is past the 90-day threshold."))]
            : [];
    }
}
