// SPDX-License-Identifier: Apache-2.0
import africa.gatekeeper.RuntimeMatcher;
import java.nio.file.*;
import java.util.*;

/** Observational benchmark kept outside deterministic conformance gates. */
class BenchmarkRuntime {
    public static void main(String[] args) throws Exception {
        Map<String, Object> data = RuntimeMatcher.map(VectorJson.parse(Files.readString(Path.of("tests/conformance/vectors.json"))));
        List<Map<String, Object>> rules = new ArrayList<>();
        for (Object value : RuntimeMatcher.list(data.get("rules"))) rules.add(RuntimeMatcher.map(value));
        RuntimeMatcher runtime = new RuntimeMatcher(rules);
        for (int warm = 0; warm < 3; warm++) runtime.redact("synthetic near miss");
        String[] names = {"email-near-miss", "email-missing-domain", "keyword-digits", "reserved-masks"};
        String[] seeds = {"a", "a@.", "bvn 22222222222222222222 ", "😀[REDACTED:previous.rule] "};
        for (int size : new int[] {1000, 10000, 65536}) for (int scenario = 0; scenario < seeds.length; scenario++) {
            String seed = seeds[scenario]; int width = seed.codePointCount(0, seed.length());
            int[] points = seed.repeat((size + width - 1) / width).codePoints().limit(size).toArray();
            String input = new String(points, 0, points.length); long start = System.nanoTime();
            String result = runtime.redact(input); double elapsed = (System.nanoTime() - start) / 1_000_000.0;
            if (!result.equals(input)) throw new IllegalStateException("Unexpected benchmark outcome.");
            System.out.printf(Locale.ROOT, "JVM %s: %d scalars, %.2f ms%n", names[scenario], size, elapsed);
        }
    }
}
