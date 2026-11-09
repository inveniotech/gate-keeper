// SPDX-License-Identifier: Apache-2.0
// Test-fixture reader only; no JSON dependency enters the JVM adapter.
import java.util.*;

final class VectorJson {
    private final String text;
    private int at;
    private VectorJson(String text) { this.text = text; }
    static Object parse(String text) {
        VectorJson reader = new VectorJson(text); Object value = reader.value(0); reader.space();
        if (reader.at != text.length()) throw invalid(); return value;
    }
    private static IllegalArgumentException invalid() { return new IllegalArgumentException("Invalid conformance fixture JSON."); }
    private void space() { while (at < text.length() && Character.isWhitespace(text.charAt(at))) at++; }
    private char take() { if (at >= text.length()) throw invalid(); return text.charAt(at++); }
    private Object value(int depth) {
        if (depth > 64) throw invalid(); space(); if (at == text.length()) throw invalid(); char c = text.charAt(at);
        if (c == '"') return string();
        if (c == '{') {
            at++; Map<String, Object> result = new LinkedHashMap<>(); space(); if (text.charAt(at) == '}') { at++; return result; }
            while (true) {
                space(); String key = string(); space(); if (take() != ':' || result.containsKey(key)) throw invalid();
                result.put(key, value(depth + 1)); space(); char separator = take();
                if (separator == '}') return result; if (separator != ',') throw invalid();
            }
        }
        if (c == '[') {
            at++; List<Object> result = new ArrayList<>(); space(); if (text.charAt(at) == ']') { at++; return result; }
            while (true) { result.add(value(depth + 1)); space(); char separator = take(); if (separator == ']') return result; if (separator != ',') throw invalid(); }
        }
        for (String word : List.of("true", "false", "null")) if (text.startsWith(word, at)) {
            at += word.length(); return word.equals("null") ? null : Boolean.valueOf(word);
        }
        int start = at; if (c == '-') at++; while (at < text.length() && text.charAt(at) >= '0' && text.charAt(at) <= '9') at++;
        if (at == start) throw invalid(); long number = Long.parseLong(text.substring(start, at));
        if (number >= Integer.MIN_VALUE && number <= Integer.MAX_VALUE) return Integer.valueOf((int) number);
        return Long.valueOf(number);
    }
    private String string() {
        if (take() != '"') throw invalid(); StringBuilder value = new StringBuilder();
        while (true) {
            char c = take(); if (c == '"') return value.toString(); if (c < 32) throw invalid();
            if (c != '\\') { value.append(c); continue; }
            char escape = take();
            switch (escape) {
                case '"': value.append('"'); break; case '\\': value.append('\\'); break; case '/': value.append('/'); break;
                case 'b': value.append('\b'); break; case 'f': value.append('\f'); break; case 'n': value.append('\n'); break;
                case 'r': value.append('\r'); break; case 't': value.append('\t'); break;
                case 'u': if (at + 4 > text.length()) throw invalid(); value.append((char) Integer.parseInt(text.substring(at, at + 4), 16)); at += 4; break;
                default: throw invalid();
            }
        }
    }
}
