// SPDX-License-Identifier: Apache-2.0
package africa.gatekeeper;

import java.util.*;
import java.util.function.Consumer;
import java.util.regex.Pattern;

/** Java-friendly JVM runtime core over already validated, verified rule data. No I/O. */
public final class RuntimeMatcher {
    /** The scalar cap defined by rule-pack section 7.5. */
    public static final int MAXIMUM_SCALARS = 65536;
    private static final Pattern MASK = Pattern.compile("\\[REDACTED:[a-z][a-z0-9]*(?:\\.[a-z][a-z0-9]*)*\\]");
    private final List<Rule> rules;
    private final List<Map<String, Object>> packs;

    /** A scalar-limit failure without source content. */
    public static final class InputLimitException extends RuntimeException {
        public InputLimitException() { super("Middleware input or result exceeds 65,536 Unicode scalars. Split the value."); }
    }
    /** An incompatible or invalid already loaded rule model. */
    public static final class PackLoadException extends RuntimeException {
        public PackLoadException() { super("Invalid middleware rule data. Load a verified compatible signed pack."); }
    }
    /** Content-free emitted span, in Unicode scalar coordinates. */
    public static final class Finding {
        public final String ruleId, category, severity;
        public final int start, length;
        Finding(Rule rule, int start, int length) {
            this.ruleId = rule.id; this.category = rule.category; this.severity = rule.severity; this.start = start; this.length = length;
        }
        public Map<String, Object> asMap() {
            Map<String, Object> value = new LinkedHashMap<>();
            value.put("ruleId", ruleId); value.put("category", category); value.put("severity", severity);
            value.put("start", start); value.put("length", length); return value;
        }
    }
    private static final Comparator<Finding> SELECTION = Comparator.comparingInt((Finding item) -> item.start)
        .thenComparing(Comparator.comparingInt((Finding item) -> item.length).reversed()).thenComparing(item -> item.ruleId);

    /** Constructs immutable prepared rules without scanner-policy input. */
    public RuntimeMatcher(List<Map<String, Object>> input) { this(input, Set.of(), null, List.of()); }
    public RuntimeMatcher(List<Map<String, Object>> input, Set<String> disabled, Consumer<String> diagnostic) {
        this(input, disabled, diagnostic, List.of());
    }
    public RuntimeMatcher(List<Map<String, Object>> input, Set<String> disabled, Consumer<String> diagnostic, List<Map<String, Object>> metadata) {
        if (input == null || input.isEmpty()) throw new PackLoadException();
        Set<String> off = disabled == null ? Set.of() : Set.copyOf(disabled);
        List<Rule> prepared = new ArrayList<>(); Set<String> ids = new HashSet<>();
        for (Map<String, Object> value : input) {
            Rule rule = new Rule(value); if (!ids.add(rule.id)) throw new PackLoadException(); prepared.add(rule);
        }
        for (String id : off) if (prepared.stream().noneMatch(rule -> rule.id.equals(id) && rule.optional))
            throw new IllegalArgumentException("Only a loaded optional rule may be disabled.");
        if (!off.isEmpty()) {
            if (diagnostic == null) throw new IllegalArgumentException("Optional disablement needs a start-up diagnostic.");
            diagnostic.accept("Disabled optional redaction rules: " + String.join(", ", new TreeSet<>(off)));
        }
        prepared.removeIf(rule -> off.contains(rule.id)); rules = List.copyOf(prepared);
        List<Map<String, Object>> selected = new ArrayList<>();
        for (Map<String, Object> pack : metadata) selected.add(Map.of("id", pack.get("id"), "version", pack.get("version"), "contentDigest", pack.get("contentDigest")));
        selected.sort(Comparator.comparing(pack -> string(pack.get("id")))); packs = List.copyOf(selected);
    }

    /** Version reporting contains only code and loaded-pack identities. */
    public Map<String, Object> versions() { return Map.of("adapterVersion", BuildVersion.VALUE, "packs", packs); }

    /** All runtime matches; context and repository paths never select runtime rules. */
    public List<Finding> matches(String text) {
        int[] chars = scalars(text); List<Finding> result = new ArrayList<>(); int cursor = 0, utf16Cursor = 0;
        var masks = MASK.matcher(text);
        while (masks.find()) {
            int start = cursor + text.codePointCount(utf16Cursor, masks.start());
            matchSegment(Arrays.copyOfRange(chars, cursor, start), cursor, result);
            cursor = start + text.codePointCount(masks.start(), masks.end());
            utf16Cursor = masks.end();
        }
        matchSegment(Arrays.copyOfRange(chars, cursor, chars.length), cursor, result);
        result.sort(Comparator.comparingInt((Finding item) -> item.start).thenComparing(item -> item.ruleId).thenComparingInt(item -> item.length));
        return List.copyOf(result);
    }

    /** Selects earliest, longest, then ordinal rule; empty input returns null. */
    public Finding classify(String text, String surface, String fieldName) {
        if (!("log".equals(surface) || "response".equals(surface) || "value".equals(surface)) || "".equals(fieldName))
            throw new IllegalArgumentException("Use a valid surface and a non-empty optional field name.");
        return matches(text).stream().min(SELECTION).orElse(null);
    }

    /** Masks only emitted spans; reserved literal masks remain unchanged. */
    public String redact(String text) {
        int[] chars = scalars(text); List<Finding> selected = new ArrayList<>(matches(text)); selected.sort(SELECTION);
        StringBuilder output = new StringBuilder(); int cursor = 0, size = 0;
        for (Finding finding : selected) {
            if (finding.start < cursor) continue;
            size += finding.start - cursor + 11 + finding.ruleId.length();
            if (size > MAXIMUM_SCALARS) throw new InputLimitException();
            output.append(new String(chars, cursor, finding.start - cursor));
            output.append("[REDACTED:").append(finding.ruleId).append(']'); cursor = finding.start + finding.length;
        }
        if (size + chars.length - cursor > MAXIMUM_SCALARS) throw new InputLimitException();
        output.append(new String(chars, cursor, chars.length - cursor)); String value = output.toString(); scalars(value); return value;
    }

    /** Rejects malformed surrogate sequences and enforces the scalar cap. */
    public static int[] scalars(String text) {
        if (text == null) throw new IllegalArgumentException("Middleware input must be Unicode text.");
        int count = 0;
        for (int at = 0; at < text.length(); at++) {
            char c = text.charAt(at);
            if (Character.isHighSurrogate(c)) {
                if (++at >= text.length() || !Character.isLowSurrogate(text.charAt(at))) throw new IllegalArgumentException("Invalid Unicode text.");
            } else if (Character.isLowSurrogate(c)) throw new IllegalArgumentException("Invalid Unicode text.");
            if (++count > MAXIMUM_SCALARS) throw new InputLimitException();
        }
        return text.codePoints().toArray();
    }

    private void matchSegment(int[] chars, int base, List<Finding> result) {
        String text = new String(chars, 0, chars.length), folded = fold(text);
        int[] scalarOffsets = new int[text.length() + 1]; int utf16 = 0;
        for (int at = 0; at < chars.length; at++) { scalarOffsets[utf16] = at; utf16 += Character.charCount(chars[at]); scalarOffsets[utf16] = at + 1; }
        for (Rule rule : rules) {
            if (rule.pattern == null) {
                for (String entry : rule.entries) {
                    for (int at = text.indexOf(entry); at >= 0; at = text.indexOf(entry, at + 1))
                        if (!token(at > 0 ? text.charAt(at - 1) : -1) && !token(at + entry.length() < text.length() ? text.charAt(at + entry.length()) : -1))
                            result.add(new Finding(rule, base + scalarOffsets[at], scalarOffsets[at + entry.length()] - scalarOffsets[at]));
                }
                continue;
            }
            Map<String, Object> p = rule.pattern;
            List<String> keywords = strings(p.get("keywords"));
            if (!keywords.isEmpty() && keywords.stream().noneMatch(word -> folded.contains(fold(word)))) continue;
            if (rule.parts.stream().anyMatch(part -> part.min > 0 && !part.alternatives.isEmpty()
                && part.alternatives.stream().noneMatch(word -> folded.contains(fold(new String(word, 0, word.length)))))) continue;
            List<int[]> keywordHits = keywordIndex(chars, keywords);
            Map<Long, ShapeMatch> memo = new HashMap<>();
            int[][] runs = new int[rule.parts.size()][];
            for (int index = 0; index < runs.length; index++) {
                Part part = rule.parts.get(index); if (!part.alternatives.isEmpty()) continue;
                runs[index] = new int[chars.length + 1];
                for (int position = chars.length - 1; position >= 0; position--)
                    if (part.accepts(chars[position])) runs[index][position] = Math.min(part.max, runs[index][position + 1] + 1);
            }
            int at = 0;
            String boundary = string(p.get("boundary"));
            while (at < chars.length) {
                Part first = rule.parts.get(0);
                if (first.min > 0 && first.alternatives.isEmpty() && !first.accepts(chars[at])) { at++; continue; }
                if (neighbour(at > 0 ? chars[at - 1] : -1, boundary, false)) { at++; continue; }
                ShapeMatch found = walk(rule.parts, chars, 0, at, memo, runs);
                if (found == null || found.end <= at) { at++; continue; }
                int start = found.emitStart >= 0 ? found.emitStart : at, length = found.emitStart >= 0 ? found.emitLength : found.end - at;
                boolean before = neighbour(at > 0 ? chars[at - 1] : -1, boundary, false);
                boolean after = neighbour(found.end < chars.length ? chars[found.end] : -1, boundary, true);
                String value = new String(chars, start, length);
                if (length > 0 && !before && !after && (keywords.isEmpty() || keywordNear(keywordHits, at, found.end, number(p.get("window"), 0)))
                    && (p.get("validator") == null || validate(value, map(p.get("validator")))) && !excluded(chars, start, length, p)) {
                    result.add(new Finding(rule, base + start, length)); at = found.end;
                } else at++;
            }
        }
    }

    private static final class Rule {
        final String id, category, severity; final boolean optional;
        final Map<String, Object> pattern; final List<Part> parts; final List<String> entries;
        Rule(Map<String, Object> raw) {
            id = string(raw.get("id")); category = string(raw.get("category")); severity = string(raw.get("severity"));
            optional = Boolean.TRUE.equals(map(raw.get("redaction")).get("optional"));
            Map<String, Object> matcher = map(raw.get("matcher"));
            if ((matcher.get("pattern") == null) == (matcher.get("list") == null)) throw new PackLoadException();
            pattern = matcher.get("pattern") == null ? null : map(immutable(matcher.get("pattern")));
            if (pattern != null) {
                if (!List.of("none", "digit", "alnum", "email").contains(pattern.get("boundary"))) throw new PackLoadException();
                if (pattern.get("validator") != null && !List.of("digits-length", "nuban", "ng-phone-prefix", "email-not-example-domain")
                    .contains(map(pattern.get("validator")).get("name"))) throw new PackLoadException();
                if (!strings(pattern.get("keywords")).isEmpty() && (!integer(pattern.get("window"))
                    || number(pattern.get("window"), 0) < 1 || number(pattern.get("window"), 0) > 4096)) throw new PackLoadException();
            }
            List<Part> parsed = new ArrayList<>(); int width = 0, emits = 0;
            if (pattern != null) for (Object value : list(pattern.get("shape"))) {
                Part part = new Part(map(value)); parsed.add(part); width += part.max * part.width;
                if (part.emit) emits++;
            }
            if (pattern != null && (parsed.isEmpty() || parsed.size() > 32 || width > 256 || emits > 1)) throw new PackLoadException();
            parts = List.copyOf(parsed); Map<String, Object> names = map(matcher.get("list"));
            List<String> tokens = new ArrayList<>(strings(names.get("package"))); tokens.addAll(strings(names.get("hostname"))); entries = List.copyOf(tokens);
        }
    }
    private static final class Part {
        final int min, max, width; final boolean emit; final String kind;
        final List<int[]> alternatives; final List<int[]> ranges;
        Part(Map<String, Object> raw) {
            if (!integer(raw.get("min")) || !integer(raw.get("max"))) throw new PackLoadException();
            int atoms = 0; for (String key : List.of("literal", "class", "ranges", "oneOf")) if (raw.containsKey(key)) atoms++;
            if (atoms != 1 || raw.containsKey("class") && !List.of("digit", "alnum").contains(raw.get("class"))) throw new PackLoadException();
            if (raw.containsKey("oneOf") && (!(raw.get("oneOf") instanceof List<?>) || list(raw.get("oneOf")).isEmpty())) throw new PackLoadException();
            min = number(raw.get("min"), -1); max = number(raw.get("max"), -1); emit = Boolean.TRUE.equals(raw.get("emit")); kind = string(raw.get("class"));
            List<String> words = new ArrayList<>(strings(raw.get("oneOf"))); if (raw.get("literal") != null) words.add(string(raw.get("literal")));
            words.sort(Comparator.comparingInt((String value) -> value.codePointCount(0, value.length())).reversed().thenComparing(Comparator.naturalOrder()));
            alternatives = new ArrayList<>(); int size = 1;
            for (String word : words) { if (!word.matches("[ -~]+")) throw new PackLoadException(); int[] points = word.codePoints().toArray(); alternatives.add(points); size = Math.max(size, points.length); }
            if (raw.containsKey("ranges") && (!(raw.get("ranges") instanceof List<?>) || list(raw.get("ranges")).isEmpty())) throw new PackLoadException();
            ranges = new ArrayList<>(); for (Object range : list(raw.get("ranges"))) {
                Map<String, Object> pair = map(range); String low = string(pair.get("low")), high = string(pair.get("high"));
                if (!low.matches("[ -~]") || !high.matches("[ -~]") || low.compareTo(high) > 0) throw new PackLoadException();
                ranges.add(new int[] { low.codePointAt(0), high.codePointAt(0) });
            }
            if (min < 0 || max < 1 || max > 64 || min > max || raw.get("oneOf") != null && max != 1) throw new PackLoadException(); width = size;
        }
        List<Integer> atom(int[] text, int at) {
            List<Integer> ends = new ArrayList<>();
            if (!alternatives.isEmpty()) for (int[] word : alternatives) { if (equalsAt(text, at, word, true)) ends.add(at + word.length); }
            else if (at < text.length && accepts(text[at])) ends.add(at + 1);
            return ends;
        }
        boolean accepts(int c) {
            if ("digit".equals(kind)) return digit(c);
            if ("alnum".equals(kind)) return alnum(c);
            for (int[] range : ranges) if (c >= range[0] && c <= range[1]) return true;
            return false;
        }
    }
    private static final class ShapeMatch {
        final int end, emitStart, emitLength;
        ShapeMatch(int end, int start, int length) { this.end = end; emitStart = start; emitLength = length; }
    }
    private static ShapeMatch walk(List<Part> parts, int[] text, int index, int at, Map<Long, ShapeMatch> memo, int[][] runs) {
        if (index == parts.size()) return new ShapeMatch(at, -1, 0);
        long key = (long) index * (text.length + 1) + at; if (memo.containsKey(key)) return memo.get(key);
        Part part = parts.get(index);
        if (runs[index] != null) {
            for (int count = runs[index][at]; count >= part.min; count--) {
                ShapeMatch tail = walk(parts, text, index + 1, at + count, memo, runs);
                if (tail != null) {
                    ShapeMatch value = part.emit ? new ShapeMatch(tail.end, at, count) : tail;
                    memo.put(key, value); return value;
                }
            }
            memo.put(key, null); return null;
        }
        List<List<Integer>> levels = new ArrayList<>(); levels.add(List.of(at));
        for (int count = 1; count <= part.max; count++) {
            Set<Integer> next = new TreeSet<>(Comparator.reverseOrder());
            for (int position : levels.get(count - 1)) next.addAll(part.atom(text, position));
            levels.add(new ArrayList<>(next)); if (next.isEmpty()) break;
        }
        for (int count = Math.min(part.max, levels.size() - 1); count >= part.min; count--) for (int end : levels.get(count)) {
            ShapeMatch tail = walk(parts, text, index + 1, end, memo, runs);
            if (tail != null) {
                ShapeMatch value = part.emit ? new ShapeMatch(tail.end, at, end - at) : tail;
                memo.put(key, value); return value;
            }
        }
        memo.put(key, null); return null;
    }

    /** Native implementations of the four named validators. */
    public static boolean validate(String value, Map<String, Object> parameters) {
        int[] digits = value.chars().filter(RuntimeMatcher::digit).map(c -> c - '0').toArray();
        switch (string(parameters.get("name"))) {
            case "digits-length": return digits.length == number(parameters.get("length"), 0);
            case "ng-phone-prefix": return strings(parameters.get("prefixes")).stream().anyMatch(value::startsWith);
            case "nuban":
                if (digits.length != 10) return false;
                int[] weights = {3, 7, 3, 3, 7, 3, 3, 7, 3}; int sum = 0;
                for (int i = 0; i < 9; i++) sum += digits[i] * weights[i]; return (10 - sum % 10) % 10 == digits[9];
            case "email-not-example-domain":
                int at = value.lastIndexOf('@'); if (at < 1 || at == value.length() - 1) return false;
                String domain = fold(value.substring(at + 1));
                for (String reserved : List.of("example.com", "example.net", "example.org", "test", "invalid", "localhost"))
                    if (domain.equals(reserved) || domain.length() > reserved.length() + 1 && domain.endsWith("." + reserved)) return false;
                return true;
            default: throw new PackLoadException();
        }
    }

    private static List<int[]> keywordIndex(int[] text, List<String> keywords) {
        List<int[]> hits = new ArrayList<>();
        for (String word : keywords) {
            int[] points = word.codePoints().toArray();
            for (int at = 0; at + points.length <= text.length; at++) if (equalsAt(text, at, points, true)) {
                hits.add(new int[] { at, at + points.length });
            }
        }
        hits.sort(Comparator.comparingInt(hit -> hit[0])); int furthest = 0;
        for (int[] hit : hits) { furthest = Math.max(furthest, hit[1]); hit[1] = furthest; }
        return hits;
    }
    private static boolean keywordNear(List<int[]> hits, int start, int end, int window) {
        int low = 0, high = hits.size();
        while (low < high) { int mid = (low + high) >>> 1; if (hits.get(mid)[0] <= end + window) low = mid + 1; else high = mid; }
        return low > 0 && hits.get(low - 1)[1] >= start - window;
    }
    private static boolean excluded(int[] text, int start, int length, Map<String, Object> p) {
        String value = new String(text, start, length);
        if (strings(p.get("excludeValues")).contains(value) || strings(p.get("excludePrefixes")).stream().anyMatch(value::startsWith)) return true;
        for (String name : strings(p.get("exclusions"))) {
            if (name.equals("repeated-digits") && value.matches("([0-9])\\1+")) return true;
            if (name.equals("sequential-digits") && value.matches("[0-9]{2,}")) {
                boolean up = true, down = true;
                for (int i = 1; i < value.length(); i++) { int step = (value.charAt(i) - value.charAt(i - 1) + 10) % 10; up &= step == 1; down &= step == 9; }
                if (up || down) return true;
            }
            if (name.equals("hex-fragment") && value.matches("[0-9A-Fa-f]{2,}") && Pattern.compile("[A-Fa-f]").matcher(value).find()) return true;
            if (name.equals("version-string")) {
                int left = start, right = start + length;
                while (left > 0 && (digit(text[left - 1]) || text[left - 1] == '.')) left--;
                if (left > 0 && (text[left - 1] == 'v' || text[left - 1] == 'V') && (left == 1 || !alnum(text[left - 2]))) left--;
                while (right < text.length && (digit(text[right]) || text[right] == '.')) right++;
                if (new String(text, left, right - left).matches("[vV]?[0-9]+(?:\\.[0-9]+)+")) return true;
            }
            if (name.equals("uuid-fragment") || name.equals("date-or-timestamp")) {
                if (name.equals("date-or-timestamp") && value.matches("[0-9]{8}") && date(value.substring(0, 4), value.substring(4, 6), value.substring(6, 8))) return true;
                int reach = name.equals("uuid-fragment") ? 36 : 32;
                for (int at = Math.max(0, start - reach); at < Math.min(text.length, start + length + reach); at++) {
                    String tail = new String(text, at, Math.min(36, text.length - at)); int width = 0;
                    if (name.equals("uuid-fragment") && Pattern.compile("^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}").matcher(tail).find()) width = 36;
                    if (name.equals("date-or-timestamp") && tail.length() >= 10) {
                        String prefix = tail.substring(0, 10);
                        if (prefix.matches("[0-9]{4}([-/])[0-9]{2}\\1[0-9]{2}") && date(prefix.substring(0, 4), prefix.substring(5, 7), prefix.substring(8, 10)))
                            width = prefix.charAt(4) == '-' && Pattern.compile("^.{10}T[0-9]{2}:[0-9]{2}:[0-9]{2}").matcher(tail).find() ? 19 : 10;
                        else if (prefix.matches("[0-9]{2}-[0-9]{2}-[0-9]{4}") && date(prefix.substring(6, 10), prefix.substring(3, 5), prefix.substring(0, 2))) width = 10;
                    }
                    if (width > 0 && start < at + width && at < start + length) return true;
                }
            }
        }
        return false;
    }
    private static boolean date(String year, String month, String day) {
        int y = Integer.parseInt(year), m = Integer.parseInt(month), d = Integer.parseInt(day);
        int[] days = {31, 29, 31, 30, 31, 30, 31, 31, 30, 31, 30, 31};
        return y >= 1900 && y <= 2100 && m >= 1 && m <= 12 && d >= 1 && d <= days[m - 1];
    }
    private static boolean equalsAt(int[] text, int at, int[] word, boolean folded) {
        if (at + word.length > text.length) return false;
        for (int i = 0; i < word.length; i++) if ((folded ? fold(text[at + i]) : text[at + i]) != (folded ? fold(word[i]) : word[i])) return false;
        return true;
    }
    // The email boundary is asymmetric (IQ-91): a leading '.' blocks a match, but a trailing '.' does not.
    private static boolean neighbour(int c, String boundary, boolean trailing) {
        if (boundary.equals("digit")) return digit(c);
        if (boundary.equals("alnum")) return alnum(c);
        if (boundary.equals("email")) return alnum(c) || c == '_' || c == '%' || c == '+' || c == '-' || (!trailing && c == '.');
        return false;
    }
    private static boolean digit(int c) { return c >= '0' && c <= '9'; }
    private static boolean alnum(int c) { return digit(c) || c >= 'A' && c <= 'Z' || c >= 'a' && c <= 'z'; }
    private static boolean token(int c) { return alnum(c) || c == '.' || c == '-' || c == '_' || c == '+'; }
    private static int fold(int c) { return c >= 'A' && c <= 'Z' ? c + 32 : c; }
    private static String fold(String value) { return value.codePoints().collect(StringBuilder::new, (out, c) -> out.appendCodePoint(fold(c)), StringBuilder::append).toString(); }
    public static String string(Object value) { return value instanceof String ? (String) value : ""; }
    private static int number(Object value, int fallback) { return value instanceof Number ? ((Number) value).intValue() : fallback; }
    private static boolean integer(Object value) {
        if (!(value instanceof Number)) return false;
        double number = ((Number) value).doubleValue();
        return Double.isFinite(number) && number == Math.rint(number) && number >= Integer.MIN_VALUE && number <= Integer.MAX_VALUE;
    }
    public static Map<String, Object> map(Object value) {
        Map<String, Object> result = new LinkedHashMap<>();
        if (value instanceof Map<?, ?>) for (Map.Entry<?, ?> entry : ((Map<?, ?>) value).entrySet()) {
            if (!(entry.getKey() instanceof String)) throw new PackLoadException(); result.put((String) entry.getKey(), entry.getValue());
        }
        return result;
    }
    public static List<Object> list(Object value) { return value instanceof List<?> ? new ArrayList<>((List<?>) value) : List.of(); }
    private static Object immutable(Object value) {
        if (value instanceof Map<?, ?>) {
            Map<String, Object> copy = new LinkedHashMap<>();
            for (Map.Entry<String, Object> entry : map(value).entrySet()) copy.put(entry.getKey(), immutable(entry.getValue()));
            return Collections.unmodifiableMap(copy);
        }
        if (value instanceof List<?>) { List<Object> copy = new ArrayList<>(); for (Object item : list(value)) copy.add(immutable(item)); return Collections.unmodifiableList(copy); }
        return value;
    }
    private static List<String> strings(Object value) { List<String> result = new ArrayList<>(); for (Object item : list(value)) result.add(string(item)); return result; }
}
