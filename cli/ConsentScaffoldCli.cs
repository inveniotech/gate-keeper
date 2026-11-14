// SPDX-License-Identifier: Apache-2.0

using System.CommandLine;
using System.Text;
using InvenioTech.GateKeeper.Engine;

namespace InvenioTech.GateKeeper.Cli;

internal static class ConsentScaffoldCli
{
    internal const string Usage = "Consent scaffolding needs --stack dotnet, node, jvm or python. Run 'scaffold consent --stack <stack>' in the application root; no existing files will be replaced.";

    internal static Command Create(TextWriter output, TextWriter errors, string root)
    {
        var command = new Command("consent", "Creates an append-only consent provider and framework instructions in a new consent/ directory");
        var stack = new Option<string>("--stack") { Description = "The application stack: dotnet (ASP.NET Core), node (Express), jvm (Spring Boot) or python (FastAPI)", Required = true };
        stack.AcceptOnlyFromAmong("dotnet", "node", "jvm", "python");
        command.Options.Add(stack);
        command.SetAction(async (parse, token) =>
        {
            var selected = parse.GetValue(stack)!;
            var file = selected switch { "dotnet" => "ConsentProvider.cs", "node" => "consent-provider.ts", "jvm" => "ConsentProvider.java", _ => "consent_provider.py" };
            string? staging = null;
            try
            {
                token.ThrowIfCancellationRequested();
                var destination = DoctorCli.ResolveOutput(root, "consent");
                if (Path.Exists(destination))
                    throw new EngineToolException("The consent path already exists. Scaffolding preserves existing files; move that path or run in a different application directory.");
                var temporary = Path.Combine(Path.GetFullPath(root), $".gatekeeper-consent-{Guid.NewGuid():N}.tmp");
                Directory.CreateDirectory(temporary);
                staging = temporary;
                await WriteTemplateAsync(selected, file, staging, token).ConfigureAwait(false);
                await WriteTemplateAsync(selected, "README.md", staging, token).ConfigureAwait(false);
                token.ThrowIfCancellationRequested();
                // Moving the complete directory also refuses a destination created by another invocation.
                Directory.Move(staging, destination);
                staging = null;
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                await errors.WriteLineAsync("Consent scaffolding was cancelled before the consent/ directory was created. Run the command again when ready.").ConfigureAwait(false);
                return (int)ExitOutcome.ToolError;
            }
            catch (EngineToolException exception)
            {
                await errors.WriteLineAsync(exception.Message).ConfigureAwait(false);
                return (int)ExitOutcome.ToolError;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                await errors.WriteLineAsync("The consent scaffold could not create a new consent/ directory. Existing files were preserved. Check directory permissions and any existing consent path, then run again.").ConfigureAwait(false);
                return (int)ExitOutcome.ToolError;
            }
            finally
            {
                if (staging is not null)
                {
                    try { Directory.Delete(staging, true); }
                    catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                    { await errors.WriteLineAsync("A temporary consent scaffold directory could not be removed. Check directory permissions and remove .gatekeeper-consent-*.tmp from the application root.").ConfigureAwait(false); }
                }
            }
            await output.WriteLineAsync($"Gate-Keeper {EngineProduct.EngineVersion}: created consent/{file} and consent/README.md. Follow the README to connect the provider to your application.").ConfigureAwait(false);
            return (int)ExitOutcome.Pass;
        });
        return command;
    }

    private static async Task WriteTemplateAsync(string stack, string file, string staging, CancellationToken token)
    {
        var assembly = typeof(ConsentScaffoldCli).Assembly;
        using var stream = assembly.GetManifestResourceStream($"ConsentTemplates/{stack}/{file}")
            ?? throw new EngineToolException("The consent template is missing from this build. Reinstall Gate-Keeper from complete installation media, then run again.");
        using var reader = new StreamReader(stream, Encoding.UTF8);
        var text = (await reader.ReadToEndAsync(token).ConfigureAwait(false))
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace("{{version}}", EngineProduct.EngineVersion, StringComparison.Ordinal);
        await File.WriteAllTextAsync(Path.Combine(staging, file), text, new UTF8Encoding(false), token).ConfigureAwait(false);
    }
}
