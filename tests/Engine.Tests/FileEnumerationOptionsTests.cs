// SPDX-License-Identifier: Apache-2.0

using InvenioTech.GateKeeper.Engine;

namespace InvenioTech.GateKeeper.Engine.Tests;

/// <summary>
/// The contracts the names of the enumeration options carry before any scan
/// work runs, so a misconfigured scan is a loud tool error and not a partial
/// tree (section 6.3 of the configuration specification).
/// </summary>
public sealed class FileEnumerationOptionsTests
{
    private static readonly string ValidRoot = Path.Combine(
        Path.GetTempPath(),
        "gatekeeper-tests",
        $"gk-{Guid.NewGuid():N}");

    [Fact]
    public void Options_Should_DefaultTheMaxFileBytesToOneMebibyte_When_TheScanOmitsTheLimit()
    {
        var options = new FileEnumerationOptions { Root = ValidRoot };

        options.MaxFileBytes.Should().Be(1_048_576);
    }

    [Fact]
    public void Options_Should_RejectTheScanExcludeGlob_When_AnEntryIsOutsideTheSubset()
    {
        var act = () => new FileEnumerationOptions { Root = ValidRoot, ExcludeGlobs = ["data/**", "plain?.md"] };

        act.Should().Throw<EngineToolException>().WithMessage(
            "The 'scan.exclude' glob 'plain?.md' is not in the supported pattern subset. " +
            "The '?' character is not supported. " +
            "The only metacharacters are '*' inside one path segment and '**' across segments.");
    }

    [Fact]
    public async Task Enumerator_Should_RejectTheRoot_When_ItIsMissing()
    {
        var options = new FileEnumerationOptions { Root = string.Empty };

        var act = async () => await new FileEnumerator().EnumerateAsync(options, CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentException>().WithParameterName("root");
    }

    [Fact]
    public void Options_Should_RejectTheLimit_When_TheMaxFileBytesIsBelowOne()
    {
        var act = () => new FileEnumerationOptions { Root = ValidRoot, MaxFileBytes = 0 };

        act.Should().Throw<ArgumentOutOfRangeException>().WithParameterName("MaxFileBytes");
    }
}
