// SPDX-License-Identifier: Apache-2.0
import africa.gatekeeper.*;
import java.nio.file.*;
import java.util.*;

/** Signed-loading acceptance checks using development-only vectors. */
public final class CheckPackLoad {
    private static final Path ROOT = Path.of("tests/vectors/pack");
    private static byte[] bytes(String name) throws Exception { return Files.readAllBytes(ROOT.resolve(name)); }
    private static byte[] hex(String text) {
        byte[] out = new byte[text.length() / 2];
        for (int i = 0; i < out.length; i++) out[i] = (byte) Integer.parseInt(text.substring(i * 2, i * 2 + 2), 16);
        return out;
    }
    private static SignedPackVerifier.Input input(String name) throws Exception {
        String content = Files.exists(ROOT.resolve(name + ".pack.json")) ? name : "pack";
        return new SignedPackVerifier.Input(bytes(content + ".pack.json"), bytes(name + ".manifest.armour"),
            hex(Files.readString(ROOT.resolve(name + ".signature.hex")).trim()), null, null);
    }
    private static void fails(Runnable action) {
        try { action.run(); } catch (RuntimeMatcher.PackLoadException expected) { return; }
        throw new AssertionError("Expected a content-free pack-load failure.");
    }
    private static void check(boolean condition, String description) { if (!condition) throw new AssertionError(description); }
    private static SignedPackVerifier.Input watermark(String name) throws Exception {
        String armour = name.equals("watermark-tampered-signature") ? "watermark" : name;
        return new SignedPackVerifier.Input(bytes("pack.pack.json"), bytes("pack.manifest.armour"), hex(Files.readString(ROOT.resolve("pack.signature.hex")).trim()),
            bytes(armour + ".manifest.armour"), hex(Files.readString(ROOT.resolve(name + ".signature.hex")).trim()));
    }
    private static void replacement(Path directory, String name) throws Exception {
        Files.write(directory.resolve("pack.json"), bytes(name + ".pack.json"));
        Files.write(directory.resolve("manifest.armour"), bytes(name + ".manifest.armour"));
        Files.write(directory.resolve("manifest.signature"), hex(Files.readString(ROOT.resolve(name + ".signature.hex")).trim()));
    }
    private static void rfc8032() {
        String[][] vectors = {
            {"d75a980182b10ab7d54bfed3c964073a0ee172f3daa62325af021a68f707511a", "", "e5564300c360ac729086e2cc806e828a84877f1eb8e5d974d873e065224901555fb8821590a33bacc61e39701cf9b46bd25bf5f0595bbe24655141438e7a100b"},
            {"3d4017c3e843895a92b70aa74d1b7ebc9c982ccf2ec4968cc0cd55f12af4660c", "72", "92a009a9f0d4cab8720e820b5f642540a2b27b5416503f8fb3762223ebdb69da085ac1e43e15996e458f3613d0f11d8c387b2eaeb4302aeeb00d291612bb0c00"},
            {"fc51cd8e6218a1a38da47ed00230f0580816ed13ba3303ac5deb911548908025", "af82", "6291d657deec24024827e69c3abe01a30ce548a284743a445e3680d7db5ac3ac18ff9b538d16f290ae67f760984dc6594a7c15e9716ed28dc027beceea1ec40a"}
        };
        for (String[] vector : vectors) {
            byte[] key = hex(vector[0]), message = hex(vector[1]), signature = hex(vector[2]);
            check(SignedPackVerifier.verifySignature(key, message, signature), "RFC 8032 signature must verify.");
            signature[0] ^= 1;
            check(!SignedPackVerifier.verifySignature(key, message, signature), "Tampered RFC signature must fail.");
        }
        check(!SignedPackVerifier.verifySignature(new byte[31], new byte[0], new byte[64]), "Wrong key length must fail.");
        check(!SignedPackVerifier.verifySignature(new byte[32], new byte[0], new byte[63]), "Wrong signature length must fail.");
    }
    private static void json() {
        for (String text : List.of("{\"a\":1,\"a\":2}", "{} trailing", "1.5", "1e3", "01", "-", "\"\\ud800\"", "\"\\udc00\"", "[".repeat(33) + "0" + "]".repeat(33)))
            fails(() -> PackJson.parse(text.getBytes(java.nio.charset.StandardCharsets.UTF_8)));
        fails(() -> PackJson.parse(new byte[]{(byte) 0xc0, (byte) 0x80}));
        fails(() -> PackJson.parse(new byte[PackJson.MAXIMUM_BYTES + 1]));
        check(PackJson.parse("\"\\ud83d\\ude00\"".getBytes(java.nio.charset.StandardCharsets.UTF_8)).equals("\ud83d\ude00"), "Paired surrogates must survive.");
        PackJson.parse(("[".repeat(32) + "0" + "]".repeat(32)).getBytes(java.nio.charset.StandardCharsets.UTF_8));
    }
    /** Runs signed-loading checks from the repository root. */
    public static void main(String[] args) throws Exception {
        rfc8032(); json();
        Map<String, byte[]> packs = Map.of("gkpk-dev-1", hex("cb424a36671ebd54bc348ca3fddb74833f0e980379bb289e0a4b8efb957b65a7"));
        Map<String, byte[]> watermarks = Map.of("gkwm-dev-1", hex("c0cc459c75ca82f5f3791dbd4eef361d3b37d412506e714063b33678adbda537"));
        SignedPackVerifier verifier = new SignedPackVerifier(packs, watermarks);
        verifier.verify(watermark("watermark"));
        for (String name : List.of("watermark-digest-mismatch", "watermark-tampered-signature")) {
            SignedPackVerifier.Input candidate = watermark(name); fails(() -> verifier.verify(candidate));
        }
        for (String name : List.of("pack", "next-line", "plan-gated")) verifier.verify(input(name));
        for (String name : List.of("tampered-content", "tampered-signature", "unknown-key", "key-id-mismatch", "schema-major", "newer-engine", "local-rule-in-bundled")) {
            SignedPackVerifier.Input candidate = input(name); fails(() -> verifier.verify(candidate));
        }
        fails(() -> Installation.load(List.of(), verifier));
        Installation.load(List.of(input("pack")), verifier);
        fails(() -> Installation.load(List.of(new SignedPackVerifier.Input(new byte[0], new byte[0], new byte[0], null, null)), verifier));
        SignedPackVerifier.Input good = input("pack"), bad = input("tampered-signature"), next = input("next-line");
        check(Installation.load(List.of(good), verifier).isVerifiedInstallation(), "Only a verified installation may activate auto-configuration.");
        check(Installation.load(List.of(bad, good), verifier).versions().get("packs").equals(List.of(verifier.verify(good).metadata())), "Invalid embedded input cannot replace a verified pack.");
        SignedPackVerifier.Input half = new SignedPackVerifier.Input(bytes("pack.pack.json"), bytes("pack.manifest.armour"), hex(Files.readString(ROOT.resolve("pack.signature.hex")).trim()), bytes("watermark.manifest.armour"), null);
        fails(() -> verifier.verify(half));
        Path directory = Files.createTempDirectory("gate-keeper-jvm-vectors-");
        try {
            replacement(directory, "next-line");
            check(Installation.load(List.of(good), verifier, Set.of(), null, directory).versions().get("packs").equals(List.of(verifier.verify(next).metadata())), "A newer same-ID replacement must become active.");
            replacement(directory, "pack");
            fails(() -> Installation.load(List.of(good), verifier, Set.of(), null, directory));
            fails(() -> Installation.load(List.of(next), verifier, Set.of(), null, directory));
            replacement(directory, "rival");
            fails(() -> Installation.load(List.of(good), verifier, Set.of(), null, directory));
            for (String name : List.of("schema-major", "newer-engine")) {
                replacement(directory, name); fails(() -> Installation.load(List.of(good), verifier, Set.of(), null, directory));
            }
            List<String> diagnostics = new ArrayList<>();
            for (String name : List.of("tampered-signature", "tampered-content")) {
                replacement(directory, name);
                check(Installation.load(List.of(good), verifier, Set.of(), diagnostics::add, directory).versions().get("packs").equals(List.of(verifier.verify(good).metadata())), "Bad replacement must retain embedded pack.");
            }
            check(diagnostics.size() == 2, "Rejected replacements must report safe diagnostics.");
            replacement(directory, "next-line"); Files.write(directory.resolve("watermark.armour"), bytes("watermark.manifest.armour"));
            check(Installation.load(List.of(good), verifier, Set.of(), null, directory).versions().get("packs").equals(List.of(verifier.verify(good).metadata())), "Half watermark must reject only the replacement.");
        } finally {
            for (String name : List.of("pack.json", "manifest.armour", "manifest.signature", "watermark.armour", "watermark.signature")) Files.deleteIfExists(directory.resolve(name));
            Files.delete(directory);
        }
        System.out.println("JVM signed pack loading checks passed.");
    }
}
