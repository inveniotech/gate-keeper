// SPDX-License-Identifier: Apache-2.0

using InvenioTech.GateKeeper.Sample.AspNetCore;

namespace InvenioTech.GateKeeper.Middleware.Tests;

public sealed class SampleAcceptanceTests
{
    private const string CommittedSyntheticValue = "32826314843";

    [Fact]
    public void Sample_Should_RedactBothLoggingPaths_When_ItStartsWithTheSignedPack()
    {
        using var pack = new TempDirectory();
        WriteSignedPack(pack.Path);
        using var keys = new TempDirectory();
        var keyFile = TestPacks.WritePublicCatalogue(Path.Combine(keys.Path, "catalogue.json"));
        var output = new StringWriter();
        var error = new StringWriter();

        var exit = SampleApplication.Run(pack.Path, keyFile, output, error);

        exit.Should().Be(0);
        error.ToString().Should().BeEmpty();
        var lines = output.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        lines.Should().HaveCount(2);
        lines.Should().OnlyContain(line => line.Contains("[REDACTED:pii.bvn]"));
        lines.Should().OnlyContain(line => !line.Contains(CommittedSyntheticValue));
    }

    [Fact]
    public void Sample_Should_RefuseToStart_When_ThePackDirectoryIsMissing()
    {
        using var pack = new TempDirectory();
        using var keys = new TempDirectory();
        var keyFile = TestPacks.WritePublicCatalogue(Path.Combine(keys.Path, "catalogue.json"));
        var output = new StringWriter();
        var error = new StringWriter();

        var exit = SampleApplication.Run(Path.Combine(pack.Path, "absent"), keyFile, output, error);

        exit.Should().NotBe(0);
        output.ToString().Should().BeEmpty();
        error.ToString().Should().Contain("does not exist");
    }

    [Fact]
    public void Sample_Should_RefuseToStart_When_ThePackDirectoryCarriesNoSignedPack()
    {
        using var pack = new TempDirectory();
        using var keys = new TempDirectory();
        var keyFile = TestPacks.WritePublicCatalogue(Path.Combine(keys.Path, "catalogue.json"));
        var output = new StringWriter();
        var error = new StringWriter();

        var exit = SampleApplication.Run(pack.Path, keyFile, output, error);

        exit.Should().NotBe(0);
        output.ToString().Should().BeEmpty();
        error.ToString().Should().Contain("carries no signed pack");
    }

    [Fact]
    public void Sample_Should_RefuseToStart_When_ThePackSignatureIsTampered()
    {
        using var pack = new TempDirectory();
        WriteSignedPack(pack.Path);
        var signaturePath = Path.Combine(pack.Path, "bvn.signature.hex");
        var tampered = File.ReadAllText(signaturePath).Trim().ToCharArray();
        tampered[0] = tampered[0] == '0' ? '1' : '0';
        File.WriteAllText(signaturePath, new string(tampered));
        using var keys = new TempDirectory();
        var keyFile = TestPacks.WritePublicCatalogue(Path.Combine(keys.Path, "catalogue.json"));
        var output = new StringWriter();
        var error = new StringWriter();

        var exit = SampleApplication.Run(pack.Path, keyFile, output, error);

        exit.Should().NotBe(0);
        output.ToString().Should().BeEmpty();
        error.ToString().Should().Contain("did not verify");
    }

    [Fact]
    public void Sample_Should_RefuseTheCatalogue_When_ItCarriesPrivateKeyMaterial()
    {
        using var pack = new TempDirectory();
        WriteSignedPack(pack.Path);
        var output = new StringWriter();
        var error = new StringWriter();

        var exit = SampleApplication.Run(pack.Path, TestPacks.CataloguePath, output, error);

        exit.Should().NotBe(0);
        output.ToString().Should().BeEmpty();
        error.ToString().Should().Contain("private key material");
    }

    [Fact]
    public void Sample_Should_RefuseToStart_When_TheCatalogueDoesNotCarryTheSigningKey()
    {
        using var pack = new TempDirectory();
        var content = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "packs", "bvn.pack.json"));
        var signed = TestPacks.SignInMemory(content, "bvn", "gkpk-other", TestPacks.SigningKeyId);
        File.WriteAllBytes(Path.Combine(pack.Path, "bvn.pack.json"), content);
        File.WriteAllText(Path.Combine(pack.Path, "bvn.manifest.armour"), signed.ManifestArmour);
        File.WriteAllText(Path.Combine(pack.Path, "bvn.signature.hex"), Convert.ToHexString(signed.ManifestSignature));
        using var keys = new TempDirectory();
        var keyFile = TestPacks.WritePublicCatalogue(Path.Combine(keys.Path, "catalogue.json"));
        var output = new StringWriter();
        var error = new StringWriter();

        var exit = SampleApplication.Run(pack.Path, keyFile, output, error);

        exit.Should().NotBe(0);
        output.ToString().Should().BeEmpty();
        error.ToString().Should().Contain("key catalogue does not carry");
    }

    private static void WriteSignedPack(string directory)
    {
        var content = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "packs", "bvn.pack.json"));
        var signed = TestPacks.SignInMemory(content, "bvn");
        File.WriteAllBytes(Path.Combine(directory, "bvn.pack.json"), content);
        File.WriteAllText(Path.Combine(directory, "bvn.manifest.armour"), signed.ManifestArmour);
        File.WriteAllText(Path.Combine(directory, "bvn.signature.hex"), Convert.ToHexString(signed.ManifestSignature));
    }
}
