// SPDX-License-Identifier: Apache-2.0

using InvenioTech.GateKeeper.Engine;

namespace InvenioTech.GateKeeper.Middleware.Tests;

public sealed class RuntimePacksTests
{
    [Fact]
    public void RuntimePacks_Should_ReturnTheVerifiedPack_When_OneEmbeddedPackVerifies()
    {
        var rules = RuntimePacks.Load([TestPacks.SignedPack("pack")], TestPacks.Catalogue);

        var pack = rules.Packs.Should().ContainSingle().Subject;
        pack.Id.Should().Be("invenio.ng.example.p1");
        pack.Version.Should().Be("2027.1.0");
        pack.Provenance.Should().Be(PackProvenance.Bundled);
        rules.Rules.Select(item => item.Rule.Id).Should().Equal("pii.bvn");
    }

    [Fact]
    public void RuntimePacks_Should_AdmitPlanGatedContent_When_NoLicenceIsConsulted()
    {
        var rules = RuntimePacks.Load([TestPacks.SignedPack("plan-gated")], TestPacks.Catalogue);

        var pack = rules.Packs.Should().ContainSingle().Subject;
        pack.Id.Should().Be("invenio.ng.example.gated");
        pack.Provenance.Should().Be(PackProvenance.PlanGated);
        rules.Rules.Select(item => item.Rule.Id).Should().Equal("pii.bvn");
    }

    [Fact]
    public void RuntimePacks_Should_ReplaceTheEmbeddedPack_When_TheSameIdentifierIsStrictlyNewer()
    {
        var rules = RuntimePacks.Load(
            [TestPacks.SignedPack("pack")],
            TestPacks.Catalogue,
            [TestPacks.SignedPack("next-line")]);

        var pack = rules.Packs.Should().ContainSingle().Subject;
        pack.Id.Should().Be("invenio.ng.example.p1");
        pack.Version.Should().Be("2027.2.0");
        rules.Rules.Select(item => item.Rule.Id).Should().Equal("pii.bvn", "pii.nin");
    }

    [Fact]
    public void RuntimePacks_Should_KeepTheVerifiedEmbeddedPack_When_AReplacementSignatureFails()
    {
        var diagnostics = new List<string>();
        var rules = RuntimePacks.Load(
            [TestPacks.SignedPack("pack")],
            TestPacks.Catalogue,
            [TestPacks.SignedPack("tampered-signature")],
            diagnostics.Add);

        rules.Packs.Should().ContainSingle().Which.Version.Should().Be("2027.1.0");
        diagnostics.Should().ContainSingle().Which.Should().Contain("tampered-signature");
    }

    [Fact]
    public void RuntimePacks_Should_ThrowPackLoadException_When_AReplacementHasTheSameVersion()
    {
        var load = () => RuntimePacks.Load(
            [TestPacks.SignedPack("pack")],
            TestPacks.Catalogue,
            [TestPacks.SignedPack("conflict")]);

        load.Should().Throw<PackLoadException>()
            .Which.Message.Should().Contain("invenio.ng.example.p1").And.Contain("2027.1.0").And.Contain("not newer");
    }

    [Fact]
    public void RuntimePacks_Should_ThrowPackLoadException_When_AReplacementIsOlder()
    {
        var load = () => RuntimePacks.Load(
            [TestPacks.SignedPack("next-line")],
            TestPacks.Catalogue,
            [TestPacks.SignedPack("pack")]);

        load.Should().Throw<PackLoadException>()
            .Which.Message.Should().Contain("2027.1.0").And.Contain("2027.2.0").And.Contain("not newer");
    }

    [Fact]
    public void RuntimePacks_Should_ThrowPackLoadException_When_AReplacementHasADifferentIdentifier()
    {
        var load = () => RuntimePacks.Load(
            [TestPacks.SignedPack("pack")],
            TestPacks.Catalogue,
            [TestPacks.SignedPack("rival")]);

        load.Should().Throw<PackLoadException>()
            .Which.Message.Should().Contain("invenio.ng.example.rival").And.Contain("identifier");
    }

    [Fact]
    public void RuntimePacks_Should_ThrowPackLoadException_When_NoEmbeddedPackVerifies()
    {
        var load = () => RuntimePacks.Load([TestPacks.SignedPack("tampered-signature")], TestPacks.Catalogue);

        load.Should().Throw<PackLoadException>()
            .Which.Message.Should().Contain("tampered-signature").And.Contain("reinstall");
    }

    [Fact]
    public void RuntimePacks_Should_ThrowPackLoadException_When_NoPackIsSupplied()
    {
        var load = () => RuntimePacks.Load([], TestPacks.Catalogue);

        load.Should().Throw<PackLoadException>().Which.Message.Should().Contain("no signed rule pack");
    }

    [Fact]
    public void RuntimePacks_Should_LoadTheReplacementSequence_When_TheDirectoryCarriesIt()
    {
        using var directory = new TempDirectory();
        WriteSequence(directory.Path, "replacement", TestPacks.SignedPack("next-line"));

        var rules = RuntimePacks.LoadFromDirectory([TestPacks.SignedPack("pack")], TestPacks.Catalogue, directory.Path);

        rules.Packs.Should().ContainSingle().Which.Version.Should().Be("2027.2.0");
    }

    [Fact]
    public void RuntimePacks_Should_KeepTheEmbeddedPack_When_TheDirectoryCarriesAnUnsignedPack()
    {
        using var directory = new TempDirectory();
        File.WriteAllBytes(Path.Combine(directory.Path, "loose.pack.json"), TestPacks.ReadContent("next-line"));
        var diagnostics = new List<string>();

        var rules = RuntimePacks.LoadFromDirectory(
            [TestPacks.SignedPack("pack")],
            TestPacks.Catalogue,
            directory.Path,
            diagnostics.Add);

        rules.Packs.Should().ContainSingle().Which.Version.Should().Be("2027.1.0");
        diagnostics.Should().ContainSingle().Which.Should().Contain("loose");
    }

    [Fact]
    public void RuntimePacks_Should_ThrowPackLoadException_When_TheDirectoryDoesNotExist()
    {
        using var directory = new TempDirectory();
        var missing = Path.Combine(directory.Path, "missing");

        var load = () => RuntimePacks.LoadFromDirectory([TestPacks.SignedPack("pack")], TestPacks.Catalogue, missing);

        load.Should().Throw<PackLoadException>().Which.Message.Should().Contain("does not exist");
    }

    [Fact]
    public void RuntimePacks_Should_LoadTheWatermarkedReplacement_When_ThePairBindsToItsContent()
    {
        using var directory = new TempDirectory();
        var content = TestPacks.ReadContent("next-line");
        var signed = TestPacks.SignInMemory(content, "watermarked");
        WriteSequence(directory.Path, "watermarked", signed, TestPacks.SignWatermarkInMemory(content));

        var rules = RuntimePacks.LoadFromDirectory([TestPacks.SignedPack("pack")], TestPacks.Catalogue, directory.Path);

        rules.Packs.Should().ContainSingle().Which.Version.Should().Be("2027.2.0");
    }

    [Fact]
    public void RuntimePacks_Should_KeepTheEmbeddedPack_When_TheWatermarkDoesNotBindToTheContent()
    {
        using var directory = new TempDirectory();
        var content = TestPacks.ReadContent("next-line");
        var signed = TestPacks.SignInMemory(content, "mismatched-watermark");
        WriteSequence(directory.Path, "mismatched-watermark", signed,
            TestPacks.SignWatermarkInMemory(TestPacks.ReadContent("pack")));
        var diagnostics = new List<string>();

        var rules = RuntimePacks.LoadFromDirectory(
            [TestPacks.SignedPack("pack")],
            TestPacks.Catalogue,
            directory.Path,
            diagnostics.Add);

        rules.Packs.Should().ContainSingle().Which.Version.Should().Be("2027.1.0");
        diagnostics.Should().ContainSingle().Which.Should().Contain("watermark");
    }

    private static void WriteSequence(
        string directory,
        string stem,
        SignedPackInput input,
        (string Armour, byte[] Signature)? watermark = null)
    {
        File.WriteAllBytes(Path.Combine(directory, stem + ".pack.json"), input.ContentBytes);
        File.WriteAllText(Path.Combine(directory, stem + ".manifest.armour"), input.ManifestArmour);
        File.WriteAllText(Path.Combine(directory, stem + ".signature.hex"), Convert.ToHexString(input.ManifestSignature));
        if (watermark is { } pair)
        {
            File.WriteAllText(Path.Combine(directory, stem + ".watermark.armour"), pair.Armour);
            File.WriteAllText(Path.Combine(directory, stem + ".watermark.signature.hex"), Convert.ToHexString(pair.Signature));
        }
    }
}
