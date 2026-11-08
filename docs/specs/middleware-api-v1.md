# Middleware API, version 1

**Status: draft — not accepted.** P3-01 proposes this contract for owner review.
The normative words MUST, MUST NOT and SHOULD describe the proposed contract;
they do not claim approval. The legal-adjacent paragraph in section 8 needs
separate owner review. Adapter implementation is P3-02 to P3-05.

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
| Mask | `[REDACTED:<rule-id>]` | No value-derived suffix or identifier |
| Subject | Non-empty opaque application string | MUST NOT be logged |
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
of locale. Overlaps MUST resolve by earliest start, then longest span, then
ordinal rule identifier. After accepting a span, overlapping candidates MUST be
discarded. Adapters MUST NOT normalise Unicode or change unrelated text.

## 5. Redaction

`redact(text)` MUST apply every loaded rule regardless of severity, with no
context argument. Each accepted span MUST be replaced by its stable mask, for
example `[REDACTED:pii.bvn]`. Output MUST retain all other scalars in order.
An empty string MUST return an empty string. Null MUST raise an argument error.

Only a rule marked `redaction.optional` in the pack MAY be disabled, by identifier
at start-up (IQ-76). Unknown identifiers and attempts to disable mandatory rules
MUST fail start-up. The adapter MUST log disabled rule identifiers once, without
source text. Severity overrides MUST NOT reduce redaction.

The mask syntax MUST be treated as already redacted, including a literal
mask-shaped string supplied by the caller, so a second call is idempotent.
Masks MUST NOT be passed through the matcher again. This reserved syntax is a
documented limitation: the middleware does not classify the content of a mask.

The proposed input limit is 1,048,576 UTF-8 bytes per call, checked before
matching. Oversized input MUST raise the stack's limit error without returning
text without redaction. This limit and mask reservation need owner acceptance.
Adapters SHOULD measure the per-call matching budget using the existing
[quality budgets](quality-budgets.md); this draft creates no new latency budget.

## 6. Classification

`classify(value, context)` MUST return the first finding under section 4, or none.
The surface and optional field name MAY select pack-defined context scopes;
adapters MUST NOT invent a rule or infer consent from the field name.
Invalid context or null input MUST raise an argument error. A non-match MUST
return none. Classification MUST NOT modify the input or record its contents.

## 7. Response filtering

The proposed path syntax is RFC 6901 JSON Pointer, with no wildcard extension.
Configured paths MUST be validated at start-up. A numeric segment addresses an
array element; `~0` and `~1` decode to tilde and slash. Missing fields MUST be
ignored. Selected strings MUST pass through `redact`; selected non-string values
MUST produce a type error before any response bytes are written. Other fields
MUST remain unchanged. Applications MUST explicitly list nested string paths.

Only bounded JSON responses are supported. A configured protected response that
is streaming, exceeds the limit or has a non-JSON content type MUST fail before
headers or body are sent. A response without configured protected paths MUST pass through unchanged.
Adapters MUST NOT buffer an unbounded stream or silently return unfiltered
protected content. JSON Pointer and this bounded-response policy are proposals
for owner acceptance, not existing adapter behaviour.

## 8. Consent check

The consent helper is for processing whose lawful basis is consent. Consent is
one of several lawful bases under the Nigeria Data Protection Act 2023; this
surface does not decide which basis applies. An application MUST NOT treat its
absence as a finding for processing based on another lawful basis. This
paragraph is draft legal-adjacent wording for owner review.

The application MUST supply a consent provider. The adapter MUST NOT store,
cache or copy consent state. The provider MUST return `granted`, `denied` or
`unknown`; no record MUST mean `denied`. `unknown` means the answer could not be
obtained, rather than that no consent record exists (IQ-32).

`check` MUST return `unknown` if no provider is registered, if a provider throws,
or if it returns a value outside the three states. `isGranted` MUST be true only
for `granted`. A caller MUST treat `unknown` as not granted.

`subjectId` MUST be a non-empty opaque string; applications SHOULD use an
internal surrogate. Purpose MUST match
`^[a-z][a-z0-9-]{0,31}(\.[a-z][a-z0-9-]{0,31}){1,4}$`, with at most 160 ASCII
characters, for example `acme.marketing-sms`. Invalid arguments MUST raise the
stack's argument error immediately, without calling the provider.

On a provider exception, the adapter MUST write one diagnostic containing the
purpose and exception type name only. It MUST NOT log the message, stack trace
or subject. Caller cancellation MUST propagate and MUST NOT become `unknown`.
The adapter MUST NOT introduce retries or timeouts; they belong to the provider.
Providers MUST be safe to call concurrently, and adapter documentation MUST
state this requirement.

Consent MUST NOT influence classification, redaction or response filtering.
The marker is the call to `check` or `isGranted`; version 1 MUST NOT introduce
an annotation, attribute or decorator. Detection of missing calls is rule-pack
work and is not specified here.

## 9. Pack loading

Free installations MUST embed free packs; paid installation media MUST embed
paid packs. An optional local path MAY supply a newer pack. Older or equal
versions MUST be rejected, using numeric pack-version comparison. There is no
same-kind restriction on a newer pack (IQ-33).

Every pack MUST be validated and its signature verified at load, using the
rule-pack contract. The adapter MUST NOT fetch a pack or verify a licence.
Loading MUST be atomic: unverified candidates MUST never become active.
Version reporting MUST expose adapter version and loaded pack identity,
version and content digest (NFR-031), without application inputs.

## 10. Cross-cutting requirements

Adapters MUST NOT contain update checks, remote pack fetching, telemetry,
crash uploads or remote logging. Diagnostics MUST contain metadata only.
Public errors MUST name the cause and recovery action without input values.
.NET MUST remain ahead-of-time and trim safe. No named integration in this
document approves a new dependency; rule 8 still applies.

The stack error mapping is .NET `ArgumentException` and a typed middleware
failure; Node `TypeError` and a typed `Error` subclass; JVM
`IllegalArgumentException` and a typed runtime failure; Python `ValueError`
and a typed exception. Cancellation MUST retain its native stack form.

## 11. Fail-closed policy

| Failure | Required behaviour | Reason |
|---|---|---|
| Pack verification fails | MUST reject the candidate; retain the already verified active pack, or refuse start-up if none exists | Redact more, never less; no weaker fallback |
| Pack absent | MUST refuse start-up | No silent empty matcher |
| Pack unreadable | MUST reject the candidate; retain active verified pack or refuse start-up | No partial load |
| Unsupported pack version | MUST reject candidate and explain required adapter upgrade | No guessed semantics |
| No consent provider | MUST return `unknown`; emit one start-up warning | No invented grant |
| Provider throws | MUST return `unknown`, with the safe diagnostic from section 8 | No content leakage |
| Provider returns `unknown` | MUST preserve `unknown`; `isGranted` MUST return false | No inferred grant |
| Invalid text | MUST raise an argument error before output | No lossy matching |
| Input exceeds limit | MUST raise a limit error before output | Bounded work |
| Unsupported protected content type | MUST fail before response bytes are sent | No unfiltered protected payload |
| Constructed without verified pack | MUST refuse construction | No operational empty matcher |

## 12. Idiomatic stack shapes

The IQ-31 integration set is owner direction pending interview validation.
These are proposed signatures, not installed packages or runnable adapter code.

### .NET

Package names follow the .NET namespace row in the source card, targeting .NET 10. ASP.NET Core hooks
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

```python
class ExampleConsentProvider:
    def get_state(self, subject_id: str, purpose: str) -> ConsentState:
        return ConsentState.DENIED

    async def get_state_async(self, subject_id: str, purpose: str) -> ConsentState:
        return ConsentState.DENIED

consent = ConsentCheck(ExampleConsentProvider())
safe = redactor.redact("ordinary synthetic text")
finding = classifier.classify("ordinary synthetic text", context)
granted = consent.is_granted("synthetic-subject", "acme.marketing-sms")
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
existing engine classification vectors; redaction and consent vectors MUST
remain pending until this draft is accepted and the reference behaviour exists.

## 14. Compatibility and versions

This is contract version 1. Adapter releases MUST pin exact pack content and
report both versions. Public API breaks require a new adapter major; pack schema
breaks follow the pack contract. A deprecated surface SHOULD remain available
through its current major and name its replacement in documentation.

## 15. Review and deferred work

IQ-32, IQ-33, IQ-54, IQ-76, IQ-78 and IQ-79 are recorded as decided in the source
card; this draft does not reopen them. IQ-31 remains pending interview validation.
The owner must review this document, especially the proposed limit, reserved
mask syntax, JSON Pointer policy and section 8 legal-adjacent wording. No new
decision is claimed here. The current engine exposes UTF-16 spans internally;
the middleware boundary must translate them rather than change existing results.

Change log: 8 November 2026 — first complete P3-01 draft for owner review.
