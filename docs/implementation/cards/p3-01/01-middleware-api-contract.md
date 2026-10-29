# Card P3-01: Middleware API contract specification

- **Work package:** P3-01 Middleware API contract specification
- **Mode:** AI-DRAFT — an agent drafts, the owner approves (rule 15)
- **Size:** S
- **Branch:** `wp/p3-01-01-middleware-api-contract`
- **Pull request title:** `docs(p3-01): add the middleware API contract specification`
- **Traces:** R-1 (middleware is thin helpers only), F-005 and UC-003 (consent helper), IQ-09 (decided), IQ-31 (owner direction), IQ-32 (decided, 29 October 2026), IQ-33 (decided, 29 October 2026), IQ-76, IQ-78 and IQ-79 (decided, 29 October 2026), IQ-54 (decided, 29 October 2026), NFR-031 (version stamps), NFR-028 (glossary), rules 5, 6, 8, 9, 10, 17, 18, 22, 24
- **Spec produced:** `docs/specs/middleware-api-v1.md`
- **Context pack:** `AGENTS.md` and this card. Doc 15 vocabulary applies to the specification. The launch set, the decided fail-closed rule, and the IQ-32, IQ-33, IQ-76, IQ-78 and IQ-79 decisions are restated below.

---

## 1. What "done" means

1. `docs/specs/middleware-api-v1.md` exists and is self-contained: a developer who has read nothing else can build an adapter from it.
2. It states one common, language-neutral surface, then the idiomatic shape of that surface in .NET, Node/TypeScript, Kotlin/JVM and Python, each with examples that use synthetic identifiers only.
3. The IQ-32 decision (section 3.1) and the IQ-33 decision (section 3.2) are written into the document as normative text, not left as recommendations.
4. The owner has marked the document accepted. **An agent must never write "Approved", "Accepted" or "Final" into it, and must never merge it.**

## 2. Starting position

The repository contains `AGENTS.md`, `CLAUDE.md`, `.gitattributes` and this card. There is no `docs/specs/` yet, no `src/`, no test suite and no CI. Four consequences for this card:

- `docs/specs/` is being created by this pull request. Nothing else lives there yet, so nothing constrains the document's internal structure except this plan.
- The rule-pack specification does not exist yet. This document must therefore **not** define pack fields, pack versions or signature encoding. It names the rule pack as an input and defers every detail to the rule-pack specification. Writing pack detail here would create a second source of truth, which rule 24 forbids.
- There are no lint gates, no spelling gate and no banned-terms checker in CI yet. Section 7 lists the manual checks that substitute for them, and section 9 lists the follow-up that fixes the gap permanently.
- Nothing in this repository may describe or link to a private repository (rule 29). IQ identifiers are safe to use because this card is public; a register location is not, and must not be written down.

Decision status at the time of writing:

| Question | Status | What it settles |
|---|---|---|
| IQ-09 | Decided — no runtime dependency on the command-line tool | Can be written as a normative statement today |
| Fail-closed redaction | Decided by the work package | When pack verification fails, redaction fails closed: redact more, never less. Written in §11 now. Not part of IQ-33. |
| IQ-31 | Owner direction, pending interview validation; Spring Boot line amended by IQ-78 | The launch set in §12. Write that set, and do not add a framework or logging library that is not listed. |
| IQ-32 | **Decided** by the owner on 29 October 2026 | The consent surface: provider interface, tri-state result, `isGranted`, purpose grammar, the call as the marker, no annotation. Written in section 3.1. |
| IQ-76 | **Decided** by the owner on 29 October 2026 | Redaction applies every rule in the pack; only rules the pack marks `redaction.optional` may be switched off. |
| IQ-78 | **Decided** by the owner on 29 October 2026 | The Spring Boot module targets Spring Boot 3 and 4 at Java 17 bytecode; other JVM modules stay at Java 15 bytecode. |
| IQ-79 | **Decided** by the owner on 29 October 2026 | Middleware supports every runtime line under upstream support at each major's release. |
| IQ-54 | **Decided** by the owner on 29 October 2026 | The Maven namespace is `africa.gatekeeper` (reverse of the product domain `gatekeeper.africa`). Artefacts are `africa.gatekeeper:gate-keeper-*`; the Kotlin package root is `africa.gatekeeper`. |
| IQ-33 | **Decided** by the owner on 29 October 2026 | Free packs are embedded in the free installation. Paid packs are embedded in the paid installation media. A path may load a newer pack. The adapter verifies the signature and does not verify a licence. |

## 3. Decisions the specification must state

The card is no longer blocked. IQ-32 and question 10 were answered on 29 October 2026, and IQ-33 was decided the same day, so every section can be written now. Write each decision below as normative text. Do not reopen any of them; if a decision seems wrong or incomplete, say so in the pull request (rule 20) and keep writing what was decided.

### 3.1 IQ-32, decided by the owner on 29 October 2026 (consent)

| # | Question | Decision |
|---|---|---|
| 1 | Is the helper a provider interface the application implements, or a free function the adapter owns? | A provider interface the application implements. The adapter never holds, caches or copies consent state. |
| 2 | Is the result exactly `granted`, `denied`, `unknown`, and is `unknown` distinct from "no record found"? | Three states. **No record found is `denied`**: consent that cannot be shown was not given. `unknown` means only that the adapter could not get an answer — no provider registered, the provider raised an error, or it returned a value outside the three states. Keeping `unknown` for faults makes it something an operator can alert on. |
| 3 | What must a caller do when the answer is `unknown`? | Treat it as not granted. Every adapter offers `isGranted(subjectId, purpose)`, true only for `granted`, so the safe path is also the shortest. `check` still returns the three states. |
| 4 | How is a purpose identified? | An application-defined namespaced string with a fixed grammar (section 3.1.1, 8.5), so it is portable, testable in conformance vectors and safe to log. |
| 5 | Does the check take anything beyond subject and purpose? | No. Lawful basis, notice version and timestamps belong in the application's consent records. |
| 6 | Does v1 name a marker the command-line tool can detect? | **The marker is the call** to `check` or `isGranted`. Version 1 defines **no annotation, attribute or decorator**: an annotation that enforces nothing would give false assurance, and one that enforces would be request gating, which R-1 excludes. The specification defines no detection rule (rule 9). |

Two further consequences of the decision:

- **Consent never influences classification, redaction or response filtering.** The classification context in §3 carries no subject and no purpose, so a `granted` answer can never make the adapter redact less.
- **Consent is one lawful basis among several** under the Nigeria Data Protection Act 2023. The specification says the helper applies to processing whose lawful basis is consent, and that its absence is not a finding for processing that rests on another basis. That sentence is legal-adjacent wording (rule 15): draft it, flag it in the pull request for the owner's legal review, and do not cite sections of the Act.

#### 3.1.1 Normative text for §8

Write §8 with this content. Wording may be polished; the requirements may not change.

> **8.1 Purpose of this surface.** The consent check lets an application ask whether a data subject has consented to a named purpose. It is for processing whose lawful basis is consent. Consent is one of several lawful bases under the Nigeria Data Protection Act 2023; this surface does not decide which basis applies to any processing, and an application MUST NOT treat its absence as a finding for processing that rests on another basis.
>
> **8.2 Provider.** The application supplies a consent provider. The adapter MUST NOT store, cache or copy consent state. The provider answers with one of `granted` or `denied`. A provider MAY answer `unknown` when it cannot reach its own store. A provider SHOULD answer `denied` when it holds no record for the subject and purpose, because consent that cannot be shown was not given.
>
> **8.3 Result.** The check returns exactly one of `granted`, `denied` or `unknown`. The adapter MUST return `unknown` when no provider is registered, when the provider raises an error, or when the provider returns a value that is not one of the three states. The adapter MUST NOT return `granted` in any other case.
>
> **8.4 Caller duty.** A caller MUST treat `unknown` as not granted. Each adapter MUST offer `isGranted(subjectId, purpose)`, which returns true only when the result is `granted`.
>
> **8.5 Arguments.** `subjectId` is an opaque, non-empty string chosen by the application. Applications SHOULD use an internal surrogate identifier, not a BVN, NIN, telephone number or email address. `purpose` MUST match the grammar `^[a-z][a-z0-9-]{0,31}(\.[a-z][a-z0-9-]{0,31}){1,4}$` (two to five dot-separated segments of lower-case ASCII, at most 160 characters), for example `acme.marketing-sms`. An empty `subjectId` or a `purpose` that does not match the grammar is a programming error: the adapter MUST raise the stack's argument error immediately and MUST NOT call the provider.
>
> **8.6 Errors, cancellation and time.** When the provider raises an error, the adapter MUST return `unknown` and MUST write one diagnostic log event containing the purpose and the error's type name only. It MUST NOT log the error message or the subject identifier, because either may contain personal data. Cancellation by the caller is not an answer: the adapter MUST propagate cancellation rather than convert it to `unknown`. The adapter MUST NOT add timeouts or retries; those belong to the provider.
>
> **8.7 Independence from redaction.** Consent state MUST NOT influence classification, redaction or response filtering. The classification context (§3) carries no subject and no purpose.
>
> **8.8 Concurrency.** The consent check is stateless and safe to call from many threads at once. The provider MUST be safe to call concurrently; the adapter documentation says so.
>
> **8.9 Marker.** A call to `check` or `isGranted` is the marker that later rule packs may look for. Version 1 defines no annotation, attribute or decorator for consent.

#### 3.1.2 Per-stack shapes for the consent check (§12)

Spring MVC is blocking, so the JVM shape is blocking. Django is mostly synchronous and FastAPI asynchronous, so Python offers both.

| Stack | State type | Provider | Check |
|---|---|---|---|
| .NET | `enum ConsentState { Granted, Denied, Unknown }` | `IConsentProvider.GetStateAsync(string subjectId, string purpose, CancellationToken cancellationToken) → ValueTask<ConsentState>` | `ConsentCheck.CheckAsync(...)` and `ConsentCheck.IsGrantedAsync(...)`, registered through dependency injection |
| Node/TypeScript | `type ConsentState = 'granted' \| 'denied' \| 'unknown'` | `interface ConsentProvider { getState(subjectId: string, purpose: string, signal?: AbortSignal): Promise<ConsentState> }` | `consent.check(...)` and `consent.isGranted(...)` |
| Kotlin/JVM | `enum class ConsentState { GRANTED, DENIED, UNKNOWN }` | `fun interface ConsentProvider { fun getState(subjectId: String, purpose: String): ConsentState }` | `ConsentCheck.check(...)` and `ConsentCheck.isGranted(...)`, usable from Java |
| Python | `class ConsentState(enum.Enum)` | `ConsentProvider` and `AsyncConsentProvider` protocols (`get_state` and `async get_state`) | `check` / `is_granted` and `acheck` / `ais_granted` |

### 3.2 IQ-33, decided by the owner on 29 October 2026

Questions 7, 8 and 9 are closed. The specification writes the following as normative text.

- Free packs are embedded in the free installation. The embedded pack is the pack that shipped with that release.
- Paid packs are embedded in the paid installation media. The embedded pack is the pack that shipped with that release.
- The application may pass a path to a newer pack. An older pack, or a pack of the same version, is not this path. The work package already requires the signature to be verified when the pack is loaded.
- The adapter does not verify a licence, and it does not fetch a pack. A paid pack is present because it was embedded in the paid installation media, not because the adapter evaluated an entitlement at runtime.

The owner did not require the newer pack to be the same kind as the embedded pack. Do not add that restriction, and do not add a licence check on the path, unless the owner adds either one later.

### Settled by the work package (not a question)

Three statements are already required. They are written as normative text. They are not asked again.

- There is no runtime dependency on the command-line tool (IQ-09). Shared behaviour travels as rule packs and is proved by conformance vectors.
- There is no network access and no telemetry.
- When a pack fails verification, redaction fails closed: redact more, never less. The adapter does not load the unverified pack, and it does not run with zero patterns. It does not fall back to a pack that would redact less than a pack that has already passed verification. If the pack that failed was only a candidate, the verified pack already in use stays in use. If no verified pack is available, the adapter refuses to start. An option that loads a weaker pack is out of scope.

IQ-33 does not reopen the fail-closed sentence. Section 3.2 is the pack-delivery decision. Fail-closed behaviour still applies when the embedded pack or the pack at the path fails verification.

### 3.3 Redaction signature and scope (question 10 and IQ-76, decided 29 October 2026)

- **Question 10:** keep `Redactor.redact(text) → text`, with no context argument. Context stays on `classify` and on the response filter. A context argument on `redact` could suppress a match, which would redact less than the work package's signature.
- **IQ-76:** severity is a CI concept; `redact` applies **every** rule in the loaded pack, whatever its severity. A rule may be switched off for redaction only if the **rule pack** marks it `redaction.optional = true` (a rule-pack field defined by the rule-pack specification, not here). Switching off is start-up configuration by rule identifier, and the adapter logs the list of switched-off rule identifiers once at start-up. No other way to reduce redaction exists.

## 4. Specification outline

Fifteen sections. Each entry gives the purpose, the normative content to write, and how a reviewer knows it is finished. Section numbers here are the plan's; the document itself may renumber once written.

**§0 Document status.** Draft banner naming the pending decisions, the approval block for the owner to sign, normative keywords (MUST, MUST NOT, SHOULD) defined once, and a change log. *Done when* the banner is accurate and no approval is claimed.

**§1 Scope and non-goals.** In scope: redaction, classification, response filtering, consent check, pack loading. Out of scope: request gating, deep scanning, dashboards, evidence writing, licence enforcement. *Done when* a reader can tell from this section alone that nothing else in Gate-Keeper is described here.

**§2 Relationship to the rest of the product.** Rule packs are data (rule 9). Behaviour parity with the command-line tool is proved by conformance vectors, not by calling the tool (IQ-09). No network access anywhere (rule 5). *Done when* the three claims are stated as normative statements, not as prose asides.

**§3 Common model.** A language-neutral set of types with a field table: `Finding` (rule identifier, category, start, length, severity), `Classification` (a finding or none), `Context` (surface, field name — no subject and no purpose, per IQ-32), `Mask`, `Subject`, `Purpose`, and one error type per language mapping. Two rules belong here in bold: a `Finding` **MUST NOT** carry the matched text (rule 10), and offsets are counted in Unicode scalar values, with each stack's mapping to native string indices stated. *Done when* every type has a field table and an explicit exclusion list.

**§4 Encoding, offsets and determinism.** UTF-8 input, no byte-order mark assumptions, scalar-value offsets, leftmost-longest resolution for overlapping matches, ordinal comparison and invariant casing (rule 6). *Done when* two adapters reading this section would produce byte-identical masks for the same input.

**§5 Redaction.** `redact(text) → text`, which is the work package signature, with no context argument (question 10). Every rule in the pack applies; only rules the pack marks `redaction.optional` may be switched off, as section 3.3 states (IQ-76). Mask format `[REDACTED:<rule-id>]`, illustrated by `[REDACTED:pii.bvn]`, and why it is stable; idempotence (redacting an already-redacted string changes nothing); what happens when the input legitimately contains a literal mask-shaped string; oversized input behaviour; the per-call latency budget. *Done when* the mask is unambiguous enough to be asserted in a conformance vector.

**§6 Classification.** `classify(value, context) → finding | none`; which context fields may influence the decision and which may not; "no match" is an ordinary result and never an exception. *Done when* a reader knows exactly which inputs change the outcome.

**§7 Response filtering.** The framework hook. The field-path syntax (decide between dot-path and JSON pointer and state it once), behaviour for absent fields, arrays, unknown types and non-JSON content types, streaming responses, and the prohibition on unbounded buffering (rule 28). *Done when* a reviewer can predict what happens to a streaming binary response.

**§8 Consent check.** Write the normative text in section 3.1.1: purpose of the surface, provider, result, caller duty, arguments, errors and cancellation, independence from redaction, concurrency, and the marker. Do not define the engine rule that detects a missing check, and do not add an annotation. *Done when* every statement in section 3.1.1 appears as a MUST or SHOULD.

**§9 Pack loading.** Write the decision in section 3.2. Free packs are embedded in the free installation. Paid packs are embedded in the paid installation media. An optional path loads a newer pack and rejects an older pack or the same version. Validation at load. Signature verification is required and the signature format is delegated to the rule-pack specification. The adapter does not verify a licence and does not fetch a pack. Version reporting for NFR-031. Verification failure is stated in §11. *Done when* a reader can tell which pack a free installation embeds, which pack the paid installation media embeds, and what a path is allowed to load.

**§10 Cross-cutting requirements.** Zero egress spelled out as a list of forbidden behaviours (no update check, no telemetry, no crash reporting, no remote pack fetch, no remote logging); no personal data in errors, logs or examples; immutability and thread-safety; latency budget; version stamps; stable public API; ahead-of-time compilation and trim obligations for .NET; the dependency rule (nothing new without rule-8 approval).

**§11 Fail-closed policy.** One table, every failure mode, the required behaviour, and the rationale. The pack-verification row quotes the work package: fail closed for redaction, redact more, never less. That row refuses the unverified pack. If a verified pack is already in use, it stays in use. If no verified pack is available, the adapter refuses to start. The row does not offer a fallback that redacts less than a pack that has already passed verification. Other rows: pack absent; pack unreadable; pack version newer than the adapter understands; no consent provider registered (`unknown`, one start-up warning); provider throws (`unknown`, per 8.6); provider returns `unknown`; input is not valid text; input exceeds the size limit; unknown content type; adapter constructed without a pack. Together with the verification row, that is eleven rows. This is the section the owner should read first, because a wrong row is a compliance failure.

**§12 Per-stack shapes.** Four subsections. Each one uses the launch set below: package name, runtime, web framework, logging integration, registration idiom, the consent shapes in section 3.1.2 with a consent-provider example, and an eight-to-fifteen line example. IQ-31 records this set as owner direction, pending interview validation. The specification says that, and it does not add a framework or a logging library that is not in the table. The examples must use synthetic identifiers generated by the documented corpus script (rule 10) — a made-up account number is fine, a plausible real one is not. Naming an integration below describes the hook the example must show. It does not approve a new package. The adapter cards (P3-02 to P3-05) stop under rule 8 if a named package is not already approved.

| Stack | Package | Runtime | Web | Logging |
|---|---|---|---|---|
| .NET | `InvenioTech.GateKeeper.*` | .NET 10 | ASP.NET Core: one middleware, an endpoint filter for minimal APIs, and a result filter for MVC controllers | `Microsoft.Extensions.Logging` through the `Microsoft.Extensions.Compliance.Redaction` abstractions, and Serilog (an enricher or destructuring policy) |
| Node/TypeScript | `@inveniotech/gate-keeper-*` | Every Node LTS line under upstream support when the middleware major is released (IQ-79); at the time of writing Node 22, 24 and 26 | Express middleware and a NestJS interceptor | pino and winston |
| Kotlin/JVM | `africa.gatekeeper:gate-keeper-*` | Java 15 bytecode for the core and logging modules, tested on Java 17, 21 and 25; the Spring Boot module targets Java 17 (IQ-78) | Spring Boot 3 and 4 auto-configuration (a servlet filter and `ResponseBodyAdvice`, using `jakarta.servlet`). The public API must be usable from Java. | Logback and Log4j2 |
| Python | `inveniotech-gate-keeper` | Every Python version under upstream support when the middleware major is released (IQ-79); at the time of writing 3.11 to 3.15 | Django middleware and FastAPI (Starlette) middleware | the standard-library `logging` filter |

*Done when* each subsection names only the row above, and each example is under fifteen lines and uses synthetic data.

**§13 Conformance obligations.** Every MUST in §3 to §11 maps to at least one vector family: redaction, classification, consent, pack failure. The consent family has two kinds of case, both expressible as data: purpose-grammar cases (accepted and rejected strings) and state-mapping cases (provider returns each state, returns an invalid value, or throws, with the expected result). The redaction family includes a case showing that a rule not marked `redaction.optional` cannot be switched off. The pack-failure family must include a case that shows a failed verification does not redact less than a pack that has already passed verification. State that the specification, not the adapter, is the source of truth, and that an adapter must not add behaviour the vectors cannot express.

**§14 Compatibility and versions.** Specification version 1, adapter semantic versioning, the pack version pinned per release, and the deprecation policy.

**§15 Open questions and deferred items.** Every deferral written down with its identifier and the decision that closed it. IQ-33 is recorded here as decided on 29 October 2026, with the text in section 3.2. IQ-32, question 10 and IQ-76 are recorded as decided on 29 October 2026. IQ-54 (Maven coordinates) is recorded as decided on 29 October 2026.

## 5. Execution steps

Each step is one commit, all on one branch, all in one pull request.

| Step | Work | Commit message | Gate |
|---|---|---|---|
| 0 | Read section 3; no questions are outstanding | — | Questions 1 to 10 and IQ-54 are closed |
| 1 | Branch, create `docs/specs/`, write §0 to §2 | `docs(p3-01): add the middleware API contract skeleton` | Status banner is honest |
| 2 | §3 and §4, the common model and encoding rules | `docs(p3-01): define the common classification model` | No type carries matched text |
| 3 | §5 to §7, the three text operations, with `redact(text) → text` | `docs(p3-01): specify redaction, classification and response filtering` | Mask format is assertable, redaction has no context argument, and only `redaction.optional` rules can be switched off |
| 4 | §9 to §11, pack loading from section 3.2, cross-cutting rules and the fail-closed table | `docs(p3-01): add pack loading and fail-closed policy` | Free and paid embedding are both stated, the path loads a newer pack only, eleven fail-closed rows, and the verification row redacts more, never less |
| 5 | §12 for .NET | `docs(p3-01): add the .NET adapter shape` | ASP.NET Core, `Microsoft.Extensions.Logging` and Serilog only; example under fifteen lines |
| 6 | §12 for Node/TypeScript | `docs(p3-01): add the Node adapter shape` | Express, NestJS, pino and winston only; example under fifteen lines |
| 7 | §12 for Kotlin/JVM | `docs(p3-01): add the JVM adapter shape` | Spring Boot 3 and 4, Logback and Log4j2 only; Maven coordinates `africa.gatekeeper:gate-keeper-*` (IQ-54); example under fifteen lines |
| 8 | §12 for Python | `docs(p3-01): add the Python adapter shape` | Django, FastAPI and standard-library logging only; example under fifteen lines |
| 9 | §13 to §15, conformance, versions, open questions | `docs(p3-01): add conformance obligations and versioning` | Every MUST has a vector family |
| 10 | Self-review against the checklist in section 6; open the pull request as draft | — | Checklist complete |
| 11 | §8 from section 3.1.1, then ask for approval | `docs(p3-01): specify the consent check` | §8 states every requirement in section 3.1.1; no annotation and no detection rule |

Expect two review rounds, not one: one for the document as a whole, and one for the legal-adjacent sentence in §8.1.

## 6. Acceptance checklist

- [ ] The file is at `docs/specs/middleware-api-v1.md`.
- [ ] The document says it is a draft, states that no questions are open, and claims no approval.
- [ ] Each of the five operations has a signature in the common model, normative behaviour, failure modes, and at least one idiomatic example per stack.
- [ ] Redaction is specified as `redact(text) → text`, illustrated by `[REDACTED:pii.bvn]`, with no context argument; every rule applies, and only rules the pack marks `redaction.optional` may be switched off (IQ-76).
- [ ] The fail-closed table covers all eleven rows in §11 of this plan, and the pack-verification row states: fail closed for redaction, redact more, never less, with no fallback that redacts less than a pack that has already passed verification.
- [ ] Free packs are embedded in the free installation, and paid packs are embedded in the paid installation media. The path loads a newer pack and rejects an older pack or the same version. The adapter verifies the signature and does not verify a licence. No same-kind restriction is added unless the owner has added one.
- [ ] Each per-stack section uses only the launch set in §12 of this plan.
- [ ] Zero egress is stated per stack, as a list of forbidden behaviours rather than an assertion.
- [ ] No type, example, error message or log message contains a matched value; every identifier is synthetic.
- [ ] The command-line tool appears only as a shared-data and conformance relationship, never as a dependency.
- [ ] Glossary conforms: "rule pack" not "ruleset"; "posture" not "compliance score"; "updates plan" not "subscription"; "Gate-Keeper" in prose, `gate-keeper` for packages, `GateKeeper` in .NET names, `gatekeeper` for the binary.
- [ ] Language is en-GB and en-NG: -ise spellings, "licence" as noun and "license" as verb, dates as "28 October 2026", full sentences, acronyms expanded once.
- [ ] Every normative statement is marked MUST or SHOULD.
- [ ] No new dependency is proposed anywhere; any dependency mentioned is already approved. A named logging or web integration is a hook, not a package approval.
- [ ] The consent section states every requirement in section 3.1.1: provider interface, tri-state result, `isGranted`, the purpose grammar, no logging of subject identifiers, independence from redaction, and the call as the marker. It defines no annotation and no engine rule that detects a missing check.
- [ ] The classification context carries no subject and no purpose.
- [ ] Every internal link resolves and no link points at a private repository.

## 7. Local gates available today

There is no CI, so these are run by hand and the result is recorded in the pull request:

```powershell
# Banned terms and name forms (rule 4)
Select-String -Path docs/specs/middleware-api-v1.md -Pattern 'ruleset|compliance score|subscription|Gatekeeper|Gate Keeper|gate_keeper' -CaseSensitive:$false

# American spellings that the en-GB lint will later catch (rule 17)
Select-String -Path docs/specs/middleware-api-v1.md -Pattern 'organiz|recogniz|analyz|behavior|color|license ' -CaseSensitive:$false

# Placeholder text left behind
Select-String -Path docs/specs/middleware-api-v1.md -Pattern 'TODO|TBD|FIXME|XXX'
```

The lints do not exist yet, so a human read of the whole document is still the real gate. Say so in the pull request rather than implying a gate ran.

## 8. Risks

| Risk | Mitigation |
|---|---|
| The document defines rule-pack detail that belongs to the pack specification, creating a second source of truth | §2 defers all pack detail; the pull request is reviewed specifically for this |
| The consent section defines the engine rule that flags a missing check, which is rule-pack data (rule 9), or adds an annotation | Section 3.1 settles both: the call is the marker, there is no annotation, and no detection rule is defined |
| Consent state leaks into classification and reduces redaction | 8.7 and the §3 context table forbid it; reviewers check the context type |
| A failed candidate pack is adopted, or the adapter falls back to a weaker pack and redacts less | The unverified pack is never a baseline. §11 keeps the verified pack already in use, and it forbids any fallback that redacts less than that pack |
| `redact` grows a context argument, or a general switch that turns rules off | §5 writes `redact(text) → text` (question 10) and only the `redaction.optional` switch (IQ-76) |
| Four stacks and one author produce vague per-stack sections, or invent a framework that is not in the launch set | Each example must fit in fifteen lines and must use only the §12 row for that stack |
| The document drifts into describing adapter implementation, which is P3-02 to P3-05 | §1 non-goals list; reviewers reject implementation detail |
| §9 adds a licence check, or refuses a newer paid pack on a free installation | Section 3.2 does not say either of those things. The draft writes the decision and stops |
| Two review rounds are needed but the pull request is opened as ready | Keep it in draft until the owner has reviewed the legal-adjacent sentence in §8.1 |

## 9. Follow-ups, not this card

- Add `docs/specs/README.md` as an index of specifications with a status column.
- Add the banned-terms and en-GB spelling lint as a CI gate; this card cannot verify them mechanically.
- Hand the §13 vector families to P3-00 as its starting set.
- Decide whether the website needs a hand-written middleware guide page (rule 12); if so, a linked pull request in the website repository.
- If interview validation changes the IQ-31 launch set, update §12 in a later card. This card does not make that change.

## 10. Handoff notes

- The card is startable today and nothing in it is blocked. IQ-32, IQ-33, IQ-54, IQ-76, IQ-78 and IQ-79 were decided on 29 October 2026.
- Callers treat `unknown` as not granted, and "no record found" is `denied` (section 3.1). `redact` takes no context argument (question 10).
- Write the fail-closed sentence as the work package states it: redact more, never less. Do not add an option that loads a weaker pack.
- Write pack loading exactly as section 3.2. Free packs are embedded in the free installation. Paid packs are embedded in the paid installation media. The path loads a newer pack. Do not add a licence check, and do not require the newer pack to be the same kind as the embedded pack.
- The launch set in §12 is owner direction pending interview validation. Write that set, and do not add a framework or logging library that is not listed.
- No code, no tests and no CI exist in this repository yet. This card's specification pull request creates `docs/specs/`.
