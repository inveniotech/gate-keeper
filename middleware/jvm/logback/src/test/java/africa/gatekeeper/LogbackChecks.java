// SPDX-License-Identifier: Apache-2.0
package africa.gatekeeper;
import ch.qos.logback.classic.LoggerContext;
import ch.qos.logback.classic.spi.LoggingEvent;
import java.util.*;
/** Synthetic logging acceptance checks. */
public final class LogbackChecks {
    public static void main(String[] args) {
        RuntimeMatcher matcher = new RuntimeMatcher(List.of(Map.of("id", "pii.synthetic", "category", "test", "severity", "error", "matcher", Map.of("list", Map.of("package", List.of("synthetic"))))));
        LoggerContext context = new LoggerContext(); context.putObject(RedactingConverter.MATCHER_KEY, matcher);
        RedactingConverter converter = new RedactingConverter(); converter.setContext(context);
        LoggingEvent event = new LoggingEvent(); event.setMessage("synthetic");
        if (!converter.convert(event).equals("[REDACTED:pii.synthetic]")) throw new AssertionError("Message must be redacted.");
        LoggingEvent oversized = new LoggingEvent(); oversized.setMessage("x".repeat(65537));
        if (!converter.convert(oversized).equals(RedactingConverter.FAILURE_MESSAGE)) throw new AssertionError("Oversized input must fail safely.");
        RedactingConverter withoutMatcher = new RedactingConverter(); withoutMatcher.setContext(new LoggerContext());
        if (!withoutMatcher.convert(event).equals(RedactingConverter.FAILURE_MESSAGE)) throw new AssertionError("Missing matcher must fail safely.");
    }
}
