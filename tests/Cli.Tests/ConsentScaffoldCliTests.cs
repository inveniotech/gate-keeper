// SPDX-License-Identifier: Apache-2.0

using InvenioTech.GateKeeper.Cli;

namespace InvenioTech.GateKeeper.Cli.Tests;

public sealed class ConsentScaffoldCliTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "gate-keeper-consent", Guid.NewGuid().ToString("N"));

    public ConsentScaffoldCliTests() => Directory.CreateDirectory(root);

    [Theory]
    [InlineData("dotnet", "ConsentProvider.cs", "IConsentProvider")]
    [InlineData("node", "consent-provider.ts", "ConsentProvider")]
    [InlineData("jvm", "ConsentProvider.java", "ConsentCheck.ConsentProvider")]
    [InlineData("python", "consent_provider.py", "ConsentProvider")]
    public async Task Scaffold_Should_WriteDeterministicTemplates_When_AStackIsSelected(string stack, string file, string contract)
    {
        var output = new StringWriter();
        var errors = new StringWriter();
        (await CliModule.RunAsync(["scaffold", "consent", "--stack", stack], output, errors, root)).Should().Be(0, errors.ToString());
        errors.ToString().Should().BeEmpty();
        output.ToString().Should().Contain($"consent/{file}").And.Contain("consent/README.md");
        var source = await File.ReadAllBytesAsync(Path.Combine(root, "consent", file));
        System.Text.Encoding.UTF8.GetString(source).Should().Contain(contract).And.NotContain("\r").And.NotContain("{{version}}");
        source.Take(3).Should().NotEqual(new byte[] { 0xef, 0xbb, 0xbf });
        var other = Path.Combine(root, "other");
        Directory.CreateDirectory(other);
        (await CliModule.RunAsync(["scaffold", "consent", "--stack", stack], new StringWriter(), errors, other)).Should().Be(0);
        (await File.ReadAllBytesAsync(Path.Combine(other, "consent", file))).Should().Equal(source);
        (await File.ReadAllBytesAsync(Path.Combine(other, "consent", "README.md"))).Should()
            .Equal(await File.ReadAllBytesAsync(Path.Combine(root, "consent", "README.md")));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("ruby")]
    [InlineData("../node")]
    public async Task Scaffold_Should_ExplainStackChoices_When_StackIsMissingOrInvalid(string? stack)
    {
        string[] args = stack is null ? ["scaffold", "consent"]
            : stack.Length == 0 ? ["scaffold", "consent", "--stack"]
            : ["scaffold", "consent", "--stack", stack];
        var output = new StringWriter();
        var errors = new StringWriter();
        (await CliModule.RunAsync(args, output, errors, root)).Should().Be(2);
        errors.ToString().Should().Contain("--stack").And.Contain("dotnet").And.Contain("node").And.Contain("jvm").And.Contain("python");
        output.ToString().Should().BeEmpty();
        Directory.GetFileSystemEntries(root).Should().BeEmpty();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Scaffold_Should_PreserveExistingDestination_When_AFileOrDirectoryAlreadyExists(bool directory)
    {
        var path = Path.Combine(root, "consent");
        if (directory) { Directory.CreateDirectory(path); path = Path.Combine(path, "keep.txt"); }
        await File.WriteAllTextAsync(path, "keep");
        var errors = new StringWriter();
        var output = new StringWriter();
        (await CliModule.RunAsync(["scaffold", "consent", "--stack", "node"], output, errors, root)).Should().Be(2);
        (await File.ReadAllTextAsync(path)).Should().Be("keep");
        errors.ToString().Should().Contain("consent").And.Contain("existing");
        output.ToString().Should().BeEmpty();
        Directory.GetFileSystemEntries(root).Should().HaveCount(1);
        if (directory) Directory.GetFileSystemEntries(Path.GetDirectoryName(path)!).Should().HaveCount(1);
    }

    [Fact]
    public async Task Scaffold_Should_ReportAnAccessFailure_When_TheApplicationRootIsAFile()
    {
        var path = Path.Combine(root, "application");
        await File.WriteAllTextAsync(path, "keep");
        var output = new StringWriter();
        var errors = new StringWriter();
        (await CliModule.RunAsync(["scaffold", "consent", "--stack", "node"], output, errors, path)).Should().Be(2);
        errors.ToString().Should().Contain("permissions").And.NotContain("Exception").And.NotContain("temporary");
        output.ToString().Should().BeEmpty();
        (await File.ReadAllTextAsync(path)).Should().Be("keep");
        Directory.GetFileSystemEntries(root).Should().HaveCount(1);
    }

    [Fact]
    public async Task Scaffold_Should_PublishOnlyOneCompleteDirectory_When_TwoInvocationsCompete()
    {
        var codes = await Task.WhenAll(
            CliModule.RunAsync(["scaffold", "consent", "--stack", "node"], new StringWriter(), new StringWriter(), root),
            CliModule.RunAsync(["scaffold", "consent", "--stack", "node"], new StringWriter(), new StringWriter(), root));
        codes.Order().Should().Equal(0, 2);
        Directory.GetFileSystemEntries(root).Should().HaveCount(1);
        Directory.GetFiles(Path.Combine(root, "consent")).Select(Path.GetFileName).Order(StringComparer.Ordinal)
            .Should().Equal("README.md", "consent-provider.ts");
    }

    [Fact]
    public async Task Scaffold_Should_LeaveNoFiles_When_CancelledBeforeGeneration()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var operation = () => CliModule.RunAsync(["scaffold", "consent", "--stack", "dotnet"], new StringWriter(), new StringWriter(), root, cancellation.Token);
        await operation.Should().ThrowAsync<OperationCanceledException>();
        Directory.GetFileSystemEntries(root).Should().BeEmpty();
    }

    public void Dispose() => Directory.Delete(root, true);
}
