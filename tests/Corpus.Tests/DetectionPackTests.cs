// SPDX-License-Identifier: Apache-2.0
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using InvenioTech.GateKeeper.Engine;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Crypto.Signers;
using Xunit.Abstractions;

namespace InvenioTech.GateKeeper.Corpus.Tests;

public sealed class DetectionPackTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData("bvn")]
    [InlineData("nin")]
    [InlineData("phone")]
    public async Task DetectionPack_Should_MatchExactExpectations_When_RunAgainstEverySyntheticCase(string family)
    {
        var rules = Load(family);
        var ids = rules.Select(rule => rule.Id).ToHashSet(StringComparer.Ordinal);
        var runs = new List<(CorpusCase Case, IReadOnlyList<Finding> Findings)>();
        foreach (var item in CorpusCase.LoadAll(CorpusPaths.Root))
        {
            var selected = item with { Expected = item.Expected.Where(entry => ids.Contains(entry.RuleId)).ToArray() };
            var scan = await new CorpusScanRunner(selected, rules).RunAsync();
            using var cleanup = new CorpusScanScavenger(scan);
            scan.MatcherStagePresent.Should().BeTrue();
            var diff = CorpusMeasurements.Score(selected.Expected, scan.Findings);
            diff.Unexpected.Should().BeEmpty(item.Name);
            diff.MatchedCount.Should().Be(diff.ExpectedCount, item.Name);
            scan.Findings.Should().OnlyContain(finding => finding.Fingerprint == null);
            runs.Add((selected, scan.Findings));
        }
        var measurements = CorpusMeasurements.PerRule(runs);
        measurements.Overall.ExpectedCount.Should().BeGreaterThan(0);
        measurements.FalsePositiveGatePassed.Should().BeTrue();
        var report = new StringWriter();
        CorpusMeasurements.WriteReport(report, measurements, matcherStagePresent: true);
        output.WriteLine(report.ToString());
    }

    // This signs only in-memory test inputs with the existing labelled development seed.
    // No development signature is written into rule-packs or trusted by a released CLI.
    private static IReadOnlyList<Rule> Load(string family)
    {
        const string developmentKeyId = "gkpk-dev-1";
        var root = Path.GetDirectoryName(Path.GetDirectoryName(CorpusPaths.Root))!;
        var content = File.ReadAllBytes(Path.Combine(root, "rule-packs", family + ".pack.json"));
        using var document = JsonDocument.Parse(content);
        using var catalogue = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(root, "tests", "vectors", "pack", "development-public-key.json")));
        var key = catalogue.RootElement.GetProperty("keys").EnumerateArray().Single(item => item.GetProperty("keyId").GetString() == developmentKeyId);
        key.GetProperty("label").GetString().Should().Be("development-only");
        key.GetProperty("purpose").GetString().Should().Be("pack-signing");
        var privateKey = new Ed25519PrivateKeyParameters(Convert.FromHexString(key.GetProperty("seedHex").GetString()!), 0);
        var packId = document.RootElement.GetProperty("id").GetString()!;
        var version = document.RootElement.GetProperty("version").GetString()!;
        var sha256 = Convert.ToHexStringLower(SHA256.HashData(content));
        var sha512 = Convert.ToHexStringLower(SHA512.HashData(content));
        var payload = Encoding.UTF8.GetBytes($"{{\"keyId\":\"{developmentKeyId}\",\"packId\":\"{packId}\",\"version\":\"{version}\",\"contentHashes\":{{\"sha256\":\"{sha256}\",\"sha512\":\"{sha512}\"}}}}");
        var signer = new Ed25519Signer();
        signer.Init(true, privateKey);
        signer.BlockUpdate(payload, 0, payload.Length);
        var armour = $"-----BEGIN GATE-KEEPER RULE PACK MANIFEST-----\nKey-Id: {developmentKeyId}\n{Encoding.UTF8.GetString(payload)}\n-----END GATE-KEEPER RULE PACK MANIFEST-----\n";
        var input = new SignedPackInput(family, content, armour, signer.GenerateSignature());
        return PackLoader.Load([input], [], new DevelopmentCatalogue(developmentKeyId, privateKey.GeneratePublicKey().GetEncoded()),
            BundledPacksOnlyPolicy.Instance).Rules.Select(item => item.Rule).ToArray();
    }

    private sealed class DevelopmentCatalogue(string keyId, byte[] key) : IPackKeyCatalogue
    {
        public bool TryGetPackSigningKey(string id, out byte[] publicKey)
        {
            publicKey = id == keyId ? key.ToArray() : [];
            return id == keyId;
        }

        public bool TryGetWatermarkKey(string id, out byte[] publicKey)
        {
            publicKey = [];
            return false;
        }
    }
}
