# Card P3-01: Middleware API contract specification

- **Work package:** P3-01 Middleware API contract specification
- **Mode:** AI-DRAFT — an agent drafts, the owner approves (rule 15)
- **Size:** S
- **Branch:** `wp/p3-01-01-middleware-api-contract`
- **Pull request title:** `docs(p3-01): add the middleware API contract specification`
- **Traces:** R-1 (middleware is thin helpers only), F-005 and UC-003 (consent helper), IQ-09 (decided), IQ-32 and IQ-33 (undecided), NFR-031 (version stamps), NFR-028 (glossary), rules 5, 6, 8, 10, 17, 18, 22, 24
- **Spec produced:** `docs/specs/middleware-api-v1.md`
- **Context pack:** `AGENTS.md`; this card; the middleware phase detail (P3-01 to P3-05); the open-questions entries for IQ-09, IQ-31, IQ-32 and IQ-33; Doc 15 vocabulary

---

## 1. What "done" means

1. `docs/specs/middleware-api-v1.md` exists and is self-contained: a developer who has read nothing else can build an adapter from it.
2. It states one common, language-neutral surface, then the idiomatic shape of that surface in .NET, Node/TypeScript, Kotlin/JVM and Python, each with examples that use synthetic identifiers only.
3. The owner has answered IQ-32 and IQ-33, and the answers are written into the document rather than left as recommendations.
4. The owner has marked the document accepted. **An agent must never write "Approved", "Accepted" or "Final" into it, and must never merge it.**

## 2. Starting position

The repository currently contains only `AGENTS.md`, `CLAUDE.md` and `.gitattributes`. There is no `docs/`, no `src/`, no test suite and no CI. Four consequences for this card:

- `docs/specs/` is being created by this pull request. Nothing else lives there yet, so nothing constrains the document's internal structure except this plan.
- The rule-pack specification does not exist yet. This document must therefore **not** define pack fields, pack versions or signature encoding. It names the rule pack as an input and defers every detail to the rule-pack specification. Writing pack detail here would create a second source of truth, which rule 24 forbids.
- There are no lint gates, no spelling gate and no banned-terms checker in CI yet. Section 7 lists the manual checks that substitute for them, and section 9 lists the follow-up that fixes the gap permanently.
- Nothing in this repository may describe or link to a private repository (rule 29). IQ identifiers are safe to use because the card that carries them is public; a register location is not, and must not be written down.

Decision status at the time of writing:

| Question | Status | What it settles |
|---|---|---|
| IQ-09 | Decided — no CLI runtime dependency | Can be written as a normative statement today |
| IQ-31 | Owner direction, pending interview validation | Supplies the framework and logging matrix for the per-stack sections |
| IQ-32 | **Undecided** — recommendation only | The consent surface: provider interface, tri-state semantics, detectable marker |
| IQ-33 | **Undecided** — recommendation only | Pack delivery: embedded, path override, plan-gated packs, failure behaviour |

## 3. Block the card, then split the work

Rule 1 applies: the consent and pack-delivery parts of the specification cannot be written against a decision that does not exist, so they must stop. That does not block the other nine tenths of the document, and stalling the whole card would waste a week. The plan is therefore:

1. Send one message to the owner containing the nine questions in section 3.1 and 3.2 below, each with a recommended answer and the consequence of the alternative. Ask for the answers as written decisions.
2. Mark the card `Blocked (IQ-32, IQ-33)` and say so in the pull request description, while drafting the sections that do not depend on them.
3. Draft everything except the consent section and the plan-gated part of the pack section. Leave both as a numbered stub that lists the pending question and states plainly that the section is incomplete.
4. Keep the pull request in draft until the owner answers. If the answers arrive mid-draft, they become two ordinary commits on the same branch; the card does not become two cards.
5. If the owner defers rather than decides, the specification states the deferral explicitly and the adapters (P3-02 to P3-05) must not implement the deferred part. A deferral is a decision and is written down as one.

### 3.1 Questions for IQ-32 (consent)

| # | Question | Recommendation |
|---|---|---|
| 1 | Is the helper a provider interface the application implements, or a free function the adapter owns? | An interface the application implements. The adapter must never hold consent state. |
| 2 | Is the result exactly `granted`, `denied`, `unknown`, and is `unknown` distinct from "no record found"? | Three states, and `unknown` is the answer whenever the adapter cannot tell — provider absent, provider threw, record absent. |
| 3 | What must a caller do when the answer is `unknown`? | Treat it as `denied`. The helper reports; it never grants by omission. |
| 4 | How is a purpose identified — closed enum, namespaced string, or constant? | A namespaced string defined by the application, because purposes are application-specific and this specification must not become a legal taxonomy (rule 15). |
| 5 | Does the check take anything beyond subject and purpose (lawful basis, timestamp, processing activity)? | Only subject and purpose in v1. Anything else widens the contract for all four stacks. |
| 6 | Is a detectable marker part of v1, so the CLI can flag processing paths with no check? | That belongs to the rule pack and the engine rule, not to this specification (rule 9). This specification may state only that calling the helper is the detectable act, if the owner confirms it. |

### 3.2 Questions for IQ-33 (pack delivery)

| # | Question | Recommendation |
|---|---|---|
| 7 | Is the bundled pack embedded in each package and version-locked to the release? | Yes. It removes a deployment step and a failure mode. |
| 8 | Is there an optional path override, and must that pack carry the same provenance as a release pack? | Optional override, yes; same provenance, yes. |
| 9 | Are plan-gated packs supported in middleware for v1, which would put a licence verifier and a licence file in four languages and in production deployments? | No. Out of scope for v1. |
| 10 | When a pack fails verification, does the adapter refuse to start, or fall back to the embedded pack? | Refuse to start by default, with an explicit opt-out that falls back to the embedded pack. Never run with zero patterns. |

Question 10 is the one that decides the shape of the fail-closed table, and it is the single most important answer in this card.

## 4. Specification outline

Fifteen sections. Each entry gives the purpose, the normative content to write, and how a reviewer knows it is finished. Section numbers here are the plan's; the document itself may renumber once written.

**§0 Document status.** Draft banner naming the pending decisions, the approval block for the owner to sign, normative keywords (MUST, MUST NOT, SHOULD) defined once, and a change log. *Done when* the banner is accurate and no approval is claimed.

**§1 Scope and non-goals.** In scope: redaction, classification, response filtering, consent check, pack loading. Out of scope: request gating, deep scanning, dashboards, evidence writing, licence enforcement. *Done when* a reader can tell from this section alone that nothing else in Gate-Keeper is described here.

**§2 Relationship to the rest of the product.** Rule packs are data (rule 9). Behaviour parity with the CLI is proven by conformance vectors, not by calling the CLI (IQ-09). No network access anywhere (rule 5). *Done when* the three claims are stated as normative statements, not as prose asides.

**§3 Common model.** A language-neutral set of types with a field table: `Finding` (rule identifier, category, start, length, severity), `Classification` (a finding or none), `Context` (surface, field name, subject, purpose), `Mask`, `Subject`, `Purpose`, and one error type per language mapping. Two rules belong here in bold: a `Finding` **MUST NOT** carry the matched text (rule 10), and offsets are counted in Unicode scalar values, with each stack's mapping to native string indices stated. *Done when* every type has a field table and an explicit exclusion list.

**§4 Encoding, offsets and determinism.** UTF-8 input, no byte-order mark assumptions, scalar-value offsets, leftmost-longest resolution for overlapping matches, ordinal comparison and invariant casing (rule 6). *Done when* two adapters reading this section would produce byte-identical masks for the same input.

**§5 Redaction.** `redact(text, context) → text`; mask format `[REDACTED:<rule-id>]` and why it is stable; idempotence (redacting an already-redacted string changes nothing); what happens when the input legitimately contains a literal mask-shaped string; oversized input behaviour; the per-call latency budget. *Done when* the mask is unambiguous enough to be asserted in a conformance vector.

**§6 Classification.** `classify(value, context) → finding | none`; which context fields may influence the decision and which may not; "no match" is an ordinary result and never an exception. *Done when* a reader knows exactly which inputs change the outcome.

**§7 Response filtering.** The framework hook. The field-path syntax (decide between dot-path and JSON pointer and state it once), behaviour for absent fields, arrays, unknown types and non-JSON content types, streaming responses, and the prohibition on unbounded buffering (rule 28). *Done when* a reviewer can predict what happens to a streaming binary response.

**§8 Consent check.** Stubbed until IQ-32 is answered. When answered: the provider interface, the tri-state, `unknown` treated as denied, no storage, no caching, exception mapping, thread-safety.

**§9 Pack loading.** Default embedded pack, optional override path, validation at load, signature verification delegated to the rule-pack specification, version reporting for NFR-031, and the plan-gated question stubbed until IQ-33 is answered.

**§10 Cross-cutting requirements.** Zero egress spelled out as a list of forbidden behaviours (no update check, no telemetry, no crash reporting, no remote pack fetch, no remote logging); no personal data in errors, logs or examples; immutability and thread-safety; latency budget; version stamps; stable public API; AOT and trim obligations for .NET; the dependency rule (nothing new without rule-8 approval).

**§11 Fail-closed policy.** One table, every failure mode, the required behaviour, and the rationale. Rows: pack absent; pack unreadable; pack fails signature verification; pack version newer than the adapter understands; provider throws; provider returns `unknown`; input is not valid text; input exceeds the size limit; unknown content type; adapter constructed without a pack. This is the section the owner should read first, because it is where a wrong answer causes a compliance failure rather than a bug.

**§12 Per-stack shapes.** Four subsections, each with package name, entry point, registration idiom, logging integration, web-framework integration, consent provider example, and an eight-to-fifteen line example. The examples must use synthetic identifiers generated by the documented corpus script (rule 10) — a made-up account number is fine, a plausible real one is not.

**§13 Conformance obligations.** Every MUST in §3 to §11 maps to at least one vector family: redaction, classification, consent, pack failure. State that the specification, not the adapter, is the source of truth, and that an adapter must not add behaviour the vectors cannot express.

**§14 Compatibility and versions.** Specification version 1, adapter semantic versioning, the pack version pinned per release, and the deprecation policy.

**§15 Open questions and deferred items.** Every deferral written down with its identifier and the decision that closed it.

## 5. Execution steps

Each step is one commit, all on one branch, all in one pull request.

| Step | Work | Commit message | Gate |
|---|---|---|---|
| 0 | Send the ten questions to the owner; mark the card blocked | — | Owner answers 1–6 and 7–10, or explicitly defers |
| 1 | Branch, create `docs/specs/`, write §0 to §2 | `docs(p3-01): add the middleware API contract skeleton` | Status banner is honest |
| 2 | §3 and §4, the common model and encoding rules | `docs(p3-01): define the common classification model` | No type carries matched text |
| 3 | §5 to §7, the three text operations | `docs(p3-01): specify redaction, classification and response filtering` | Mask format is assertable |
| 4 | §9 to §11, pack loading, cross-cutting rules and the fail-closed table | `docs(p3-01): add pack loading and fail-closed policy` | Every failure mode has a row |
| 5 | §12, four per-stack subsections, one commit each | `docs(p3-01): add the <stack> adapter shape` | Each example is under fifteen lines and uses synthetic data |
| 6 | §13 to §15, conformance, versions, open questions | `docs(p3-01): add conformance obligations and versioning` | Every MUST has a vector family |
| 7 | Self-review against the checklist in section 6; open the pull request as draft | — | Checklist complete |
| 8 | After the owner answers: fill §8 and the pack subsection of §9, update §15, ask for approval | `docs(p3-01): record the consent and pack-delivery decisions` | Owner has answered every question |

Expect two review rounds, not one. Steps 1 to 6 can all happen before the owner answers, which is why the card is worth starting now rather than waiting.

## 6. Acceptance checklist

- [ ] The file is at `docs/specs/middleware-api-v1.md`.
- [ ] The document says it is a draft, names the unanswered questions, and claims no approval.
- [ ] Each of the five operations has a signature in the common model, normative behaviour, failure modes, and at least one idiomatic example per stack.
- [ ] The fail-closed table covers all ten rows in §11 of this plan.
- [ ] Zero egress is stated per stack, as a list of forbidden behaviours rather than an assertion.
- [ ] No type, example, error message or log message contains a matched value; every identifier is synthetic.
- [ ] The CLI appears only as a shared-data and conformance relationship, never as a dependency.
- [ ] Glossary conforms: "rule pack" not "ruleset"; "posture" not "compliance score"; "updates plan" not "subscription"; "Gate-Keeper" in prose, `gate-keeper` for packages, `GateKeeper` in .NET names, `gatekeeper` for the binary.
- [ ] Language is en-GB and en-NG: -ise spellings, "licence" as noun and "license" as verb, dates as "28 October 2026", full sentences, acronyms expanded once.
- [ ] Every normative statement is marked MUST or SHOULD.
- [ ] No new dependency is proposed anywhere; any dependency mentioned is already approved.
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
| The consent section quietly designs the engine's `consent.missing` rule, which is rule-pack data | The marker question is answered by the owner and then lives in the rule pack, not here |
| Four stacks and one author produce vague per-stack sections | Each example must fit in fifteen lines; a vague example does not fit |
| The document drifts into describing adapter implementation, which is P3-02 to P3-05 | §1 non-goals list; reviewers reject implementation detail |
| Two review rounds are needed but the pull request is opened as ready | Keep it in draft until the owner has answered |

## 9. Follow-ups, not this card

- Add `docs/specs/README.md` as an index of specifications with a status column.
- Add the banned-terms and en-GB spelling lint as a CI gate; this card cannot verify them mechanically.
- Hand the §13 vector families to P3-00 as its starting set.
- Decide whether the website needs a hand-written middleware guide page (rule 12); if so, a linked pull request in the website repository.

## 10. Handoff notes

- The card is startable today; only two sections are genuinely blocked.
- The owner's answers to questions 3 and 10 (unknown means denied; refuse to start on a bad pack) change the contract materially, so do not pre-empt them in prose.
- No code, no tests and no CI exist in this repository yet. The first three cards in this phase will each create something the next one needs; this one creates `docs/specs/`.
