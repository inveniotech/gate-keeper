# Kotlin / JVM middleware

**Status: executable Java-friendly JVM core for P3-00.** The Kotlin package,
Spring Boot and logging registrations, signed installation loading and package
publication remain P3-04. The core is Java source at Java 15 bytecode, callable
from Java or Kotlin without a Kotlin runtime dependency.

`RuntimeMatcher` consumes already validated, verified rule models, prepares
private immutable matching data and interprets bounded shapes with memoised
states. It implements every validator, exclusion, boundary, keyword window and
list-token rule; no unbounded native regex backtracking is used for shapes.
Runtime paths and context do not select rules. Classification and redaction
use scalar emitting spans, reserved literal masks and the same overlap order
and 65,536-scalar input/result cap as .NET and Node. `versions()` accepts only
the supplied verified pack metadata. It performs no entitlement or network work.

`ConsentCheck` supplies the blocking Java-friendly provider interface and safe
tri-state mapping, propagating cancellation and thread interruption. The adapter
never retains answers. `ResponseFilter` transforms a private parsed JSON tree,
filtering every selected array element with the explicit `*` extension. Pointer
escapes `~0`, `~1`, `~2` name literal tilde, slash and star keys. Framework hooks
must bound parsing first and honour the failure's `abortRequired` flag.

Present null leaves or intermediate containers fail as type errors; missing
properties are ignored. Committed protected responses always abort because the
contract defines an already started body as streaming. Null or empty content
types fail closed. All runtime matcher exceptions become content-free response
failures. Provider cancellation without caller thread interruption returns
unknown with a diagnostic; caller interruption still propagates.

The core has no repository-path matching overload; scanner scopes live in the
test fixture. Startup validation rejects unknown boundaries and validators,
invalid windows and malformed shape atoms. Null disabled-rule sets mean none.
The host owns bounded parsing; this core adds no stack-specific depth cap and
passes unprotected responses through without inspecting their trees.

`BuildVersion.java` is generated from `middleware/version.txt` using
`tests/conformance/Sync-RuntimeVersion.ps1`. Conformance compares reported metadata
with actual .NET loader output and independently hashed source pack bytes.
Metadata supplied by the loader remains a trust boundary, not signature proof.

## Validate

```sh
mkdir -p artifacts/conformance-jvm
javac --release 15 -d artifacts/conformance-jvm middleware/jvm/src/africa/gatekeeper/*.java tests/conformance/VectorJson.java tests/conformance/ResponseFailureProbe.java tests/conformance/CheckRuntime.java
java -cp artifacts/conformance-jvm CheckRuntime
```

The JSON reader is test-fixture infrastructure only and is not a pack parser
inside the adapter. CI runs the core on Java 17, 21 and 25. No new dependency,
production key or published JVM package is introduced.

## Toolchain

| Concern | Choice |
|---|---|
| Runtime | Java 15 bytecode for the core and logging modules, tested on Java 17, 21 and 25; the Spring Boot module targets Java 17 (IQ-78) |
| Web framework | Spring Boot 3 and 4 auto-configuration, using `jakarta.servlet` |
| Package | `africa.gatekeeper:gate-keeper-*` on Maven Central (IQ-54) |
| Logging | Logback and Log4j2 |
