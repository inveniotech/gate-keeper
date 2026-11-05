# False-positive policy and performance budgets, version 1

**Status: draft — not accepted.** This specification is an AI draft for the owner to review and approve (rule 15 of `AGENTS.md`). It turns the recorded decisions in section 0.1 into exact definitions, counting rules and measurement methods. Nothing in this document is final, accepted or approved until the owner says so.

This document is the written contract for work package P0-17 ([issue #10](https://github.com/inveniotech/gate-keeper/issues/10)). It does not implement anything: building the golden fixture corpus is P1-02, adding the validation corpus of real public repositories is P2-06, building the measurement harness and the weekly report is P2-07, and running the benchmark jobs is P1-15. Those cards implement exactly what this document defines.

This document changes no budget. Accepting, raising or lowering the budgets themselves is a decision only the owner makes (rule 27 of `AGENTS.md` and the "Human-only" section). What this document defines is how each budget is measured, so that every number published in future means one thing.

---

## 0. Document status

### 0.1 Decision status

Normative text in this specification comes from the recorded decisions listed here. The specification itself stays a draft, because rule 15 reserves final approval of specifications to the owner.

| Identifier | Status | What it settles |
|---|---|---|
| IQ-49 | Decided | The 5 % false-positive gate (NFR-013) is measured on failing findings. Findings reported at `note` sit outside the gate. A randomly generated identifier that happens to match a real one is not a problem in code or tests: the corpus, the unit tests and the conformance vectors may use random synthetic values. Documentation uses obviously fake sequential values instead. |
| IQ-60 | Decided | The `main` build and the candidate build are benchmarked in the same continuous-integration job, and a regression of more than 20 % fails. Machine-independent counts — allocations per file and bytes scanned per regex call — are tracked as well, because they catch regressions that timing noise on hosted runners would hide. |
| IQ-90 | Amended by the owner on 5 November 2026 | The native executable size cap is 10 MiB (10,485,760 bytes) per runtime identifier. |
| IQ-83 | Decided | History scanning is off in the `default` profile and on in `deep` unless `scan.history = false`. History findings are a separate result list; they are not fingerprinted and not baselined. A clean default-profile result does not claim that history was scanned. |

The register entry for each identifier is an owner record. This specification cites the identifier and does not describe where that register is kept. Nothing here restates more of any decision than the decision itself settles.

### 0.2 Normative keywords

The key words **MUST**, **MUST NOT**, **REQUIRED**, **SHALL**, **SHALL NOT**, **SHOULD**, **SHOULD NOT** and **MAY** are to be interpreted as described in RFC 2119. When this document writes "the binary", it means `gatekeeper-ndpa-check`. When it writes "a measurement run", it means one invocation of the binary, or of the measurement harness, under the conditions this document fixes. When it writes "a finding", it means one item in the findings output of a scan, in the shape the results specification ([results-format-v1.md](results-format-v1.md)) defines.

Percentages in this document are exact rational numbers. The gate in section 5 is decided by integer arithmetic, never by rounding and never by floating-point comparison. "10 MB" means 10,485,760 bytes and "256 MB" means 268,435,456 bytes, because the configuration specification already fixes the byte-exact meaning of a megabyte ([config-v1.md](config-v1.md)).

### 0.3 Change log

| Version | Date | Change |
|---|---|---|
| 1 (draft) | 2 November 2026 | First draft. |

### 0.4 Details this draft fixes

The decisions name the gate, the paired benchmark and the profile the 30-second budget is measured on. They do not spell the label set, the arithmetic, the corpus folders, the pinned repository list or the measurement conditions. This draft fixes the following so that an implementer has one reading. Each item is part of the draft for the owner to accept or amend.

1. **What a false positive is, and who labels it.** Section 3. Manual triage is not needed where the corpus can answer on its own: a finding in a positive case that matches the rule's documented purpose is a true positive, and a finding in a near-miss case is a false positive with no triage at all.
2. **The triage label set.** `true-positive`, `false-positive` and `unresolved`. An `unresolved` finding counts as a false positive in every rate until the owner labels it, so that incomplete triage can never work in the product's favour.
3. **"Failing findings" means severity `error`.** The default failure policy is `fail-on = "error"`, and a measurement run carries no exceptions and no baseline, so every reported finding whose severity is `error` is a failing finding (IQ-49). Findings at `warning` and `note` are reported in every measurement report but sit outside the gate in version 1.
4. **Exact arithmetic.** A rate "is more than 5 %" exactly when 20 × fp > tp + fp. There is no rounding and no floating-point comparison anywhere in the gate.
5. **What the rate is computed over.** Per rule and overall, over findings at failing severity from both corpora. Per-repository rates are reported for information only: the gate uses the per-rule rates and the overall rate only.
6. **The measurement-run configuration.** Default profile, history off, no exceptions file, no baseline file, no fingerprint key, and no disabled rules. A rule tagged so that it runs only in `deep` is measured in a separate `deep` run and reported separately; the default-profile measurement never charges the rule for findings it cannot produce (IQ-23, IQ-83).
7. **Where the corpora live.** The golden corpus stays at `tests/corpus/<case-name>/`, as P1-02 fixes. The validation corpus adds `tests/corpus/validation/`, which holds `repos.json` (the pinned repository list) and one triage record per repository. Repository content is never committed.
8. **The pinned repository list.** The initial list in section 4.5, for the owner to confirm or replace before acceptance. The pinned rows for Node/TypeScript and for open-source projects published by Nigerian companies are named at acceptance.
9. **The benchmark repository.** A synthetic tree of 10,000 eligible files, generated deterministically by a committed script with pinned parameters. The generated tree is never committed; the run records the generator version and seed instead.
10. **The CI reference sample.** A synthetic tree of exactly 500 eligible files, total size at most 10 MB, generated by the same script, on which the added-continuous-integration budget (NFR-012) is measured.
11. **The measurement conditions.** Paired runs of `main` and the candidate in the same continuous-integration (CI) job, on the same machine, with one warm-up pair discarded and three pairs measured; every statistic is the median of its three values. A candidate that regresses more than 20 % against `main` on any budget or counter fails. The runners are this repository's standard GitHub-hosted Linux pull-request runners; the report records the image and machine type. The owner confirms the runner size at acceptance.
12. **Per-file time boundaries.** A file's time runs from the moment the enumeration hands the file's read bytes to the matching stage until that stage completes. Writing output is not part of the per-file time. Per-file timing is instrumentation inside the same whole-scan run used for NFR-010; no second run is needed.
13. **Counters gate as well as timings.** A regression of more than 20 % on the allocations-per-file counter, or on the bytes-per-regex-call counter, fails the benchmark job too. IQ-60 decided the counters are tracked; this draft additionally decides that they gate. The owner can reject that addition and keep the counters reported-only.
14. **The weekly report location.** `docs/reports/`, with the report's UTC evaluation date in the file name. The same report is attached to release notes. The report date comes from the continuous-integration run metadata: a report is a repository artefact, not product output, so rule 6's ban on wall-clock times in outputs is not at stake.

---

## 1. Scope and non-goals

### 1.1 In scope

The definition of a false positive and the triage rules that label findings; the composition of the two corpora; the exact computation of the 5 % rate (NFR-013); the measurement method, conditions and artefacts for NFR-010, NFR-011 and NFR-012; and the content of the reports that P2-06, P2-07 and P1-15 produce.

### 1.2 Not in scope

- The severity ladder, the `fail-on` semantics and the scan profiles. The configuration specification fixes those (IQ-22, IQ-23); this document only fixes which severities the gate counts.
- The other budgets. Licence verification (NFR-002) and the dashboard posture page (NFR-014) are measured by their own work; this document does not touch them.
- The harness implementation, the corpus cases and the benchmark jobs. P1-02, P2-06, P2-07 and P1-15 implement this document; they do not redefine it.
- Any change to a budget value, to the 20 % tolerance, or to the continuous-integration gates. Owner-only (rule 27 and the "Human-only" section of `AGENTS.md`).
- Marking this document accepted.

---

## 2. The three things this document makes measurable

1. **Trust (NFR-013).** A finding that should not exist costs a customer a build break, lost triage time or credibility. The gate is ≤ 5 % false positives on the validation corpus, measured on failing findings (IQ-49).
2. **Throughput (NFR-010 and NFR-011).** A full scan of 10,000 files finishes within 60 seconds (NFR-010), and the per-file medians and percentile stay ≤ 10 ms and ≤ 50 ms respectively (NFR-011).
3. **Continuous-integration cost (NFR-012).** The scan adds at most 30 seconds to a default-profile continuous-integration job. The configuration specification already fixes that a default-profile scan with history left off is the scan this budget is measured on, and that `deep` and history runs are outside it (IQ-83).

Each number is defined once, in this document, and every artefact that publishes one — the harness output, the weekly report, a release report — computes it exactly as the section names.

---

## 3. What counts as a false positive

### 3.1 Definitions

- **Finding.** One reported detection, exactly as the results format defines it. In a measurement run there are no exceptions, no baseline and no disabled rules, so every reported finding is live.
- **True positive.** A finding the human triage accepts: the rule found the pattern it was written to find, at a location where the rule's citation and description truly apply, at the right severity for that context.
- **False positive.** A finding the human triage rejects: the matched content is not what the rule's citation and description cover, or the location is a place the rule was written to leave alone, such as a documentation example, a test fixture or configuration the rule covers by design.
- **Unresolved.** A finding whose triage could not be settled with confidence. It stays `unresolved` until the owner labels it, and every rate counts it as a false positive until then.
- **Missed detection.** A finding a corpus case expects but the run does not produce. It is the opposite face of the same coin; it is measured through recall (section 5.3), never through NFR-013.

### 3.2 Where no manual triage is needed

The corpus answers most labels on its own:

1. **A finding in a positive case** is a true positive when it matches an entry in that case's `expected.json`. A finding that does not match any expected entry is triaged once by the owner, like any other finding.
2. **A finding in a near-miss or otherwise negative case** is a false positive with no triage. Near-misses exist precisely to make the wrong match visible and cheap; their `expected.json` is empty by design.
3. **A finding in a validation repository that already has a triage record** reuses that record. Re-triage happens only when a rule change alters the outcome at the record's location.
4. **A finding at severity `note`, including catalogue findings**, is outside the gate in version 1 (IQ-49). It appears in the report so its volume is visible, without polluting the trust number customers read.

### 3.3 What is never counted

1. **Findings in a scan that ended with a tool error.** A tool error is exit code 2, it is its own work item, and nothing around it may be counted: an invalid scan proves nothing either way.
2. **Findings from a rule the run deliberately disabled.** A measurement run disables nothing, so this set is empty by construction. If someday a run must disable a rule to be able to finish at all, that run is invalid and is repeated, never published.
3. **A clean scan.** A scan that finds nothing contributes no denominator and no numerator; a rate is never computed from nothing.
4. **History findings.** They are a separate result list (IQ-83), they carry neither fingerprint nor baseline state, and they take part in no rate in this document.

---

## 4. The corpora

### 4.1 The golden corpus (P1-02)

A case is a folder `tests/corpus/<case-name>/` containing a small synthetic repository under `repo/` and an `expected.json` that lists, per rule, file and line, every finding the engine must produce. P1-02 fixes the opening categories, and this document records them together with what each kind proves; the severity of an expected finding is whatever the rule under test carries, because severity belongs to the rule pack, not to the corpus.

| Kind | Category | What the case looks like | Expected |
|---|---|---|---|
| Positive | In a log statement | An identifier written out in a log call | Finding |
| Positive | In a test fixture | A label-less value inside test data | Finding |
| Positive | In a configuration value | A real-looking value stored per the configuration file's own format | Finding |
| Positive | In a comment | The identifier appears only inside a comment, where intent is unreliable | Finding |
| Near-miss | Right length, wrong check digit | A value that passes every shape check and fails the named validator | No finding |
| Near-miss | Documented example | The obviously fake sequential value that documentation uses (IQ-49) | No finding |
| Near-miss | Placeholder mail | An address on the `example.com` domain | No finding |
| Near-miss | Phone-like, not Nigerian | A number whose shape and length are plausible but whose prefix or country code is not | No finding |
| Edge case | UTF-16 file | A file stored as UTF-16 so the encoding path is exercised | As content dictates |
| Edge case | Very long line | A single line of at least 100,000 characters | As content dictates, within the per-file budget |
| Edge case | Minified JavaScript | A minified bundle, which the built-in exclusion list omits from the scan | No finding (excluded) |
| Edge case | Binary content in a `.txt` file | Content is binary, the name is text | No finding, and no tool error |
| Edge case | Excluded by `.gitignore` | A rule-matching file that git ignores | No finding |

P1-02 fixes the foundations (at least 30 cases, the harness in under 10 seconds, and adding a case requires no code change). This document adds two requirements the golden corpus owns:

1. **Coverage per rule before the rule is trusted.** Before a rule's documentation page exists, the corpus holds, for that rule, one positive at failing severity, one near-miss that fails the named validator, one near-miss that fails shape or length, and one case in each of the storage-like contexts (comment, fixture, documentation). Rule 2 then holds in practice: cases land first, the rule lands second.
2. **The corpus is the recall denominator.** Recall in section 5.3 is computed from the corpus's expected results, so the cases must be recorded with the care of test data, never as an afterthought.

### 4.2 Synthetic identifiers and personal-data discipline (rule 10, NFR-004, IQ-49)

All identifiers in the corpus are synthetic and generated by a documented script. The script is recorded in the corpus README so that anyone can reproduce and audit the values. Random synthetic values are permitted in code, in tests and in the conformance vectors: IQ-49 decided that a generated value which happens to match a real identifier is not a problem in those places. Documentation examples, meanwhile, use the obviously fake sequential values that decision names, and the corpus README lists the small set of sequential examples whose check digit must stay valid, so that every documentation page and pack example uses the same values.

Any path or location the corpus commits into the repository uses the repository-relative path format the configuration and results specifications fix: forward slashes, no absolute paths, no content of a matched value anywhere (rule 10). A triage record or an `expected.json` carries rule identifier, path, line, column and, where relevant, the triage label — nothing else and never the matched text.

### 4.3 The validation corpus of real public repositories (P2-06)

The synthetic corpus overstates precision on its own, because the same hands wrote both the rules and the cases. The countermeasure is a realistic negative set: real public repositories, downloaded and measured at **test time only**, never as product behaviour. Section 4.4 fixes the mechanics; this section fixes the composition and care rules:

1. **One pinned list.** `tests/corpus/validation/repos.json` names each repository once: the repository name in `owner/repo` form, the pinned commit hash (fully spelled, at least 40 hexadecimal characters), the licence from the permissive list only — MIT, Apache-2.0, BSD or ISC — the UTC date the pin was set, and the role from section 4.5. Entries are sorted by repository name ordinal; the file is deterministic, and no tooling may regenerate it with a different order (rule 6).
2. **Repository content is never committed.** The clone fetches the pinned commit into a temporary directory at measurement time and the directory is cleaned up afterwards. A shallow fetch of the pinned commit is enough; the full history is not needed.
3. **Network stays in the scheduled job.** Downloading the validation repositories belongs to a scheduled continuous-integration job, and to nothing else. Every pull request continues to run the zero-egress container test, which has no network access (rule 5), and the product binary itself never downloads anything, in measurement or in production.
4. **Findings and records never carry matched values.** The harness and its output persist as rule identifier, forward-slash repository-relative path, line and column, and the triage label — never the matched text (rule 10).
5. **Triage once and pin it.** Every finding in a validation repository has one owner triage, stored as an expected result. A later measurement run needs no network and no triage: it replays the pinned corpus and computes the rates in section 5. A rule change that would alter an outcome at a record's location arrives only after its corpus cases (rule 2), and the same change re-triages the records it affects.

### 4.4 The download and record mechanics

The clone, the measurement and the recording of results are fixed once here so that no later work package re-decides them:

1. **The download step clones exactly the pinned commit** and rejects a repository whose licence at that commit is not on the permissive list: the pull request that adds a pin carries the licence check as a corpus-level test.
2. **The clone is bounded.** A repository larger than 256 MB, or yielding more than 20,000 eligible files, is not chosen in the first place. The harness refuses to measure one that has since grown past those bounds; the pin is then replaced or dropped instead of the bounds being stretched.
3. **The measurement configuration is the one in section 5.2**, with no exceptions and no baseline, because applying a customer's risk acceptance would make two repositories' numbers incomparable.
4. **Records land in `tests/corpus/validation/records/<repository-name>.json`**, sorted by repository name ordinal, holding one entry per finding, as section 4.3 item 4 fixes. The records are exactly what the harness reads back; nothing is imported from anywhere else.

### 4.5 The pinned list (for the owner to confirm)

Selection criteria for every pin: permissive licence (MIT, Apache-2.0, BSD or ISC); maintained; one of the launch stacks (the CLI is .NET, and the first-release middleware are .NET, Node/TypeScript and Kotlin), or an open-source project published by a Nigerian company; between 200 and 20,000 eligible files; at most 256 MB.

The two pinned rows named here carry licences on the permissive list and are maintained today; their exact commit hashes are named at acceptance. A row in this table is a candidate, never a substitute for the checks: the licence, the activity and the checkout size of every row — named here or named at acceptance — are confirmed by the same pin test when the pin lands. The Node/TypeScript row and the Nigerian-company row are named at acceptance for the same reason: a candidate has to survive those checks, and naming a pin is the owner's acceptance decision.

| Repository | Launch stack | Licence | Role |
|---|---|---|---|
| StackExchange/StackExchange.Redis | .NET | MIT | Client-library code, tests, samples |
| JetBrains/Exposed | Kotlin/JVM | Apache-2.0 | Data-access code, tests, samples |
| Named at acceptance | Node/TypeScript | Permissive | Real application or tooling code in the Node stack |
| Named at acceptance | Any | Permissive | Open-source projects published by Nigerian companies |

---

## 5. Computing and reporting the rate (NFR-013)

### 5.1 The counting unit is a finding

The card asks whether the 5 % is per finding, per rule or per repository. This draft fixes: the count is **per finding**, because a finding is what a customer sees and what the triage labels; the gate is **per rule** and **overall**, because those are the two levels a pull request can actually affect; and per-repository rates are **reported only**, because a rate that is fine on average must still be able to show a single repository's concentration. Per-repository gating is rejected in version 1: a small repository with an unlucky rule would fail the gate for every other repository, which would punish one rule everywhere and explain nothing about the rest.

### 5.2 The rates

For a rule `r`, let `tp_r` be the number of findings at failing severity labelled `true-positive` and `fp_r` the number labelled `false-positive`, with every `unresolved` finding counted in `fp`. Then:

- `rate_r = 100 × fp_r / (tp_r + fp_r)` when `tp_r + fp_r` is greater than zero, and `0` otherwise.
- `rate_total = 100 × (Σ fp_r) / (Σ (tp_r + fp_r))` summed over all rules, with the same zero handling.

The counts summed across the golden corpus and across the validation corpus are one set; the two corpora feed the same rates. Only findings at failing severity feed these counts: a `note` finding contributes to neither numerator nor denominator, even when it comes from a rule that also produces `error` findings (section 3.2 item 4).

### 5.3 What is reported but not gated, in version 1

1. **Findings at `warning` and `note`**, per rule and overall, so the volume is visible. IQ-49 places them outside the gate, and version 1 does not change that.
2. **Recall on synthetic positives**, per rule and overall: for each rule, the number of expected positive findings the engine produced, divided by the number `expected.json` fixes. A missing expected finding is a missed detection, not a false positive. Version 1 does not fail a rule on recall: a rule that remembers too little raises a coverage conversation between the rule pack and the owner, rather than breaking the trust gate, and coverage per rule is the owner's decision.
3. **Per-repository rates**, affected by nothing but their own repositories, as section 5.1 fixes.
4. **The machine-independent counters** of section 6.7, which the same report publishes.

### 5.4 The gate

A measurement run passes NFR-013 exactly when, for every rule that has at least one triaged failing-severity finding, and for the overall total:

1. `20 × fp ≤ tp + fp`, which says the rate is exactly 5 % or below.

The gate runs on a rule pull request (P2-07): a change that pushes any single rule, or the overall total, above the boundary fails, and the report names the top three rules by `fp`, sorted by `fp` descending and then by rule identifier ordinal. A rule with zero failing-severity findings never gates; a corpus with zero failing-severity findings across every rule is a clean run.

### 5.5 The weekly report (P2-07)

The harness runs on every rule pull request and on the scheduled measurement job. It prints, per rule and overall: findings at failing severity (`error`), true positives, false positives, the rate, and recall on synthetic positives — with `warning` and `note` counts beside them, as section 5.3 fixes.

A Markdown report is committed weekly at `docs/reports/false-positive-<year>-<month>-<day>.md`, named from the report's UTC evaluation date, and attached to release notes. Each report records its inputs so that a reader can reproduce the numbers: the pinned commit hashes of the validation repositories, the rule-pack versions, the binary version, and the harness version. The report also names the section of this document (the counts and arithmetic) so that the published numbers are traceable.

A report never names a matched value (rule 10); if a sample is ever needed for clarity, the report describes the shape in general and points to the corpus case that reproduces it.

---

## 6. Performance measurement (NFR-010, NFR-011, NFR-012)

### 6.1 The budgets, unchanged

| Budget | What it says |
|---|---|
| NFR-010 | A full scan of 10,000 files finishes within 60 seconds, on 4 cores with 8 GB of memory. |
| NFR-011 | Per file: median ≤ 10 ms, 95th percentile ≤ 50 ms. |
| NFR-012 | The scan adds at most 30 seconds to a default-profile continuous-integration job. |

Rule 27 also contains the standing rule this whole section enforces: a change that slows the benchmark by more than 20 % fails review unless the card accepts it. This section turns that round number into an experiment that a hosted runner can run reliably (IQ-60), because a budget nobody can measure is a budget nobody can keep.

### 6.2 The benchmark repository

The benchmark repository is a synthetic tree of exactly 10,000 eligible files, generated by a committed script from pinned parameters: the seed, the file mix by kind and size, the encoding mix, and which files are on which sides of the exclusion lines. The generator is part of the same script the corpus uses, so the two corpora stay consistent in how they class their content, and no real name, identifier or URL enters the tree (rule 10).

The generated tree is deterministic: the same parameters give byte-identical files, because a benchmark whose corpus wobbles is not a benchmark. The tree is regenerated on each measurement run and never committed; the run records the generator version, the seed and the parameter hash, so any result is reproducible elsewhere. The generated tree is not a git work tree, so the scan exercises the plain directory walk, and the `.gitignore`-driven exclusion branch is not measured here (that behaviour has its own corpus cases).

### 6.3 Paired measurement and runners (IQ-60)

Every measurement is **paired**: the continuous-integration job that does the benchmarking runs the `main` build and the candidate build in the same job, on the same machine, so that runner noise hits both arms equally. One warm-up pair is measured and discarded, then three pairs are measured and every statistic is the median of its three values.

The gate is the 20 % band of the standing rule: the candidate fails if any of its medians — whichever number is being measured — lands more than 20 % above the same-arm `main` median. Being faster is never a failure. The job also records, beside the timings, the runner image name, the machine type, the CPU model, the operating system version, and the binary and rule-pack versions measured, so that the published number is auditable against an environment the reader can check.

Absolute numbers vary with the runner; the paired comparison does not. Each release publishes the absolute numbers from that release's measurement run, and the published budget claims (60 seconds for 10,000 files, 10 ms median per file, 30 added seconds) stay visible beside them, so the customer-facing claims and the measured reality sit in one place. The runner size — the standard GitHub-hosted Linux runners this repository's pull-request jobs use — is confirmed by the owner at acceptance and re-confirmed whenever the runner catalogue for this repository changes, because an untracked catalogue drift is the one way a paired number can drift too.

### 6.4 Measuring NFR-010 (whole-scan time)

One measurement run is a whole invocation of the binary over the benchmark repository with the default profile, history off, no exceptions, no baseline, no disabled rules and no external pack: only the embedded default pack. The measured time is the wall-clock duration from process start to process exit, including argument parsing, pack loading, the scan itself, sorting and writing every output. The harness measures with a monotonic process timer; a report value is repository artefact output, and the reproducibility story is the pairing plus the recorded runner, not a fixed clock.

Each pair contributes one candidate value and one `main` value; the statistic is the median on each arm. The gate is the 20 % band of section 6.3. The report also states the candidate's absolute value next to the 60-second budget, so the published claim can be checked against a measured fact on the recorded runner.

### 6.5 Measuring NFR-011 (per-file time)

In the same whole-scan measurement run, an instrumented build records, for every file, the elapsed time between the two per-file boundaries: from the moment the enumeration hands the file's read bytes to the matching stage, until that stage completes. Writing output is not part of the per-file time; the boundary ends at the matching stage, before any output write. The whole-scan run therefore carries both numbers with no second run.

The per-file values are sorted ascending. The median is the value at the position of `0.5 × n` rounded up — for 10,000 values, the 5,000th — and the 95th percentile is the value at the position of `0.95 × n` rounded up — for 10,000 values, the 9,500th. Both come out of integer positions, with no interpolation.

The gate is twofold: the candidate's own absolute values (median ≤ 10 ms, 95th percentile ≤ 50 ms), and the 20 % band against the same-arm medians, as section 6.3 fixes. The absolute budget gates because it is the published claim; the band gates because a published claim measured only once on a fast runner is not a measured claim at all.

### 6.6 Measuring NFR-012 (added continuous-integration time)

The added-cost claim is about what a customer's continuous-integration job pays on top of its own work to include one Gate-Keeper step. The measurement is one invocation of the binary over the CI reference sample: a tree of exactly 500 eligible files, total size at most 10 MB, generated by the same pinned generator. The profile is the default one, history off, with the embedded default pack and a cold, first-run invocation — no cached artefacts, no prior process of the same binary.

The measured time is the whole process duration from start to exit, exactly as section 6.4 fixes. The paired schedule and the median-of-three rule are the same, and the gate is the 20 % band; the 30-second budget is stated beside the measured value so the published claim can be checked. Where a customer places the step inside their job — before or after checkout, inside a container, beside other tools — is the GitHub Action's concern (P4-04) and is not what this budget measures. Turning history on, or selecting `deep`, is outside the budget, exactly as the configuration specification already states (IQ-83).

### 6.7 Machine-independent counters (IQ-60)

The instrumented build that times per file also counts, across the same paired schedule:

- **Allocations per file** — the total bytes allocated while a file is matched, summed per file and averaged over the run.
- **Bytes scanned per regex call** — how many content bytes each regular expression sees per call, aggregated per rule and overall.

Both counters gate on the same 20 % band as the timings (section 0.4 item 13). Their job is to catch what timing noise would hide: an allocation-heavy refactor can pass on a fast runner and fail the absolute budget on the 4-core machine the published claim names, and the counters see through that. Publishing the counters in the same weekly report (section 5.3 item 4) makes the engine's cost story auditable without re-running anything.

### 6.8 What is not measured by this document

1. **The `deep` profile and any scan with history on** (IQ-83). A clean default-profile result does not claim history was scanned, and the configuration specification already fixes where that sentence lives.
2. **The other budgets.** Licence verification ≤ 10 ms (NFR-002), the dashboard posture page ≤ 2 s (NFR-014), and the binary-size budget the NativeAOT spike records are all separate measurements on separate cards.
3. **The rule-pack pipeline.** The pack build, the signature and the coverage checks are later work; none of them is inside these three budgets.

---

## 7. Traceability

| Identifier | Where |
|---|---|
| NFR-013 | Sections 2 item 1, 3, 5 |
| NFR-010 | Sections 2 item 2, 6.2, 6.4 |
| NFR-011 | Sections 2 item 2, 6.5 |
| NFR-012 | Sections 2 item 3, 6.6; also [config-v1.md](config-v1.md) (IQ-83) |
| IQ-49 | Sections 0.1, 3.2 item 4, 4.2, 5.2, 5.3, 5.5 |
| IQ-60 | Sections 0.1, 6.3, 6.7 |
| IQ-83 | Sections 0.1, 3.3 item 4, 5.2, 6.6, 6.8 |
| P0-17 | This card, [issue #10](https://github.com/inveniotech/gate-keeper/issues/10) |
| P1-02 (golden corpus, kept at `tests/corpus/`) | Section 4.1 |
| P2-06 (validation corpus of real public repositories) | Sections 4.3, 4.4, 4.5 |
| P2-07 (measurement harness and weekly report) | Sections 5.4, 5.5 |
| P1-15 (benchmark jobs and the release report) | Section 6.3 |
| Rule 27 (the 20 % standing rule, unchanged here) | Sections 2 item 3, 6.3 |
| Rule 10 / NFR-004 (personal-data discipline) | Sections 4.2, 4.3, 4.5 |
| Rule 6 (determinism) | Sections 4.3 item 1, 6.2, 6.5 |

## 8. Native executable size amendment (IQ-90)

On 5 November 2026, the owner approved a **10 MiB (10,485,760 bytes)** hard cap per uncompressed native executable, superseding the earlier 8 MiB build threshold. The completed CLI with local support bundles measured 8,634,832 bytes on Linux arm64 and passed its corpus run; the earlier threshold blocked packaging after that successful test. The new cap applies uniformly to every supported runtime identifier. Archive sizes are separate from executable sizes.

The packaging gate accepts an executable exactly at the limit and rejects one byte above it. The benchmark report records the applied byte limit. The 20 per cent performance and counter regression checks, the 60-second whole-scan budget, the 10 ms median and 50 ms 95th-percentile per-file budgets, and the 30-second added continuous-integration budget are unchanged. This records the owner's specific size decision; the rest of this draft is not marked accepted by this amendment.