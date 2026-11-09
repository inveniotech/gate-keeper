// SPDX-License-Identifier: Apache-2.0
package africa.gatekeeper;

import java.util.concurrent.CancellationException;
import java.util.function.BiConsumer;
import java.util.regex.Pattern;

/** Blocking Java-friendly consent helper. It never stores or caches consent answers. */
public final class ConsentCheck {
    public enum ConsentState { GRANTED, DENIED, UNKNOWN }
    @FunctionalInterface public interface ConsentProvider {
        ConsentState getState(String subjectId, String purpose) throws Exception;
    }
    private static final Pattern PURPOSE = Pattern.compile("[a-z][a-z0-9-]{0,31}(\\.[a-z][a-z0-9-]{0,31}){1,4}");
    private final ConsentProvider provider;
    private final BiConsumer<String, String> diagnostic;

    public ConsentCheck(ConsentProvider provider, BiConsumer<String, String> diagnostic) {
        if (diagnostic == null) throw new IllegalArgumentException("A safe diagnostic callback is required.");
        this.provider = provider; this.diagnostic = diagnostic;
        if (provider == null) diagnostic.accept(null, "NoConsentProvider");
    }
    public ConsentState check(String subjectId, String purpose) {
        if (subjectId == null || subjectId.isEmpty() || subjectId.length() > 2048 || RuntimeMatcher.scalars(subjectId).length > 1024)
            throw new IllegalArgumentException("Supply a subject of 1 to 1,024 Unicode scalars.");
        if (purpose == null || purpose.length() > 164 || !PURPOSE.matcher(purpose).matches())
            throw new IllegalArgumentException("Supply a valid namespaced consent purpose.");
        if (Thread.currentThread().isInterrupted()) throw new CancellationException();
        if (provider == null) return ConsentState.UNKNOWN;
        try {
            ConsentState state = provider.getState(subjectId, purpose);
            if (Thread.currentThread().isInterrupted()) throw new CancellationException();
            return state == null ? ConsentState.UNKNOWN : state;
        } catch (InterruptedException error) { Thread.currentThread().interrupt(); throw new CancellationException(); }
        catch (Exception error) {
            if (Thread.currentThread().isInterrupted()) throw new CancellationException();
            diagnostic.accept(purpose, error.getClass().getSimpleName()); return ConsentState.UNKNOWN;
        }
    }
    public boolean isGranted(String subjectId, String purpose) { return check(subjectId, purpose) == ConsentState.GRANTED; }
}
