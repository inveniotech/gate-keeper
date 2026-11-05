// SPDX-License-Identifier: Apache-2.0

using System.CommandLine;
using InvenioTech.GateKeeper.Engine;

namespace InvenioTech.GateKeeper.Cli;

internal static class DoctorCli
{
    internal static Command Create(TextWriter output, TextWriter errors, string root)
    {
        var command = new Command("doctor", "Checks configuration, rule packs, Git and output access without scanning source");
        var date = new Option<string?>("--evaluated-on") { Description = "The evaluation date in YYYY-MM-DD form" };
        var destination = new Option<string>("--output") { Description = "The repository-relative output directory to check", DefaultValueFactory = _ => ".ndpa-check" };
        command.Options.Add(date);
        command.Options.Add(destination);
        command.SetAction(async (parse, token) =>
        {
            var failed = false;
            var load = ConfigurationLoader.Load(root);
            await output.WriteLineAsync($"Gate-Keeper {EngineProduct.EngineVersion}").ConfigureAwait(false);
            await output.WriteLineAsync(load.Settings is not null && load.Errors.Count == 0 ? "configuration: valid" : "configuration: invalid").ConfigureAwait(false);
            foreach (var message in load.Errors) { await errors.WriteLineAsync(message).ConfigureAwait(false); failed = true; }
            foreach (var message in load.Warnings) await errors.WriteLineAsync(message).ConfigureAwait(false);
            if (load.Settings is { } settings)
            {
                try
                {
                    var day = EvaluationDate.Resolve(TimeProvider.System, parse.GetValue(date), Environment.GetEnvironmentVariable("SOURCE_DATE_EPOCH"));
                    var packs = await ScanCli.LoadPacksAsync(root, settings, token).ConfigureAwait(false);
                    var rules = packs.Rules.Select(r => r.Rule).ToArray();
                    SeverityPolicy.ValidateSettings(settings, rules);
                    await PolicyStore.LoadAsync(root, rules, token).ConfigureAwait(false);
                    foreach (var pack in packs.Packs)
                        await output.WriteLineAsync(pack.Provenance == PackProvenance.Local ? $"rule pack {pack.Id}: unsigned local pack; valid" : $"rule pack {pack.Id}: signature and content valid").ConfigureAwait(false);
                    foreach (var warning in PackAgeWarning.Evaluate(packs.Packs, day)) await errors.WriteLineAsync(warning.Message).ConfigureAwait(false);
                    await output.WriteLineAsync(packs.Packs.All(p => p.ContentIssuedAt is null) ? "pack age: no declared content issue dates" : "pack age: checked against the evaluation date").ConfigureAwait(false);
                }
                catch (EngineToolException exception) { await errors.WriteLineAsync(exception.Message).ConfigureAwait(false); failed = true; }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                { await errors.WriteLineAsync("The pack or policy check could not read a repository file. Check .ndpa-check paths and access permissions, then run doctor again.").ConfigureAwait(false); failed = true; }
            }
            var git = await RepositoryGitState.ReadAsync(root, token).ConfigureAwait(false);
            await output.WriteLineAsync(git.Available ? (git.InsideWorkTree ? "git: available; work tree detected" : "git: available; outside a work tree") : "git: unavailable; scans use the directory walk").ConfigureAwait(false);
            if (git.FingerprintKeyTracked)
                await errors.WriteLineAsync("Git is tracking .ndpa-check/fingerprint.key. Anyone who can read the repository can reverse a fingerprint of a short identifier. Stop tracking the file, remove it from history, and replace the key.").ConfigureAwait(false);
            try
            {
                var path = ResolveOutput(root, parse.GetValue(destination)!);
                Directory.CreateDirectory(path);
                var probe = Path.Combine(path, $".gate-keeper-doctor-{Guid.NewGuid():N}.tmp");
                await using (var stream = new FileStream(probe, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1, FileOptions.Asynchronous | FileOptions.DeleteOnClose))
                    await stream.WriteAsync(new byte[] { 0 }, token).ConfigureAwait(false);
                await output.WriteLineAsync("output: writable").ConfigureAwait(false);
            }
            catch (EngineToolException exception) { await errors.WriteLineAsync(exception.Message).ConfigureAwait(false); failed = true; }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            { await errors.WriteLineAsync("The output directory is not writable. Choose a writable repository-relative --output directory or repair its permissions, then run doctor again.").ConfigureAwait(false); failed = true; }
            await output.WriteLineAsync("licence: not checked; licence verification is not in this build").ConfigureAwait(false);
            return failed ? (int)ExitOutcome.ToolError : (int)ExitOutcome.Pass;
        });
        return command;
    }

    internal static string ResolveOutput(string root, string relative)
    {
        if (string.IsNullOrWhiteSpace(relative) || Path.IsPathRooted(relative) || relative.Replace('\\', '/').Split('/').Contains("..", StringComparer.Ordinal))
            throw new EngineToolException("The output path must be repository-relative and must not traverse parent directories. Choose a path inside this repository.");
        var fullRoot = Path.GetFullPath(root);
        var path = Path.GetFullPath(Path.Combine(fullRoot, relative));
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (!path.Equals(fullRoot, comparison) && !path.StartsWith(fullRoot + Path.DirectorySeparatorChar, comparison))
            throw new EngineToolException("The output path must be repository-relative. Choose a path inside this repository.");
        var current = fullRoot;
        foreach (var segment in Path.GetRelativePath(fullRoot, path).Split(Path.DirectorySeparatorChar))
        {
            current = Path.Combine(current, segment);
            if ((File.Exists(current) || Directory.Exists(current)) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new EngineToolException("The output path contains a symbolic link. Choose a repository-relative output path without links.");
        }
        return path;
    }
}
