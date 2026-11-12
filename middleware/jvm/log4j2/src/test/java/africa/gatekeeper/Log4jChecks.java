// SPDX-License-Identifier: Apache-2.0
package africa.gatekeeper;
import org.apache.logging.log4j.core.impl.Log4jLogEvent;
import org.apache.logging.log4j.message.SimpleMessage;
import java.util.*;
/** Synthetic rewrite-policy acceptance checks. */
public final class Log4jChecks {
    public static void main(String[] args) {
        RuntimeMatcher matcher = new RuntimeMatcher(List.of(Map.of("id", "pii.synthetic", "category", "test", "severity", "error", "matcher", Map.of("list", Map.of("package", List.of("synthetic"))))));
        GatekeeperLog4j.install(matcher); GatekeeperRewritePolicy policy = GatekeeperRewritePolicy.createPolicy();
        var event = Log4jLogEvent.newBuilder().setMessage(new SimpleMessage("synthetic")).build();
        var safe = policy.rewrite(event);
        if (safe == event || !safe.getMessage().getFormattedMessage().equals("[REDACTED:pii.synthetic]")) throw new AssertionError("New event must carry a safe message.");
        var large = Log4jLogEvent.newBuilder().setMessage(new SimpleMessage("x".repeat(65537))).build();
        if (!policy.rewrite(large).getMessage().getFormattedMessage().equals(GatekeeperRewritePolicy.FAILURE_MESSAGE)) throw new AssertionError("Oversized input must fail safely.");
    }
}
