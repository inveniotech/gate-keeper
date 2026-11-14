// SPDX-License-Identifier: Apache-2.0

using System.Security.Cryptography;
using System.Text;
using InvenioTech.GateKeeper.Engine;

namespace InvenioTech.GateKeeper.Engine.Tests;

/// <summary>
/// Behaviour tests for the pack loader (card P1-03; IQ-24, IQ-25, IQ-36).
/// The acceptance tests of the card are here: a valid pack loads, one tampered
/// byte fails loudly with exit code 2 phrased sentences, an unknown key
/// identifier fails, a newer schema major fails with an upgrade sentence and
/// duplicate rule identifiers behave as IQ-24 decides. Signed scenarios read
/// the committed development vectors; local-pack scenarios write small
/// unsigned documents with synthetic identifiers (rule 10).
/// </summary>
public sealed class PackLoaderTests
{
    private static readonly IPackEntitlementPolicy BundledOnly = BundledPacksOnlyPolicy.Instance;

    // --- The signed path -----------------------------------------------------

    [Fact]
    public void PackLoader_Should_LoadTheSignedPackWithItsRulesAndMatcher_When_TheBytesAgree()
    {
        var input = PackTestSupport.SignedPack("pack");

        var load = PackLoader.Load([input], [], PackTestSupport.Catalogue, BundledOnly);

        load.Packs.Should().ContainSingle();
        var pack = load.Packs[0];
        pack.Id.Should().Be("invenio.ng.example.p1");
        pack.Version.Should().Be("2027.1.0");
        pack.CoverageDate.Should().Be("2027-01-01");
        pack.Provenance.Should().Be(PackProvenance.Bundled);
        pack.ContentDigest.Should().Be(LowerHex(SHA256.HashData(PackTestSupport.ReadContent("pack"))));
        load.Rules.Should().ContainSingle();
        var rule = load.Rules[0];
        rule.Rule.Id.Should().Be("pii.bvn");
        rule.Rule.Severity.Should().Be(Severity.Error);
        rule.Rule.Category.Should().Be("bvn");
        rule.Rule.Citation.Title.Should().Contain("Placeholder citation");
        rule.Rule.SinceVersion.Should().Be("2027.1.0");
        rule.Rule.Redaction.Should().BeNull();
        rule.Rule.Profiles.Should().BeNull();
        rule.Pack.Should().BeSameAs(pack);
        rule.Rule.Matcher.Should().NotBeNull();
        var pattern = rule.Rule.Matcher!.Pattern;
        pattern.Should().NotBeNull();
        pattern!.Boundary.Should().Be(MatchBoundary.Digit);
        pattern.Shape.Should().ContainSingle();
        var part = pattern.Shape[0];
        part.Min.Should().Be(11);
        part.Max.Should().Be(11);
        part.Class.Should().Be(ShapeClass.Digit);
        part.Emit.Should().BeTrue();
        var validator = pattern.Validator;
        validator.Should().NotBeNull();
        validator!.Name.Should().Be(RuleValidatorName.DigitsLength);
        validator.Length.Should().Be(11);
    }

    [Fact]
    public void PackLoader_Should_FailLoudlyNamingTheDigest_When_ASignedContentByteIsTampered()
    {
        var input = PackTestSupport.SignedPack("tampered-content");

        Action load = () => PackLoader.Load([input], [], PackTestSupport.Catalogue, BundledOnly);

        load.Should().Throw<EngineToolException>()
            .Which.Message.Should().Contain("'tampered-content'").And
            .Contain("SHA-256").And.Contain("manifest");
    }

    [Fact]
    public void PackLoader_Should_FailLoudly_When_TheManifestSignatureIsTamperedByOneByte()
    {
        var input = PackTestSupport.SignedPack("tampered-signature");

        Action load = () => PackLoader.Load([input], [], PackTestSupport.Catalogue, BundledOnly);

        load.Should().Throw<EngineToolException>()
            .Which.Message.Should().Contain("'tampered-signature'").And
            .Contain("signature");
    }

    [Fact]
    public void PackLoader_Should_RefuseAnUnknownKeyIdBeforeVerifying_When_TheCatalogueCarriesNoSuchKey()
    {
        var input = PackTestSupport.SignedPack("pack") with
        {
            ManifestArmour = PackTestSupport.ReadManifestArmour("unknown-key"),
            ManifestSignature = PackTestSupport.ReadSignature("unknown-key"),
        };

        Action load = () => PackLoader.Load([input], [], PackTestSupport.Catalogue, BundledOnly);

        load.Should().Throw<EngineToolException>()
            .Which.Message.Should().Contain("gkpk-unknown").And
            .Contain("never approved");
    }

    [Fact]
    public void PackLoader_Should_RefuseTheManifest_When_TheArmourHeaderAndPayloadKeyIdsDisagree()
    {
        var input = PackTestSupport.SignedPack("pack") with
        {
            ManifestArmour = PackTestSupport.ReadManifestArmour("key-id-mismatch"),
            ManifestSignature = PackTestSupport.ReadSignature("key-id-mismatch"),
        };

        Action load = () => PackLoader.Load([input], [], PackTestSupport.Catalogue, BundledOnly);

        load.Should().Throw<EngineToolException>()
            .Which.Message.Should().Contain("gkpk-dev-1").And
            .Contain("gkpk-other").And.Contain("agree");
    }

    [Theory]
    [InlineData("begin-label")]
    [InlineData("missing-header")]
    [InlineData("missing-end")]
    public void PackLoader_Should_RefuseMalformedArmour_When_TheManifestIsNotTheFixedLayout(string mutation)
    {
        var input = PackTestSupport.SignedPack("pack") with
        {
            ManifestArmour = MutateArmour(PackTestSupport.ReadManifestArmour("pack"), mutation),
        };

        Action load = () => PackLoader.Load([input], [], PackTestSupport.Catalogue, BundledOnly);

        load.Should().Throw<EngineToolException>()
            .Which.Message.Should().Contain("manifest").And.Contain("'pack'");
    }

    [Fact]
    public void PackLoader_Should_RefuseAnOversizedManifest_When_ItsPayloadIsOver4096Bytes()
    {
        var payload = "{\"keyId\":\"gkpk-dev-1\",\"packId\":\"" + new string('x', 4_200) +
            "\",\"version\":\"2027.1.0\",\"contentHashes\":{\"sha256\":\"" + new string('0', 64) +
            "\",\"sha512\":\"" + new string('0', 128) + "\"}}";
        var armour =
            "-----BEGIN GATE-KEEPER RULE PACK MANIFEST-----\n" +
            "Key-Id: gkpk-dev-1\n" +
            payload + "\n" +
            "-----END GATE-KEEPER RULE PACK MANIFEST-----\n";
        var input = PackTestSupport.SignedPack("pack") with { ManifestArmour = armour, ManifestSignature = new byte[64] };

        Action load = () => PackLoader.Load([input], [], PackTestSupport.Catalogue, BundledOnly);

        load.Should().Throw<EngineToolException>()
            .Which.Message.Should().Contain("4,096").And.Contain("'pack'");
    }

    [Fact]
    public void PackLoader_Should_RefuseANewerSchemaVersionWithAnUpgradeSentence_When_TheContentDeclaresOne()
    {
        var input = PackTestSupport.SignedPack("schema-major");

        Action load = () => PackLoader.Load([input], [], PackTestSupport.Catalogue, BundledOnly);

        load.Should().Throw<EngineToolException>()
            .Which.Message.Should().Contain("Upgrade the CLI").And
            .Contain("schema version 2").And.Contain("'schema-major'");
    }

    [Fact]
    public void PackLoader_Should_RefuseAPackForANewerEngine_When_ItsMinEngineVersionIsNewerThanTheBuild()
    {
        var input = PackTestSupport.SignedPack("newer-engine");

        Action load = () => PackLoader.Load([input], [], PackTestSupport.Catalogue, BundledOnly);

        load.Should().Throw<EngineToolException>()
            .Which.Message.Should().Contain("100.0.0").And
            .Contain(EngineProduct.EngineVersion).And.Contain("'newer-engine'");
    }

    [Fact]
    public void PackLoader_Should_RefuseAPlanGatedPack_When_ThePolicyAdmitsBundledPacksOnly()
    {
        var input = PackTestSupport.SignedPack("plan-gated");

        Action load = () => PackLoader.Load([input], [], PackTestSupport.Catalogue, BundledOnly);

        load.Should().Throw<EngineToolException>()
            .Which.Message.Should().Contain("plan-gated").And
            .Contain("invenio.ng.example.gated");
    }

    // --- Merging (IQ-24) ------------------------------------------------------

    [Fact]
    public void PackLoader_Should_KeepTheNewerVersionAsAWhole_When_TwoCopiesOfOnePackLoad()
    {
        var load = PackLoader.Load(
            [PackTestSupport.SignedPack("pack"), PackTestSupport.SignedPack("next-line")],
            [],
            PackTestSupport.Catalogue,
            BundledOnly);

        load.Packs.Should().ContainSingle();
        load.Packs[0].Version.Should().Be("2027.2.0");
        load.Packs[0].ContentDigest.Should().Be(LowerHex(SHA256.HashData(PackTestSupport.ReadContent("next-line"))));
        load.Rules.Should().HaveCount(2);
        load.Rules.Select(rule => rule.Rule.Id).Should().Equal(["pii.bvn", "pii.nin"]);
    }

    [Fact]
    public void PackLoader_Should_MergeToTheSameResult_When_TheSameCopiesArriveInReverseOrder()
    {
        var forward = PackLoader.Load(
            [PackTestSupport.SignedPack("pack"), PackTestSupport.SignedPack("next-line")],
            [],
            PackTestSupport.Catalogue,
            BundledOnly);
        var reverse = PackLoader.Load(
            [PackTestSupport.SignedPack("next-line"), PackTestSupport.SignedPack("pack")],
            [],
            PackTestSupport.Catalogue,
            BundledOnly);

        reverse.Packs[0].Version.Should().Be("2027.2.0");
        reverse.Packs[0].ContentDigest.Should().Be(forward.Packs[0].ContentDigest);
        reverse.Rules.Select(rule => rule.Rule.Id).Should().Equal(["pii.bvn", "pii.nin"]);
    }

    [Fact]
    public void PackLoader_Should_KeepOneCopy_When_TwoIdenticalCopiesLoad()
    {
        var load = PackLoader.Load(
            [PackTestSupport.SignedPack("pack"), PackTestSupport.SignedPack("pack")],
            [],
            PackTestSupport.Catalogue,
            BundledOnly);

        load.Packs.Should().ContainSingle();
        load.Rules.Should().ContainSingle();
    }

    [Fact]
    public void PackLoader_Should_FailLoudlyOnTheDigestConflict_When_TwoCopiesShareAnIdAndVersionButNotTheContent()
    {
        var load = () => PackLoader.Load(
            [PackTestSupport.SignedPack("pack"), PackTestSupport.SignedPack("conflict")],
            [],
            PackTestSupport.Catalogue,
            BundledOnly);

        load.Should().Throw<EngineToolException>()
            .Which.Message.Should().Contain("invenio.ng.example.p1").And
            .Contain("2027.1.0").And.Contain("digest");
    }

    [Fact]
    public void PackLoader_Should_FailLoudlyOnADuplicateRuleIdentifier_When_DifferentPacksCarryTheSameRule()
    {
        var load = () => PackLoader.Load(
            [PackTestSupport.SignedPack("pack"), PackTestSupport.SignedPack("rival")],
            [],
            PackTestSupport.Catalogue,
            BundledOnly);

        load.Should().Throw<EngineToolException>()
            .Which.Message.Should().Contain("pii.bvn").And
            .Contain("invenio.ng.example.p1").And
            .Contain("invenio.ng.example.rival");
    }

    // --- The detached watermark (section 2.3) ---------------------------------

    [Fact]
    public void PackLoader_Should_KeepThePackWithVerifiedWatermark_When_TheDetachedBlockMatchesTheDigest()
    {
        var input = PackTestSupport.SignedPack("pack", "watermark");

        var load = PackLoader.Load([input], [], PackTestSupport.Catalogue, BundledOnly);

        load.Packs.Should().ContainSingle();
        load.Packs[0].Id.Should().Be("invenio.ng.example.p1");
    }

    [Fact]
    public void PackLoader_Should_RefuseThePack_When_TheWatermarkDigestDoesNotMatchTheContent()
    {
        var input = PackTestSupport.SignedPack("pack") with
        {
            WatermarkArmour = PackTestSupport.ReadManifestArmour("watermark-digest-mismatch"),
            WatermarkSignature = PackTestSupport.ReadSignature("watermark-digest-mismatch"),
        };

        Action load = () => PackLoader.Load([input], [], PackTestSupport.Catalogue, BundledOnly);

        load.Should().Throw<EngineToolException>()
            .Which.Message.Should().Contain("watermark").And
            .Contain("'pack'");
    }

    [Fact]
    public void PackLoader_Should_RefuseThePack_When_TheWatermarkSignatureDoesNotVerify()
    {
        var input = PackTestSupport.SignedPack("pack") with
        {
            WatermarkArmour = PackTestSupport.ReadManifestArmour("watermark"),
            WatermarkSignature = PackTestSupport.ReadSignature("watermark-tampered-signature"),
        };

        Action load = () => PackLoader.Load([input], [], PackTestSupport.Catalogue, BundledOnly);

        load.Should().Throw<EngineToolException>()
            .Which.Message.Should().Contain("watermark").And
            .Contain("'pack'");
    }

    // --- The Invenio content checks on signed bytes ----------------------------

    [Fact]
    public void PackLoader_Should_RefuseACoverageDateAfterTheIssueDate_When_TheCorrectionCarriesIt()
    {
        var input = PackTestSupport.SignedPack("coverage-after-issued");

        Action load = () => PackLoader.Load([input], [], PackTestSupport.Catalogue, BundledOnly);

        load.Should().Throw<EngineToolException>()
            .Which.Message.Should().Contain("2027-03-01").And
            .Contain("2027-02-01").And.Contain("'coverage-after-issued'");
    }

    [Fact]
    public void PackLoader_Should_RefuseABaseVersionOffTheLine_When_TheCorrectionCarriesIt()
    {
        var input = PackTestSupport.SignedPack("base-version-wrong");

        Action load = () => PackLoader.Load([input], [], PackTestSupport.Catalogue, BundledOnly);

        load.Should().Throw<EngineToolException>()
            .Which.Message.Should().Contain("2027.1.0").And
            .Contain("2027.3.1").And.Contain("'base-version-wrong'");
    }

    [Fact]
    public void PackLoader_Should_RefuseALocalRuleIdentifierInABundledPack_When_TheContentCarriesOne()
    {
        var input = PackTestSupport.SignedPack("local-rule-in-bundled");

        Action load = () => PackLoader.Load([input], [], PackTestSupport.Catalogue, BundledOnly);

        load.Should().Throw<EngineToolException>()
            .Which.Message.Should().Contain("local.synthetic-code").And
            .Contain("'local-rule-in-bundled'");
    }

    // --- Local packs (IQ-25) ---------------------------------------------------

    [Fact]
    public void PackLoader_Should_LoadAnUnsignedLocalPack_When_ItsIdentifiersStartWithLocalDot()
    {
        var bytes = PackTestSupport.LocalPack(
            "local.example",
            "2",
            PackTestSupport.LocalRule("local.synthetic.digit", PackTestSupport.DigitMatcherJson));
        var input = new LocalPackInput("local.example.json", bytes);

        var load = PackLoader.Load([], [input], PackTestSupport.Catalogue, BundledOnly);

        load.Packs.Should().ContainSingle();
        load.Packs[0].Id.Should().Be("local.example");
        load.Packs[0].Version.Should().Be("2");
        load.Packs[0].Provenance.Should().Be(PackProvenance.Local);
        var rule = load.Rules.Should().ContainSingle().Which;
        rule.Pack.Should().BeSameAs(load.Packs[0]);
        rule.Rule.Id.Should().Be("local.synthetic.digit");
        rule.Rule.SinceVersion.Should().Be("2");
        rule.Rule.Matcher.Should().NotBeNull();
        rule.Rule.Matcher!.Pattern.Should().NotBeNull();
    }

    [Fact]
    public void PackLoader_Should_ReadAnEmailBoundary_When_ARuleDeclaresOne()
    {
        const string matcher = """{ "pattern": { "shape": [ { "min": 11, "max": 11, "class": "digit", "emit": true } ], "boundary": "email" } }""";
        var input = new LocalPackInput("local.email.json", PackTestSupport.LocalPack(
            "local.example",
            "2",
            PackTestSupport.LocalRule("local.synthetic.email", matcher)));

        var load = PackLoader.Load([], [input], PackTestSupport.Catalogue, BundledOnly);

        var pattern = load.Rules.Should().ContainSingle().Which.Rule.Matcher!.Pattern;
        pattern!.Boundary.Should().Be(MatchBoundary.Email);
    }

    [Fact]
    public void PackLoader_Should_NameEveryAcceptedBoundary_When_ARuleDeclaresAnUnknownOne()
    {
        const string matcher = """{ "pattern": { "shape": [ { "min": 11, "max": 11, "class": "digit", "emit": true } ], "boundary": "token" } }""";
        var input = new LocalPackInput("local.email.json", PackTestSupport.LocalPack(
            "local.example",
            "2",
            PackTestSupport.LocalRule("local.synthetic.email", matcher)));

        Action load = () => PackLoader.Load([], [input], PackTestSupport.Catalogue, BundledOnly);

        load.Should().Throw<EngineToolException>()
            .Which.Message.Should().Contain("local.synthetic.email").And
            .Contain("'none'").And.Contain("'digit'").And.Contain("'alnum'").And.Contain("'email'").And
            .Contain("(section 5.1)");
    }

    [Fact]
    public void PackLoader_Should_RefuseALocalPack_When_ARuleIdentifierDoesNotStartWithLocalDot()
    {
        var input = new LocalPackInput("local.example.json", PackTestSupport.LocalPack(
            "local.example",
            "2",
            PackTestSupport.LocalRule("pii.bvn", PackTestSupport.DigitMatcherJson)));

        Action load = () => PackLoader.Load([], [input], PackTestSupport.Catalogue, BundledOnly);

        load.Should().Throw<EngineToolException>()
            .Which.Message.Should().Contain("pii.bvn").And.Contain("local.");
    }

    [Fact]
    public void PackLoader_Should_RefuseALocalPack_When_ItsVersionIsNotAPositiveInteger()
    {
        var input = new LocalPackInput("local.example.json", PackTestSupport.LocalPack(
            "local.example",
            "\"2027.1.0\"",
            PackTestSupport.LocalRule("local.synthetic.digit", PackTestSupport.DigitMatcherJson)));

        Action load = () => PackLoader.Load([], [input], PackTestSupport.Catalogue, BundledOnly);

        load.Should().Throw<EngineToolException>()
            .Which.Message.Should().Contain("positive integer").And.Contain("'local.example.json'");
    }

    [Fact]
    public void PackLoader_Should_RefuseAnInvenioPack_When_ItIsGivenAsALocalPack()
    {
        var input = new LocalPackInput("invenio-copy.json", PackTestSupport.ReadContent("pack"));

        Action load = () => PackLoader.Load([], [input], PackTestSupport.Catalogue, BundledOnly);

        load.Should().Throw<EngineToolException>()
            .Which.Message.Should().Contain("provenance").And.Contain("'invenio-copy.json'");
    }

    [Fact]
    public void PackLoader_Should_RefuseALocalPackClaimingAnInvenioPackIdentifier_When_BothLoad()
    {
        var bytes = PackTestSupport.LocalPack(
            "invenio.ng.example.p1",
            "2",
            PackTestSupport.LocalRule("local.synthetic.digit", PackTestSupport.DigitMatcherJson));
        var local = new LocalPackInput("local-claim.json", bytes);

        Action load = () => PackLoader.Load(
            [PackTestSupport.SignedPack("pack")],
            [local],
            PackTestSupport.Catalogue,
            BundledOnly);

        load.Should().Throw<EngineToolException>()
            .Which.Message.Should().Contain("invenio.ng.example.p1").And
            .Contain("local-claim.json");
    }

    [Fact]
    public void PackLoader_Should_FailLoudlyOnADuplicateRuleIdentifier_When_TwoLocalPacksCarryTheSameRule()
    {
        var first = new LocalPackInput("local-one.json", PackTestSupport.LocalPack(
            "local.one",
            "1",
            PackTestSupport.LocalRule("local.synthetic.digit", PackTestSupport.DigitMatcherJson)));
        var second = new LocalPackInput("local-two.json", PackTestSupport.LocalPack(
            "local.two",
            "1",
            PackTestSupport.LocalRule("local.synthetic.digit", PackTestSupport.DigitMatcherJson)));

        Action load = () => PackLoader.Load([], [first, second], PackTestSupport.Catalogue, BundledOnly);

        load.Should().Throw<EngineToolException>()
            .Which.Message.Should().Contain("local.synthetic.digit").And
            .Contain("'local.one'").And.Contain("'local.two'");
    }

    [Fact]
    public void PackLoader_Should_FailLoudlyOnADuplicateRuleIdentifier_When_OneLocalPackCarriesItTwice()
    {
        var input = new LocalPackInput("local-one.json", PackTestSupport.LocalPack(
            "local.one",
            "1",
            PackTestSupport.LocalRule("local.synthetic.digit", PackTestSupport.DigitMatcherJson),
            PackTestSupport.LocalRule("local.synthetic.digit", PackTestSupport.DigitMatcherJson)));

        Action load = () => PackLoader.Load([], [input], PackTestSupport.Catalogue, BundledOnly);

        load.Should().Throw<EngineToolException>()
            .Which.Message.Should().Contain("local.synthetic.digit").And.Contain("local-one.json");
    }

    [Fact]
    public void PackLoader_Should_KeepTheNewerLocalVersionAsAWhole_When_TwoLocalVersionsOfOnePackLoad()
    {
        var older = new LocalPackInput("local-one.json", LocalVersion("1", one: true));
        var newer = new LocalPackInput("local-two.json", LocalVersion("2", one: false));

        var load = PackLoader.Load([], [older, newer], PackTestSupport.Catalogue, BundledOnly);

        load.Packs.Should().ContainSingle();
        load.Packs[0].Id.Should().Be("local.one");
        load.Packs[0].Version.Should().Be("2");
        load.Rules.Should().ContainSingle();

        static byte[] LocalVersion(string version, bool one) =>
            PackTestSupport.LocalPack(
                "local.one",
                version,
                PackTestSupport.LocalRule(one ? "local.synthetic.digit" : "local.synthetic.code", PackTestSupport.DigitMatcherJson));
    }

    // --- Content structure through the local path ------------------------------

    [Theory]
    [InlineData("""{ "pattern": { "shape": [ { "min": 11, "max": 11, "class": "digit", "emit": true } ], "boundary": "digit" }, "list": { "package": ["x"] } }""")]
    [InlineData("""{ "list": { "package": ["local-synth"] }, "hostname": ["local-synth.example"] }""")]
    public void PackLoader_Should_ProduceAtMostOneMatcherKind_When_ARuleTriesMoreThanOne(string matcherJson)
    {
        var input = new LocalPackInput("local.json", PackTestSupport.LocalPack(
            "local.one", "1", PackTestSupport.LocalRule("local.synthetic.digit", matcherJson)));

        Action load = () => PackLoader.Load([], [input], PackTestSupport.Catalogue, BundledOnly);

        load.Should().Throw<EngineToolException>().Which.Message.Should().Contain("matcher");
    }

    [Fact]
    public void PackLoader_Should_RefuseAShapePartWithMoreThanOneAtom_When_APartCarriesTwo()
    {
        const string matcher = """{ "pattern": { "shape": [ { "min": 1, "max": 1, "class": "digit", "literal": "7" } ], "boundary": "digit" } }""";
        var input = new LocalPackInput("local.json", PackTestSupport.LocalPack(
            "local.one", "1", PackTestSupport.LocalRule("local.synthetic.digit", matcher)));

        Action load = () => PackLoader.Load([], [input], PackTestSupport.Catalogue, BundledOnly);

        load.Should().Throw<EngineToolException>().Which.Message.Should().Contain("shape").And.Contain("atom");
    }

    [Fact]
    public void PackLoader_Should_RefuseAShapePart_When_ItsMinimumExceedsItsMaximum()
    {
        const string matcher = """{ "pattern": { "shape": [ { "min": 12, "max": 11, "class": "digit", "emit": true } ], "boundary": "digit" } }""";
        var input = new LocalPackInput("local.json", PackTestSupport.LocalPack(
            "local.one", "1", PackTestSupport.LocalRule("local.synthetic.digit", matcher)));

        Action load = () => PackLoader.Load([], [input], PackTestSupport.Catalogue, BundledOnly);

        load.Should().Throw<EngineToolException>().Which.Message.Should().Contain("max").And.Contain("min");
    }

    [Fact]
    public void PackLoader_Should_RefuseAShape_When_ItsPartsCouldConsumeMoreThan256ScalarValues()
    {
        const string matcher = """{ "pattern": { "shape": [ { "min": 1, "max": 64, "class": "digit" }, { "min": 1, "max": 64, "class": "digit" }, { "min": 1, "max": 64, "literal": "ABC", "emit": true } ], "boundary": "digit" } }""";
        var input = new LocalPackInput("local.json", PackTestSupport.LocalPack(
            "local.one", "1", PackTestSupport.LocalRule("local.synthetic.digit", matcher)));

        Action load = () => PackLoader.Load([], [input], PackTestSupport.Catalogue, BundledOnly);

        load.Should().Throw<EngineToolException>().Which.Message.Should().Contain("256");
    }

    [Fact]
    public void PackLoader_Should_RefuseAnAlternationPart_When_ItRepeats()
    {
        const string matcher = """{ "pattern": { "shape": [ { "min": 1, "max": 2, "oneOf": ["A", "B"], "emit": true } ], "boundary": "none" } }""";
        var input = new LocalPackInput("local.json", PackTestSupport.LocalPack(
            "local.one", "1", PackTestSupport.LocalRule("local.synthetic.digit", matcher)));

        Action load = () => PackLoader.Load([], [input], PackTestSupport.Catalogue, BundledOnly);

        load.Should().Throw<EngineToolException>().Which.Message.Should().Contain("oneOf").And.Contain("max");
    }

    [Fact]
    public void PackLoader_Should_RefuseAShape_When_MoreThanOnePartIsMarkedEmit()
    {
        const string matcher = """{ "pattern": { "shape": [ { "min": 1, "max": 1, "oneOf": ["A", "B"], "emit": true }, { "min": 1, "max": 1, "literal": "-", "emit": true } ], "boundary": "none" } }""";
        var input = new LocalPackInput("local.json", PackTestSupport.LocalPack(
            "local.one", "1", PackTestSupport.LocalRule("local.synthetic.digit", matcher)));

        Action load = () => PackLoader.Load([], [input], PackTestSupport.Catalogue, BundledOnly);

        load.Should().Throw<EngineToolException>().Which.Message.Should().Contain("emit");
    }

    [Fact]
    public void PackLoader_Should_RefuseAKeyword_When_ItIsNotAscii()
    {
        const string matcher = """{ "pattern": { "shape": [ { "min": 11, "max": 11, "class": "digit", "emit": true } ], "boundary": "digit", "keywords": ["café"], "window": 64 } }""";
        var input = new LocalPackInput("local.json", PackTestSupport.LocalPack(
            "local.one", "1", PackTestSupport.LocalRule("local.synthetic.digit", matcher)));

        Action load = () => PackLoader.Load([], [input], PackTestSupport.Catalogue, BundledOnly);

        load.Should().Throw<EngineToolException>().Which.Message.Should().Contain("keyword");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(5000)]
    public void PackLoader_Should_RefuseAWindowOutsideItsBounds_When_ThePrefilterCarriesOne(int window)
    {
        var matcher = """{ "pattern": { "shape": [ { "min": 11, "max": 11, "class": "digit", "emit": true } ], "boundary": "digit", "keywords": ["BVN"], "window": WINDOW } }"""
            .Replace("WINDOW", window.ToString(System.Globalization.CultureInfo.InvariantCulture));
        var input = new LocalPackInput("local.json", PackTestSupport.LocalPack(
            "local.one", "1", PackTestSupport.LocalRule("local.synthetic.digit", matcher)));

        Action load = () => PackLoader.Load([], [input], PackTestSupport.Catalogue, BundledOnly);

        load.Should().Throw<EngineToolException>().Which.Message.Should().Contain("window");
    }

    [Fact]
    public void PackLoader_Should_RefuseAMissingTitle_When_TheCitationCarriesNone()
    {
        var rule = PackTestSupport.LocalRule("local.synthetic.digit", PackTestSupport.DigitMatcherJson)
            .Replace("Placeholder citation — approved NDPA wording arrives in P2-02", "");
        var input = new LocalPackInput("local.json", PackTestSupport.LocalPack("local.one", "1", rule));

        Action load = () => PackLoader.Load([], [input], PackTestSupport.Catalogue, BundledOnly);

        load.Should().Throw<EngineToolException>().Which.Message.Should().Contain("citation");
    }

    [Fact]
    public void PackLoader_Should_RefuseAnUnknownValidator_When_ARuleNamesOne()
    {
        const string matcher = """{ "pattern": { "shape": [ { "min": 11, "max": 11, "class": "digit", "emit": true } ], "boundary": "digit", "validator": { "name": "iban" } } }""";
        var input = new LocalPackInput("local.json", PackTestSupport.LocalPack(
            "local.one", "1", PackTestSupport.LocalRule("local.synthetic.digit", matcher)));

        Action load = () => PackLoader.Load([], [input], PackTestSupport.Catalogue, BundledOnly);

        load.Should().Throw<EngineToolException>().Which.Message.Should().Contain("validator").And.Contain("iban");
    }

    [Fact]
    public void PackLoader_Should_RefuseAnEmptyListMatcher_When_ARuleCarriesNeitherPackagesNorHostnames()
    {
        const string matcher = """{ "list": {} }""";
        var input = new LocalPackInput("local.json", PackTestSupport.LocalPack(
            "local.one", "1", PackTestSupport.LocalRule("local.synthetic.digit", matcher)));

        Action load = () => PackLoader.Load([], [input], PackTestSupport.Catalogue, BundledOnly);

        load.Should().Throw<EngineToolException>().Which.Message.Should().Contain("list");
    }

    [Fact]
    public void PackLoader_Should_RefuseASeverityOutsideItsSet_When_ARuleDeclaresOne()
    {
        var input = new LocalPackInput("local.json", PackTestSupport.LocalPack(
            "local.one", "1", PackTestSupport.LocalRule("local.synthetic.digit", PackTestSupport.DigitMatcherJson, severity: "fatal")));

        Action load = () => PackLoader.Load([], [input], PackTestSupport.Catalogue, BundledOnly);

        load.Should().Throw<EngineToolException>().Which.Message.Should().Contain("severity");
    }

    [Fact]
    public void PackLoader_Should_SortTheMergedRulesByIdentifier_When_SeveralRulesArriveOutOfOrder()
    {
        var input = new LocalPackInput("local.json", PackTestSupport.LocalPack(
            "local.one",
            "1",
            PackTestSupport.LocalRule("local.zz.last", PackTestSupport.DigitMatcherJson),
            PackTestSupport.LocalRule("local.aa.first", PackTestSupport.DigitMatcherJson)));

        var load = PackLoader.Load([], [input], PackTestSupport.Catalogue, BundledOnly);

        load.Rules.Select(rule => rule.Rule.Id).Should().Equal(["local.aa.first", "local.zz.last"]);
    }

    // --- helpers ---------------------------------------------------------------

    private static string LowerHex(byte[] bytes) => Convert.ToHexString(bytes).ToLowerInvariant();

    private static string MutateArmour(string armour, string mutation)
    {
        var lines = armour.Replace("\r\n", "\n").TrimEnd('\n').Split('\n');
        return mutation switch
        {
            "begin-label" => new StringBuilder(armour).Replace("BEGIN GATE-KEEPER RULE PACK MANIFEST", "BEGIN GATEKEEPER RULE PACK").ToString(),
            "missing-header" => string.Join('\n', lines.Where(line => !line.StartsWith("Key-Id:", StringComparison.Ordinal))) + "\n",
            "missing-end" => string.Join('\n', lines.Take(lines.Length - 1)) + "\n",
            _ => throw new ArgumentOutOfRangeException(nameof(mutation)),
        };
    }
}
