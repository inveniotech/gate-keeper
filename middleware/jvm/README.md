# JVM middleware

Gate-Keeper's offline JVM adapter implements middleware contract version 1
(P3-04, issue #41, IQ-54, IQ-78 and IQ-79). Core and logging artefacts target
Java 15 bytecode; the Spring Boot artefact targets Java 17. Continuous
integration runs on Java 17, 21 and 25. No `javax` module exists.

| Reserved Maven coordinate | Public surface | Runtime dependencies |
| --- | --- | --- |
| `africa.gatekeeper:gate-keeper-core` | `Installation`, `SignedPackVerifier`, `RuntimeMatcher`, `ConsentCheck`, `ResponseFilter` | JDK only |
| `africa.gatekeeper:gate-keeper-logback` | `RedactingConverter` | Kotlin standard library; application supplies Logback |
| `africa.gatekeeper:gate-keeper-log4j2` | `GatekeeperLog4j`, `GatekeeperRewritePolicy` | Kotlin standard library; application supplies Log4j2 |
| `africa.gatekeeper:gate-keeper-spring-boot` | `GatekeeperFilter`, `GatekeeperResponseAdvice`, `GatekeeperAutoConfiguration` | Kotlin standard library; application supplies Spring, Jackson 2 and Jakarta Servlet |

Production pack bytes and Maven Central publication remain human release steps.
No production trust key, embedded production pack or release publication is
included here. Versions come from `middleware/version.txt`; regenerate
`BuildVersion.java` with `tests/conformance/Sync-RuntimeVersion.ps1`.

## Signed installation

Release media supply `SignedPackVerifier.Input` values containing content bytes,
manifest armour and the raw 64-byte detached signature. Optional watermark
armour and its raw signature must both be present or both absent. Construct
`SignedPackVerifier` with separate maps of trusted key identifiers to raw
32-byte Ed25519 public keys for manifests and watermarks. Development-vector
keys are test arguments only and are never an implicit trust root.

```java
RuntimeMatcher runtime = Installation.load(embeddedInputs,
    new SignedPackVerifier(packKeys, watermarkKeys));
String safe = runtime.redact("ordinary synthetic text");
RuntimeMatcher.Finding finding = runtime.classify("ordinary synthetic text", "value", null);
```

The five-argument `Installation.load` overload also accepts disabled rule
identifiers, a metadata-only start-up diagnostic and an optional replacement
directory. Only optional redaction rules may be disabled, and disablement
requires a diagnostic. The loader reads only these named files:

```text
pack.json
manifest.armour
manifest.signature
watermark.armour       (optional pair)
watermark.signature    (optional pair)
```

The content limit is 16 MiB. The JSON reader rejects invalid UTF-8, duplicate
properties, trailing data, non-integers, unpaired surrogates and nesting beyond
32 containers. Signatures cover the exact manifest payload bytes. Both content
digests must agree before JSON is parsed. Unsupported schemas and newer engine
requirements refuse start-up with an upgrade instruction. Unsigned local packs
and `local.` rule identifiers cannot load. Plan-gated provenance is accepted
without checking a licence or contacting a service.

At least one embedded pack must verify. An integrity failure in a replacement
retains the verified embedded pack and reports a safe diagnostic when supplied.
A verified older, equal or different-ID replacement refuses start-up. A newer
numeric `(year, month, patch)` version of the same identifier becomes active
atomically. `versions()` reports adapter version and ordinally sorted pack
identities, versions and exact-content SHA-256 digests.

## Logging hooks

Install the verified matcher on the Logback logger context before logging:

```java
context.putObject(RedactingConverter.MATCHER_KEY, runtime);
```

```xml
<conversionRule conversionWord="gkmsg" converterClass="africa.gatekeeper.RedactingConverter"/>
<!-- Use %gkmsg in the application's encoder pattern for its message. -->
```

For Log4j2, call `GatekeeperLog4j.install(runtime)` once before logging, then
route the application's output through a Rewrite appender:

```xml
<Rewrite name="Protected">
  <GatekeeperRewritePolicy/>
  <AppenderRef ref="Destination"/>
</Rewrite>
```

The policy is registered through Log4j2's generated plugin descriptor. Both
hooks redact the formatted message. A missing matcher, invalid text or scalar
limit failure returns the fixed sentence `The log message could not be redacted
safely.` Applications remain responsible for protecting other event fields,
structured context and exception output.

## Spring Boot

Provide a `RuntimeMatcher` bean created by `Installation.load`. Set
`gatekeeper.paths=/customer/phone,/customers/*/phone` to enable the servlet
filter and controller advice through Boot's auto-configuration imports file.
A configured installation without a matcher bean fails application start-up.
The adapter compiles against Boot 3 and separately against Boot 4. On Boot 4,
the application must supply Jackson 2 for this adapter's parsing boundary.

The advice transforms a private JSON tree before conversion and marks the
request to prevent a second transformation. The servlet fallback requires a
known length, a JSON media type and at most 1 MiB of body bytes. It never sends
original or partially transformed protected content. Present null selected
leaves fail; missing properties select nothing. Committed protected responses
abort by closing the stream. With no paths the hooks pass responses through.
Selected strings retain the core limit of 65,536 Unicode scalars.

Pointers use `~0` for tilde, `~1` for slash and `~2` for a literal star property.
A complete `*` segment selects every array element. Selection and redaction are
atomic; wrong types fail the protected response. The host must not commit
headers or stream protected responses before these hooks complete.

`ConsentCheck` is blocking and does not store or cache state. Providers must
support concurrent calls. Unknown answers are never grants; caller interruption
propagates. Diagnostics contain purpose and exception type only.

## Build and validate

Use Gradle 8.14.3 and a supported JDK. From the repository root:

```sh
mkdir -p artifacts/conformance-jvm
javac --release 15 -d artifacts/conformance-jvm middleware/jvm/src/africa/gatekeeper/*.java tests/conformance/VectorJson.java tests/conformance/ResponseFailureProbe.java tests/conformance/CheckRuntime.java tests/conformance/CheckPackLoad.java middleware/jvm/sample/CoreExample.java
java -cp artifacts/conformance-jvm CheckRuntime
java -cp artifacts/conformance-jvm CheckPackLoad
gradle -p middleware/jvm build verifyAdapters
```

`verifyAdapters` runs synthetic logging and servlet checks, compiles Spring
public types on the Boot 4 classpath, and compiles Java samples at releases 15
and 17. The core sample has only core classes on its classpath. Framework
dependencies are `compileOnly`; the jars do not shade them. Logging tests also
exercise the 65,536-scalar limit without returning original text.

Pinned dependencies are Kotlin 2.1.21 (Apache-2.0), Logback 1.5.18
(EPL-1.0/LGPL-2.1, application-provided), Log4j2 2.24.3 (Apache-2.0), Boot 3.5.0
and 4.0.0, Spring 6.2.7 and 7.0.1 (Apache-2.0), Jackson 2.19.0 (Apache-2.0),
and Servlet 6.0.0/6.1.0 (EPL-2.0/GPL-2.0 with classpath exception,
application-provided). These established upstream projects provide the named
hooks; no framework is bundled. The only shipped additional runtime library
is the Kotlin standard library in integration dependency metadata. The core
has no third-party dependency; native ahead-of-time .NET concerns do not apply.
