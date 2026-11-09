// SPDX-License-Identifier: Apache-2.0
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using InvenioTech.GateKeeper.Engine;
using InvenioTech.GateKeeper.Middleware;

namespace InvenioTech.GateKeeper.Corpus.Tests;

public sealed partial class ConformanceExportTests
{
    [Fact]
    public async Task Conformance_Should_ExportActualConsentAndResponseOutcomes_When_TheReferenceCoreRuns()
    {
        var root = Path.GetDirectoryName(Path.GetDirectoryName(CorpusPaths.Root))!;
        using var output = new MemoryStream();
        using (var json = new Utf8JsonWriter(output, new JsonWriterOptions { Indented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping }))
        {
            json.WriteStartObject(); json.WriteNumber("version", 1);
            json.WriteStartObject("consent"); json.WriteStartArray("cases");
            foreach (var mode in new[] { "granted", "denied", "unknown", "invalid", "absent", "throws", "cancelled", "provider-timeout", "caller-cancelled-during" })
            {
                using var cancellation = new CancellationTokenSource();
                var calls = 0; var events = new List<ConsentDiagnostic>();
                IConsentProvider? provider = mode == "absent" ? null : new VectorProvider(() =>
                {
                    calls++;
                    return mode switch
                    {
                        "granted" or "cancelled" => ConsentState.Granted,
                        "denied" => ConsentState.Denied,
                        "unknown" => ConsentState.Unknown,
                        "invalid" => (ConsentState)999,
                        "provider-timeout" => throw new TaskCanceledException("synthetic-sensitive-marker"),
                        "caller-cancelled-during" => CancelCaller(),
                        _ => throw new InvalidOperationException("synthetic-sensitive-marker"),
                    };
                });
                var check = new ConsentCheck(provider, events.Add);
                if (mode == "cancelled") cancellation.Cancel();
                string state; bool? granted;
                try { state = (await check.CheckAsync("synthetic-subject", "acme.marketing-sms", cancellation.Token)).ToString().ToLowerInvariant(); }
                catch (OperationCanceledException) { state = "cancelled"; }
                try { granted = await check.IsGrantedAsync("synthetic-subject", "acme.marketing-sms", cancellation.Token); }
                catch (OperationCanceledException) { granted = null; }
                json.WriteStartObject(); json.WriteString("provider", mode); json.WriteString("expected", state);
                if (granted is { } answer) json.WriteBoolean("isGranted", answer); else json.WriteNull("isGranted");
                json.WriteNumber("calls", calls); json.WriteNumber("diagnostics", events.Count); json.WriteEndObject();

                ConsentState CancelCaller() { cancellation.Cancel(); throw new OperationCanceledException(cancellation.Token); }
            }
            json.WriteEndArray(); json.WriteStartArray("purposes");
            foreach (var purpose in new[] { "acme.marketing-sms", "a.b", "", "single", "Acme.marketing", "acme..marketing", "acme.marketing\n",
                string.Join('.', Enumerable.Repeat(new string('a', 32), 5)), string.Join('.', Enumerable.Repeat(new string('a', 32), 5)) + "a" })
            {
                var check = new ConsentCheck(new VectorProvider(() => ConsentState.Granted), _ => { }); var accepted = true;
                try { await check.CheckAsync("synthetic-subject", purpose); } catch (ArgumentException) { accepted = false; }
                json.WriteStartObject(); json.WriteString("value", purpose); json.WriteBoolean("accepted", accepted); json.WriteEndObject();
            }
            json.WriteEndArray(); json.WriteEndObject();

            var rule = new Rule("local.marker", Severity.Note, "synthetic", new Citation("Synthetic response conformance.", null, null),
                "2026.11.0", null, null, null, null, new RuleMatcher(null, new RuleListMatcher(["marker"], null)));
            var pack = new LoadedPack("synthetic.runtime", "2026.11.0", PackProvenance.Bundled, new string('a', 64));
            var runtime = new RuntimeMatcher(new MergedRules([pack], [new LoadedRule(rule, pack)]));
            json.WriteStartObject("response"); json.WriteStartArray("rules"); JsonSerializer.Serialize(json, rule, EngineJsonSerializerContext.Default.Rule); json.WriteEndArray();
            json.WriteStartArray("cases");
            foreach (var sample in ResponseSamples())
            {
                var body = JsonNode.Parse(sample.Body); var filter = new ResponseFilter(runtime, sample.Paths);
                JsonNode? result = null; var failed = false; var abort = false;
                try { result = filter.Transform(body, sample.Known, sample.Committed, sample.ContentType); }
                catch (ResponseFilterException error) { failed = true; abort = error.AbortRequired; }
                json.WriteStartObject(); json.WriteString("name", sample.Name); json.WritePropertyName("body");
                if (body is null) json.WriteNullValue(); else body.WriteTo(json);
                json.WriteStartArray("paths"); foreach (var path in sample.Paths) json.WriteStringValue(path); json.WriteEndArray();
                json.WriteBoolean("lengthKnown", sample.Known); json.WriteBoolean("headersCommitted", sample.Committed); json.WriteString("contentType", sample.ContentType);
                json.WriteBoolean("failed", failed); json.WriteBoolean("abortRequired", abort); json.WritePropertyName("expected");
                if (result is null) json.WriteNullValue(); else result.WriteTo(json); json.WriteEndObject();
            }
            json.WriteEndArray(); json.WriteEndObject(); json.WriteEndObject();
        }
        Verify(root, "behaviour-vectors.json", Encoding.UTF8.GetString(output.ToArray()).Replace("\r\n", "\n", StringComparison.Ordinal) + "\n");
    }

    private static IEnumerable<(string Name, string Body, string[] Paths, bool Known, bool Committed, string? ContentType)> ResponseSamples()
    {
        const string customers = """{"customers":[{"phone":"marker"},{"phone":"marker"}],"other":"unchanged"}""";
        yield return ("all-array-elements", customers, ["/customers/*/phone"], true, false, "application/json");
        yield return ("exact-index-is-not-all-elements", customers, ["/customers/0/phone"], true, false, "application/json");
        yield return ("duplicate-selection", customers, ["/customers/*/phone", "/customers/0/phone"], true, false, "application/json; charset=utf-8");
        yield return ("mixed-types-fail-atomically", """{"customers":[{"phone":"marker"},{"phone":123}]}""", ["/customers/*/phone"], true, false, "application/json");
        yield return ("nested-wildcards", """{"groups":[[{"value":"marker"},{"value":"marker"}]]}""", ["/groups/*/*/value"], true, false, "application/problem+json");
        yield return ("escaped-keys", """{"a/b":{"~":{"*":"marker"}}}""", ["/a~1b/~0/~2"], true, false, "application/json");
        yield return ("empty-array", """{"customers":[]}""", ["/customers/*/phone"], true, false, "application/json");
        yield return ("missing-object-field", """{"customers":[{}]}""", ["/customers/*/phone"], true, false, "application/json");
        yield return ("wildcard-needs-array", """{"customers":{}}""", ["/customers/*/phone"], true, false, "application/json");
        yield return ("selected-null-fails", """{"phone":null}""", ["/phone"], true, false, "application/json");
        yield return ("null-parent-fails", """{"user":null}""", ["/user/phone"], true, false, "application/json");
        yield return ("missing-parent-passes", "{}", ["/user/phone"], true, false, "application/json");
        yield return ("missing-content-type-fails", customers, ["/customers/*/phone"], true, false, null);
        yield return ("empty-content-type-fails", customers, ["/customers/*/phone"], true, false, "");
        yield return ("streaming-fails", customers, ["/customers/*/phone"], false, false, "application/json");
        yield return ("committed-requires-abort", customers, ["/customers/*/phone"], true, true, "application/json");
        yield return ("wrong-content-type-fails", customers, ["/customers/*/phone"], true, false, "text/plain");
        yield return ("unprotected-pass-through", customers, [], false, true, "application/octet-stream");
        yield return ("root-string", "\"marker\"", [""], true, false, "application/json");
        yield return ("null-root-protected", "null", [""], true, false, "application/json");
        yield return ("null-root-committed", "null", [""], true, true, "application/json");
        yield return ("null-root-unprotected", "null", [], false, true, "application/octet-stream");
    }
    private sealed class VectorProvider(Func<ConsentState> answer) : IConsentProvider
    {
        public ValueTask<ConsentState> GetStateAsync(string subjectId, string purpose, CancellationToken cancellationToken) => ValueTask.FromResult(answer());
    }
}
