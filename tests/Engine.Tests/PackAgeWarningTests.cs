// SPDX-License-Identifier: Apache-2.0

using InvenioTech.GateKeeper.Engine;

namespace InvenioTech.GateKeeper.Engine.Tests;

public sealed class PackAgeWarningTests
{
    [Theory]
    [InlineData("2026-03-31", false)]
    [InlineData("2026-04-01", false)]
    [InlineData("2026-04-02", true)]
    [InlineData("2025-12-31", false)]
    public void PackAgeWarning_Should_WarnOnlyPastNinetyDays_When_UsingTheInjectedEvaluationDate(string today, bool stale)
    {
        var day = EvaluationDate.Resolve(new FixedClock(DateOnly.ParseExact(today, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture)), null, null);
        var warnings = PackAgeWarning.Evaluate([Pack("pack.example", "2026-01-01")], day);
        warnings.Count.Should().Be(stale ? 1 : 0);
        if (stale) warnings[0].Should().Be(new ScanWarning("pack.stale", "Rule pack pack.example was issued 91 days before the evaluation date, which is past the 90-day threshold."));
    }

    [Fact]
    public void PackAgeWarning_Should_UseTheNewestContentIssueDate_When_MultiplePacksAreLoaded()
    {
        var older = Pack("pack.older", "2026-01-01") with { CoverageDate = "2025-01-01" };
        var newer = Pack("pack.newer", "2026-03-01") with { CoverageDate = "2025-01-01" };
        PackAgeWarning.Evaluate([older, newer], new DateOnly(2026, 4, 2)).Should().BeEmpty();
        PackAgeWarning.Evaluate([newer, older], new DateOnly(2026, 7, 1)).Should().Equal(new ScanWarning("pack.stale",
            "Rule pack pack.newer was issued 122 days before the evaluation date, which is past the 90-day threshold."));
    }

    [Fact]
    public void PackAgeWarning_Should_IgnoreUndatedLocalPacks_When_NoContentIssueDateExists()
    {
        PackAgeWarning.Evaluate([new LoadedPack("local.example", "1", PackProvenance.Local, new string('a', 64))], new DateOnly(2026, 11, 1)).Should().BeEmpty();
        PackAgeWarning.Evaluate([], new DateOnly(2026, 11, 1)).Should().BeEmpty();
    }

    [Fact]
    public void PackLoader_Should_RetainContentIssueDate_When_LoadingASignedPack()
    {
        var loaded = PackLoader.Load([PackTestSupport.SignedPack("pack")], [], PackTestSupport.Catalogue, BundledPacksOnlyPolicy.Instance);
        loaded.Packs.Single().ContentIssuedAt.Should().Be("2027-01-01");
    }

    private static LoadedPack Pack(string id, string date) => new(id, "2026.01.0", PackProvenance.Bundled, new string('a', 64)) { ContentIssuedAt = date };

    private sealed class FixedClock(DateOnly day) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(day.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc));
    }
}
