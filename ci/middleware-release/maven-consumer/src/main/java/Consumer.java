// SPDX-License-Identifier: Apache-2.0
import africa.gatekeeper.ConsentCheck;

/** Executes the published core without repository classes on its classpath. */
public final class Consumer {
    private Consumer() { }
    public static void main(String[] arguments) {
        ConsentCheck check = new ConsentCheck((subject, purpose) -> ConsentCheck.ConsentState.DENIED, (purpose, type) -> { });
        if (check.isGranted("synthetic-subject", "example.purpose")) {
            throw new AssertionError("The packaged helper must preserve a denied answer.");
        }
        System.out.println("Published Maven dependency graph resolves and executes.");
    }
}
