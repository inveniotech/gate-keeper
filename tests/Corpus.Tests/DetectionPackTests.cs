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
    private const string WriteReportEnvironmentVariable = "GATEKEEPER_WRITE_SYNTHETIC_REPORT";

    [Theory]
    [InlineData("transfer.processor.interswitch", "api.interswitchng.com")]
    [InlineData("transfer.processor.palmpay", "open-gw-prod.palmpay-inc.com")]
    [InlineData("transfer.processor.opay", "payapi.opayweb.com")]
    [InlineData("transfer.processor.nibss", "api-ninvoicex.nibss-plc.com.ng")]
    [InlineData("transfer.processor.nibss", "ninvoicex-sdk")]
    [InlineData("transfer.processor.monnify", "api.monnify.com")]
    [InlineData("transfer.processor.remita", "demo.remita.net")]
    [InlineData("transfer.processor.korapay", "api.korapay.com")]
    [InlineData("transfer.processor.dojah", "api.dojah.io")]
    [InlineData("transfer.processor.youverify", "youverify-web-sdk")]
    public void ProcessorCatalogue_Should_ReportTheNamedProvider_When_AnIntegrationTokenIsReferenced(string id, string token)
    {
        var rules = Load("processors");
        var matcher = new MatcherEngine(rules);
        var text = $"const provider = \"{token}\";";
        var findings = matcher.Match("src/integration.ts", text);
        findings.Should().ContainSingle();
        var finding = findings.Single();
        finding.RuleId.Should().Be(id);
        finding.Severity.Should().Be(Severity.Note);
        finding.Line.Should().Be(1);
        finding.Column.Should().Be(text.IndexOf(token, StringComparison.Ordinal) + 1);
        rules.Single(rule => rule.Id == id).Processor!.UsualRegion.Should().Contain("confirm");
        matcher.Match("src/integration.ts", $"prefix-{token}-suffix").Should().BeEmpty();
        matcher.Match("src/integration.ts", token + ".example.invalid").Should().BeEmpty();
    }

    [Fact]
    public void ProcessorCatalogue_Should_NotReportAnApiIntegration_When_OnlyTheNibssDeveloperPortalIsReferenced()
    {
        new MatcherEngine(Load("processors")).Match("docs/integrations.md", "https://devportal.nibss-plc.com.ng/")
            .Should().BeEmpty();
    }

    [Theory]
    [InlineData("bvn")]
    [InlineData("nin")]
    [InlineData("phone")]
    [InlineData("processors")]
    [InlineData("locations")]
    [InlineData("email")]
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
            if (family == "processors")
            {
                scan.Findings.Should().OnlyContain(finding => finding.Severity == Severity.Note);
                foreach (var finding in scan.Findings)
                {
                    var processor = rules.Single(rule => rule.Id == finding.RuleId).Processor;
                    processor.Should().NotBeNull();
                    processor!.DataCategories.Should().NotBeEmpty();
                    processor.UsualRegion.Should().NotBeNullOrWhiteSpace();
                }
            }
            runs.Add((selected, scan.Findings));
        }
        var measurements = CorpusMeasurements.PerRule(runs);
        measurements.Overall.ExpectedCount.Should().BeGreaterThan(0);
        measurements.FalsePositiveGatePassed.Should().BeTrue();
        var report = new StringWriter();
        CorpusMeasurements.WriteReport(report, measurements, matcherStagePresent: true);
        output.WriteLine(report.ToString());
    }

    [Fact]
    public async Task DetectionReport_Should_MatchTheCommittedReport_When_AllAuthoredRulesScanEverySyntheticCase()
    {
        var root = Path.GetDirectoryName(Path.GetDirectoryName(CorpusPaths.Root))!;
        var paths = Directory.GetFiles(Path.Combine(root, "rule-packs"), "*.pack.json")
            .OrderBy(path => path, StringComparer.Ordinal).ToArray();
        paths.Should().NotBeEmpty("a measurement requires authored detection packs");
        var rules = paths.SelectMany(path => Load(Path.GetFileName(path)[..^".pack.json".Length])).ToArray();
        var ids = rules.Select(rule => rule.Id).ToHashSet(StringComparer.Ordinal);
        ids.Count.Should().Be(rules.Length, "duplicate rules cannot silently inflate measurements");
        var runs = new List<(CorpusCase Case, IReadOnlyList<Finding> Findings)>();
        foreach (var item in CorpusCase.LoadAll(CorpusPaths.Root))
        {
            var selected = item with { Expected = item.Expected.Where(entry => ids.Contains(entry.RuleId)).ToArray() };
            var scan = await new CorpusScanRunner(selected, rules).RunAsync();
            using var cleanup = new CorpusScanScavenger(scan);
            runs.Add((selected, scan.Findings));
        }
        var measurements = CorpusMeasurements.PerRule(runs);
        measurements.Overall.Unexpected.Should().BeEmpty();
        measurements.Overall.MatchedCount.Should().Be(measurements.Overall.ExpectedCount);
        measurements.FalsePositiveGatePassed.Should().BeTrue();
        var complete = rules.Select(rule => measurements.PerRule.SingleOrDefault(row => row.RuleId == rule.Id)
            ?? new CorpusRuleMeasurement(rule.Id, 0, 0, 0, [])).OrderBy(row => row.RuleId, StringComparer.Ordinal).ToArray();
        var table = new StringWriter { NewLine = "\n" };
        CorpusMeasurements.WriteReport(table, new CorpusRunMeasurements(complete, measurements.Overall), matcherStagePresent: true);
        var lines = table.ToString().Split('\n').Where(line => line.Length > 0 && !line.StartsWith("--", StringComparison.Ordinal)).ToList();
        lines.Insert(1, string.Join(" | ", Enumerable.Repeat("---", lines[0].Split('|').Length)));
        var report = new StringBuilder("# Synthetic detection measurements\n\n<!-- Generated by DetectionPackTests. Do not hand-edit. -->\n\n");
        report.Append("These measurements cover synthetic repositories only. Real-repository findings and owner triage remain unmeasured; these results do not establish production precision. No baselines, exceptions or disabled rules are applied.\n\n");
        report.Append("The error-level gate is `20 × false positives <= true positives + false positives`, per rule and overall. Warning and note counts are separate. Recall includes only the currently authored rules; other planned rules remain outside this measurement.\n\n");
        foreach (var path in paths)
        {
            using var pack = JsonDocument.Parse(File.ReadAllBytes(path));
            report.Append($"- `{pack.RootElement.GetProperty("id").GetString()}` version `{pack.RootElement.GetProperty("version").GetString()}`, SHA-256 `{Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)))}`.\n");
        }
        report.Append($"\nScanned {runs.Count} synthetic repositories. Rates are percentages; `n/a` means no denominator.\n\n");
        report.Append(string.Join('\n', lines)).Append('\n');
        var target = Path.Combine(root, "docs-data", "synthetic-measurements.md");
        if (Environment.GetEnvironmentVariable(WriteReportEnvironmentVariable) == "1")
            File.WriteAllText(target, report.ToString(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        File.ReadAllText(target).Should().Be(report.ToString(), "the committed report must be regenerated after a pack or corpus change");
        output.WriteLine(report.ToString());
    }

    [Fact]
    public void LocationPack_Should_LabelEachIdentifier_When_InEveryInventoryContext()
    {
        var matcher = new MatcherEngine(Load("locations"));
        var cases = CorpusCase.LoadAll(CorpusPaths.Root).Where(item => item.Name.StartsWith("p2-location-", StringComparison.Ordinal) && item.Expected.Count > 0).ToArray();
        cases.Should().HaveCount(25);
        foreach (var item in cases)
        {
            var expected = item.Expected.First();
            var finding = matcher.Match(expected.Path, File.ReadAllText(Path.Combine(item.RepositoryRoot, expected.Path))).Single();
            finding.RuleId.Should().Be(expected.RuleId + "." + (item.Name.EndsWith("-api-model", StringComparison.Ordinal) ? "api" : item.Name.Split('-')[3]));
            finding.Line.Should().Be(expected.Line);
            finding.Column.Should().Be(expected.Column);
            finding.Severity.Should().Be(Severity.Error);
            finding.LocationKind.Should().Be(item.Name.EndsWith("-api-model", StringComparison.Ordinal) ? LocationKind.ApiModel
                : item.Name.EndsWith("-configuration", StringComparison.Ordinal) ? LocationKind.Configuration
                : item.Name.EndsWith("-fixture", StringComparison.Ordinal) ? LocationKind.Fixture
                : item.Name.EndsWith("-migration", StringComparison.Ordinal) ? LocationKind.Migration : LocationKind.Log);
        }
    }

    [Theory]
    [InlineData("fixture", "__tests__/customer.ts")]
    [InlineData("fixture", "src/customer.spec.ts")]
    [InlineData("fixture", "db/seeds/customer.json")]
    [InlineData("fixture", "db/seed.sql")]
    [InlineData("configuration", ".env")]
    [InlineData("configuration", "env/.env.local")]
    [InlineData("configuration", "appsettings.Development.json")]
    [InlineData("configuration", "src/application-prod.yml")]
    [InlineData("configuration", "src/application.yaml")]
    [InlineData("configuration", "src/customer.properties")]
    [InlineData("migration", "Migrations/customer.sql")]
    [InlineData("api-model", "Models/customer.cs")]
    [InlineData("api-model", "api/responses/customer.ts")]
    public void LocationPack_Should_ApplyPackPathScopes_When_AnAliasContainsAnIdentifier(string kind, string path)
    {
        var matcher = new MatcherEngine(Load("locations"));
        foreach (var family in new[] { "bvn", "nin", "nuban", "phone", "email" })
        {
            var item = CorpusCase.LoadAll(CorpusPaths.Root).Single(item => item.Name == $"p2-location-{family}-{kind}");
            var expected = item.Expected.First();
            var text = File.ReadAllText(Path.Combine(item.RepositoryRoot, expected.Path));
            var finding = matcher.Match(path, text).Single();
            finding.RuleId.Should().Be($"pii.{family}.{(kind == "api-model" ? "api" : kind)}");
            finding.Severity.Should().Be(Severity.Error);
            finding.Line.Should().Be(expected.Line);
            finding.Column.Should().Be(expected.Column);
            matcher.Match("src/ordinary.ts", text).Should().BeEmpty();
        }
    }

    [Theory]
    [InlineData("logger.info(")]
    [InlineData("Log.Information(")]
    [InlineData("print(")]
    [InlineData("logger.LogWarning(")]
    public void LocationPack_Should_RecogniseCallSpellings_When_ALiteralIsNearALogCall(string call)
    {
        var matcher = new MatcherEngine(Load("locations"));
        foreach (var family in new[] { "bvn", "nin", "nuban", "phone", "email" })
        {
            var item = CorpusCase.LoadAll(CorpusPaths.Root).Single(item => item.Name == $"p2-location-{family}-log");
            var expected = item.Expected.First();
            var text = File.ReadAllText(Path.Combine(item.RepositoryRoot, expected.Path));
            var finding = matcher.Match("src/ordinary.ts", text.Replace("console.log(", call, StringComparison.Ordinal)).Single();
            finding.RuleId.Should().Be($"pii.{family}.log");
            finding.LocationKind.Should().Be(LocationKind.Log);
            matcher.Match("src/ordinary.ts", text.Replace("console.log(", "process(", StringComparison.Ordinal)).Should().BeEmpty();
        }
    }

    [Fact]
    public void LocationPack_Should_ReportUnsafeFields_When_NoLiteralIdentifierIsPresent()
    {
        var matcher = new MatcherEngine(Load("locations"));
        var cases = CorpusCase.LoadAll(CorpusPaths.Root).Where(item => item.Name.StartsWith("p2-field-", StringComparison.Ordinal) && item.Expected.Count > 0).ToArray();
        cases.Should().HaveCount(24);
        foreach (var item in cases)
        {
            var expected = item.Expected.Single();
            var findings = matcher.Match(expected.Path, File.ReadAllText(Path.Combine(item.RepositoryRoot, expected.Path)));
            findings.Should().ContainSingle(item.Name);
            var finding = findings.Single();
            finding.RuleId.Should().Be(expected.RuleId, item.Name);
            finding.Line.Should().Be(expected.Line);
            finding.Column.Should().Be(expected.Column);
            finding.Severity.Should().Be(Severity.Error);
            finding.LocationKind.Should().Be(item.Name.Contains("-migration-", StringComparison.Ordinal) ? LocationKind.Migration
                : item.Name.Contains("-api-", StringComparison.Ordinal) ? LocationKind.ApiModel : LocationKind.Log);
        }
    }

    // This signs only in-memory test inputs with the existing labelled development seed.
    // No development signature is written into rule-packs or trusted by a released CLI.
    internal static IReadOnlyList<Rule> Load(string family) => LoadMerged(family).Rules.Select(item => item.Rule).ToArray();

    internal static MergedRules LoadMerged(string family)
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
            BundledPacksOnlyPolicy.Instance);
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
