// SPDX-License-Identifier: Apache-2.0

using InvenioTech.GateKeeper.Engine;

namespace InvenioTech.GateKeeper.Engine.Tests;

public sealed class FindingPolicyTests
{
    private static readonly DateOnly Day = new(2026, 11, 1);

    [Theory]
    [InlineData(0, 0, 1)]
    [InlineData(1, 1, 0)]
    public async Task Policy_Should_RespectExpiryBoundary_When_TheEvaluationDateChanges(int days, int findings, int applied)
    {
        using var repo = new PolicyRepository();
        repo.Exceptions();
        var result = await Evaluate(repo, Day.AddDays(days));
        result.Findings.Should().HaveCount(findings);
        result.Exceptions.Should().HaveCount(applied);
        result.EvaluatedOn.Should().Be(Day.AddDays(days));
        if (applied > 0) result.Exceptions[0].EntryHash.Should().HaveLength(64);
    }

    [Theory]
    [InlineData("justification = \"\"", "justification")]
    [InlineData("owner = \"\"", "owner")]
    [InlineData("expires = \"bad\"", "expires")]
    [InlineData("severity = \"fatal\"", "severity")]
    [InlineData("category = \"synthetic\"", "scope")]
    [InlineData("unknown = true", "unknown")]
    public async Task Loader_Should_RejectInvalidEntries_When_AFieldCannotBeUsed(string field, string reason)
    {
        using var repo = new PolicyRepository();
        repo.Exceptions(field);
        Func<Task> load = async () => await PolicyStore.LoadAsync(repo.Root, [PolicyRepository.Rule]);
        await load.Should().ThrowAsync<EngineToolException>().WithMessage($"*exceptions.toml*exc-test*{reason}*");
    }

    [Fact]
    public async Task Policy_Should_ReportTheOverride_When_AnExceptionHasSeverity()
    {
        using var repo = new PolicyRepository();
        repo.Exceptions("severity = \"note\"");
        var result = await Evaluate(repo, Day);
        result.Findings.Should().ContainSingle().Which.Severity.Should().Be(Severity.Note);
        result.Exceptions.Should().ContainSingle().Which.Outcome.Should().Be("overridden");
    }

    [Fact]
    public async Task Policy_Should_RejectAmbiguity_When_TwoRuleExceptionsMatch()
    {
        using var repo = new PolicyRepository();
        repo.Exceptions();
        var path = Path.Combine(repo.Root, ".gatekeeper", "exceptions.toml");
        var text = await File.ReadAllTextAsync(path);
        await File.AppendAllTextAsync(path, text[(text.IndexOf("[[exception]]", StringComparison.Ordinal))..].Replace("exc-test", "exc-other", StringComparison.Ordinal));
        Func<Task> apply = async () => await Evaluate(repo, Day);
        await apply.Should().ThrowAsync<EngineToolException>().WithMessage("*exc-other*exc-test*");
    }

    [Fact]
    public async Task Baseline_Should_PreserveDatesAndBytes_When_TheSameFindingsAreWrittenAgain()
    {
        using var repo = new PolicyRepository();
        await PolicyStore.EnsureKeyAsync(repo.Root);
        var result = await Evaluate(repo, Day);
        await PolicyStore.WriteBaselineAsync(repo.Root, result);
        var path = Path.Combine(repo.Root, ".gatekeeper", "baseline.json");
        var first = await File.ReadAllBytesAsync(path);
        first.Should().NotContain((byte)'\r');
        var again = await Evaluate(repo, Day.AddDays(1));
        again.Findings.Should().ContainSingle().Which.BaselineState.Should().Be(BaselineState.Unchanged);
        again.Baseline.Should().Be(new BaselineSummary(1, 1));
        await PolicyStore.WriteBaselineAsync(repo.Root, again);
        (await File.ReadAllBytesAsync(path)).Should().Equal(first);
        (await File.ReadAllTextAsync(path)).Should().NotContain("marker").And.NotContain("local.test");
        (await File.ReadAllTextAsync(Path.Combine(repo.Root, ".gatekeeper", ".gitignore"))).Should().Be("fingerprint.key\n");
    }

    [Theory]
    [InlineData(90, BaselineState.Unchanged)]
    [InlineData(91, BaselineState.New)]
    public async Task Baseline_Should_RespectAgeBoundary_When_MaxAgeIsSet(int age, BaselineState expected)
    {
        using var repo = new PolicyRepository();
        await PolicyStore.EnsureKeyAsync(repo.Root);
        await PolicyStore.WriteBaselineAsync(repo.Root, await Evaluate(repo, Day));
        var state = await PolicyStore.LoadAsync(repo.Root, [PolicyRepository.Rule]);
        var settings = ConfigurationLoader.Load(repo.Root).Settings! with { BaselineMaxAgeDays = 90 };
        var result = FindingPolicy.Apply(PolicyRepository.Scan(), settings, state, Day.AddDays(age));
        result.Findings[0].BaselineState.Should().Be(expected);
    }

    [Theory]
    [InlineData("{\"schemaVersion\":2,\"entries\":[]}")]
    [InlineData("{\"schemaVersion\":1,\"entries\":[],\"unknown\":true}")]
    [InlineData("{\"schemaVersion\":1,\"schemaVersion\":1,\"entries\":[]}")]
    [InlineData("{\"schemaVersion\":1,\"entries\":[{\"fingerprint\":\"bad\",\"addedOn\":\"2026-11-01\"}]}")]
    public async Task Baseline_Should_RejectMalformedInput_When_TheFileExists(string json)
    {
        using var repo = new PolicyRepository();
        await File.WriteAllTextAsync(Path.Combine(repo.Root, ".gatekeeper", "baseline.json"), json);
        Func<Task> load = async () => await PolicyStore.LoadAsync(repo.Root, [PolicyRepository.Rule]);
        await load.Should().ThrowAsync<EngineToolException>().WithMessage("*baseline.json*");
    }

    [Fact]
    public async Task Policy_Should_KeepFindingsVisible_When_TheKeyIsMissing()
    {
        using var repo = new PolicyRepository();
        await File.WriteAllTextAsync(Path.Combine(repo.Root, ".gatekeeper", "baseline.json"), "{\"schemaVersion\":1,\"entries\":[]}");
        var result = await Evaluate(repo, Day);
        result.Findings[0].Fingerprint.Should().BeNull();
        result.Warnings.Select(w => w.Code).Should().Equal("baseline.not-applied", "fingerprint.missing");
    }

    [Fact]
    public void EvaluationDate_Should_UseTheInjectedUtcClock_When_NoExplicitInputExists()
    {
        EvaluationDate.Resolve(new FixedClock(), null, null).Should().Be(Day);
        EvaluationDate.Resolve(new FixedClock(), "2026-12-01", "bad").Should().Be(new DateOnly(2026, 12, 1));
        Action invalid = () => EvaluationDate.Resolve(new FixedClock(), null, "-1");
        invalid.Should().Throw<EngineToolException>();
    }

    private sealed class FixedClock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(2026, 11, 1, 23, 0, 0, TimeSpan.Zero);
    }

    [Fact]
    public async Task BaselineCommand_Should_WriteAcceptedDebt_When_InvokedWithRules()
    {
        using var repo = new PolicyRepository();
        await File.WriteAllTextAsync(Path.Combine(repo.Root, "sample.txt"), "marker");
        var result = await BaselineCommand.RunAsync(repo.Root, [PolicyRepository.Rule], new FixedClock());
        result.Findings.Should().ContainSingle();
        result.EvaluatedOn.Should().Be(Day);
        var again = await BaselineCommand.RunAsync(repo.Root, [PolicyRepository.Rule], new FixedClock());
        again.Baseline.MatchedCount.Should().Be(1);
    }

    [Fact]
    public async Task Key_Should_PreserveExistingBytes_When_EnsuredTwice()
    {
        using var repo = new PolicyRepository();
        await PolicyStore.EnsureKeyAsync(repo.Root);
        var path = Path.Combine(repo.Root, ".gatekeeper", "fingerprint.key");
        var original = await File.ReadAllBytesAsync(path);
        await PolicyStore.EnsureKeyAsync(repo.Root);
        (await File.ReadAllBytesAsync(path)).Should().Equal(original);
    }

    [Fact]
    public async Task Key_Should_RestrictAccess_When_ItIsCreated()
    {
        using var repo = new PolicyRepository();
        await PolicyStore.EnsureKeyAsync(repo.Root);
        var path = Path.Combine(repo.Root, ".gatekeeper", "fingerprint.key");
        if (OperatingSystem.IsWindows())
        {
            var security = System.IO.FileSystemAclExtensions.GetAccessControl(new FileInfo(path));
            security.AreAccessRulesProtected.Should().BeTrue();
            var rules = security.GetAccessRules(true, true, typeof(System.Security.Principal.SecurityIdentifier));
            rules.Count.Should().Be(1);
            using var identity = System.Security.Principal.WindowsIdentity.GetCurrent();
            rules[0]!.IdentityReference.Should().Be(identity.User);
        }
        else
        {
            File.GetUnixFileMode(path).Should().Be(UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
    }

    [Theory]
    [InlineData("../outside/**")]
    [InlineData("[bad]")]
    public async Task Loader_Should_NameTheException_When_AnEntryHasInvalidPaths(string glob)
    {
        using var repo = new PolicyRepository();
        repo.Exceptions($"paths = [\"{glob}\"]");
        Func<Task> load = async () => await PolicyStore.LoadAsync(repo.Root, [PolicyRepository.Rule]);
        await load.Should().ThrowAsync<EngineToolException>().WithMessage("*exceptions.toml*exc-test*paths*");
    }

    [Fact]
    public void Fingerprint_Should_AgreeWithThePublishedVector_When_ComputingTheKeyedMessage()
    {
        var key = Convert.FromHexString("00112233445566778899aabbccddeeff00112233445566778899aabbccddeeff");
        FingerprintCalculator.Compute(key, "pii.bvn", "src/a.cs", "12345678901", 0).Should().Be("00cca3a4746d65803db2fa8f18dfa1d9f05ca1c3c382ca92b2078b31a0e9c3a7");
        FingerprintCalculator.Compute(key, "pii.bvn", "src/a.cs", "12345678901", 1).Should().Be("45a95165c37f218e00fb180ed710a9ac8aae15930204f1b4b483b9bae04442b6");
    }

    [Fact]
    public async Task ExceptionHash_Should_AgreeWithThePublishedVector_When_FieldsAreTrimmed()
    {
        using var repo = new PolicyRepository();
        await File.WriteAllTextAsync(Path.Combine(repo.Root, ".gatekeeper", "exceptions.toml"), """
            schemaVersion = 1
            [[exception]]
            id = "exc-fixture-bvn"
            rule = "pii.bvn"
            paths = ["tests/fixtures/**"]
            justification = " Synthetic values from the corpus script. "
            owner = "dpo@example.com"
            expires = "2026-11-01"
            """);
        var state = await PolicyStore.LoadAsync(repo.Root, [PolicyRepository.Rule with { Id = "pii.bvn" }]);
        state.Exceptions[0].EntryHash.Should().Be("f9de64c03afa66c3d6def1cb5d697dcb9e8f0f7c14824717ed5e1e31cbe6d8b0");
    }

    [Fact]
    public async Task Policy_Should_PreferRuleScope_When_ACategoryExceptionAlsoMatches()
    {
        using var repo = new PolicyRepository();
        repo.Exceptions("severity = \"note\"");
        await File.AppendAllTextAsync(Path.Combine(repo.Root, ".gatekeeper", "exceptions.toml"), """

            [[exception]]
            id = "exc-category"
            category = "synthetic"
            paths = ["src/**"]
            justification = "Synthetic test content."
            owner = "test-team"
            expires = "2026-11-01"
            """);
        var result = await Evaluate(repo, Day);
        result.Findings[0].Severity.Should().Be(Severity.Note);
        result.Exceptions.Should().ContainSingle().Which.Id.Should().Be("exc-test");
    }

    [Theory]
    [InlineData("not-a-key\n")]
    [InlineData("00112233445566778899aabbccddeeff00112233445566778899aabbccddeeff")]
    public async Task Key_Should_RejectInvalidContentWithoutDisclosingIt_When_Loaded(string content)
    {
        using var repo = new PolicyRepository();
        await File.WriteAllTextAsync(Path.Combine(repo.Root, ".gatekeeper", "fingerprint.key"), content);
        Func<Task> load = async () => await PolicyStore.LoadAsync(repo.Root, [PolicyRepository.Rule]);
        var error = await load.Should().ThrowAsync<EngineToolException>();
        error.Which.Message.Should().Contain("fingerprint.key").And.NotContain(content.Trim());
    }

    private static async Task<PolicyResult> Evaluate(PolicyRepository repo, DateOnly day) => FindingPolicy.Apply(
        PolicyRepository.Scan(), ConfigurationLoader.Load(repo.Root).Settings!,
        await PolicyStore.LoadAsync(repo.Root, [PolicyRepository.Rule]), day);
}

internal sealed class PolicyRepository : IDisposable
{
    internal static readonly Rule Rule = new("local.test", Severity.Error, "synthetic", new Citation("Synthetic test citation.", null, null), "1", null, null, null, null,
        new RuleMatcher(null, new RuleListMatcher(["marker"], [])));
    internal string Root { get; } = Path.Combine(Path.GetTempPath(), "gate-keeper-policy", Guid.NewGuid().ToString("N"));
    internal PolicyRepository() => Directory.CreateDirectory(Path.Combine(Root, ".gatekeeper"));
    internal void Exceptions(string replacement = "")
    {
        var lines = new List<string> { "id = \"exc-test\"", "rule = \"local.test\"", "paths = [\"src/**\"]", "justification = \"Synthetic test content.\"", "owner = \"test-team\"", "expires = \"2026-11-01\"" };
        if (replacement.Length > 0)
        {
            var key = replacement.Split('=')[0].Trim();
            lines.RemoveAll(line => line.StartsWith(key + " =", StringComparison.Ordinal));
            lines.Add(replacement);
        }
        File.WriteAllText(Path.Combine(Root, ".gatekeeper", "exceptions.toml"), "schemaVersion = 1\n[[exception]]\n" + string.Join('\n', lines) + "\n");
    }
    internal static ScanMatchResult Scan()
    {
        var input = new FileEnumerationResult([new("src/a.cs", "marker")], [], [], [], false);
        return new ScanMatchResult(new MatcherEngine([Rule]).Match("src/a.cs", "marker"), [], input);
    }
    public void Dispose() => Directory.Delete(Root, true);
}
