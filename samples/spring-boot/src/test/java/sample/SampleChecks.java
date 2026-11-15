// SPDX-License-Identifier: Apache-2.0
package sample;

import africa.gatekeeper.GateKeeperResponseAdvice;
import com.fasterxml.jackson.databind.ObjectMapper;
import java.io.StringWriter;
import java.nio.file.Path;
import org.springframework.test.web.servlet.setup.MockMvcBuilders;
import static org.springframework.test.web.servlet.request.MockMvcRequestBuilders.get;

/** Runs a real Boot context, Spring MVC controller, Logback hook and response advice without a listening socket. */
public final class SampleChecks {
    private static void require(boolean result) { if (!result) throw new AssertionError("Spring Boot sample acceptance failed."); }

    /** Exercises both consent answers and confirms that neither logging nor the response exposes the synthetic value. */
    public static void main(String[] args) throws Exception {
        StringWriter logs = new StringWriter();
        var runtime = SampleApplication.installation(Path.of("artifacts/sample-media"), Path.of("artifacts/sample-media/public-keys.json"));
        try (var context = SampleApplication.start(runtime, logs)) {
            var mvc = MockMvcBuilders.standaloneSetup(context.getBean(SampleApplication.CustomerController.class))
                .setControllerAdvice(context.getBean(GateKeeperResponseAdvice.class)).build();
            for (String subject : new String[] { "synthetic-subject", "other-subject" }) {
                var response = mvc.perform(get("/customer").param("subject", subject)).andReturn().getResponse();
                require(response.getStatus() == 200);
                var body = new ObjectMapper().readTree(response.getContentAsString());
                require(body.path("customer").path("bvn").asText().equals("bankVerificationNumber [REDACTED:pii.bvn]"));
                require(body.path("granted").asBoolean() == subject.equals("synthetic-subject"));
            }
            require(logs.toString().lines().count() == 2);
            require(logs.toString().lines().allMatch(line -> line.contains("[REDACTED:pii.bvn]")));
            require(!logs.toString().contains("32826314843") && !logs.toString().contains("synthetic-subject"));
        }
        System.out.println("Spring Boot sample acceptance passed.");
    }
}
