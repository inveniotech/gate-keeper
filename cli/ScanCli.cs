// SPDX-License-Identifier: Apache-2.0

using System.CommandLine;
using System.Globalization;
using System.Text;
using InvenioTech.GateKeeper.Engine;

namespace InvenioTech.GateKeeper.Cli;

internal static class ScanCli
{
    internal static void Configure(Command command, TextWriter output, TextWriter errors, string root)
    {
        var json = new Option<bool>("--json") { Description = "Writes Gate-Keeper evidence JSON to standard output" };
        var date = new Option<string?>("--evaluated-on") { Description = "The evaluation date in YYYY-MM-DD form" };
        command.Options.Add(json);
        command.Options.Add(date);
        command.SetAction(async (parse, token) =>
        {
            try
            {
                var prepared = await PrepareAsync(root, parse.GetValue(date), token).ConfigureAwait(false);
                var input = prepared.Results;
                var outcome = SeverityPolicy.Evaluate(input.Policy, input.Settings.FailOn);
                foreach (var warning in input.Policy.Warnings) await errors.WriteLineAsync(warning.Message).ConfigureAwait(false);
                if (parse.GetValue(json))
                    await output.WriteAsync(Encoding.UTF8.GetString(ResultsWriter.Write(input with { Outcome = outcome }).Evidence)).ConfigureAwait(false);
                else await WriteSummaryAsync(output, input with { Outcome = outcome }).ConfigureAwait(false);
                return (int)outcome;
            }
            catch (EngineToolException exception)
            {
                await errors.WriteLineAsync(exception.Message).ConfigureAwait(false);
                return (int)ExitOutcome.ToolError;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                await errors.WriteLineAsync("The scan could not read a repository file. Check .gatekeeper paths and access permissions, then run the scan again.").ConfigureAwait(false);
                return (int)ExitOutcome.ToolError;
            }
        });
    }

    internal static async Task<PreparedScan> PrepareAsync(string root, string? date, CancellationToken token,
        Func<IReadOnlyList<Rule>, MatcherEngine>? matcherFactory = null)
    {
        var load = ConfigurationLoader.Load(root);
        if (load.Settings is not { } settings || load.Errors.Count > 0) throw new EngineToolException(string.Join(" ", load.Errors));
        var packs = await LoadPacksAsync(root, settings, token).ConfigureAwait(false);
        var rules = packs.Rules.Select(r => r.Rule).ToArray();
        SeverityPolicy.ValidateSettings(settings, rules);
        var day = EvaluationDate.Resolve(TimeProvider.System, date, Environment.GetEnvironmentVariable("SOURCE_DATE_EPOCH"));
        var state = await PolicyStore.LoadAsync(root, rules, token).ConfigureAwait(false);
        var enabled = rules.Where(r => !settings.DisabledRules.Any(d => d.Id == r.Id)
            && (r.Profiles is null || r.Profiles.Count == 0 || r.Profiles.Contains(settings.Profile == ScanProfile.Deep ? "deep" : "default", StringComparer.Ordinal))).ToArray();
        var scan = await new ScanOrchestrator(matcherFactory is null ? new MatcherEngine(enabled) : matcherFactory(enabled)).ScanAsync(new FileEnumerationOptions
        {
            Root = root,
            ScanHistory = settings.ScanHistory,
            MaxFileBytes = settings.MaxFileBytes,
            ExcludeGlobs = settings.Exclude,
            IncludeSubmodules = settings.IncludeSubmodules,
        }, token).ConfigureAwait(false);
#if GATEKEEPER_BENCHMARK
        if (Environment.GetEnvironmentVariable("GATEKEEPER_BENCHMARK_OUTPUT") is { Length: > 0 } metricsPath)
            await File.WriteAllBytesAsync(metricsPath, BenchmarkMetrics.Write(scan.Measurements), token).ConfigureAwait(false);
#endif
        var policy = FindingPolicy.Apply(scan, settings, state, day, token);
        var metadata = await RepositoryMetadata.ReadAsync(root, settings.RepositoryId, token).ConfigureAwait(false);
        var warnings = policy.Warnings.Concat(PackAgeWarning.Evaluate(packs.Packs, day)).Concat(load.Warnings.Select(w => new ScanWarning("config.unknown-key", w))).ToList();
        if (metadata.Id.Length == 0) warnings.Add(new("repository.identity-empty", "No repository identity could be derived. Set repository.id in gatekeeper.toml."));
        policy = policy with { Warnings = warnings.OrderBy(w => w.Code, StringComparer.Ordinal).ThenBy(w => w.Message, StringComparer.Ordinal).ToArray() };
        return new(new ResultsInput(policy, settings, packs.Packs, metadata.Id, metadata.Revision,
            settings.ScanHistory && scan.Enumeration.IsInsideGitWorkTree, SeverityPolicy.Evaluate(policy, settings.FailOn)), packs);
    }

    internal static async Task<MergedRules> LoadPacksAsync(string root, ScanSettings settings, CancellationToken token)
    {
        var inputs = new List<LocalPackInput>();
        foreach (var path in settings.LocalPacks)
        {
            var file = Path.Combine(root, path);
            if (new FileInfo(file).Length > 16 * 1024 * 1024) throw new EngineToolException("A local rule pack exceeds 16 MiB. Reduce the pack size before scanning.");
            inputs.Add(new(path, await File.ReadAllBytesAsync(file, token).ConfigureAwait(false)));
        }
        var packs = PackLoader.Load([], inputs, new NoEmbeddedKeys(), BundledPacksOnlyPolicy.Instance);
        if (packs.Packs.Count == 0) throw new EngineToolException("No rule pack is available for scan. This build has no bundled pack; configure packs.local before running scan.");
        return packs;
    }

    private static async Task WriteSummaryAsync(TextWriter output, ResultsInput input)
    {
        var text = new StringBuilder();
        text.Append(CultureInfo.InvariantCulture, $"Gate-Keeper {EngineProduct.EngineVersion}\n");
        foreach (var pack in input.Packs) text.Append(CultureInfo.InvariantCulture, $"Rule pack {pack.Id}: {pack.Version}\n");
        foreach (var file in input.Policy.Findings.GroupBy(f => f.Path).OrderBy(g => g.Key, StringComparer.Ordinal))
        {
            text.Append(file.Key).Append('\n');
            foreach (var finding in file.OrderBy(f => f.Line).ThenBy(f => f.Column).ThenBy(f => f.RuleId, StringComparer.Ordinal))
            {
                text.Append(CultureInfo.InvariantCulture, $"  {finding.Line}:{finding.Column} {finding.Severity.ToString().ToLowerInvariant()} {finding.RuleId} ({finding.BaselineState.ToString().ToLowerInvariant()})\n");
                text.Append("    ").Append(finding.Citation.Title);
                if (finding.Citation.NdpaSection is { } ndpa) text.Append(" NDPA ").Append(ndpa);
                if (finding.Citation.GaidSection is { } gaid) text.Append(" GAID ").Append(gaid);
                text.Append('\n');
                AppendProcessorDetail(text, finding.Processor);
            }
        }
        foreach (var finding in input.Policy.History)
        {
            text.Append(CultureInfo.InvariantCulture, $"History {finding.CommitId} {finding.Match.Path}:{finding.Match.Line}:{finding.Match.Column} {finding.Match.Severity.ToString().ToLowerInvariant()} {finding.Match.RuleId}: {finding.Match.Citation.Title}\n");
            AppendProcessorDetail(text, finding.Match.Processor);
        }
        text.Append(CultureInfo.InvariantCulture, $"Findings: {input.Policy.Findings.Count} work tree, {input.Policy.History.Count} history. Exit code: {(int)input.Outcome}.\n");
        if (!input.HistoryScanned) text.Append("A clean result does not mean git history was scanned.\n");
        await output.WriteAsync(text.ToString()).ConfigureAwait(false);
    }

    private static void AppendProcessorDetail(StringBuilder text, ProcessorInfo? processor)
    {
        if (processor is null) return;
        text.Append("    Data categories: ").AppendJoin(", ", processor.DataCategories.Distinct(StringComparer.Ordinal)).Append('\n');
        text.Append("    ").Append(processor.UsualRegion).Append('\n');
        text.Append("    Integration reference detected. Confirm the categories of data actually sent, the service region and the applicable transfer basis for your deployment.\n");
    }

    private sealed class NoEmbeddedKeys : IPackKeyCatalogue
    {
        public bool TryGetPackSigningKey(string keyId, out byte[] publicKey) { publicKey = []; return false; }
        public bool TryGetWatermarkKey(string keyId, out byte[] publicKey) { publicKey = []; return false; }
    }
}

internal sealed record PreparedScan(ResultsInput Results, MergedRules Packs);
