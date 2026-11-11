// SPDX-License-Identifier: Apache-2.0

using InvenioTech.GateKeeper.Engine;
using InvenioTech.GateKeeper.Middleware;

namespace InvenioTech.GateKeeper.Middleware.Tests;

/// <summary>
/// Synthetic verified loads for the framework-hook tests. The values are the
/// synthetic identifiers already committed in the detection corpus; no real
/// personal data is involved (rule 10).
/// </summary>
internal static class TestRuntimes
{
    /// <summary>The synthetic identifier the synthetic rule matches.</summary>
    internal const string MarkerValue = "32826314843";

    /// <summary>A verified merged load whose one rule marks the synthetic identifier.</summary>
    internal static MergedRules Merged() => Merged([]);

    /// <summary>A verified merged load with the identifier rule plus any extra rules.</summary>
    internal static MergedRules Merged(IReadOnlyList<Rule> extra)
    {
        var pack = new LoadedPack("synthetic.runtime", "2026.11.0", PackProvenance.Bundled, new string('a', 64));
        var rules = new List<Rule>
        {
            new(
                "pii.bvn",
                Severity.Error,
                "synthetic",
                new Citation("Synthetic adapter test.", null, null),
                "2026.11.0",
                null,
                null,
                null,
                null,
                new RuleMatcher(null, new RuleListMatcher([MarkerValue], null))),
        };
        rules.AddRange(extra);
        return new MergedRules([pack], rules.Select(rule => new LoadedRule(rule, pack)).ToArray());
    }

    /// <summary>A verified merged load whose one rule matches the one-character token.</summary>
    internal static MergedRules TokenMerged(string token)
    {
        var pack = new LoadedPack("synthetic.runtime", "2026.11.0", PackProvenance.Bundled, new string('a', 64));
        var rule = new Rule(
            "pii.bvn",
            Severity.Error,
            "synthetic",
            new Citation("Synthetic adapter test.", null, null),
            "2026.11.0",
            null,
            null,
            null,
            null,
            new RuleMatcher(null, new RuleListMatcher([token], null)));
        return new MergedRules([pack], [new LoadedRule(rule, pack)]);
    }

    /// <summary>A runtime whose one rule marks the synthetic identifier.</summary>
    internal static RuntimeMatcher Matcher() => new(Merged());

    /// <summary>A runtime with the identifier rule plus any extra rules.</summary>
    internal static RuntimeMatcher Matcher(IReadOnlyList<Rule> extra) => new(Merged(extra));

    /// <summary>A runtime whose one rule matches the one-character token.</summary>
    internal static RuntimeMatcher TokenMatcher(string token) => new(TokenMerged(token));

    /// <summary>Compiles the response filter over the given pointers.</summary>
    internal static ResponseFilter Filter(this RuntimeMatcher matcher, IReadOnlyList<string> paths) => new(matcher, paths);

    /// <summary>An optional rule the application may switch off at start-up.</summary>
    internal static Rule OptionalRule() => new(
        "local.synthetic",
        Severity.Warning,
        "synthetic",
        new Citation("Synthetic optional rule.", null, null),
        "2026.11.0",
        new RedactionOptions(true),
        null,
        null,
        null,
        new RuleMatcher(null, new RuleListMatcher(["customervalue"], null)));
}
