// SPDX-License-Identifier: Apache-2.0
package africa.gatekeeper;

import java.io.*;
import java.nio.file.*;
import java.util.*;
import java.util.function.Consumer;

/** Atomic offline installation loading. No key, entitlement, local-pack or network fallback exists. */
public final class Installation {
    private Installation() { }
    /** Loads embedded verified packs without replacements or disabled rules. */
    public static RuntimeMatcher load(List<SignedPackVerifier.Input> embedded, SignedPackVerifier verifier) {
        return load(embedded, verifier, Set.of(), null, null);
    }
    /** Loads embedded packs and an optional same-ID newer replacement directory, then validates disablement. */
    public static RuntimeMatcher load(List<SignedPackVerifier.Input> embedded, SignedPackVerifier verifier,
        Set<String> disabled, Consumer<String> diagnostic, Path replacementDirectory) {
        if (embedded == null || verifier == null) throw PackJson.invalid();
        Map<String, SignedPackVerifier.VerifiedPack> active = new TreeMap<>();
        for (SignedPackVerifier.Input input : embedded) {
            SignedPackVerifier.VerifiedPack pack;
            try { pack = verifier.verify(input); } catch (RuntimeMatcher.PackLoadException error) {
                if (error.requiresUpgrade) throw error;
                if (diagnostic != null) diagnostic.accept("An embedded signed pack failed verification. Reinstall verified release media."); continue;
            }
            if (active.putIfAbsent(pack.id, pack) != null) throw PackJson.invalid();
        }
        if (active.isEmpty()) throw new RuntimeMatcher.PackLoadException("No verified embedded pack is available. Install signed release media before starting.");
        if (replacementDirectory != null) {
            SignedPackVerifier.VerifiedPack replacement = null;
            try {
                replacement = verifier.verify(read(replacementDirectory));
            } catch (RuntimeMatcher.PackLoadException error) {
                if (error.requiresUpgrade) throw error;
                if (diagnostic != null) diagnostic.accept("The replacement pack could not be verified. The verified embedded pack remains active; reinstall the replacement.");
            }
            if (replacement != null) {
                SignedPackVerifier.VerifiedPack previous = active.get(replacement.id);
                if (previous == null || SignedPackVerifier.compare(SignedPackVerifier.version(replacement.version, true), SignedPackVerifier.version(previous.version, true)) <= 0)
                    throw new RuntimeMatcher.PackLoadException("The replacement is not a newer version of an embedded pack. Supply the same pack identifier with a greater calendar version.");
                active.put(replacement.id, replacement);
            }
        }
        List<Map<String, Object>> rules = new ArrayList<>(), metadata = new ArrayList<>();
        for (SignedPackVerifier.VerifiedPack pack : active.values()) { rules.addAll(pack.rules); metadata.add(pack.metadata()); }
        return new RuntimeMatcher(rules, disabled, diagnostic, metadata, true);
    }
    private static byte[] bounded(Path path, int limit) throws IOException {
        try (InputStream stream = Files.newInputStream(path)) {
            byte[] bytes = stream.readNBytes(limit + 1); if (bytes.length > limit) throw PackJson.invalid(); return bytes;
        }
    }
    private static SignedPackVerifier.Input read(Path directory) {
        try {
            Path watermark = directory.resolve("watermark.armour"), signature = directory.resolve("watermark.signature");
            return new SignedPackVerifier.Input(bounded(directory.resolve("pack.json"), PackJson.MAXIMUM_BYTES),
                bounded(directory.resolve("manifest.armour"), 8192), bounded(directory.resolve("manifest.signature"), 64),
                Files.exists(watermark) ? bounded(watermark, 8192) : null, Files.exists(signature) ? bounded(signature, 64) : null);
        } catch (IOException | SecurityException error) { throw PackJson.invalid(); }
    }
}
