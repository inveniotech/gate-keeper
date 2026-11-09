# Middleware API, version 1

**Status: accepted by the owner on 9 November 2026.** The owner approved the
changes in pull request #123, including the section 8 consent wording.
The normative words MUST, MUST NOT and SHOULD define the accepted contract.
Adapter implementation is P3-02 to P3-05.

Traces: R-1, F-005, UC-003, NFR-001, NFR-004, NFR-028, NFR-031;
IQ-09, IQ-31, IQ-32, IQ-33, IQ-54, IQ-76, IQ-78 and IQ-79.
The source card is [P3-01](../implementation/cards/p3-01/01-middleware-api-contract.md).

## 1. Scope

The middleware supplies classification, text redaction, configured response-field
filtering, a consent-provider check and verified pack loading. It MUST NOT gate
requests, perform repository or history scans, write evidence or enforce licences.
The first release covers .NET, Node/TypeScript and Kotlin/JVM. Python is specified
for compatibility but is built after the first release.

## 2. Relationship to the scanner

An adapter MUST NOT invoke or require the command-line interface at runtime
(IQ-09). Rules MUST remain pack data. Classification parity MUST be established
with the P3-00 vectors. Pack fields, structured shapes, versions and signature
bytes are defined only by [rule-pack-v1.md](rule-pack-v1.md).
Adapters MUST NOT access the network or emit telemetry. A consent provider is
application code; the adapter MUST NOT implement its storage or transport.

## 3. Common model

| Type | Fields or values | Exclusions |
|---|---|---|
| Finding | `ruleId`, `category`, `start`, `length`, `severity` | MUST NOT contain matched text, source text, subject or purpose |
| Classification | One Finding, or none | No exception for an ordinary non-match |
| Context | `surface`: log, response or value; optional `fieldName` | MUST NOT carry subject or purpose |
| Mask | Literal `[REDACTED:` followed by a pack rule identifier and `]` | Matched value, subject and purpose MUST NOT appear |
| Subject | Opaque application string of 1 to 1,024 Unicode scalars | MUST NOT be logged |
| Purpose | Namespaced string under section 8 | No free-form log message |

The common operations are `redact(text) -> text`,
`classify(value, context) -> finding | none`, a response-filter framework hook,
`check(subjectId, purpose) -> granted | denied | unknown`, and verified pack load.
Models MUST be immutable. Concurrent calls MUST NOT share mutable match state.

## 4. Text, offsets and deterministic ordering

Input MUST be valid Unicode text. UTF-8 boundaries MUST reject malformed bytes;
native strings MUST reject unpaired surrogates. A byte-order mark MUST NOT be
silently removed from an application string. Finding offsets MUST be zero-based
Unicode scalar offsets; lengths MUST count scalars too. .NET and Java MUST map
UTF-16 indices to scalar counts; Node MUST count code points rather than string
code units; Python MUST reject surrogate values before using native indices.

Matching MUST use the pack's ASCII folding and ordinal comparison, independent
of locale. A pattern's finding start and length MUST identify its single `emit`
part when present, or the whole shape when no part emits, as
[rule-pack section 7.1](rule-pack-v1.md#7-the-structured-shape-iq-14) defines.
Redaction MUST replace that finding span, not the surrounding shape, label or
keyword. List rules use the exact accepted token's span.

The scanner reports every rule's own accepted matches. Middleware MUST preserve
that per-rule matching and the pack's within-rule resume semantics. Separately,
`classify` MUST select one finding by earliest scalar start, then longest scalar
length, then ordinal rule identifier. `redact` MUST use the same ordering and
discard spans overlapping a previously selected span. This cross-rule selection
is middleware behaviour; it MUST NOT be applied to scanner results. P3-00 parity
means the same rule identifier and the same scalar span after translating engine
UTF-16 indexes. Vectors MUST include an astral character before an emitted span.
Adapters MUST NOT normalise Unicode or change unrelated text.

## 5. Redaction

`redact(text)` MUST apply every loaded rule regardless of severity, with no
context argument. Each selected finding's emit span MUST be replaced by the
literal `[REDACTED:` plus its exact pack rule identifier plus `]`, for example
`[REDACTED:pii.bvn]`. The mask MUST NOT contain matched values, subjects or
purposes. Output MUST retain all other scalars in order.
An empty string MUST return an empty string. Null MUST raise an argument error.

Only a rule marked `redaction.optional` in the pack MAY be disabled, by identifier
at start-up (IQ-76). Unknown identifiers and attempts to disable mandatory rules
MUST fail start-up. The adapter MUST log disabled rule identifiers once, without
source text. Severity overrides MUST NOT reduce redaction. Scanner configuration
keys `rules.disabled`, `severity.overrides`, profiles, exclusions and
`packs.local` MUST NOT be middleware inputs. Middleware start-up disablement is
the separate optional-rule mechanism in this section, not scan configuration.

The reserved literal grammar is
`\[REDACTED:[a-z][a-z0-9]*(?:\.[a-z][a-z0-9]*)*\]`, using the pack schema's
rule-identifier grammar. The identifier need not be in the loaded pack: masks
from a previous pack remain reserved. Both operations MUST remove these spans
from matcher input before matching, process the intervening text independently,
and preserve original scalar offsets. Redaction MUST restore literal masks
unchanged. A malformed mask is ordinary text. No match may cross a removed
mask. Reserving caller-supplied literals is an explicit limitation, not evidence
that the literal was produced by the adapter. This gives the same idempotence
semantics for produced masks and literal masks across stacks.

The sole per-call cap is **65,536 Unicode scalar values**, fixed by
[rule-pack section 7.5](rule-pack-v1.md#75-indexes-and-the-resume-rule). It is
neither UTF-8 bytes nor UTF-16 code units. Adapters MUST validate text and count
all input scalars, including reserved masks, before matching. Oversized input
MUST raise the named input-limit error in section 10 without returning text.
A successful redaction MUST also fit that same cap; if expansion into masks
would exceed it, the entire call MUST fail with the same limit error. This
keeps a successful result valid for a second, idempotent call.
This contract sets **no per-call latency number**. Command-line file and
continuous-integration budgets are not middleware latency budgets.

## 6. Classification

`classify(value, context)` MUST return the first finding under section 4, or none.
`surface` MUST be exactly `log`, `response` or `value`. `fieldName` MAY be absent;
when present it MUST be a non-empty string. Unknown surfaces, empty field names,
a missing context or null input MUST raise the argument error in section 10.
An empty input MUST return none after context validation.

For version 1, `redact` and `classify` MUST execute the loaded rules without
using surface, field name or repository paths to select rules. Pack
`location.kind` remains a label, not a context filter. `location.paths` MUST NOT
filter runtime strings, which have no repository path. Context does not alter
keywords, boundaries, validators, exclusions or any other matching semantics.
The operations MUST NOT invent rules or infer consent. A non-match MUST return
none. Classification MUST NOT modify the input or record its contents.

## 7. Response filtering

The object-field path syntax is RFC 6901 JSON Pointer. Configured paths MUST
be validated at start-up; `~0` and `~1` decode to tilde and slash. Missing object
properties MAY be ignored. Selected strings MUST pass through `redact`; selected
non-string values and any redaction failure, including invalid text or the
section 5 scalar cap, MUST fail the protected response before body bytes are
written. Transformations MUST be atomic, not a partially redacted body.

A present JSON `null` is a non-string value, not a missing property. For
`/user/phone`, `{}` selects nothing; `{"user":null}` fails because traversal
encounters a non-container, and `{"user":{"phone":null}}` fails because the
selected leaf is not a string. A nullable protected field therefore requires
the application to omit that field or supply a string under this contract.

**Owner decision, 8 November 2026:** add an explicit wildcard extension and
filter every selected array element. A complete unescaped path segment `*`
MUST select every element of an array, for example `/customers/*/phone`.
Wildcards MUST NOT select object properties. For literal object property `*`,
this extension uses `~2`; standard `~0` and `~1` retain their meanings. Other
escape sequences MUST be rejected at start-up. An empty array selects nothing.
A wildcard applied to a non-array MUST fail the protected response; selected
elements of the wrong type MUST also fail, not be skipped. Exact numeric
indices remain explicit single-element selection and MUST NOT be described as
protecting siblings. Nested wildcards MUST visit every selected element.
Paths selecting the same string MUST redact it once. Selection and redaction
MUST complete on a private copy before any transformed body can be committed.

Only bounded JSON responses are supported. A configured protected response that
is streaming, exceeds the cap or has a missing, empty or non-JSON content type MUST fail before
headers or body are sent. Streaming means the length is unknown or the host has
already started the body. If headers are already committed when a protected
response fails, the adapter MUST abort the response/connection; it MUST NOT send
the original or partially transformed body. A response without configured
protected paths MUST pass through unchanged.
Because an already started body is streaming, committed headers on a protected
response require an abort even when every selected value could be redacted.
The adapter MUST NOT infer a JSON content type when the host supplies none.
Adapters MUST NOT buffer an unbounded stream or silently return unfiltered
protected content. JSON Pointer and this bounded-response policy are contract
requirements; acceptance does not establish existing adapter conformance.

## 8. Consent check

The consent helper is for processing whose lawful basis is consent. Consent is
one of several lawful bases under the Nigeria Data Protection Act 2023; this
surface does not decide which basis applies. An application MUST NOT treat
absence of this surface as a finding for processing based on another lawful basis.

The application supplies a consent provider. The adapter MUST NOT store, cache
or copy consent state. A provider SHOULD return `denied` when it holds no record
for the subject and purpose. It MAY return `unknown` only when it cannot reach
its own store. `unknown` is inability to obtain an answer, not the absence of a
consent record (IQ-32). The adapter cannot inspect the provider's records.

`check` MUST return `unknown` if no provider is registered, if a provider throws,
or if it returns a value outside the three states. The adapter MUST NOT return
`granted` in any other case. `isGranted` MUST perform exactly the same check,
returning true for `granted` and false for `denied` and `unknown`, with the same
diagnostic and cancellation behaviour. A caller MUST treat `unknown` as not granted.

`subjectId` MUST be an opaque string of 1 to 1,024 Unicode scalars; applications
SHOULD use an internal surrogate. Purpose MUST match
`^[a-z][a-z0-9-]{0,31}(\.[a-z][a-z0-9-]{0,31}){1,4}$`, with at most 164 ASCII
characters, for example `acme.marketing-sms`. Invalid arguments MUST raise the
stack's argument error immediately, without calling the provider.

On a provider exception, the adapter MUST write one diagnostic containing the
purpose and exception type name only. It MUST NOT log the message, stack trace
or subject. Caller cancellation MUST propagate and MUST NOT become `unknown`.
A provider's own timeout or internal cancellation is a provider exception, not
caller cancellation, unless the caller's cancellation token or signal is set.
The adapter MUST NOT introduce retries or timeouts; they belong to the provider.
Providers MUST be safe to call concurrently, and adapter documentation MUST
state this requirement.

Consent MUST NOT influence classification, redaction or response filtering.
The marker is the call to `check` or `isGranted`; version 1 MUST NOT introduce
an annotation, attribute or decorator. Detection of missing calls is rule-pack
work and is not specified here.

## 9. Pack loading

Free installations MUST embed free packs; paid installation media MUST embed
paid packs. The optional filesystem path MUST load a signed replacement for an
embedded pack with the **same pack identifier** and a strictly greater numeric
`(year, month, patch)` triple from `YYYY.MM.P`, as defined by
[rule-pack section 9](rule-pack-v1.md#9-pack-lines-versions-and-dates-iq-36).
Different identifiers have no replacement precedence. An older, equal or
different-ID candidate MUST refuse process start-up with a pack-load error.
Unsigned local packs remain a scanner feature and MUST NOT load in middleware.

Every middleware pack MUST be validated and its manifest signature verified at
load using the rule-pack contract. A detached watermark MUST be verified only
when the block is present, including its own signature and content-digest
binding. A missing watermark MUST NOT be invented or required. These checks
are not licence checks: the adapter MUST NOT fetch a pack, verify a licence or
run entitlement policy.
Loading MUST be atomic: unverified candidates MUST never become active.
`versions() -> VersionInfo` MUST return `adapterVersion` and an ordinally
pack-ID-sorted `packs` array containing `id`, `version` and `contentDigest`
(NFR-031). .NET names this `Versions()`, Node/JVM `versions()`, and Python
`versions()`. It MUST be side-effect-free and contain no application inputs.

## 10. Cross-cutting requirements

Adapters MUST NOT contain update checks, remote pack fetching, telemetry,
crash uploads or remote logging. Diagnostics MUST contain metadata only.
Public errors MUST name the cause and recovery action without input values.
.NET MUST remain ahead-of-time and trim safe. No named integration in this
document approves a new dependency; rule 8 still applies.

| Failure | .NET | Node | JVM | Python |
|---|---|---|---|---|
| Invalid argument/text/context | `ArgumentException` | `TypeError` | `IllegalArgumentException` | `ValueError` |
| Scalar cap exceeded | `InputLimitException` | `InputLimitError` | `InputLimitException` | `InputLimitError` |
| Pack load/verification/version failure | `PackLoadException` | `PackLoadError` | `PackLoadException` | `PackLoadError` |
| Protected response failure | `ResponseFilterException` | `ResponseFilterError` | `ResponseFilterException` | `ResponseFilterError` |

The named limit, pack and response errors MUST be separate public types derived
from the stack's ordinary exception/error base. Cancellation MUST retain its
native stack form and MUST NOT become a consent answer or response success.

## 11. Fail-closed policy

| Failure | Required behaviour | Reason |
|---|---|---|
| Pack verification fails | MUST reject the candidate; retain the already verified active pack, or refuse start-up if none exists | Redact more, never less; no weaker fallback |
| Pack absent | MUST refuse start-up | No silent empty matcher |
| Pack unreadable | MUST reject the candidate; retain active verified pack or refuse start-up | No partial load |
| Older/equal calendar version or different pack ID | MUST refuse start-up with a pack-load error | Not a newer replacement of that embedded pack |
| Unsupported `schemaVersion` or newer `minEngineVersion` | MUST refuse start-up and explain required adapter upgrade | Engine capability is distinct from calendar precedence |
| No consent provider | MUST return `unknown`; emit one start-up warning | No invented grant |
| Provider throws | MUST return `unknown`, with the safe diagnostic from section 8 | No content leakage |
| Provider returns `unknown` | MUST preserve `unknown`; `isGranted` MUST return false | No inferred grant |
| Invalid text | MUST raise an argument error before output | No lossy matching |
| Input exceeds limit | MUST raise a limit error before output | Bounded work |
| Unsupported protected content type | MUST fail before response bytes are sent | No unfiltered protected payload |
| Selected string fails redaction or has wrong type | MUST fail atomically before body bytes; abort if headers are committed | No original or partially transformed body |
| Protected response is streaming or already started | MUST refuse before commit, or abort when committed | No unbounded buffering or continued body |
| Constructed without verified pack | MUST refuse construction | No operational empty matcher |

## 12. Idiomatic stack shapes

The IQ-31 integration set is owner direction pending interview validation. The
named frameworks and logging libraries are the closed integration list. Samples
below show core operations, not framework-hook registration. Registration
examples belong to individual adapter guides and cannot be claimed implemented
from these samples. These are contract signatures, not installed packages or
runnable adapter code.

### .NET

Packages are `InvenioTech.GateKeeper.*`, targeting .NET 10. ASP.NET Core hooks
MUST include middleware, minimal-API endpoint filtering and MVC result filtering.
Logging integrations are `Microsoft.Extensions.Logging` through
`Microsoft.Extensions.Compliance.Redaction`, and Serilog.
Registration MUST use dependency injection. Consent uses
`IConsentProvider.GetStateAsync(string, string, CancellationToken)` returning
`ValueTask<ConsentState>`, with `ConsentCheck.CheckAsync` and `IsGrantedAsync`.

```csharp
sealed class ExampleConsentProvider : IConsentProvider
{
    public ValueTask<ConsentState> GetStateAsync(string subjectId, string purpose,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(ConsentState.Denied);
    }
}
// Proposed common-operation calls after dependency-injection registration:
var safe = redactor.Redact("ordinary synthetic text");
var finding = classifier.Classify("ordinary synthetic text", context);
var granted = await consent.IsGrantedAsync("synthetic-subject", "acme.marketing-sms", cancellationToken);
```

### Node/TypeScript

Packages are `@inveniotech/gate-keeper-*`, with ESM, CommonJS and declarations.
Every supported Node long-term-support line at the major's release MUST pass
conformance (IQ-79). The named hooks are Express, NestJS, pino and winston.
Consent is asynchronous and MUST accept an optional `AbortSignal`.

```typescript
type ConsentState = 'granted' | 'denied' | 'unknown';
const provider: ConsentProvider = {
  async getState(subjectId, purpose, signal): Promise<ConsentState> {
    signal?.throwIfAborted();
    return 'denied';
  },
};
const consent = new ConsentCheck(provider);
const safe = redactor.redact('ordinary synthetic text');
const finding = classifier.classify('ordinary synthetic text', context);
const granted = await consent.isGranted('synthetic-subject', 'acme.marketing-sms');
```

### Kotlin/JVM

Coordinates are `africa.gatekeeper:gate-keeper-*`, package `africa.gatekeeper`.
Core and logging modules MUST target Java 15 bytecode and run on Java 17, 21
and 25. Spring Boot 3/4 hooks MUST live in a Java 17 module using
`jakarta.servlet`, a servlet filter and `ResponseBodyAdvice` (IQ-78).
Logging hooks are Logback and Log4j2. Public types MUST be usable from Java.
Consent MUST be blocking, with no coroutine requirement.

```kotlin
val provider = ConsentProvider { subjectId, purpose -> ConsentState.DENIED }
val consent = ConsentCheck(provider)
val safe = redactor.redact("ordinary synthetic text")
val finding = classifier.classify("ordinary synthetic text", context)
val granted = consent.isGranted("synthetic-subject", "acme.marketing-sms")
```

### Python, after the first release

The package is `inveniotech-gate-keeper`. Supported upstream Python lines at
the major's release MUST pass conformance. Hooks are Django, FastAPI/Starlette
and standard-library `logging`. The provider and check MUST offer synchronous
and asynchronous forms; the asynchronous check MUST NOT block its event loop.

`ConsentCheck.check` and `is_granted` MUST call the synchronous
`ConsentProvider.get_state`. `acheck` and `ais_granted` MUST await
`AsyncConsentProvider.get_state`; they MUST NOT call the synchronous provider.
Both pairs MUST apply the same state mapping, safe diagnostics and cancellation
rules from section 8.

```python
class ExampleConsentProvider:
    def get_state(self, subject_id: str, purpose: str) -> ConsentState:
        return ConsentState.DENIED

class ExampleAsyncConsentProvider:
    async def get_state(self, subject_id: str, purpose: str) -> ConsentState:
        return ConsentState.DENIED

consent = ConsentCheck(ExampleConsentProvider())
safe = redactor.redact("ordinary synthetic text")
finding = classifier.classify("ordinary synthetic text", context)
granted = consent.is_granted("synthetic-subject", "acme.marketing-sms")
# With an asynchronous provider: await async_consent.acheck(...) or ais_granted(...).
```

For every stack, response filtering MUST be registered through the named web
framework's hook with an explicit list of JSON Pointers, for example `/customer/name`.
The concrete registration overloads belong to adapter cards; no implementation
or dependency is introduced by these examples.

## 13. Conformance obligations

| Sections | Required vector families |
|---|---|
| 2–4, 6 | Current pack shapes, list boundaries, validators, context, scalar offsets, ties and no-match |
| 5 | Every severity, optional/mandatory switches, overlap, masks, literal masks, idempotence and input limits |
| 7 | JSON Pointer escaping, arrays, missing paths, type errors and bounded protected-response failures |
| 8 | Purpose grammar; every provider state; invalid state; absent provider; exceptions; cancellation; no caching; independence from redaction |
| 9, 11 | Missing, invalid, unreadable and unsupported packs; equal/older versions; failed candidate retains verified active pack |
| 10, 12, 14 | Metadata-only diagnostics, version stamps, stack mappings, runtime matrix and deterministic regeneration |

Every MUST in sections 3–11 MUST be mapped to one of these families before an
adapter is accepted. The specification is the source of truth; adapters MUST NOT
add behaviours that the shared cases cannot express. P3-00 can begin with
existing engine classification vectors. Reference redaction and consent
implementation and executable vectors may be developed for review; an adapter
MUST NOT be accepted or released against an unaccepted contract.

## 14. Compatibility and versions

This is contract version 1. Adapter releases MUST pin exact pack content and
report both versions. Public API breaks require a new adapter major; pack schema
breaks follow the pack contract. A deprecated surface SHOULD remain available
through its current major and name its replacement in documentation.

## 15. Review and deferred work

IQ-32, IQ-33, IQ-54, IQ-76, IQ-78 and IQ-79 are recorded as decided in the source
card. The owner's 8 November review makes replacement same-ID and signed-only,
fixes the cap to rule-pack section 7.5, and requires the corrections recorded
above. On 9 November 2026 the owner approved the changes in pull request #123,
including the subject bound, mask reservation, output expansion policy and
section 8 legal-adjacent wording. IQ-31 remains pending interview validation.
The current engine exposes UTF-16 spans internally;
the middleware boundary must translate them rather than change existing results.

Change log: 8 November 2026 — first complete P3-01 draft for owner review.
8 November 2026 — incorporated owner review of cap units, emit spans, mask
grammar, runtime context, signed replacement, response failures, consent mapping
and named API errors. The owner selected explicit wildcard protection of every
array element; section 7 records its proposed concrete path syntax.
9 November 2026 — recorded the owner's approval of pull request #123 and
acceptance of this contract. No adapter release or conformance is implied.
