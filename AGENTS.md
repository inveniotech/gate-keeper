# AGENTS.md — Rules for Implementing Agents

> Approved by the owner on 27 October 2026; Parts B2 and B4 runtime floors amended by the owner on 29 October 2026 (IQ-79). Parts A and B are identical in every Gate-Keeper repository; Part C applies to this repository only. Changes need owner approval.
> Rules 1–18 keep the numbers used in the engineering plan; never renumber — retire a rule by marking it "Withdrawn".

**How to read the rules.** "Must" and "must not" are absolute: breaking one fails review. "Should" means do it unless the card or the owner says otherwise, and explain any deviation in the pull request. When two rules seem to conflict, stop and ask (rule 1).

---

## Part A — Rules for every repository

### A1. Scope and decisions

1. **Spec-first.** You must implement only against a written spec and acceptance tests: the task card, the specification it links to, the ADRs, and the schemas. If the spec is missing, ambiguous or contradicts the code, stop that part of the work, record an `IQ-nn` entry in the open-questions register, mark the card `Blocked (IQ-nn)`, and say so. Never improvise scope.

15. **Agents propose, the owner decides.** ADRs, specifications, rule citations (NDPA sections, GAID articles), pricing and licence wording, terms of sale, and any legal-adjacent text (NFR-009) are human-only. You may draft them; you must never mark them final, accepted or approved.

19. **One card, one repository, one pull request.** A task card changes exactly one repository. Work that spans repositories is split into linked cards. Branch name: `wp/<wp-id>-<card-no>-<short-slug>` (for example `wp/p1-06-02-nuban-validator`).

20. **Read before you write.** Before changing a module you must read its `README.md`, the spec section named in the card, and the existing tests for the code you will touch. Follow the patterns already in the module. If you believe a pattern is wrong, say so in the pull request; do not quietly introduce a second one.

21. **One way to do each thing.** Reuse existing abstractions (the clock, the signature verifier, the file enumerator, the error type, the output writers). You must not add a parallel implementation, a new architectural pattern, a new layer or a new framework without an owner-approved ADR.

34. **Stay inside the card.** No drive-by refactoring, renaming, reformatting or "while I was here" fixes. If you find something worth fixing, list it under "Follow-ups" in the pull request.

### A2. Tests and quality

2. **Failing test first.** No production code without a test that fails before the change and passes after it. For detection work, add corpus cases (positives and near-misses) **before** changing a rule. Bug fixes start with a test that reproduces the bug.

26. **Tests must be trustworthy.**
    - Deterministic: no real clock (inject it), no network, no sleeps or timing assumptions, no dependence on test order, file-system order or machine locale.
    - Named for the behaviour they prove (`Rejects_exception_without_justification`), one behaviour per test.
    - Assertions must check outcomes precisely; a test that would still pass if the code were deleted is not a test. Snapshot tests only for genuinely large outputs, and the snapshot is reviewed like code.
    - Coverage targets are a floor, not a goal: ≥ 80 % on the engine and the licence verifier (NFR-026).

35. **Never cheat a gate.** You must not skip, delete, weaken or mark as flaky a test, lint rule, analyser warning or CI check to make a build pass. You must not add suppressions (`#pragma warning disable`, `// eslint-disable`, `@Suppress`, `# noqa`, `[UnconditionalSuppressMessage]`) unless the card explicitly allows it, with a comment citing why. If you try the same approach twice and it still fails, stop and report what you tried instead of thrashing.

27. **Respect performance budgets.** CLI scan ≤ 60 s for 10,000 files and ≤ 10 ms median / ≤ 50 ms 95th-percentile per file (NFR-010/011); added CI time ≤ 30 s (NFR-012); licence verification ≤ 10 ms (NFR-002); dashboard posture page ≤ 2 s (NFR-014). A change that slows the benchmark by more than 20 % fails review unless the card accepts it. Avoid allocations, LINQ and regex compilation inside per-file hot paths.

33. **Self-review before opening the pull request.** Run every local gate (build, tests, lint, format, AOT publish where relevant, spelling, banned terms), then walk the review checklist in the execution model. Fix what you find before asking for review.

### A3. Traceability, honesty and handoff

3. **Trace everything.** Every pull request cites its work package and card, and the `UC-` / `F-` / `NFR-` / ADR / `IQ-` identifiers it implements. Commit messages use Conventional Commits with the work package in the scope: `feat(p1-06): add NUBAN check-digit validator`. No orphan code.

16. **Report honestly.** Skipped tests, TODOs, known gaps, assumptions you made and anything you could not verify must be listed in the pull request. Hidden debt is the one unforgivable sin in a compliance product. Never claim a test passed if you did not run it.

36. **Leave a handoff.** Every pull request ends with a **Handoff** section: what is done, what is not, decisions taken and why, open questions, and suggested follow-ups — written so the next session can continue without reading the conversation that produced the change.

### A4. Product principles enforced in code

5. **Zero egress.** No outbound network code anywhere in the CLI, the engine, the middleware or the licence verifier — no telemetry, analytics, crash reporting, update checks or remote logging. The **CLI contains no network code at all**; evidence uploads are done by CI pipeline steps (IQ-27). The only network code permitted in any distributed component is the dashboard's ingestion module reading customer-configured sources. Any new network-touching code needs an owner-approved ADR first.

6. **Determinism.** Same inputs (files, packs, configuration, `evaluatedOn` date) must give byte-identical output. No wall-clock times in outputs; sort everything that is emitted (paths ordinal, then line, column, rule ID); no unordered collections or dictionary iteration order in output; JSON property bags written in sorted key order.

22. **Time, culture and text.** Get time only from the injected clock (`TimeProvider` in .NET, an injected clock elsewhere); dates in UTC, ISO 8601. Use invariant culture and ordinal string comparison for anything that is not user-facing prose. Write files as UTF-8 without byte-order mark, with LF line endings. Paths in any output use forward slashes and are relative to the repository root.

9. **Rules are data.** Detection logic changes ship as rule-pack JSON with an NDPA/GAID citation and corpus cases — never as engine code (NFR-027). Engine changes are allowed only to add a capability the rule-pack schema defines (for example a named validator), and every such capability needs cross-stack conformance vectors.

10. **PII discipline.** No source code or personal-data content in logs, tests, fixtures, support bundles, issues, pull requests or commit messages — synthetic identifiers only, generated by the documented corpus script (NFR-004). Real identifiers never enter any repository. Never print a matched value; print the rule ID and location. Fingerprints use the per-repository keyed hash (IQ-20); never a plain hash of a matched value.

13. **Exit codes and versioning are law.** CLI exit codes: 0 pass, 1 violations, 2 tool error — nothing else, and never 2 for "violations found". Licence problems never change the exit code. Version stamps (product and pack) on every artefact (NFR-031). New features only in a new major; fixes and tweaks in minor/patch releases (R-7).

24. **Public contracts are stable.** The rule-pack schema, results formats (SARIF profile and GK evidence JSON), configuration and exceptions files, the licence-token format and every public API are contracts. You must not change them incompatibly outside a new major version, and every change updates its JSON Schema, its spec and its version field in the same pull request.

23. **Errors are for humans.** Every user-facing error says what happened, why it matters, and what to do next, in plain en-GB, naming the file and line where relevant. Tool errors exit with code 2. Never swallow an exception silently; never show a stack trace unless `--verbose` is set. A missing, invalid or expired licence is never an error — it degrades with a message.

30. **Platform scope.** Supported: Windows and Linux, x64 and arm64 only. Code must work on both operating systems: no hard-coded path separators, no reliance on case-sensitive or case-insensitive file systems, no OS-specific API without implementations and tests for both. Do not add macOS-specific code or build targets.

### A5. Security and dependencies

14. **Security.** No secrets, tokens or keys in code, tests, CI logs or pull requests. Licence tokens are masked wherever displayed. Any change to signature-verification code requires the full test-vector run (RFC 8032 vectors plus licence vectors: tampered, expired, wrong major, missing). You must never handle, generate, request or print production signing keys; only the clearly labelled development keys are used in tests.

28. **Treat every input as hostile.** Rule packs, configuration, exceptions files, results files, licence files and anything read from a sink must be validated before use. Guard against path traversal (globs, support-bundle archives), oversized inputs, deeply nested JSON/TOML, and catastrophic regular expressions (use `NonBacktracking` and timeouts in .NET; keep pack patterns inside the portable subset).

8. **No new dependencies without owner approval.** Each proposed dependency needs: licence (MIT, Apache-2.0, BSD or ISC only; nothing copyleft in anything we ship), maintenance health, size impact, AOT compatibility for .NET code in the CLI or libraries, and a one-line reason. Already approved: `BouncyCastle.Cryptography` (Ed25519), `Tomlyn` (TOML), `SarifKit` (SARIF), `TerraPDF` (CAR PDF rendering, private repository only). Lock files are committed; versions are pinned.

7. **AOT-safe code (all .NET in the CLI, engine and libraries).** No runtime code generation, no `Assembly.Load`, no reflection-based serialisation; System.Text.Json and Tomlyn source generators only. Every change publishes with `PublishAot` and `PublishTrimmed` with **zero** trim/AOT warnings.

### A6. Language, naming and documentation

4. **Glossary conformance.** Doc 15's vocabulary and banned terms apply to identifiers, docs, UI copy and messages (NFR-028 lints it): "rule pack" never "ruleset"; "posture" never "compliance score"; "updates plan" never "subscription"; the binary is `gatekeeper-ndpa-check`. **Name forms:** "Gate-Keeper" in prose; `gate-keeper` for repositories, packages and images; `GateKeeper` in .NET namespaces and type names; `gatekeeper` for domains, the binary and the licence product ID; never "Gatekeeper", "Gate Keeper" or "gate_keeper" (Doc 15).

17. **Language: en-NG / en-GB.** All prose you produce — docs, READMEs, comments, commit messages, UI copy, error messages — uses Nigerian/British English: -ise spellings, "licence" as the noun and "license" as the verb, dates as "27 October 2026". Code identifiers follow each stack's idiom and are exempt.

18. **No compressed prose.** Full sentences; acronyms expanded on first use; reasoning spelled out rather than referenced. A developer new to NDPA compliance must understand the docs on one reading. Code comments are concise complete sentences that explain *why*, not *what*.

32. **Document public surfaces.** Every public type and member has API documentation (XML docs, TSDoc, KDoc, docstrings) stating behaviour, errors and examples where useful. Module READMEs (NFR-029) describe purpose, public contract, dependencies and how to test, and change in the same pull request as the module.

12. **Docs or it didn't happen.** A user-visible behaviour change ships with its documentation. Reference material (rule pages, CLI reference, schemas) is generated from data in the public repository and must be updated in the same pull request; hand-written guides live in the website repository and are updated in a **linked** website pull request. A release is blocked until linked docs pull requests are merged. Never hand-edit generated files (`docs-data`, conformance vectors, lock files, generated pages) — regenerate them.

### A7. Size and review

11. **Small vertical slices.** One pull request = one testable behaviour, reviewable in minutes. Aim for under about 400 changed lines excluding tests, corpus data and generated files; split anything bigger into more cards. No cross-module rewrites.

29. **Respect repository boundaries.** The public repository must never reference, import or describe anything from the private repositories. Never paste private context (paid designs, runbooks, customer information, commercial policies) into public work, issues or pull requests — anything you write in the public repository may become public.

31. **Output conventions.** CLI results go to standard output and diagnostics to standard error; honour `NO_COLOR`; output must read correctly without colour and in screen readers (NFR-025). The dashboard meets WCAG 2.1 AA (NFR-024).

25. **Generated code and data are reproducible.** Every generated file has a documented command that regenerates it, and CI fails if regeneration produces a diff.

---

## Part B — Stack rules

### B1. C# / .NET (CLI, engine, verifier, .NET middleware, dashboard back end)

- .NET 10; nullable reference types enabled; warnings as errors; file-scoped namespaces; `sealed` by default; records or immutable types for data.
- Async all the way with `CancellationToken` parameters; no `.Result` or `.Wait()`; no `async void`.
- No static mutable state; no service locator; dependencies through constructors.
- Use `TimeProvider` for time, `System.IO.Abstractions`-style seams only where the module already uses them (rule 21).
- Logging through `ILogger` with message templates; never interpolate personal data into log messages.
- Tests use the framework set up in the repository scaffold; do not introduce another.

### B2. TypeScript / Node.js (middleware, GitHub Action)

- Runtime floor: the middleware supports every Node LTS line under upstream support when each middleware major is released (IQ-79; at the time of writing Node 22, 24 and 26); other Node code, such as the GitHub Action, uses Node 26 or later. TypeScript `strict`; no `any` (use `unknown` and narrow); ESM first with a CommonJS build for the middleware.
- Zero runtime dependencies in the middleware unless approved (rule 8); Ed25519 via the built-in `crypto` module.
- Errors are typed `Error` subclasses; never throw strings.

### B3. Kotlin / JVM (middleware)

- Compile to Java 15 bytecode; test on Java 17, 21 and 25.
- Kotlin explicit API mode; the public API must be comfortable from Java: no exposed coroutines, `@JvmStatic` / `@JvmOverloads` where useful, no Kotlin-only types in signatures.
- Ed25519 via the JDK (`Signature.getInstance("Ed25519")`); no Bouncy Castle.

### B4. Python (middleware)

- Runtime floor: every Python version under upstream support when each middleware major is released (IQ-79; at the time of writing 3.11 to 3.15); use no syntax newer than the oldest supported version. Full type hints with `py.typed`; type checker in strict mode; linter and formatter as configured in the repository.
- No mutable default arguments; no bare `except:`; no `print` for diagnostics — use `logging`.

### B5. Angular (dashboard)

- Angular 22, following the conventions in P6-14 exactly: standalone components and lazy routes, zoneless with OnPush, signals for all state, signal inputs and queries, `httpResource()` for reads, Signal Forms for editing, built-in control flow with `track`, `@defer` for heavy content, `inject()`, no NgModules and no NgRx.
- Bundle budgets in `angular.json` must pass; no new library without rule-8 approval.
- Every component has accessibility checks (axe) in its tests.

---

## Part C — This repository

### C1. Public monorepo (`gate-keeper`) only

- Everything here is public. Write as if a customer, a competitor and the regulator will read it.
- Rule-pack changes: corpus cases first, citation from the approved citation register only (never invent or paraphrase a legal citation), false-positive harness green (≤ 5 %, NFR-013).
- Detection rules and middleware must behave identically: any change to a named validator or matcher capability regenerates the cross-stack conformance vectors and passes in all four languages.
- The zero-egress container test (deny-all network, byte-identical output) must pass on every pull request.

---

## Human-only — agents never do these

- Accept or finalise ADRs, specifications, citations, legal or pricing text.
- Merge pull requests, publish releases, push tags, or publish packages.
- Generate, use or rotate production keys; issue licences.
- Change CI gates, coverage thresholds, budgets or this file.
- Contact customers, DPCOs or third parties.
