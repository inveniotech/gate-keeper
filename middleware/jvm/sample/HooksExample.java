// SPDX-License-Identifier: Apache-2.0
import africa.gatekeeper.*;
import com.fasterxml.jackson.databind.ObjectMapper;
import java.util.List;
/** Compiles against the public Kotlin hooks without Kotlin source. */
public final class HooksExample {
    public static void use(RuntimeMatcher matcher) {
        new RedactingConverter(); GatekeeperLog4j.install(matcher); GatekeeperRewritePolicy.createPolicy();
        new GatekeeperFilter(matcher, List.of("/value"), new ObjectMapper());
        new GatekeeperResponseAdvice(matcher, List.of("/value"), new ObjectMapper());
    }
}
