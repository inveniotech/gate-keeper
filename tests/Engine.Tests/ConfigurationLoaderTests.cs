// SPDX-License-Identifier: Apache-2.0

using InvenioTech.GateKeeper.Engine;

namespace InvenioTech.GateKeeper.Engine.Tests;

/// <summary>
/// Behaviour tests for the loader of the settings file
/// <c>.gatekeeper/gatekeeper.toml</c> (configuration specification, section 4;
/// IQ-02, IQ-17, IQ-23). Every key the specification lists gets at least one
/// test, per the card's acceptance test.
/// </summary>
public sealed class ConfigurationLoaderTests
{
    private const int DefaultFileLimit = 1_048_576;
    private const int DeepFileLimit = 10_485_760;

    [Fact]
    public void ConfigurationLoader_Should_ApplyEveryDefault_When_TheSettingsFileIsAbsent()
    {
        using var repository = new TemporaryRepository();

        var load = ConfigurationLoader.Load(repository.Root);

        load.Errors.Should().BeEmpty();
        load.Warnings.Should().BeEmpty();
        var settings = load.Settings.Should().BeOfType<ScanSettings>().Which;
        settings.Profile.Should().Be(ScanProfile.Default);
        settings.ScanHistory.Should().BeFalse();
        settings.MaxFileBytes.Should().Be(DefaultFileLimit);
        settings.Exclude.Should().BeEmpty();
        settings.IncludeSubmodules.Should().BeFalse();
        settings.RepositoryId.Should().BeNull();
        settings.BaselineMaxAgeDays.Should().Be(0);
        settings.FailOn.Should().Be(Severity.Error);
        settings.SeverityOverrides.Should().BeEmpty();
        settings.DisabledRules.Should().BeEmpty();
        settings.LocalPacks.Should().BeEmpty();
    }

    [Fact]
    public void ConfigurationLoader_Should_ApplyEveryDefault_When_TheCheckFolderExistsWithoutTheSettingsFile()
    {
        using var repository = new TemporaryRepository();
        Directory.CreateDirectory(Path.Combine(repository.Root, ".gatekeeper"));

        var load = ConfigurationLoader.Load(repository.Root);

        load.Errors.Should().BeEmpty();
        load.Warnings.Should().BeEmpty();
        var settings = load.Settings.Should().BeOfType<ScanSettings>().Which;
        settings.Profile.Should().Be(ScanProfile.Default);
        settings.MaxFileBytes.Should().Be(DefaultFileLimit);
    }

    [Fact]
    public void ConfigurationLoader_Should_HonourSchemaVersionOne_When_TheFileDeclaresIt()
    {
        using var repository = new TemporaryRepository();
        repository.WriteSettings("schemaVersion = 1");

        var load = ConfigurationLoader.Load(repository.Root);

        load.Errors.Should().BeEmpty();
        load.Warnings.Should().BeEmpty();
        load.Settings.Should().NotBeNull();
    }

    [Fact]
    public void ConfigurationLoader_Should_Error_When_TheSettingsFileIsMissingSchemaVersion()
    {
        using var repository = new TemporaryRepository();
        repository.WriteSettings("fail-on = \"error\"");

        var load = ConfigurationLoader.Load(repository.Root);

        load.Settings.Should().BeNull();
        load.Errors.Should().Contain(e => e.Contains("gatekeeper.toml") && e.Contains("schemaVersion"));
    }

    [Fact]
    public void ConfigurationLoader_Should_Error_When_SchemaVersionIsOtherThanOne()
    {
        using var repository = new TemporaryRepository();
        repository.WriteSettings("schemaVersion = 2");

        var load = ConfigurationLoader.Load(repository.Root);

        load.Settings.Should().BeNull();
        load.Errors.Should().Contain(e => e.Contains("schemaVersion"));
    }

    [Fact]
    public void ConfigurationLoader_Should_Warn_When_TheSettingsFileCarriesAnUnknownRootKey()
    {
        using var repository = new TemporaryRepository();
        repository.WriteSettings("schemaVersion = 1\nsupervisionLevel = 24");

        var load = ConfigurationLoader.Load(repository.Root);

        load.Errors.Should().BeEmpty();
        load.Warnings.Should().Contain(w => w.Contains("unknown key 'supervisionLevel'"));
        load.Warnings.Should().HaveCount(1);
        load.Settings.Should().NotBeNull();
    }

    [Fact]
    public void ConfigurationLoader_Should_Warn_When_AKnownTableCarriesAnUnknownKey()
    {
        using var repository = new TemporaryRepository();
        repository.WriteSettings("schemaVersion = 1\n\n[scan]\nsupervisionDepth = 512\n");

        var load = ConfigurationLoader.Load(repository.Root);

        load.Errors.Should().BeEmpty();
        load.Warnings.Should().Contain(w => w.Contains("unknown key 'scan.supervisionDepth'"));
        load.Settings.Should().NotBeNull();
    }

    [Fact]
    public void ConfigurationLoader_Should_WarnUnknownKeysInOrdinalOrder_When_SeveralArePresent()
    {
        using var repository = new TemporaryRepository();
        repository.WriteSettings("schemaVersion = 1\n\nsupervisionLevel = 24\n\n[scan]\nsupervisionDepth = 512\n");

        var load = ConfigurationLoader.Load(repository.Root);

        // Unknown keys are warned in ordinal order of the dotted key, so the
        // scan-table key ('scan.supervisionDepth') sorts before the root key
        // ('supervisionLevel') whatever order the file carries them in.
        load.Warnings.Should().SatisfyRespectively(
            w => w.Should().Contain("unknown key 'scan.supervisionDepth'"),
            w => w.Should().Contain("unknown key 'supervisionLevel'"));
    }

    [Fact]
    public void ConfigurationLoader_Should_Error_When_TheSettingsFileIsNotValidToml()
    {
        using var repository = new TemporaryRepository();
        repository.WriteSettings("schemaVersion = 1 = 2");

        var load = ConfigurationLoader.Load(repository.Root);

        load.Settings.Should().BeNull();
        load.Errors.Should().Contain(e => e.Contains("gatekeeper.toml"));
    }

    [Fact]
    public void ConfigurationLoader_Should_Error_When_TheSettingsFileCarriesABom()
    {
        using var repository = new TemporaryRepository();
        repository.WriteSettingsBytes(new byte[]
        {
            0xEF, 0xBB, 0xBF,
            (byte)'s', (byte)'c', (byte)'h', (byte)'e', (byte)'m', (byte)'a', (byte)'V', (byte)'e', (byte)'r',
            (byte)'s', (byte)'i', (byte)'o', (byte)'n', (byte)' ', (byte)'=', (byte)' ', (byte)'1',
        });

        var load = ConfigurationLoader.Load(repository.Root);

        load.Settings.Should().BeNull();
        load.Errors.Should().Contain(e => e.Contains("gatekeeper.toml") && e.Contains("byte-order mark"));
    }

    [Fact]
    public void ConfigurationLoader_Should_Error_When_TheSettingsFileIsOverOneMegabyte()
    {
        using var repository = new TemporaryRepository();
        var padding = new string('x', 1_048_577);
        repository.WriteSettings($"schemaVersion = 1\n# {padding}");

        var load = ConfigurationLoader.Load(repository.Root);

        load.Settings.Should().BeNull();
        load.Errors.Should().Contain(e => e.Contains("gatekeeper.toml") && e.Contains("1,048,576"));
    }

    [Fact]
    public void ConfigurationLoader_Should_Error_When_AValueWhereAnIntegerIsRequiredCarriesAnotherType()
    {
        using var repository = new TemporaryRepository();
        repository.WriteSettings("schemaVersion = 1\n\n[baseline]\nmaxAgeDays = true\n");

        var load = ConfigurationLoader.Load(repository.Root);

        load.Settings.Should().BeNull();
        load.Errors.Should().Contain(e => e.Contains("gatekeeper.toml"));
    }

    [Fact]
    public void ConfigurationLoader_Should_HonourTheRepositoryIdentity_When_ItIsSet()
    {
        using var repository = new TemporaryRepository();
        repository.WriteSettings("""
            schemaVersion = 1

            [repository]
            id = "github.com/acme/payments"
            """);

        var load = ConfigurationLoader.Load(repository.Root);

        load.Errors.Should().BeEmpty();
        load.Settings.Should().BeOfType<ScanSettings>().Which.RepositoryId.Should().Be("github.com/acme/payments");
    }

    [Fact]
    public void ConfigurationLoader_Should_Error_When_TheRepositoryIdentityCarriesWhitespace()
    {
        using var repository = new TemporaryRepository();
        repository.WriteSettings("schemaVersion = 1\n\n[repository]\nid = \"acme payments\"\n");

        var load = ConfigurationLoader.Load(repository.Root);

        load.Settings.Should().BeNull();
        load.Errors.Should().Contain(e => e.Contains("repository.id"));
    }

    [Fact]
    public void ConfigurationLoader_Should_Error_When_TheRepositoryIdentityEscapesTheRepository()
    {
        using var repository = new TemporaryRepository();
        repository.WriteSettings("schemaVersion = 1\n\n[repository]\nid = \"../secrets\"\n");

        var load = ConfigurationLoader.Load(repository.Root);

        load.Settings.Should().BeNull();
        load.Errors.Should().Contain(e => e.Contains("repository.id"));
    }

    [Fact]
    public void ConfigurationLoader_Should_HonourTheDeepProfile_When_ItIsSelected()
    {
        using var repository = new TemporaryRepository();
        repository.WriteSettings("schemaVersion = 1\n\n[scan]\nprofile = \"deep\"\n");

        var load = ConfigurationLoader.Load(repository.Root);

        load.Errors.Should().BeEmpty();
        load.Settings.Should().BeOfType<ScanSettings>().Which.Profile.Should().Be(ScanProfile.Deep);
    }

    [Fact]
    public void ConfigurationLoader_Should_Error_When_TheProfileIsUnknown()
    {
        using var repository = new TemporaryRepository();
        repository.WriteSettings("schemaVersion = 1\n\n[scan]\nprofile = \"wide\"\n");

        var load = ConfigurationLoader.Load(repository.Root);

        load.Settings.Should().BeNull();
        load.Errors.Should().Contain(e => e.Contains("scan.profile"));
    }

    [Fact]
    public void ConfigurationLoader_Should_TurnHistoryOnByDefault_When_TheDeepProfileIsSelected()
    {
        using var repository = new TemporaryRepository();
        repository.WriteSettings("schemaVersion = 1\n\n[scan]\nprofile = \"deep\"\n");

        var load = ConfigurationLoader.Load(repository.Root);

        load.Settings.Should().BeOfType<ScanSettings>().Which.ScanHistory.Should().BeTrue();
    }

    [Fact]
    public void ConfigurationLoader_Should_RaiseTheFileLimitByDefault_When_TheDeepProfileIsSelected()
    {
        using var repository = new TemporaryRepository();
        repository.WriteSettings("schemaVersion = 1\n\n[scan]\nprofile = \"deep\"\n");

        var load = ConfigurationLoader.Load(repository.Root);

        load.Settings.Should().BeOfType<ScanSettings>().Which.MaxFileBytes.Should().Be(DeepFileLimit);
    }

    [Fact]
    public void ConfigurationLoader_Should_KeepHistoryOff_When_ItIsExplicitlyDisabledUnderTheDeepProfile()
    {
        using var repository = new TemporaryRepository();
        repository.WriteSettings("schemaVersion = 1\n\n[scan]\nprofile = \"deep\"\nhistory = false\n");

        var load = ConfigurationLoader.Load(repository.Root);

        load.Settings.Should().BeOfType<ScanSettings>().Which.ScanHistory.Should().BeFalse();
    }

    [Fact]
    public void ConfigurationLoader_Should_TurnHistoryOn_When_ItIsExplicitlyEnabledUnderTheDefaultProfile()
    {
        using var repository = new TemporaryRepository();
        repository.WriteSettings("schemaVersion = 1\n\n[scan]\nhistory = true\n");

        var load = ConfigurationLoader.Load(repository.Root);

        load.Settings.Should().BeOfType<ScanSettings>().Which.ScanHistory.Should().BeTrue();
    }

    [Fact]
    public void ConfigurationLoader_Should_KeepTheExplicitFileLimit_When_MaxFileBytesIsSetUnderTheDeepProfile()
    {
        using var repository = new TemporaryRepository();
        repository.WriteSettings("schemaVersion = 1\n\n[scan]\nprofile = \"deep\"\nmaxFileBytes = 4096\n");

        var load = ConfigurationLoader.Load(repository.Root);

        load.Settings.Should().BeOfType<ScanSettings>().Which.MaxFileBytes.Should().Be(4096);
    }

    [Fact]
    public void ConfigurationLoader_Should_Error_When_TheFileLimitIsBelowTheFloor()
    {
        using var repository = new TemporaryRepository();
        repository.WriteSettings("schemaVersion = 1\n\n[scan]\nmaxFileBytes = 0\n");

        var load = ConfigurationLoader.Load(repository.Root);

        load.Settings.Should().BeNull();
        load.Errors.Should().Contain(e => e.Contains("scan.maxFileBytes"));
    }

    [Fact]
    public void ConfigurationLoader_Should_Error_When_TheFileLimitIsAboveTheCeiling()
    {
        using var repository = new TemporaryRepository();
        repository.WriteSettings("schemaVersion = 1\n\n[scan]\nmaxFileBytes = 104857601\n");

        var load = ConfigurationLoader.Load(repository.Root);

        load.Settings.Should().BeNull();
        load.Errors.Should().Contain(e => e.Contains("scan.maxFileBytes"));
    }

    [Fact]
    public void ConfigurationLoader_Should_HonourTheExcludeList_When_GlobsAreListed()
    {
        using var repository = new TemporaryRepository();
        repository.WriteSettings("schemaVersion = 1\n\n[scan]\nexclude = [\"scratch/**\", \"docs/**\"]\n");

        var load = ConfigurationLoader.Load(repository.Root);

        load.Errors.Should().BeEmpty();
        var settings = load.Settings.Should().BeOfType<ScanSettings>().Which;
        settings.Exclude.Should().HaveCount(2);
        settings.Exclude.Should().Contain("scratch/**");
        settings.Exclude.Should().Contain("docs/**");
    }

    [Fact]
    public void ConfigurationLoader_Should_HonourTheSubmoduleSwitch_When_ItIsEnabled()
    {
        using var repository = new TemporaryRepository();
        repository.WriteSettings("schemaVersion = 1\n\n[scan]\nincludeSubmodules = true\n");

        var load = ConfigurationLoader.Load(repository.Root);

        load.Settings.Should().BeOfType<ScanSettings>().Which.IncludeSubmodules.Should().BeTrue();
    }

    [Fact]
    public void ConfigurationLoader_Should_HonourTheBaselineAgeCap_When_ItIsSet()
    {
        using var repository = new TemporaryRepository();
        repository.WriteSettings("schemaVersion = 1\n\n[baseline]\nmaxAgeDays = 90\n");

        var load = ConfigurationLoader.Load(repository.Root);

        load.Settings.Should().BeOfType<ScanSettings>().Which.BaselineMaxAgeDays.Should().Be(90);
    }

    [Fact]
    public void ConfigurationLoader_Should_Error_When_TheBaselineAgeCapIsNegative()
    {
        using var repository = new TemporaryRepository();
        repository.WriteSettings("schemaVersion = 1\n\n[baseline]\nmaxAgeDays = -5\n");

        var load = ConfigurationLoader.Load(repository.Root);

        load.Settings.Should().BeNull();
        load.Errors.Should().Contain(e => e.Contains("baseline.maxAgeDays"));
    }

    [Fact]
    public void ConfigurationLoader_Should_HonourTheFailureThreshold_When_ItNamesWarning()
    {
        using var repository = new TemporaryRepository();
        repository.WriteSettings("schemaVersion = 1\nfail-on = \"warning\"\n");

        var load = ConfigurationLoader.Load(repository.Root);

        load.Settings.Should().BeOfType<ScanSettings>().Which.FailOn.Should().Be(Severity.Warning);
    }

    [Fact]
    public void ConfigurationLoader_Should_HonourTheFailureThreshold_When_ItNamesNote()
    {
        using var repository = new TemporaryRepository();
        repository.WriteSettings("schemaVersion = 1\nfail-on = \"note\"\n");

        var load = ConfigurationLoader.Load(repository.Root);

        load.Settings.Should().BeOfType<ScanSettings>().Which.FailOn.Should().Be(Severity.Note);
    }

    [Fact]
    public void ConfigurationLoader_Should_Error_When_TheFailureThresholdIsNotASeverity()
    {
        using var repository = new TemporaryRepository();
        repository.WriteSettings("schemaVersion = 1\nfail-on = \"fatal\"\n");

        var load = ConfigurationLoader.Load(repository.Root);

        load.Settings.Should().BeNull();
        load.Errors.Should().Contain(e => e.Contains("fail-on"));
    }

    [Fact]
    public void ConfigurationLoader_Should_HonourTheSeverityOverrides_When_TheTableMapsRules()
    {
        using var repository = new TemporaryRepository();
        repository.WriteSettings("""
            schemaVersion = 1

            [severity.overrides]
            "pii.phone" = "warning"
            "pii.bvn" = "note"
            """);

        var load = ConfigurationLoader.Load(repository.Root);

        load.Errors.Should().BeEmpty();
        var settings = load.Settings.Should().BeOfType<ScanSettings>().Which;
        settings.SeverityOverrides.Should().HaveCount(2);
        settings.SeverityOverrides["pii.phone"].Should().Be(Severity.Warning);
        settings.SeverityOverrides["pii.bvn"].Should().Be(Severity.Note);
    }

    [Fact]
    public void ConfigurationLoader_Should_Error_When_AnOverrideValueIsNotASeverity()
    {
        using var repository = new TemporaryRepository();
        repository.WriteSettings("schemaVersion = 1\n\n[severity.overrides]\n\"pii.bvn\" = \"fatal\"\n");

        var load = ConfigurationLoader.Load(repository.Root);

        load.Settings.Should().BeNull();
        load.Errors.Should().Contain(e => e.Contains("pii.bvn"));
    }

    [Fact]
    public void ConfigurationLoader_Should_HonourDisabledRules_When_EntriesCarryIdAndReason()
    {
        using var repository = new TemporaryRepository();
        repository.WriteSettings("""
            schemaVersion = 1

            [[rules.disabled]]
            id = "pii.email"
            reason = "Fixture values are synthetic."

            [[rules.disabled]]
            id = "pii.phone"
            reason = "Contact lists are synthetic."
            """);

        var load = ConfigurationLoader.Load(repository.Root);

        load.Errors.Should().BeEmpty();
        var settings = load.Settings.Should().BeOfType<ScanSettings>().Which;
        settings.DisabledRules.Should().HaveCount(2);
        settings.DisabledRules[0].Id.Should().Be("pii.email");
        settings.DisabledRules[0].Reason.Should().Be("Fixture values are synthetic.");
        settings.DisabledRules[1].Id.Should().Be("pii.phone");
    }

    [Fact]
    public void ConfigurationLoader_Should_Error_When_ADisabledRuleEntryIsMissingItsIdentifier()
    {
        using var repository = new TemporaryRepository();
        repository.WriteSettings("schemaVersion = 1\n\n[[rules.disabled]]\nreason = \"Reviewed each quarter.\"\n");

        var load = ConfigurationLoader.Load(repository.Root);

        load.Settings.Should().BeNull();
        load.Errors.Should().Contain(e => e.Contains("rules.disabled"));
    }

    [Fact]
    public void ConfigurationLoader_Should_Error_When_ADisabledRuleEntryIsMissingItsReason()
    {
        using var repository = new TemporaryRepository();
        repository.WriteSettings("schemaVersion = 1\n\n[[rules.disabled]]\nid = \"pii.bvn\"\n");

        var load = ConfigurationLoader.Load(repository.Root);

        load.Settings.Should().BeNull();
        load.Errors.Should().Contain(e => e.Contains("pii.bvn"));
    }

    [Fact]
    public void ConfigurationLoader_Should_Error_When_ADisabledRuleReasonIsBlank()
    {
        using var repository = new TemporaryRepository();
        repository.WriteSettings("schemaVersion = 1\n\n[[rules.disabled]]\nid = \"pii.bvn\"\nreason = \"   \"\n");

        var load = ConfigurationLoader.Load(repository.Root);

        load.Settings.Should().BeNull();
        load.Errors.Should().Contain(e => e.Contains("pii.bvn"));
    }

    [Fact]
    public void ConfigurationLoader_Should_Error_When_ADisabledRuleReasonSpansLines()
    {
        using var repository = new TemporaryRepository();
        repository.WriteSettings(
            "schemaVersion = 1\n\n[[rules.disabled]]\nid = \"pii.bvn\"\nreason = \"\"\"first\nsecond\"\"\"\n");

        var load = ConfigurationLoader.Load(repository.Root);

        load.Settings.Should().BeNull();
        load.Errors.Should().Contain(e => e.Contains("pii.bvn"));
    }

    [Fact]
    public void ConfigurationLoader_Should_Error_When_ADisabledRuleReasonExceedsTheLimit()
    {
        using var repository = new TemporaryRepository();
        var longReason = new string('x', 2_001);
        repository.WriteSettings($"schemaVersion = 1\n\n[[rules.disabled]]\nid = \"pii.bvn\"\nreason = \"{longReason}\"\n");

        var load = ConfigurationLoader.Load(repository.Root);

        load.Settings.Should().BeNull();
        load.Errors.Should().Contain(e => e.Contains("pii.bvn"));
    }

    [Fact]
    public void ConfigurationLoader_Should_Error_When_ADisabledRuleIdentifierIsRepeated()
    {
        using var repository = new TemporaryRepository();
        repository.WriteSettings("""
            schemaVersion = 1

            [[rules.disabled]]
            id = "pii.bvn"
            reason = "Reviewed each quarter."

            [[rules.disabled]]
            id = "pii.bvn"
            reason = "Reviewed again."
            """);

        var load = ConfigurationLoader.Load(repository.Root);

        load.Settings.Should().BeNull();
        load.Errors.Should().Contain(e => e.Contains("pii.bvn"));
    }

    [Fact]
    public void ConfigurationLoader_Should_HonourTheLocalPackList_When_PacksAreListed()
    {
        using var repository = new TemporaryRepository();
        repository.WriteSettings(
            "schemaVersion = 1\n\n[packs]\nlocal = [\".gatekeeper/packs/acme-customer-id.json\"]\n");

        var load = ConfigurationLoader.Load(repository.Root);

        load.Errors.Should().BeEmpty();
        var settings = load.Settings.Should().BeOfType<ScanSettings>().Which;
        settings.LocalPacks.Should().Contain(".gatekeeper/packs/acme-customer-id.json");
    }

    [Fact]
    public void ConfigurationLoader_Should_Error_When_ALocalPackEscapesTheRepositoryRoot()
    {
        using var repository = new TemporaryRepository();
        repository.WriteSettings("schemaVersion = 1\n\n[packs]\nlocal = [\"../outside.json\"]\n");

        var load = ConfigurationLoader.Load(repository.Root);

        load.Settings.Should().BeNull();
        load.Errors.Should().Contain(e => e.Contains("packs.local"));
    }

    [Fact]
    public void ConfigurationLoader_Should_Error_When_ALocalPackPathCarriesABackslash()
    {
        using var repository = new TemporaryRepository();
        repository.WriteSettings("schemaVersion = 1\n\n[packs]\nlocal = [\"packs\\\\rules.json\"]\n");

        var load = ConfigurationLoader.Load(repository.Root);

        load.Settings.Should().BeNull();
        load.Errors.Should().Contain(e => e.Contains("packs.local"));
    }

    [Fact]
    public void ConfigurationLoader_Should_Error_When_ALocalPackPathCarriesALeadingSlash()
    {
        using var repository = new TemporaryRepository();
        repository.WriteSettings("schemaVersion = 1\n\n[packs]\nlocal = [\"/abs/rules.json\"]\n");

        var load = ConfigurationLoader.Load(repository.Root);

        load.Settings.Should().BeNull();
        load.Errors.Should().Contain(e => e.Contains("packs.local"));
    }

    [Fact]
    public void ConfigurationLoader_Should_HonourEveryKey_When_TheSpecificationExampleIsPresented()
    {
        using var repository = new TemporaryRepository();
        repository.WriteSettings("""
            schemaVersion = 1

            fail-on = "warning"

            [repository]
            id = "github.com/acme/payments"

            [scan]
            profile = "deep"
            history = false
            maxFileBytes = 1048576
            includeSubmodules = true
            exclude = ["scratch/**"]

            [baseline]
            maxAgeDays = 90

            [severity.overrides]
            "pii.phone" = "warning"

            [[rules.disabled]]
            id = "pii.email"
            reason = "Fixture values are synthetic and reviewed each quarter."

            [packs]
            local = [".gatekeeper/packs/acme-customer-id.json"]
            """);

        var load = ConfigurationLoader.Load(repository.Root);

        load.Errors.Should().BeEmpty();
        load.Warnings.Should().BeEmpty();
        var settings = load.Settings.Should().BeOfType<ScanSettings>().Which;
        settings.Profile.Should().Be(ScanProfile.Deep);
        settings.ScanHistory.Should().BeFalse();
        settings.MaxFileBytes.Should().Be(DefaultFileLimit);
        settings.IncludeSubmodules.Should().BeTrue();
        settings.Exclude.Should().Contain("scratch/**");
        settings.RepositoryId.Should().Be("github.com/acme/payments");
        settings.BaselineMaxAgeDays.Should().Be(90);
        settings.FailOn.Should().Be(Severity.Warning);
        settings.SeverityOverrides["pii.phone"].Should().Be(Severity.Warning);
        settings.DisabledRules.Should().Contain(e => e.Id == "pii.email");
        settings.LocalPacks.Should().Contain(".gatekeeper/packs/acme-customer-id.json");
    }

    /// <summary>
    /// A throw-away repository folder with the configuration directory. Each
    /// instance uses a fresh folder name, so tests never depend on the order
    /// they run in or on leftovers from previous runs.
    /// </summary>
    private sealed class TemporaryRepository : IDisposable
    {
        public TemporaryRepository()
        {
            var root = Path.Combine(
                Path.GetTempPath(),
                "gatekeeper-p1-08",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            Root = root;
        }

        public string Root { get; }

        public void WriteSettings(string content)
        {
            File.WriteAllText(SettingsPath(), content);
        }

        public void WriteSettingsBytes(byte[] bytes)
        {
            File.WriteAllBytes(SettingsPath(), bytes);
        }

        private string SettingsPath()
        {
            var folder = Path.Combine(Root, ".gatekeeper");
            Directory.CreateDirectory(folder);
            return Path.Combine(folder, "gatekeeper.toml");
        }

        public void Dispose()
        {
            try
            {
                Directory.Delete(Root, true);
            }
            catch (DirectoryNotFoundException)
            {
                // Already gone; nothing to clean.
            }
        }
    }
}
