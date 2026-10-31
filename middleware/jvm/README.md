# Kotlin / JVM middleware

**Status: not started.** The JVM adapter arrives in Phase 3 (P3-04).

## Toolchain

| Concern | Choice |
|---|---|
| Runtime | Java 15 bytecode for the core and logging modules, tested on Java 17, 21 and 25; the Spring Boot module targets Java 17 (IQ-78) |
| Web framework | Spring Boot 3 and 4 auto-configuration, using `jakarta.servlet` |
| Package | `africa.gatekeeper:gate-keeper-*` on Maven Central (IQ-54) |
| Logging | Logback and Log4j2 |
