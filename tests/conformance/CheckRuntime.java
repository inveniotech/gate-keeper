// SPDX-License-Identifier: Apache-2.0
import africa.gatekeeper.RuntimeMatcher;
import africa.gatekeeper.ConsentCheck;
import africa.gatekeeper.ResponseFilter;
import java.util.concurrent.CancellationException;
import java.nio.charset.StandardCharsets;
import java.nio.file.*;
import java.util.*;

class CheckRuntime {
    private static Map<String, Object> read(Path path) throws Exception { return RuntimeMatcher.map(VectorJson.parse(Files.readString(path, StandardCharsets.UTF_8))); }
    private static List<Map<String, Object>> rules(Object value) {
        List<Map<String, Object>> result = new ArrayList<>();
        for (Object item : RuntimeMatcher.list(value)) result.add(RuntimeMatcher.map(item)); return result;
    }
    private static void equal(Object actual, Object expected, String description) {
        if (!Objects.equals(actual, expected)) throw new IllegalStateException("JVM conformance differs: " + description);
    }
    private static void fails(Runnable action, Class<? extends RuntimeException> expected) {
        try { action.run(); } catch (RuntimeException error) { if (expected.isInstance(error)) return; throw error; }
        throw new IllegalStateException("Expected JVM failure: " + expected.getSimpleName());
    }
    private static void verify(Map<String, Object> sample, RuntimeMatcher runtime) {
        String name = RuntimeMatcher.string(sample.get("name")), text = RuntimeMatcher.string(sample.get("text"));
        List<Map<String, Object>> actual = new ArrayList<>(); for (var finding : runtime.matches(text)) actual.add(finding.asMap());
        equal(actual, sample.get("runtimeExpected"), name + " runtime findings");
        for (String surface : List.of("log", "response", "value")) {
            var finding = runtime.classify(text, surface, "synthetic");
            equal(finding == null ? null : finding.asMap(), sample.get("classification"), name + " classification");
        }
        String redacted = runtime.redact(text); equal(redacted, sample.get("redacted"), name + " redaction");
        equal(runtime.redact(redacted), redacted, name + " idempotence");
    }
    public static void main(String[] args) throws Exception {
        africa.gatekeeper.ResponseFailureProbe.run();
        Path directory = Path.of(args.length == 0 ? "tests/conformance" : args[0]);
        Map<String, Object> data = read(directory.resolve("vectors.json")); RuntimeMatcher runtime = new RuntimeMatcher(rules(data.get("rules")), Set.of(), null, rules(data.get("packs"))); int checked = 0;
        List<Map<String, Object>> packs = new ArrayList<>();
        for (Map<String, Object> pack : rules(data.get("packs"))) packs.add(Map.of("id", pack.get("id"), "version", pack.get("version"), "contentDigest", pack.get("contentDigest")));
        packs.sort(Comparator.comparing(pack -> RuntimeMatcher.string(pack.get("id"))));
        equal(runtime.versions(), Map.of("adapterVersion", "0.1.0", "packs", packs), "version identities");
        for (Object item : RuntimeMatcher.list(data.get("cases"))) {
            Map<String, Object> sample = RuntimeMatcher.map(item); String text = RuntimeMatcher.string(sample.get("text"));
            List<Map<String, Object>> actual = new ArrayList<>(), expected = new ArrayList<>();
            for (var finding : runtime.matches(text, RuntimeMatcher.string(sample.get("path")))) {
                Map<String, Object> row = finding.asMap(); row.remove("category"); actual.add(row);
            }
            for (Object entry : RuntimeMatcher.list(sample.get("expected"))) {
                Map<String, Object> row = RuntimeMatcher.map(entry); row.remove("line"); row.remove("column"); expected.add(row);
            }
            equal(actual, expected, RuntimeMatcher.string(sample.get("name")) + " scanner findings"); verify(sample, runtime); checked++;
        }
        for (Object item : RuntimeMatcher.list(data.get("syntheticRuntimeCases"))) {
            Map<String, Object> sample = RuntimeMatcher.map(item); verify(sample, new RuntimeMatcher(rules(sample.get("rules")))); checked++;
        }
        for (String name : List.of("digits-length", "nuban", "ng-phone-prefix", "email-not-example-domain")) {
            Map<String, Object> parameters = read(directory.resolve("validator-" + name + ".json")); parameters.put("name", name);
            for (Object item : RuntimeMatcher.list(parameters.get("cases"))) {
                Map<String, Object> sample = RuntimeMatcher.map(item);
                equal(RuntimeMatcher.validate(RuntimeMatcher.string(sample.get("text")), parameters), sample.get("accepted"), name + " validator");
            }
        }
        String limit = "😀".repeat(65536); equal(runtime.redact(limit), limit, "scalar cap");
        for (String seed : List.of("a@.", "bvn 22222222222222222222 ", "😀[REDACTED:previous.rule] ")) {
            int[] points = seed.repeat(65536).codePoints().limit(65536).toArray();
            String input = new String(points, 0, points.length); equal(runtime.redact(input), input, "cap-sized adversarial near miss");
        }
        fails(() -> runtime.redact(limit + "x"), RuntimeMatcher.InputLimitException.class);
        fails(() -> runtime.redact("\ud800"), IllegalArgumentException.class);
        fails(() -> runtime.classify("", "migration", null), IllegalArgumentException.class);
        fails(() -> runtime.classify("", "value", ""), IllegalArgumentException.class);
        Map<String, Object> first = RuntimeMatcher.map(RuntimeMatcher.list(data.get("syntheticRuntimeCases")).get(0));
        List<Map<String, Object>> raw = rules(first.get("rules")); Map<String, Object> optional = new LinkedHashMap<>(raw.get(0));
        optional.put("redaction", Map.of("optional", true)); List<String> messages = new ArrayList<>();
        RuntimeMatcher disabled = new RuntimeMatcher(List.of(optional), Set.of(RuntimeMatcher.string(optional.get("id"))), messages::add);
        equal(disabled.redact("label:mark"), "label:mark", "optional disablement"); equal(messages.size(), 1, "start-up diagnostic");
        fails(() -> new RuntimeMatcher(raw, Set.of(RuntimeMatcher.string(optional.get("id"))), messages::add), IllegalArgumentException.class);
        Map<String, Object> behaviour = read(directory.resolve("behaviour-vectors.json"));
        Map<String, Object> consentData = RuntimeMatcher.map(behaviour.get("consent"));
        for (Object item : RuntimeMatcher.list(consentData.get("cases"))) {
            Map<String, Object> sample = RuntimeMatcher.map(item); String mode = RuntimeMatcher.string(sample.get("provider"));
            int[] calls = {0}; List<String> events = new ArrayList<>();
            ConsentCheck.ConsentProvider provider = mode.equals("absent") ? null : (subject, purpose) -> {
                calls[0]++;
                if (mode.equals("provider-timeout")) throw new CancellationException("synthetic-sensitive-marker");
                if (mode.equals("caller-cancelled-during")) { Thread.currentThread().interrupt(); throw new CancellationException(); }
                if (mode.equals("throws")) throw new IllegalStateException("synthetic-sensitive-marker");
                if (mode.equals("invalid")) return null;
                return mode.equals("granted") || mode.equals("cancelled") ? ConsentCheck.ConsentState.GRANTED
                    : mode.equals("denied") ? ConsentCheck.ConsentState.DENIED : ConsentCheck.ConsentState.UNKNOWN;
            };
            ConsentCheck consent = new ConsentCheck(provider, (purpose, type) -> events.add(purpose + ":" + type));
            String state; Boolean granted;
            try {
                if (mode.equals("cancelled")) Thread.currentThread().interrupt();
                try { state = consent.check("synthetic-subject", "acme.marketing-sms").name().toLowerCase(Locale.ROOT); }
                catch (CancellationException error) { state = "cancelled"; }
                try { granted = consent.isGranted("synthetic-subject", "acme.marketing-sms"); }
                catch (CancellationException error) { granted = null; }
            } finally { Thread.interrupted(); }
            equal(state, sample.get("expected"), "consent " + mode); equal(granted, sample.get("isGranted"), "consent boolean " + mode);
            equal(calls[0], sample.get("calls"), "provider calls"); equal(events.size(), sample.get("diagnostics"), "safe diagnostics");
            if (events.toString().contains("synthetic-sensitive-marker") || events.toString().contains("synthetic-subject")) throw new IllegalStateException("Unsafe consent diagnostics");
        }
        ConsentCheck consent = new ConsentCheck((subject, purpose) -> ConsentCheck.ConsentState.DENIED, (purpose, type) -> {});
        for (Object item : RuntimeMatcher.list(consentData.get("purposes"))) {
            Map<String, Object> sample = RuntimeMatcher.map(item); String purpose = RuntimeMatcher.string(sample.get("value"));
            if (Boolean.TRUE.equals(sample.get("accepted"))) equal(consent.check("synthetic-subject", purpose), ConsentCheck.ConsentState.DENIED, "purpose boundary");
            else fails(() -> consent.check("synthetic-subject", purpose), IllegalArgumentException.class);
        }
        equal(consent.check("😀".repeat(1024), "a.b"), ConsentCheck.ConsentState.DENIED, "subject boundary");
        fails(() -> consent.check("😀".repeat(1025), "a.b"), IllegalArgumentException.class);
        Map<String, Object> responseData = RuntimeMatcher.map(behaviour.get("response")); RuntimeMatcher responseRuntime = new RuntimeMatcher(rules(responseData.get("rules")));
        for (Object item : RuntimeMatcher.list(responseData.get("cases"))) {
            Map<String, Object> sample = RuntimeMatcher.map(item); List<String> paths = new ArrayList<>();
            for (Object value : RuntimeMatcher.list(sample.get("paths"))) paths.add(RuntimeMatcher.string(value));
            ResponseFilter filter = new ResponseFilter(responseRuntime, paths); Object transformed = null; boolean failed = false, abort = false;
            try { transformed = filter.transform(sample.get("body"), Boolean.TRUE.equals(sample.get("lengthKnown")), Boolean.TRUE.equals(sample.get("headersCommitted")), RuntimeMatcher.string(sample.get("contentType"))); }
            catch (ResponseFilter.ResponseFilterException error) { failed = true; abort = error.abortRequired; }
            equal(failed, sample.get("failed"), "response failure " + sample.get("name"));
            equal(abort, sample.get("abortRequired"), "response abort " + sample.get("name"));
            if (!failed) equal(transformed, sample.get("expected"), "response result " + sample.get("name"));
        }
        fails(() -> new ResponseFilter(responseRuntime, List.of("/bad~9path")), IllegalArgumentException.class);
        ResponseFilter wildcard = new ResponseFilter(responseRuntime, List.of("/customers/*/phone"));
        fails(() -> wildcard.transform(Map.of("customers", List.of(Map.of("phone", "marker"), Map.of("phone", "x".repeat(65537))))), ResponseFilter.ResponseFilterException.class);
        fails(() -> wildcard.transform(Map.of("customers", List.of(Map.of("phone", "\ud800")))), ResponseFilter.ResponseFilterException.class);
        System.out.println("JVM native matching/redaction passed " + checked + " cases; four validators, consent and " + RuntimeMatcher.list(responseData.get("cases")).size() + " atomic response cases passed.");
    }
}
