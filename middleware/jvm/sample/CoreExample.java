// SPDX-License-Identifier: Apache-2.0
import africa.gatekeeper.*;
import java.util.*;
/** Plain Java example; release media supply signed bytes and trusted keys. */
public final class CoreExample {
    public static void use(List<SignedPackVerifier.Input> embedded, Map<String, byte[]> keys) {
        RuntimeMatcher matcher = Installation.load(embedded, new SignedPackVerifier(keys, Map.of()));
        matcher.redact("ordinary synthetic text");
        matcher.classify("ordinary synthetic text", "value", null);
        ConsentCheck consent = new ConsentCheck((subject, purpose) -> ConsentCheck.ConsentState.DENIED, (purpose, type) -> { });
        consent.isGranted("synthetic-subject", "acme.marketing-sms");
    }
}
