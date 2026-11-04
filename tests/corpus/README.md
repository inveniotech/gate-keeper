# Golden fixture corpus

This folder proves what the scanner must detect: small synthetic repositories whose findings are recorded ahead of the rules that produce them (rule 9 of `AGENTS.md`, and the quality-budgets specification, section 4.1). Work package P1-02 built version 0 of this corpus and its harness. Real identifiers never enter this repository (rule 10): the [New-CorpusValues.mjs](New-CorpusValues.mjs) script generates every value the cases embed.

## Layout

A case is a folder `tests/corpus/<case-name>/` holding exactly two things:

- `repo/` — the synthetic repository the scan runs on.
- `expected.json` — the findings the engine must produce, one entry per finding.

An expected entry carries the rule identifier, the repository-relative path in forward slashes, the 1-based line and column of the value's first character, and, where relevant, the triage label. It never carries the matched value (the quality-budgets specification, section 4.2). Entries sort by path, then line, then column, then rule identifier. The file is UTF-8 without a byte-order mark, with LF line endings and a final newline (rule 22).

Near-miss and edge cases fix their expectations as an empty list: a near-miss exists precisely to make a wrong match visible, so any finding at all in such a case is a false positive (the quality-budgets specification, section 3.2).

## The documented generation script

Run the generator and compare its table with the values the cases embed:

```powershell
node tests/corpus/New-CorpusValues.mjs
```

A pinned seed drives the script, so every run prints the same table (rule 6). The script also verifies the documented examples the corpus pins:

| Example | Value | Why it is pinned |
|---|---|---|
| `nuban-documented-account-one` | `0000014579` | The work example the Central Bank of Nigeria published in its 2010 NUBAN implementation proposals; the check digit must stay valid. |
| `nuban-documented-account-two` | `0000000220` | The second published work example; the check digit must stay valid. |
| `bvn-documented-example` | `12345678901` | The obviously fake sequential value documentation uses (IQ-49). |

The NUBAN values follow the published weighting: the check digit covers the 3-digit bank code and the 9-digit serial with the weights `3,7,3,3,7,3,3,7,3,3,7,3`, modulo 10, and the customer-facing account number is the serial followed by the check digit. Every NUBAN the cases embed uses the bank code `011`.

## Rule identifiers the cases name

The cases are written against the decided vocabulary of IQ-12: context-anchored bank verification number and national identity number rules, NUBAN where a check digit exists, the named validators, and the generic `pii.ng-11-digit-id` note. The identifiers the expected files name are drafts for the owner to confirm with the rule packs (rule 15):

| Rule identifier (draft) | What it covers | Severity the pack will carry |
|---|---|---|
| `pii.bvn` | The context-anchored bank verification number. | `error` |
| `pii.nin` | The context-anchored national identity number. | `error` |
| `pii.nuban` | The account number with the `nuban` validator. | `error` |
| `pii.phone` | The mobile number with the `ng-phone-prefix` validator. | `error` |
| `pii.email` | The address with the `email-not-example-domain` validator. | `error` |
| `pii.ng-11-digit-id` | Any eleven-digit run no contextual rule identified. | `note` |

The corpus assumes, and the rule packs must honour (the cases land first, the rules land second — rule 9, rule 2):

1. A span a contextual rule identified is not reported again by `pii.ng-11-digit-id` on the same location.
2. The documented examples above sit outside every rule, through the named exclusions (`sequential-digits`, `excludeValues`) the rule-pack examples already carry.
3. An expected entry names the value's own location; the pack names the severity, and the corpus does not (the quality-budgets specification, section 4.1).

These three assumptions are the part of the corpus the rule-pack work packages confirm or amend; a change to them updates the cases in the same pull request as the pack.

## The case inventory

**Positives** — one finding each, in the storage contexts the card fixes from day one:

| Case | Rule | Context |
|---|---|---|
| `bvn-in-log-statement` | `pii.bvn` | A log statement |
| `bvn-in-test-fixture` | `pii.bvn` | A test fixture |
| `bvn-in-configuration` | `pii.bvn` | A configuration file |
| `bvn-in-comment` | `pii.bvn` | A source comment |
| `bvn-in-api-model` | `pii.bvn` | An API model |
| `nin-in-log-statement` | `pii.nin` | A log statement |
| `nin-in-test-fixture` | `pii.nin` | A test fixture |
| `nin-in-configuration` | `pii.nin` | A configuration file |
| `nin-in-comment` | `pii.nin` | A source comment |
| `nuban-in-source` | `pii.nuban` | Application source |
| `nuban-in-migration` | `pii.nuban` | A database migration |
| `nuban-in-configuration` | `pii.nuban` | A configuration file |
| `phone-in-log-statement` | `pii.phone` | A log statement |
| `phone-in-api-model` | `pii.phone` | An API model |
| `phone-in-test-fixture` | `pii.phone` | A test fixture |
| `phone-in-comment` | `pii.phone` | A source comment |
| `email-in-source` | `pii.email` | Application source |
| `email-in-log-statement` | `pii.email` | A log statement |
| `email-in-configuration` | `pii.email` | A configuration file |
| `email-in-comment` | `pii.email` | A source comment |
| `email-in-fixture` | `pii.email` | A test fixture |
| `symbolic-link` | `pii.phone` | A file reached through a link's target |

**Near-misses** — empty `expected.json`, by design:

| Case | What the near-miss proves |
|---|---|
| `bvn-wrong-length` | A ten-digit run next to the bank verification context fails the shape. |
| `nin-wrong-length` | A twelve-digit run next to the identity context fails the shape, and hides no eleven-digit run inside it. |
| `nuban-wrong-check-digit` | The right length with a failed check digit fails the named `nuban` validator. |
| `nuban-wrong-length` | A nine-digit serial without its check digit fails the shape. |
| `bvn-documented-example` | The documented sequential value produces no finding. |
| `nuban-documented-example` | The two published account examples produce no finding. |
| `email-example-domain` | Addresses on the `example.com` domain produce no finding. |
| `phone-not-nigerian` | A telephone-shaped value outside every Nigerian mobile prefix produces no finding. |

**The note rule and the edge cases** — the card's opening categories:

| Case | What the case carries |
|---|---|
| `ng-11-digit-unlabelled` | An eleven-digit run no rule identified, so the note rule reports it. |
| `bvn-utf16-configuration` | The identity settings stored as UTF-16 with a byte-order mark. |
| `bvn-very-long-line` | One line of at least 100,000 characters with a value deep inside it. |
| `minified-javascript` | A minified bundle the built-in exclusions leave out of the scan, silently. |
| `binary-text-extension` | Binary content in a `.txt` name: skipped with a note, never a tool error. |
| `gitignore-excluded` | A rule-matching spool file the case's `.gitignore` keeps out of the scan. |

## The harness

`tests/Corpus.Tests` runs the engine over each case and diffs the findings the run produced against `expected.json`, reporting precision and recall per rule. One theory member per case keeps a malformed case naming itself rather than failing an aggregate run, and adding a case is a folder and a documentation row — no code change.

The matcher engine is in this build (P1-06). The harness still reports an empty findings list, because the scan orchestrator (P1-07) is what runs the matcher over an enumerated repository, and the cited rule pack is later work (P2). The report marks recall `unmeasured` rather than zero, because a scan that is not yet given those rules proves nothing about missed detections (rule 16). What this build can verify, and the suite verifies for every case:

1. Every expected location is a file the enumeration reaches, at a line and column the decoded text actually holds.
2. The edge-case behaviour is exact: the UTF-16 file decodes per its byte-order mark, the long line reaches the engine at full length, the minified bundle and the ignored spool file stay out of the scan silently, the binary text file skips with its note, and the symbolic link skips with its note wherever the checkout materialised the link.
3. Nothing the engine produced escapes a case's declared expectations — the guard phase that becomes the near-miss protection the moment the matching stage lands.

Before the measurement work packages build on the scorer, its arithmetic is proven on fabricated scans in `CorpusScorerTests`: matches, unexpected findings, the per-rule sums and the printed rates.

Measured wall time of the whole run is printed to the test output at the end; the harness scans 36 repositories well inside the ten seconds the card fixes. A test never asserts a timing (rule 26).

## Adding a case

1. Generate the value with `node tests/corpus/New-CorpusValues.mjs`; cases never invent a value (rule 10).
2. Create the folder `tests/corpus/<case-name>/repo/...` with the content, and add the case to the inventories above (`Corpus_ReadMe_Should_NameEveryCaseInItsTable` no longer passes until the documentation names it).
3. Write `expected.json` with one entry per finding the future rule must produce, sorted as section 4.2 fixes, or `[]` for a near-miss.
4. Run the harness. Nothing else changes.

## How to test

From the repository root:

```powershell
dotnet test --filter FullyQualifiedName~Corpus.Tests
```
