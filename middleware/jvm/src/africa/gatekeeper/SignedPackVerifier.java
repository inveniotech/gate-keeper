// SPDX-License-Identifier: Apache-2.0
package africa.gatekeeper;

import java.math.BigInteger;
import java.nio.charset.StandardCharsets;
import java.nio.ByteBuffer;
import java.nio.charset.CodingErrorAction;
import java.nio.charset.CharacterCodingException;
import java.security.*;
import java.security.spec.*;
import java.util.*;

/** Verifies exact signed manifest bytes with the JDK Ed25519 provider, without entitlement policy. */
public final class SignedPackVerifier {
    /** Immutable signed container. Watermark halves must both be present or both absent. */
    public static final class Input {
        private final byte[] content, manifest, signature, watermark, watermarkSignature;
        /** Copies untrusted bytes so verification cannot race caller mutation. */
        public Input(byte[] content, byte[] manifest, byte[] signature, byte[] watermark, byte[] watermarkSignature) {
            this.content = copy(content); this.manifest = copy(manifest); this.signature = copy(signature);
            this.watermark = copy(watermark); this.watermarkSignature = copy(watermarkSignature);
        }
        private static byte[] copy(byte[] bytes) { return bytes == null ? null : bytes.clone(); }
    }
    /** Verified metadata and private rule data, available only after all checks succeed. */
    public static final class VerifiedPack {
        final String id, version, digest;
        final List<Map<String, Object>> rules;
        private VerifiedPack(String id, String version, String digest, List<Map<String, Object>> rules) {
            this.id = id; this.version = version; this.digest = digest; this.rules = rules;
        }
        /** Returns pack identity without application inputs. */
        public Map<String, Object> metadata() { return Map.of("id", id, "version", version, "contentDigest", digest); }
    }
    private final Map<String, byte[]> packKeys, watermarkKeys;
    /** Copies separate pack-signing and watermark catalogues. No default production trust root exists. */
    public SignedPackVerifier(Map<String, byte[]> packKeys, Map<String, byte[]> watermarkKeys) {
        this.packKeys = keys(packKeys); this.watermarkKeys = keys(watermarkKeys);
    }
    private static Map<String, byte[]> keys(Map<String, byte[]> input) {
        Map<String, byte[]> copy = new HashMap<>();
        Objects.requireNonNull(input).forEach((id, key) -> copy.put(id, key.clone())); return Map.copyOf(copy);
    }
    /** Checks a raw 32-byte Ed25519 point and 64-byte signature. Invalid material returns false. */
    public static boolean verifySignature(byte[] key, byte[] message, byte[] signature) {
        if (key == null || key.length != 32 || message == null || signature == null || signature.length != 64) return false;
        try {
            byte[] point = key.clone(); boolean odd = (point[31] & 128) != 0; point[31] &= 127;
            byte[] bigEndian = new byte[32]; for (int i = 0; i < 32; i++) bigEndian[i] = point[31 - i];
            PublicKey publicKey = KeyFactory.getInstance("Ed25519").generatePublic(new EdECPublicKeySpec(NamedParameterSpec.ED25519, new EdECPoint(odd, new BigInteger(1, bigEndian))));
            Signature verifier = Signature.getInstance("Ed25519"); verifier.initVerify(publicKey); verifier.update(message); return verifier.verify(signature);
        } catch (GeneralSecurityException | IllegalArgumentException error) { return false; }
    }
    private static String string(Map<String, Object> map, String key) {
        Object value = map.get(key); if (!(value instanceof String) || ((String) value).isEmpty()) throw PackJson.invalid(); return (String) value;
    }
    private static Map<String, Object> object(Object value) {
        if (!(value instanceof Map<?, ?>)) throw PackJson.invalid(); return RuntimeMatcher.map(value);
    }
    private static Map<String, Object> block(byte[] armour, byte[] signature, String kind, Map<String, byte[]> keys) {
        if (armour == null || armour.length > 8192) throw PackJson.invalid();
        String text;
        try { text = StandardCharsets.UTF_8.newDecoder().onMalformedInput(CodingErrorAction.REPORT).decode(ByteBuffer.wrap(armour)).toString().replace("\r\n", "\n"); }
        catch (CharacterCodingException error) { throw PackJson.invalid(); }
        if (text.endsWith("\n")) text = text.substring(0, text.length() - 1);
        String[] lines = text.split("\n", -1);
        for (int i = 0; i < lines.length; i++) if (lines[i].endsWith("\r")) lines[i] = lines[i].substring(0, lines[i].length() - 1);
        if (lines.length != 4 || !lines[0].equals("-----BEGIN GATE-KEEPER RULE PACK " + kind + "-----")
            || !lines[3].equals("-----END GATE-KEEPER RULE PACK " + kind + "-----") || !lines[1].startsWith("Key-Id: ")) throw PackJson.invalid();
        byte[] payload = lines[2].getBytes(StandardCharsets.UTF_8);
        if (payload.length == 0 || payload.length > 4096 || !lines[2].startsWith("{")) throw PackJson.invalid();
        Map<String, Object> value = object(PackJson.parse(payload)); String id = string(value, "keyId");
        if (!lines[1].substring(8).equals(id) || !verifySignature(keys.get(id), payload, signature)) throw PackJson.invalid();
        Set<String> expected = kind.equals("MANIFEST") ? Set.of("keyId", "packId", "version", "contentHashes")
            : Set.of("keyId", "contentDigest", "organisationName", "licenceId", "generatedAt");
        if (!value.keySet().equals(expected)) throw PackJson.invalid(); return value;
    }
    private static String digest(String algorithm, byte[] content) {
        try {
            byte[] digest = MessageDigest.getInstance(algorithm).digest(content); StringBuilder hex = new StringBuilder();
            for (byte b : digest) hex.append(Character.forDigit((b & 255) >>> 4, 16)).append(Character.forDigit(b & 15, 16)); return hex.toString();
        } catch (NoSuchAlgorithmException error) { throw PackJson.invalid(); }
    }
    static int[] version(String value, boolean calendar) {
        if (!value.matches(calendar ? "[0-9]{4}\\.[0-9]{1,2}\\.(0|[1-9][0-9]*)" : "(0|[1-9][0-9]*)\\.(0|[1-9][0-9]*)\\.(0|[1-9][0-9]*)")) throw PackJson.invalid();
        try {
            int[] result = Arrays.stream(value.split("\\.")).mapToInt(Integer::parseInt).toArray();
            if (calendar && (result[0] < 1 || result[1] < 1 || result[1] > 12)) throw PackJson.invalid(); return result;
        } catch (NumberFormatException error) { throw PackJson.invalid(); }
    }
    static int compare(int[] left, int[] right) { for (int i = 0; i < 3; i++) if (left[i] != right[i]) return Integer.compare(left[i], right[i]); return 0; }
    /** Verifies signature, digests, supported content and optional watermark before returning rules. */
    public VerifiedPack verify(Input input) {
        if (input == null || input.content == null || input.content.length > PackJson.MAXIMUM_BYTES) throw PackJson.invalid();
        Map<String, Object> manifest = block(input.manifest, input.signature, "MANIFEST", packKeys);
        Map<String, Object> hashes = object(manifest.get("contentHashes"));
        if (!hashes.keySet().equals(Set.of("sha256", "sha512"))) throw PackJson.invalid();
        String sha256 = digest("SHA-256", input.content);
        if (!sha256.equals(hashes.get("sha256")) || !digest("SHA-512", input.content).equals(hashes.get("sha512"))) throw PackJson.invalid();
        Map<String, Object> content = object(PackJson.parse(input.content));
        if (!Integer.valueOf(1).equals(content.get("schemaVersion")))
            throw new RuntimeMatcher.PackLoadException("The signed pack schema is unsupported. Upgrade the adapter before loading this pack.", true);
        if (!Set.of("bundled", "plan-gated").contains(string(content, "provenance"))) throw PackJson.invalid();
        String id = string(content, "id"), version = string(content, "version"); version(version, true);
        if (!id.equals(manifest.get("packId")) || !version.equals(manifest.get("version"))) throw PackJson.invalid();
        if (compare(version(string(content, "minEngineVersion"), false), new int[]{0, 2, 0}) > 0)
            throw new RuntimeMatcher.PackLoadException("The signed pack needs a newer engine. Upgrade the adapter before loading this pack.", true);
        if (!(content.get("rules") instanceof List<?>)) throw PackJson.invalid();
        List<Map<String, Object>> rules = new ArrayList<>();
        for (Object item : (List<?>) content.get("rules")) {
            Map<String, Object> rule = object(item); if (string(rule, "id").startsWith("local.")) throw PackJson.invalid(); rules.add(rule);
        }
        new RuntimeMatcher(rules);
        if ((input.watermark == null) != (input.watermarkSignature == null)) throw PackJson.invalid();
        if (input.watermark != null) {
            Map<String, Object> watermark = block(input.watermark, input.watermarkSignature, "WATERMARK", watermarkKeys);
            if (!sha256.equals(watermark.get("contentDigest"))) throw PackJson.invalid();
            for (String key : List.of("organisationName", "licenceId", "generatedAt")) string(watermark, key);
        }
        return new VerifiedPack(id, version, sha256, rules);
    }
}
