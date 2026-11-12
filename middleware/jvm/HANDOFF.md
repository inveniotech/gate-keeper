# P3-04 implementation handoff

The signed verifier, strict bounded pack JSON reader, installation loader,
logging hooks, Spring hooks, Java samples and continuous-integration wiring are
implemented. No production key or pack is embedded; nothing was published.
The dependency and API choices follow the owner-supplied JVM adapter plan.

Validated locally on Corretto 21.0.12.1: the core and plain Java sample compile
with `--release 15`; `CheckRuntime` passes 262 matching/redaction cases, consent
and 22 atomic response cases. `CheckPackLoad` passes RFC 8032 TEST 1, 2 and 3,
signed development vectors, watermark cases, hostile JSON and replacement
precedence/fallback cases. Run these again after subsequent review fixes.

The full Gradle build remains unverified. A workspace-local Gradle cache solved
native initialisation, but dependency resolution could not find the pinned
Kotlin plugin through the sandbox. Two network escalation requests remained
pending and were interrupted. No dependency lock files were generated: generate
them with `gradle -p middleware/jvm build verifyAdapters --write-locks` after
network approval, and commit the generated files rather than editing them.
Spring Boot 3/4 compilation, servlet tests, plugin-processor output and Java
hook sample compilation remain required acceptance checks. Java 17/25 runtime
checks also remain for CI. Gradle launches on Java 21 in CI and adapter probes
use the selected 17/21/25 runtime.

An additional offline syntax compile of the logging Kotlin sources against
locally cached Kotlin 2.3.21, Logback 1.5.38 and Log4j2 2.25.2 exposed and fixed
the Log4j event-builder call. Those versions are not the pinned build and do
not establish acceptance. The Java probe attempt with that cache failed due to
JDK ZIP-file cache permissions; do not report the logging tests as passed.

Follow-ups before acceptance: complete dependency resolution and locks, execute
the full build and runtime matrix, inspect the generated Log4j plugin descriptor
and jar dependencies, and complete fresh review. Existing unrelated Node and
Python changes were preserved. Production pack provisioning and Maven Central
release remain human steps. IQ-31 remains pending; no additional hooks were
introduced.
