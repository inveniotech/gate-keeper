// SPDX-License-Identifier: Apache-2.0
import africa.gatekeeper.*;
import com.fasterxml.jackson.databind.ObjectMapper;
import java.util.List;
/** Compiles against the public Kotlin hooks without Kotlin source. */
public final class HooksExample {
    public static void use(RuntimeMatcher matcher) {
        new RedactingConverter(); GateKeeperLog4j.install(matcher); GateKeeperRewritePolicy.createPolicy();
        new GateKeeperFilter(matcher, List.of("/value"), new ObjectMapper());
        new GateKeeperResponseAdvice(matcher, List.of("/value"), new ObjectMapper());
    }
}
