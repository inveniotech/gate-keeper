// SPDX-License-Identifier: Apache-2.0
package africa.gatekeeper;

import java.nio.*;
import java.nio.charset.*;
import java.util.*;

/** Strict, bounded JSON reader for signed pack documents; no input enters errors. */
public final class PackJson {
    /** Maximum encoded document size. */
    public static final int MAXIMUM_BYTES = 16 * 1024 * 1024;
    private final String text;
    private int position;
    private PackJson(String text) { this.text = text; }
    static RuntimeMatcher.PackLoadException invalid() {
        return new RuntimeMatcher.PackLoadException("The signed pack is invalid or incompatible. Reinstall a compatible verified pack from release media.");
    }
    /** Parses UTF-8, rejecting duplicate keys, non-integers and more than 32 nested containers. */
    public static Object parse(byte[] bytes) {
        if (bytes == null || bytes.length > MAXIMUM_BYTES) throw invalid();
        try {
            String text = StandardCharsets.UTF_8.newDecoder().onMalformedInput(CodingErrorAction.REPORT)
                .onUnmappableCharacter(CodingErrorAction.REPORT).decode(ByteBuffer.wrap(bytes)).toString();
            PackJson reader = new PackJson(text); Object result = reader.read(0); reader.space();
            if (reader.position != text.length()) throw invalid();
            return result;
        } catch (CharacterCodingException | IndexOutOfBoundsException | NumberFormatException error) { throw invalid(); }
    }
    private void space() { while (position < text.length() && " \r\n\t".indexOf(text.charAt(position)) >= 0) position++; }
    private char take() { if (position == text.length()) throw invalid(); return text.charAt(position++); }
    private boolean consume(char c) { space(); if (position < text.length() && text.charAt(position) == c) { position++; return true; } return false; }
    private Object read(int depth) {
        space(); if (position == text.length()) throw invalid(); char c = text.charAt(position);
        if (c == '"') return string();
        if (c == '{' || c == '[') {
            if (depth >= 32) throw invalid(); position++;
            if (c == '{') {
                Map<String, Object> fields = new LinkedHashMap<>(); if (consume('}')) return fields;
                do { space(); String key = string(); if (!consume(':') || fields.containsKey(key)) throw invalid(); fields.put(key, read(depth + 1)); } while (consume(','));
                if (!consume('}')) throw invalid(); return fields;
            }
            List<Object> values = new ArrayList<>(); if (consume(']')) return values;
            do { values.add(read(depth + 1)); } while (consume(','));
            if (!consume(']')) throw invalid(); return values;
        }
        for (String literal : List.of("true", "false", "null")) if (text.startsWith(literal, position)) {
            position += literal.length(); return literal.equals("null") ? null : Boolean.valueOf(literal);
        }
        int start = position; if (c == '-') position++;
        int digits = position;
        while (position < text.length() && text.charAt(position) >= '0' && text.charAt(position) <= '9') position++;
        if (digits == position || position - digits > 1 && text.charAt(digits) == '0') throw invalid();
        long value = Long.parseLong(text.substring(start, position));
        if (value >= Integer.MIN_VALUE && value <= Integer.MAX_VALUE) return Integer.valueOf((int) value);
        return Long.valueOf(value);
    }
    private String string() {
        if (take() != '"') throw invalid(); StringBuilder result = new StringBuilder();
        while (true) {
            char c = take(); if (c == '"') break; if (c < 32) throw invalid();
            if (c == '\\') {
                char escaped = take();
                switch (escaped) {
                    case '"': c = '"'; break; case '\\': c = '\\'; break; case '/': c = '/'; break;
                    case 'b': c = '\b'; break; case 'f': c = '\f'; break; case 'n': c = '\n'; break;
                    case 'r': c = '\r'; break; case 't': c = '\t'; break;
                    case 'u':
                        if (position + 4 > text.length() || !text.substring(position, position + 4).matches("[0-9a-fA-F]{4}")) throw invalid();
                        c = (char) Integer.parseInt(text.substring(position, position + 4), 16); position += 4; break;
                    default: throw invalid();
                }
            }
            result.append(c);
        }
        for (int i = 0; i < result.length(); i++) {
            char c = result.charAt(i);
            if (Character.isHighSurrogate(c)) { if (++i == result.length() || !Character.isLowSurrogate(result.charAt(i))) throw invalid(); }
            else if (Character.isLowSurrogate(c)) throw invalid();
        }
        return result.toString();
    }
}
