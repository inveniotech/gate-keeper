// SPDX-License-Identifier: Apache-2.0
package sample;

import africa.gatekeeper.*;
import ch.qos.logback.classic.LoggerContext;
import ch.qos.logback.classic.spi.ILoggingEvent;
import ch.qos.logback.classic.util.LogbackMDCAdapter;
import ch.qos.logback.core.AppenderBase;
import com.fasterxml.jackson.databind.ObjectMapper;
import java.io.Writer;
import java.io.IOException;
import java.nio.file.Files;
import java.nio.file.Path;
import java.util.*;
import org.springframework.boot.*;
import org.springframework.boot.autoconfigure.EnableAutoConfiguration;
import org.springframework.context.ConfigurableApplicationContext;
import org.springframework.context.annotation.Bean;
import org.springframework.context.support.GenericApplicationContext;
import org.springframework.web.bind.annotation.*;

/** Minimal Boot application with explicitly verified media and application-owned consent. */
@SpringBootConfiguration(proxyBeanMethods = false)
@EnableAutoConfiguration
public class SampleApplication {
    private static final String SYNTHETIC_VALUE = "bankVerificationNumber 32826314843";

    /** Starts a real application context without opening a listening socket. */
    public static ConfigurableApplicationContext start(RuntimeMatcher runtime, Writer output) {
        SpringApplication app = new SpringApplication(SampleApplication.class);
        app.setWebApplicationType(WebApplicationType.NONE);
        app.setBannerMode(Banner.Mode.OFF);
        app.setLogStartupInfo(false);
        app.setDefaultProperties(Map.of("logging.level.root", "OFF", "gatekeeper.paths", "/customer/bvn"));
        app.addInitializers(context -> {
            var beans = (GenericApplicationContext) context;
            beans.registerBean(RuntimeMatcher.class, () -> runtime);
            beans.registerBean(Writer.class, () -> output);
        });
        return app.run();
    }

    /** Supplies a fixed synthetic provider, not a storage or authorisation implementation. */
    @Bean public ConsentCheck consent() {
        return new ConsentCheck((subject, purpose) -> subject.equals("synthetic-subject") && purpose.equals("acme.marketing-sms")
            ? ConsentCheck.ConsentState.GRANTED : ConsentCheck.ConsentState.DENIED, (purpose, type) -> { });
    }

    /** Installs the verified matcher before any sample log event can be rendered. */
    @Bean(destroyMethod = "stop") public LoggerContext sampleLogging(RuntimeMatcher runtime, Writer output) {
        LoggerContext context = new LoggerContext();
        context.setMDCAdapter(new LogbackMDCAdapter());
        context.putObject(RedactingConverter.MATCHER_KEY, runtime);
        RedactingConverter converter = new RedactingConverter(); converter.setContext(context); converter.start();
        AppenderBase<ILoggingEvent> appender = new AppenderBase<>() {
            @Override protected void append(ILoggingEvent event) {
                try { output.write(converter.convert(event) + "\n"); output.flush(); }
                catch (IOException failure) { throw new IllegalStateException("The sample log could not be written. Check the output destination.", failure); }
            }
        };
        appender.setContext(context); appender.start();
        context.getLogger("sample").addAppender(appender);
        return context;
    }

    /** Supplies the redacting logger to the controller through normal constructor injection. */
    @Bean public ch.qos.logback.classic.Logger sampleLogger(LoggerContext sampleLogging) {
        return sampleLogging.getLogger("sample");
    }

    /** A synthetic customer route; consent is reported separately and never disables redaction. */
    @RestController public static final class CustomerController {
        private final ConsentCheck consent;
        private final ch.qos.logback.classic.Logger logger;
        CustomerController(ConsentCheck consent, ch.qos.logback.classic.Logger logger) { this.consent = consent; this.logger = logger; }
        /** Returns JSON through the Gate-Keeper advice before any original body is committed. */
        @GetMapping(value = "/customer", produces = "application/json")
        public Map<String, Object> customer(@RequestParam(value = "subject", defaultValue = "synthetic-subject") String subject) {
            boolean granted = consent.isGranted(subject, "acme.marketing-sms");
            logger.info("Customer record {}", SYNTHETIC_VALUE);
            return Map.of("customer", Map.of("bvn", SYNTHETIC_VALUE), "granted", granted);
        }
    }

    /** Loads only explicitly supplied signed media and public keys; no development trust is embedded. */
    public static RuntimeMatcher installation(Path directory, Path keyFile) throws IOException {
        var catalogue = new ObjectMapper().readTree(Files.readAllBytes(keyFile));
        Map<String, byte[]> keys = new TreeMap<>();
        for (var key : catalogue.path("keys")) {
            if (key.has("seedHex")) throw new IllegalArgumentException("Supply a public-only catalogue without private key material.");
            if (key.path("purpose").asText().equals("pack-signing")) keys.put(key.path("keyId").asText(), HexFormat.of().parseHex(key.path("publicKeyHex").asText()));
        }
        var input = new SignedPackVerifier.Input(Files.readAllBytes(directory.resolve("pack.pack.json")),
            Files.readAllBytes(directory.resolve("pack.manifest.armour")), HexFormat.of().parseHex(Files.readString(directory.resolve("pack.signature.hex")).trim()), null, null);
        return Installation.load(List.of(input), new SignedPackVerifier(keys, Map.of()));
    }

    /** Starts with a signed pack directory and public-only catalogue; request acceptance is exercised by verifySample. */
    public static void main(String[] args) {
        try {
            if (args.length != 2) throw new IllegalArgumentException();
            try (var context = start(installation(Path.of(args[0]), Path.of(args[1])), new java.io.OutputStreamWriter(System.out, java.nio.charset.StandardCharsets.UTF_8))) {
                context.getBean(CustomerController.class).customer("synthetic-subject");
                System.out.println("Spring Boot started with verified packs. Run verifySample to exercise the filtered HTTP response.");
            }
        } catch (Exception failure) {
            System.err.println("The Spring Boot sample could not start. Supply a signed pack directory and public-only key catalogue; see samples/README.md.");
            System.exit(1);
        }
    }
}
