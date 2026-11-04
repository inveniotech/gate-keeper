// SPDX-License-Identifier: Apache-2.0

using System.CommandLine;
using InvenioTech.GateKeeper.Engine;

namespace InvenioTech.GateKeeper.Cli;

internal static class BaselineCli
{
    internal static Command Create(TextWriter output, TextWriter errors, string root)
    {
        var command = new Command("baseline", "Writes current work-tree findings to .ndpa-check/baseline.json");
        var date = new Option<string?>("--evaluated-on") { Description = "The evaluation date in YYYY-MM-DD form" };
        command.Options.Add(date);
        command.SetAction(async (parse, token) =>
        {
            try
            {
                var load = ConfigurationLoader.Load(root);
                if (load.Errors.Count > 0 || load.Settings is not { } settings) throw new EngineToolException(string.Join(" ", load.Errors));
                var inputs = new List<LocalPackInput>();
                foreach (var path in settings.LocalPacks)
                    inputs.Add(new LocalPackInput(path, await File.ReadAllBytesAsync(Path.Combine(root, path), token).ConfigureAwait(false)));
                var merged = PackLoader.Load([], inputs, new NoEmbeddedKeys(), BundledPacksOnlyPolicy.Instance);
                var result = await BaselineCommand.RunAsync(root, merged.Rules.Select(r => r.Rule).ToArray(), TimeProvider.System,
                    parse.GetValue(date), Environment.GetEnvironmentVariable("SOURCE_DATE_EPOCH"), token).ConfigureAwait(false);
                foreach (var warning in load.Warnings) await errors.WriteLineAsync(warning).ConfigureAwait(false);
                await output.WriteLineAsync($"The baseline was written to .ndpa-check/baseline.json with {result.Findings.Count} findings. Supply fingerprint.key to continuous integration as a secret file.").ConfigureAwait(false);
                return (int)ExitOutcome.Pass;
            }
            catch (EngineToolException exception)
            {
                await errors.WriteLineAsync(exception.Message).ConfigureAwait(false);
                return (int)ExitOutcome.ToolError;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                await errors.WriteLineAsync("The baseline could not read or write a repository file. Check .ndpa-check paths and access permissions, then run baseline again.").ConfigureAwait(false);
                return (int)ExitOutcome.ToolError;
            }
        });
        return command;
    }

    private sealed class NoEmbeddedKeys : IPackKeyCatalogue
    {
        public bool TryGetPackSigningKey(string keyId, out byte[] publicKey) { publicKey = []; return false; }
        public bool TryGetWatermarkKey(string keyId, out byte[] publicKey) { publicKey = []; return false; }
    }
}
