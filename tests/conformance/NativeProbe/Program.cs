// SPDX-License-Identifier: Apache-2.0
using System.Text.Json.Nodes;
using InvenioTech.GateKeeper.Engine;
using InvenioTech.GateKeeper.Middleware;

var pack = new LoadedPack("synthetic.runtime", "2026.11.0", PackProvenance.Bundled, new string('a', 64));
var rule = new Rule("local.marker", Severity.Note, "synthetic", new Citation("Synthetic native probe.", null, null),
    "2026.11.0", null, null, null, null, new RuleMatcher(null, new RuleListMatcher(["marker"], null)));
var runtime = new RuntimeMatcher(new MergedRules([pack], [new LoadedRule(rule, pack)]));
var redacted = runtime.Redact("😀 marker");
if (redacted != "😀 [REDACTED:local.marker]" || runtime.Redact(redacted) != redacted) return 1;
var finding = runtime.Classify("😀 marker", new RuntimeContext("value"));
if (finding is null || finding.Start != 2 || finding.Length != 6) return 1;
var consent = new ConsentCheck(new Provider(), _ => { });
if (!await consent.IsGrantedAsync("synthetic-subject", "acme.marketing-sms")) return 1;
var body = JsonNode.Parse("""{"customers":[{"phone":"marker"},{"phone":"marker"}]}""")!;
var response = new ResponseFilter(runtime, ["/customers/*/phone"]).Transform(body)!;
if (response["customers"]![1]!["phone"]!.GetValue<string>() != "[REDACTED:local.marker]") return 1;
if (body["customers"]![1]!["phone"]!.GetValue<string>() != "marker") return 1;
if (runtime.Versions().Packs[0].ContentDigest != new string('a', 64)) return 1;
Console.WriteLine("Middleware NativeAOT conformance passed.");
return 0;

sealed class Provider : IConsentProvider
{
    public ValueTask<ConsentState> GetStateAsync(string subjectId, string purpose, CancellationToken cancellationToken)
        => ValueTask.FromResult(ConsentState.Granted);
}
