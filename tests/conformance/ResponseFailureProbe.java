// SPDX-License-Identifier: Apache-2.0
package africa.gatekeeper;

import java.util.*;

/** Deterministic matcher-failure regression without relying on elapsed deadlines. */
public final class ResponseFailureProbe {
    public static void run() {
        Map<String, Object> body = Map.of("phone", "marker");
        ResponseFilter filter = new ResponseFilter(text -> { throw new RuntimeMatcher.PackLoadException(); }, List.of("/phone"));
        try { filter.transform(body); }
        catch (ResponseFilter.ResponseFilterException error) {
            if (error.abortRequired || error.getCause() != null || !body.get("phone").equals("marker")) throw new IllegalStateException("Unsafe protected failure.");
            return;
        }
        throw new IllegalStateException("Expected a typed protected failure.");
    }
}
