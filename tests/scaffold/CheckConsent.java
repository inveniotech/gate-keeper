// SPDX-License-Identifier: Apache-2.0
import consent.ConsentProvider;
import africa.gatekeeper.ConsentCheck.ConsentState;
import java.time.Clock;
import java.time.Instant;
import java.time.ZoneOffset;
import java.util.List;

/** Acceptance probe for the generated Spring Boot provider. */
public final class CheckConsent {
    private static void require(boolean result) { if (!result) throw new AssertionError("Consent scaffold acceptance failed."); }

    /** Runs against the real adapter and the unmodified template. */
    public static void main(String[] args) {
        Instant time = Instant.parse("2026-11-14T00:00:00Z");
        ConsentProvider provider = new ConsentProvider(Clock.fixed(time, ZoneOffset.UTC));
        require(provider.getState("subject-1", "marketing.email") == ConsentState.DENIED);
        provider.record("subject-1", "marketing.email", true, "settings", "v1");
        var first = provider.history();
        require(provider.getState("subject-1", "marketing.email") == ConsentState.GRANTED);
        require(provider.getState("subject-2", "marketing.email") == ConsentState.DENIED);
        require(provider.getState("subject-1", "marketing.sms") == ConsentState.DENIED);
        provider.record("subject-1", "marketing.email", false, "settings", "v1");
        require(provider.getState("subject-1", "marketing.email") == ConsentState.DENIED);
        provider.record("subject-1", "marketing.email", true, "settings", "v2");
        require(provider.getState("subject-1", "marketing.email") == ConsentState.GRANTED);
        require(first.size() == 1);
        require(provider.history().stream().map(ConsentProvider.ConsentEvent::kind).toList().equals(List.of("granted", "withdrawn", "granted")));
        require(provider.history().get(2).equals(new ConsentProvider.ConsentEvent("subject-1", "marketing.email", "granted", time, "settings", "v2")));
        try { provider.record("subject-1", "invalid", true, "settings", "v1"); throw new AssertionError("Invalid purpose accepted."); }
        catch (IllegalArgumentException expected) { require(provider.history().size() == 3); }
        System.out.println("Consent scaffold acceptance passed.");
    }
}
