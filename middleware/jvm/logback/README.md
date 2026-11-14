# Logback artefact

`gate-keeper-logback` supplies `RedactingConverter`, targeting Java 15. Put a
verified matcher at `RedactingConverter.MATCHER_KEY` on the logger context and
configure the conversion rule shown in the parent README. Failures return a
fixed safe sentence. Only the formatted message is protected.

The application supplies Logback 1.5; the jar depends on the Kotlin standard
library and core. Run `gradle -p middleware/jvm :logback:check` to exercise
synthetic redaction, oversize input and missing-matcher failure.
