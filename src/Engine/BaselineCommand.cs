// SPDX-License-Identifier: Apache-2.0

namespace InvenioTech.GateKeeper.Engine;

/// <summary>The baseline command's engine operation, using the existing enumerator and matcher.</summary>
public static class BaselineCommand
{
    /// <summary>Scans the work tree, applies current policy, creates an absent key and rewrites accepted fingerprints. Never scans history or prints key material.</summary>
    /// <exception cref="EngineToolException">Configuration or policy is invalid, or no rules were supplied.</exception>
    public static async Task<PolicyResult> RunAsync(string root, IReadOnlyList<Rule> rules, TimeProvider clock,
        string? evaluatedOn = null, string? sourceDateEpoch = null, CancellationToken cancellationToken = default)
    {
        var load = ConfigurationLoader.Load(root);
        if (load.Settings is not { } settings || load.Errors.Count > 0) throw new EngineToolException(string.Join(" ", load.Errors));
        if (rules.Count == 0) throw new EngineToolException("No rule pack is available for baseline. This build has no bundled pack; configure packs.local before running baseline.");
        foreach (var id in settings.SeverityOverrides.Keys.Concat(settings.DisabledRules.Select(d => d.Id)))
            if (!rules.Any(r => r.Id == id)) throw new EngineToolException($"ndpa-check.toml names unknown rule '{id}'. Load its pack or remove the rule setting.");
        var day = EvaluationDate.Resolve(clock, evaluatedOn, sourceDateEpoch);
        await PolicyStore.LoadAsync(root, rules, cancellationToken).ConfigureAwait(false);
        var enabled = rules.Where(r => !settings.DisabledRules.Any(d => d.Id == r.Id)
            && (r.Profiles is null || r.Profiles.Count == 0 || r.Profiles.Contains(settings.Profile == ScanProfile.Deep ? "deep" : "default", StringComparer.Ordinal))).ToArray();
        var scan = await new ScanOrchestrator(new MatcherEngine(enabled)).ScanAsync(new FileEnumerationOptions
        {
            Root = root,
            ScanHistory = false,
            MaxFileBytes = settings.MaxFileBytes,
            ExcludeGlobs = settings.Exclude,
            IncludeSubmodules = settings.IncludeSubmodules,
        }, cancellationToken).ConfigureAwait(false);
        await PolicyStore.EnsureKeyAsync(root, cancellationToken).ConfigureAwait(false);
        var state = await PolicyStore.LoadAsync(root, rules, cancellationToken).ConfigureAwait(false);
        var result = FindingPolicy.Apply(scan, settings, state, day);
        await PolicyStore.WriteBaselineAsync(root, result, cancellationToken).ConfigureAwait(false);
        return result;
    }
}
