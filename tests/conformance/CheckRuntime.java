// SPDX-License-Identifier: Apache-2.0
import africa.gatekeeper.RuntimeMatcher;
import africa.gatekeeper.ConsentCheck;
import africa.gatekeeper.ResponseFilter;
import java.util.concurrent.CancellationException;
import java.nio.charset.StandardCharsets;
import java.nio.file.*;
import java.util.*;
import java.security.MessageDigest;
import java.util.regex.Pattern;

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
    private static void invalidModels(List<Map<String, Object>> source) {
        Map<String, Object> original = source.stream().filter(rule -> RuntimeMatcher.map(rule.get("matcher")).get("pattern") != null).findFirst().orElseThrow();
        for (Map<String, Object> change : List.of(
            Map.<String, Object>of("boundary", "unknown"), Map.<String, Object>of("validator", Map.of("name", "unknown")),
            Map.<String, Object>of("keywords", List.of("synthetic"), "window", 0),
            Map.<String, Object>of("shape", List.of(Map.of("min", 1, "max", 1, "class", "digit", "literal", "x"))),
            Map.<String, Object>of("shape", List.of(Map.of("min", 1, "max", 1))),
            Map.<String, Object>of("shape", List.of(Map.of("min", 1, "max", 1, "class", "unknown"))),
            Map.<String, Object>of("shape", List.of(Map.of("min", 1, "max", 1, "oneOf", List.of()))),
            Map.<String, Object>of("shape", List.of(Map.of("min", 1.5, "max", 2, "class", "digit"))),
            Map.<String, Object>of("shape", List.of(Map.of("min", 1, "max", 1, "literal", ""))),
            Map.<String, Object>of("shape", List.of(Map.of("min", 1, "max", 1, "literal", "x\n"))),
            Map.<String, Object>of("shape", List.of(Map.of("min", 1, "max", 1, "ranges", List.of(Map.of("low", "z", "high", "a"))))))) {
            Map<String, Object> rule = new LinkedHashMap<>(original), matcher = RuntimeMatcher.map(rule.get("matcher")), pattern = RuntimeMatcher.map(matcher.get("pattern"));
            pattern.putAll(change); matcher.put("pattern", pattern); rule.put("matcher", matcher);
            fails(() -> new RuntimeMatcher(List.of(rule)), RuntimeMatcher.PackLoadException.class);
        }
        new RuntimeMatcher(source, null, null);
    }
    private static boolean glob(String pattern, String path) { return globParts(pattern.split("/", -1), path.split("/", -1), 0, 0); }
    private static boolean globParts(String[] p, String[] path, int i, int j) {
        if (i == p.length) return j == path.length;
        if (p[i].equals("**")) return globParts(p, path, i + 1, j) || j < path.length && globParts(p, path, i, j + 1);
        if (j == path.length) return false;
        String expression = "^" + String.join(".*", Arrays.stream(p[i].split("\\*", -1)).map(Pattern::quote).toArray(String[]::new)) + "$";
        return path[j].matches(expression) && globParts(p, path, i + 1, j + 1);
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
        invalidModels(rules(data.get("rules")));
        equal(runtime.versions(), data.get("runtimeVersions"), "actual .NET loaded version identities");
        equal(runtime.versions().get("adapterVersion"), Files.readString(Path.of("middleware/version.txt")).trim(), "build version");
        for (Map<String, Object> pack : rules(data.get("packs"))) {
            byte[] bytes = Files.readAllBytes(directory.resolve("../..").resolve(RuntimeMatcher.string(pack.get("path"))));
            StringBuilder digest = new StringBuilder();
            for (byte value : MessageDigest.getInstance("SHA-256").digest(bytes)) digest.append(String.format(Locale.ROOT, "%02x", value & 255));
            equal(pack.get("contentDigest"), digest.toString(), "actual source content digest");
        }
        Map<String, RuntimeMatcher> scannerScopes = new HashMap<>();
        for (Object item : RuntimeMatcher.list(data.get("cases"))) {
            Map<String, Object> sample = RuntimeMatcher.map(item); String text = RuntimeMatcher.string(sample.get("text"));
            List<Map<String, Object>> actual = new ArrayList<>(), expected = new ArrayList<>();
            String path = RuntimeMatcher.string(sample.get("path"));
            RuntimeMatcher scanner = scannerScopes.computeIfAbsent(path, ignored -> {
                List<Map<String, Object>> scoped = new ArrayList<>();
                for (Map<String, Object> rule : rules(data.get("rules"))) {
                    Map<String, Object> pattern = RuntimeMatcher.map(RuntimeMatcher.map(rule.get("matcher")).get("pattern"));
                    List<Object> paths = RuntimeMatcher.list(RuntimeMatcher.map(pattern.get("location")).get("paths"));
                    if (paths.isEmpty() || paths.stream().anyMatch(value -> glob(RuntimeMatcher.string(value), path))) scoped.add(rule);
                }
                return new RuntimeMatcher(scoped);
            });
            for (var finding : scanner.matches(text)) {
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
        Object deep = "marker";
        for (int depth = 0; depth < 100; depth++) deep = List.of(deep);
        equal(new ResponseFilter(responseRuntime, List.of()).transform(deep), deep, "unprotected deep response");
        Map<String, Object> nested = Map.of("phone", "marker"); String pointer = "/phone";
        for (int depth = 0; depth < 100; depth++) { nested = Map.of("child", nested); pointer = "/child" + pointer; }
        Object transformedDeep = new ResponseFilter(responseRuntime, List.of(pointer)).transform(nested, true, false, "application/json");
        for (int depth = 0; depth < 100; depth++) transformedDeep = RuntimeMatcher.map(transformedDeep).get("child");
        equal(RuntimeMatcher.map(transformedDeep).get("phone"), "[REDACTED:local.marker]", "protected host-bounded deep response");
        fails(() -> new ResponseFilter(responseRuntime, List.of("/phone")).transform(Map.of("phone", "marker")), ResponseFilter.ResponseFilterException.class);
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
        fails(() -> wildcard.transform(Map.of("customers", List.of(Map.of("phone", "marker"), Map.of("phone", "x".repeat(65537)))), true, false, "application/json"), ResponseFilter.ResponseFilterException.class);
        fails(() -> wildcard.transform(Map.of("customers", List.of(Map.of("phone", "\ud800"))), true, false, "application/json"), ResponseFilter.ResponseFilterException.class);
        System.out.println("JVM native matching/redaction passed " + checked + " cases; four validators, consent and " + RuntimeMatcher.list(responseData.get("cases")).size() + " atomic response cases passed.");
    }
}
