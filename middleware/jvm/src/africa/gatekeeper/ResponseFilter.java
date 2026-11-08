// SPDX-License-Identifier: Apache-2.0
package africa.gatekeeper;

import java.util.*;

/** Atomic filtering over a parsed JSON tree supplied by a bounded framework hook. */
public final class ResponseFilter {
    /** The host must abort instead of committing a partial or original protected body. */
    public static final class ResponseFilterException extends RuntimeException {
        public final boolean abortRequired;
        public ResponseFilterException(boolean abortRequired) {
            super("The protected response could not be filtered safely. Do not send its body."); this.abortRequired = abortRequired;
        }
    }
    private static final class Segment {
        final String key; final boolean wildcard;
        Segment(String key, boolean wildcard) { this.key = key; this.wildcard = wildcard; }
    }
    // Owned tree containers avoid unchecked casts and never alias application maps/lists.
    private static final class Node {
        Object value;
        final Map<String, Node> fields;
        final List<Node> elements;
        Node(Object input, int depth) {
            if (depth > 64) throw new ResponseFilterException(false);
            if (input instanceof Map<?, ?>) {
                fields = new LinkedHashMap<>(); elements = null;
                for (Map.Entry<?, ?> entry : ((Map<?, ?>) input).entrySet()) {
                    if (!(entry.getKey() instanceof String)) throw new ResponseFilterException(false);
                    fields.put((String) entry.getKey(), new Node(entry.getValue(), depth + 1));
                }
            } else if (input instanceof List<?>) {
                fields = null; elements = new ArrayList<>();
                for (Object item : (List<?>) input) elements.add(new Node(item, depth + 1));
            } else {
                if (input != null && !(input instanceof String || input instanceof Number || input instanceof Boolean)) throw new ResponseFilterException(false);
                fields = null; elements = null; value = input;
            }
        }
        Object result() {
            if (fields != null) { Map<String, Object> out = new LinkedHashMap<>(); for (Map.Entry<String, Node> entry : fields.entrySet()) out.put(entry.getKey(), entry.getValue().result()); return out; }
            if (elements != null) { List<Object> out = new ArrayList<>(); for (Node item : elements) out.add(item.result()); return out; }
            return value;
        }
    }
    private final RuntimeMatcher runtime;
    private final List<List<Segment>> paths;
    public ResponseFilter(RuntimeMatcher runtime, List<String> pointers) {
        this.runtime = Objects.requireNonNull(runtime); List<List<Segment>> parsed = new ArrayList<>();
        for (String pointer : pointers) {
            if (pointer == null || !pointer.isEmpty() && !pointer.startsWith("/")) throw new IllegalArgumentException("Use a JSON Pointer.");
            List<Segment> path = new ArrayList<>();
            if (!pointer.isEmpty()) for (String raw : pointer.substring(1).split("/", -1)) {
                StringBuilder key = new StringBuilder();
                for (int at = 0; at < raw.length(); at++) {
                    char c = raw.charAt(at); if (c != '~') { key.append(c); continue; }
                    if (++at >= raw.length() || raw.charAt(at) < '0' || raw.charAt(at) > '2') throw new IllegalArgumentException("Use only ~0, ~1 or ~2 in protected JSON paths.");
                    key.append(raw.charAt(at) == '0' ? '~' : raw.charAt(at) == '1' ? '/' : '*');
                }
                path.add(new Segment(key.toString(), raw.equals("*")));
            }
            parsed.add(List.copyOf(path));
        }
        paths = List.copyOf(parsed);
    }
    public Object transform(Object body) { return transform(body, true, false, "application/json"); }
    public Object transform(Object body, boolean lengthKnown, boolean headersCommitted, String contentType) {
        if (paths.isEmpty()) return new Node(body, 0).result();
        String media = contentType == null ? "" : contentType.split(";", -1)[0].trim().toLowerCase(Locale.ROOT);
        if (!lengthKnown || headersCommitted || !(media.equals("application/json") || media.startsWith("application/") && media.endsWith("+json")))
            throw new ResponseFilterException(headersCommitted);
        try {
            Node copy = new Node(body, 0); Set<Node> selected = Collections.newSetFromMap(new IdentityHashMap<>());
            for (List<Segment> path : paths) select(copy, path, 0, selected);
            for (Node node : selected) {
                if (node.fields != null || node.elements != null || !(node.value instanceof String)) throw new ResponseFilterException(headersCommitted);
                node.value = runtime.redact((String) node.value);
            }
            return copy.result();
        } catch (IllegalArgumentException | RuntimeMatcher.InputLimitException error) { throw new ResponseFilterException(headersCommitted); }
    }
    private static void select(Node node, List<Segment> path, int depth, Set<Node> selected) {
        if (depth == path.size()) { selected.add(node); return; }
        Segment segment = path.get(depth);
        if (segment.wildcard) {
            if (node.elements == null) throw new ResponseFilterException(false);
            for (Node child : node.elements) select(child, path, depth + 1, selected);
        } else if (node.fields != null) {
            Node child = node.fields.get(segment.key); if (child != null) select(child, path, depth + 1, selected);
        } else if (node.elements != null && segment.key.matches("0|[1-9][0-9]*")) {
            int index;
            try { index = Integer.parseInt(segment.key); } catch (NumberFormatException error) { throw new ResponseFilterException(false); }
            if (index >= node.elements.size()) throw new ResponseFilterException(false);
            select(node.elements.get(index), path, depth + 1, selected);
        } else throw new ResponseFilterException(false);
    }
}
