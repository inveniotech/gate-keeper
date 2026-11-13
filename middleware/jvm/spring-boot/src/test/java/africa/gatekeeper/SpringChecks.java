// SPDX-License-Identifier: Apache-2.0
package africa.gatekeeper;
import com.fasterxml.jackson.databind.ObjectMapper;
import org.springframework.mock.web.*;
import org.springframework.http.*;
import org.springframework.http.server.*;
import org.springframework.http.converter.json.MappingJackson2HttpMessageConverter;
import org.springframework.context.annotation.AnnotationConfigApplicationContext;
import org.springframework.core.MethodParameter;
import org.springframework.core.env.MapPropertySource;
import java.nio.file.*;
import java.util.*;
/** Synthetic bounded-response checks using servlet fakes. */
public final class SpringChecks {
    private static void check(boolean value) { if (!value) throw new AssertionError("Protected response contract differs."); }
    private static void root(RuntimeMatcher matcher, String pointer, String json) throws Exception {
        GateKeeperFilter filter = new GateKeeperFilter(matcher, List.of(pointer), new ObjectMapper());
        MockHttpServletResponse response = new MockHttpServletResponse();
        byte[] original = json.getBytes(java.nio.charset.StandardCharsets.UTF_8);
        filter.doFilter(new MockHttpServletRequest(), response, (req, res) -> {
            res.setContentType("application/json"); res.setContentLength(original.length); res.getOutputStream().write(original);
        });
        check(response.getContentAsString().contains("[REDACTED:pii.synthetic]"));
        if (json.contains("é")) check(response.getContentAsString().contains("é"));
    }
    private static MethodParameter returnType() throws NoSuchMethodException {
        return new MethodParameter(SpringChecks.class.getDeclaredMethod("main", String[].class), -1);
    }
    private static byte[] hex(String value) {
        byte[] bytes = new byte[value.length() / 2];
        for (int i = 0; i < bytes.length; i++) bytes[i] = (byte) Integer.parseInt(value.substring(i * 2, i * 2 + 2), 16);
        return bytes;
    }
    private static RuntimeMatcher verified() throws Exception {
        Path root = Path.of("tests/vectors/pack");
        SignedPackVerifier.Input input = new SignedPackVerifier.Input(Files.readAllBytes(root.resolve("pack.pack.json")),
            Files.readAllBytes(root.resolve("pack.manifest.armour")), hex(Files.readString(root.resolve("pack.signature.hex")).trim()), null, null);
        return Installation.load(List.of(input), new SignedPackVerifier(Map.of("gkpk-dev-1", hex("cb424a36671ebd54bc348ca3fddb74833f0e980379bb289e0a4b8efb957b65a7")), Map.of()));
    }
    private static void configuration(Map<String, Object> properties, RuntimeMatcher matcher, boolean expectedBeans, boolean expectedFailure) {
        boolean failed = false;
        try (AnnotationConfigApplicationContext context = new AnnotationConfigApplicationContext()) {
            context.getEnvironment().getPropertySources().addFirst(new MapPropertySource("synthetic", properties));
            if (matcher != null) context.registerBean(RuntimeMatcher.class, () -> matcher);
            context.register(GateKeeperAutoConfiguration.class);
            try { context.refresh(); } catch (RuntimeException expected) { failed = true; }
            if (!failed) {
                check((context.getBeanNamesForType(GateKeeperFilter.class).length == 1) == expectedBeans);
                check((context.getBeanNamesForType(GateKeeperResponseAdvice.class).length == 1) == expectedBeans);
            }
        }
        check(failed == expectedFailure);
    }
    public static void main(String[] args) throws Exception {
        RuntimeMatcher matcher = new RuntimeMatcher(List.of(Map.of("id", "pii.synthetic", "category", "test", "severity", "error", "matcher", Map.of("list", Map.of("package", List.of("synthetic"))))));
        root(matcher, "/*/value", "[{\"value\":\"synthetic\"}]");
        root(matcher, "", "\"synthetic\"");
        root(matcher, "/value", "{\"value\":\"synthetic\",\"label\":\"é\"}");
        RuntimeMatcher verified = verified();
        configuration(Map.of(), null, false, false);
        configuration(Map.of("gatekeeper.paths", "/value"), verified, true, false);
        configuration(Map.of("gatekeeper.paths[0]", "/value"), verified, true, false);
        configuration(Map.of("gatekeeper.paths[0]", "/value"), null, false, true);
        configuration(Map.of("gatekeeper.paths", "/value"), matcher, false, true);
        GateKeeperFilter filter = new GateKeeperFilter(matcher, List.of("/value"), new ObjectMapper());
        for (String scenario : List.of("object", "null", "non-json", "unknown", "large", "committed")) {
            MockHttpServletRequest request = new MockHttpServletRequest(); MockHttpServletResponse response = new MockHttpServletResponse();
            if (scenario.equals("committed")) response.setCommitted(true);
            byte[] original = (scenario.equals("null") ? "{\"value\":null}" : "{\"value\":\"synthetic\"}").getBytes(java.nio.charset.StandardCharsets.UTF_8);
            boolean failed = false;
            try {
                filter.doFilter(request, response, (req, res) -> {
                    res.setContentType(scenario.equals("non-json") ? "text/plain" : "application/json");
                    if (!scenario.equals("unknown")) res.setContentLength(scenario.equals("large") ? 1048577 : original.length);
                    res.getOutputStream().write(original);
                });
            } catch (ResponseFilter.ResponseFilterException expected) { failed = true; }
            if (scenario.equals("object")) check(response.getContentAsString().contains("[REDACTED:pii.synthetic]"));
            else { check(failed); check(response.getContentAsByteArray().length == 0); }
        }
        GateKeeperResponseAdvice advice = new GateKeeperResponseAdvice(matcher, List.of("/value"), new ObjectMapper());
        MockHttpServletRequest request = new MockHttpServletRequest(); MockHttpServletResponse response = new MockHttpServletResponse();
        Object result = advice.beforeBodyWrite(Map.of("value", "synthetic"), returnType(), MediaType.APPLICATION_JSON,
            MappingJackson2HttpMessageConverter.class, new ServletServerHttpRequest(request), new ServletServerHttpResponse(response));
        check(((Map<?, ?>) result).get("value").equals("[REDACTED:pii.synthetic]"));
        check(Boolean.TRUE.equals(request.getAttribute(GateKeeperResponseAdvice.FILTERED_ATTRIBUTE)));
        Map<String, Object> nullable = new HashMap<>(); nullable.put("value", null);
        try { advice.beforeBodyWrite(nullable, returnType(), MediaType.APPLICATION_JSON, MappingJackson2HttpMessageConverter.class,
            new ServletServerHttpRequest(new MockHttpServletRequest()), new ServletServerHttpResponse(new MockHttpServletResponse())); throw new AssertionError("Null leaf must fail."); }
        catch (ResponseFilter.ResponseFilterException expected) { }
    }
}
