# Results format, version 1

**Status: draft — not accepted.** This specification is an AI draft for the owner to review and approve (rule 15 of `AGENTS.md`). It records the decided results shape. Nothing in this document is final, accepted or approved until the owner says so.

This document is the written contract for work package P0-04 ([issue #3](https://github.com/inveniotech/gate-keeper/issues/3)). It does not implement the writers; that is P1-11, which produces this profile with `SarifKit`. The public JSON Schemas are [`schemas/gk-results.schema.json`](../../schemas/gk-results.schema.json) for the evidence document and [`schemas/sarif-profile.schema.json`](../../schemas/sarif-profile.schema.json) for the SARIF profile. Examples are in [`docs/specs/examples/results/`](examples/results/).

---

## 0. Document status

### 0.1 Decision status

Normative text in this specification comes from the recorded decisions listed here. The specification itself stays a draft, because rule 15 reserves final approval of specifications to the owner.

| Identifier | Status | What it settles |
|---|---|---|
| IQ-01 | Decided | `evaluatedOn` is an explicit civil date. The evidence file carries that date and the commit revision. It carries no wall-clock time. |
| IQ-19 | Decided | A baselined finding is still reported. Its SARIF `baselineState` is `unchanged`. The evidence file reports how many findings the baseline held back and the age of the oldest. |
| IQ-20 | Decided | SARIF `partialFingerprints` uses the name `gatekeeper/v1`. The value is the keyed fingerprint. The key is never written out. |
| IQ-22 | Decided | `error`, `warning` and `note` map one-to-one onto SARIF levels. Severity overrides and the failure threshold are recorded in the evidence file. |
| IQ-24 | Decided | A disabled rule is recorded with its reason. |
| IQ-26 | Decided | A pack-age warning is a `warnings` entry, measured against `evaluatedOn`. |
| IQ-36 | Decided | A loaded Invenio pack records `coverageDate` and `contentDigest`. |
| IQ-61 | Decided | This profile is for GitHub code scanning. It does not claim that GitLab or Azure DevOps render it. |
| IQ-80 | Decided | Each applied exception records `id` and `entryHash`. Each loaded pack records `packId`, `version`, `provenance`, `coverageDate` where the pack has one, and `contentDigest`. |
| IQ-83 | Decided | History findings are a separate list. They carry no matched value and no fingerprint, and they are not baselined. |

The register entry for each identifier is an owner record. This specification cites the identifier and does not describe where that register is kept.

### 0.2 Normative keywords

The key words **MUST**, **MUST NOT**, **REQUIRED**, **SHALL**, **SHALL NOT**, **SHOULD**, **SHOULD NOT** and **MAY** are to be interpreted as described in RFC 2119. SARIF means the Static Analysis Results Interchange Format, version 2.1.0. When this document writes "the evidence file", it means the Gate-Keeper evidence JSON document in section 5. When it writes "the SARIF log", it means one scan's SARIF document in section 4.

### 0.3 Change log

| Version | Date | Change |
|---|---|---|
| 1 (draft) | 2 November 2026 | First draft. |
| 1 (additive amendment) | 5 November 2026 | The owner approved pack-version stamps in SARIF as well as evidence. Schema artefact 1.2.0 adds the optional run property `packs`; new producers always emit it. The SARIF standard version remains 2.1.0 and the evidence schema version remains 1. This approval covers the amendment, not acceptance of the whole specification. |

### 0.4 Details this draft fixes

The decisions name the facts the files must carry. They do not always name the property order or the closed warning codes. This draft fixes the following so that two writers produce the same bytes. Each item is part of the draft for the owner to accept or amend.

1. **No field is excluded from the byte-identical guarantee.** `evaluatedOn` and the revision are inputs, not clocks.
2. **SARIF carries work-tree findings only.** History stays in the evidence file, because a historical line is not a line in the tree GitHub would annotate.
3. **`helpUri` is omitted.** Version 1 has no stable per-rule documentation address. The citation title, and any NDPA or GAID section strings, are carried as described in section 4.3. Approved wording arrives in P2-02.
4. **A suppressed finding is omitted from `findings` and from SARIF.** The evidence file records the application under `exceptions` instead. A baselined finding stays in both files.
5. **The inventory counts work-tree findings that were reported**, including baselined findings, and excludes suppressed findings and history findings.
6. **The warning codes** in section 5.6 are the closed set. The sentences for a missing fingerprint key and for a shallow history match the configuration specification.
7. **The `entryHash` line order** in section 5.5 is the order the evidence file hashes. It is the same order the configuration specification uses for an exception entry.

---

## 1. Scope and non-goals

### 1.1 In scope

The bytes of one scan's SARIF log and one scan's evidence file. Which SARIF properties are populated. How a citation is carried. How a fingerprint is attached. How severity maps to SARIF `level`. The evidence fields for packs, findings, history, exceptions, the baseline summary, warnings and the inventory.

### 1.2 Not in scope

- The writer (P1-11) and the library call that builds a SARIF log.
- The configuration file, the exceptions file and the baseline file. Those are P0-05. This specification says how a scan records the outcome of applying them.
- The evidence-sink path and the `.sha256` sidecar (P0-07).
- Marking this specification accepted.

---

## 2. Byte-identical output

The same inputs MUST produce the same bytes for both files. The inputs are the scanned tree, the loaded packs, the configuration, the fingerprint key when one is present, and `evaluatedOn`. The machine path, the locale and the wall clock are not inputs.

No property is excluded from that guarantee. Neither file contains a wall-clock time, a matched value, a line of source, or the fingerprint key.

A producer writes both files as UTF-8 with no byte-order mark, LF line endings, a trailing newline, and two-space indentation. Object keys appear in the order this specification lists them. Where a property is optional and absent, it is omitted rather than set to null. Arrays are sorted as each section requires, using ordinal comparison for strings and ascending order for numbers.

---

## 3. Facts shared by both files

| Fact | Type | Meaning |
|---|---|---|
| `evaluatedOn` | civil date `YYYY-MM-DD` | The date input from IQ-01. It is not read from the clock at write time. |
| Engine version | semantic version `major.minor.patch` | The version of `gatekeeper-ndpa-check` that scanned (NFR-031). |
| Repository identity | string | `repository.id` from configuration, or the normalised git remote. Empty when neither is available. |
| Revision | 40 lowercase hex characters, or empty | The commit that was scanned. Empty outside a git work tree. |
| Profile | `default` or `deep` | The profile that selected files and rules. |
| History scanned | boolean | True only when history scanning ran. |
| Failure threshold | `error`, `warning` or `note` | The `fail-on` value used for this scan. |

The product name in the evidence file is `gatekeeper`. The SARIF driver name is `gatekeeper-ndpa-check`.

---

## 4. SARIF profile

A SARIF log is one document, schema version 2.1.0, with exactly one run. GitHub code scanning is the consumer this profile is written for (IQ-61). The `$schema` value MUST be exactly:

`https://raw.githubusercontent.com/oasis-tcs/sarif-spec/main/sarif-2.1/schema/sarif-schema-2.1.0.json`

The root keys, in order, are `$schema`, `version`, `runs`.

### 4.1 Run

Run keys, in order: `tool`, `invocations`, `results`, `properties`.

`tool.driver` keys, in order: `name`, `version`, `informationUri`, `rules`.

| Property | Value |
|---|---|
| `name` | `gatekeeper-ndpa-check` |
| `version` | The engine version. |
| `informationUri` | `https://gatekeeper.africa/` |
| `rules` | One entry per rule that produced a work-tree result, sorted by rule identifier. A clean scan has an empty array. |

`invocations` has exactly one entry. Its keys, in order, are `executionSuccessful`, `exitCode`, and `toolExecutionNotifications` when there is at least one warning. `executionSuccessful` is true when the scan finished, including when findings failed the threshold. `exitCode` is 0 or 1, the same value as the evidence file. It is never 2, because a tool error does not write these files.

Each notification has `level` equal to `warning` and `message.text` equal to the warning sentence in section 5.6. Notifications follow the warning sort order.

`properties` keys, in alphabetical order: `evaluatedOn`, `historyScanned`, `packs`, `profile`, `repositoryId`.

`properties.packs` MUST contain every loaded pack, including packs that produced no work-tree findings. It uses the same fields, omission rules, field order and ordinal sorting as the evidence `packs` array in section 5.1. An empty array means no packs were loaded. The schema keeps this property optional so logs produced before schema artefact 1.2.0 remain valid; a missing property in such a log means pack provenance is unknown, not that no pack was loaded. New producers always write the array, including on clean scans.

The engine version identifies the scanner implementation. Pack versions identify the detection rules, which can change independently of the engine; content digests identify the exact pack payload, including local content that may reuse a declared version across scans. Carrying these stamps in both artefacts makes each independently traceable for reproducing findings, explaining differences between scans and auditing which rules were evaluated. Retaining SARIF alongside its evidence JSON already preserves that information, but a SARIF file stored or uploaded on its own must not lose it. These stamps contain no matched values, source content or secret key material.

### 4.2 Results

`results` contains work-tree findings only. Suppressed findings are omitted. History findings are omitted. Baselined findings are included.

Sort: artifact URI ordinal, then `startLine` ascending, then `startColumn` ascending, then rule identifier ordinal.

Result keys, in order: `ruleId`, `ruleIndex`, `level`, `message`, `locations`, `baselineState`, and `partialFingerprints` when a fingerprint exists.

| Property | Value |
|---|---|
| `ruleId` | The rule identifier. |
| `ruleIndex` | The index of that rule in `driver.rules`. |
| `level` | The finding severity: `error`, `warning` or `note`. |
| `message.text` | `{ruleId}. {citation.title}.` Nothing else. No matched text. |
| `baselineState` | `unchanged` when the baseline held the finding back, otherwise `new`. |
| `partialFingerprints` | Present only when a key was available. The only name is `gatekeeper/v1`. The value is 64 lowercase hex characters, computed as section 4.4 describes. |

`locations` has exactly one entry. `physicalLocation.artifactLocation.uri` is the repository-relative path, using `/`, with no leading slash and no machine path. `region` has `startLine` and `startColumn` only, both 1-based. A producer MUST NOT write `snippet`, `contextRegion`, or any other region property. Those properties would copy source into the log.

### 4.3 Rules and citations

Rule keys, in order: `id`, `shortDescription`, and `properties` when the citation has an NDPA or GAID string.

`shortDescription.text` is `citation.title`.

`helpUri` MUST NOT be set. Version 1 has no per-rule page to point at.

When `citation.ndpa` is present, `properties.ndpa` carries that string. When `citation.gaid` is present, `properties.gaid` carries that string. Those two keys are alphabetical. A citation with only a title omits `properties`. The strings are the pack's citation text. This specification does not invent a section number; examples use a placeholder title.

### 4.4 Fingerprints

The fingerprint is HMAC-SHA-256 (RFC 2104, SHA-256). The HMAC key is the 32 bytes decoded from `.ndpa-check/fingerprint.key`. The message is UTF-8, four lines, each ended by LF: the rule identifier, the repository-relative path, the matched span, and the 0-based occurrence index of that same span for that rule in that file. Line numbers and line text are not inputs.

The log never contains the key or the matched span. When the key file is absent, every result omits `partialFingerprints`, and the evidence file records the missing-key warning. History findings have no fingerprint.

Test vector. The key file's hex is `00112233445566778899aabbccddeeff00112233445566778899aabbccddeeff` and the LF after it is not part of the key. The message lines `pii.bvn`, `src/a.cs`, `12345678901`, `0` fingerprint to:

`00cca3a4746d65803db2fa8f18dfa1d9f05ca1c3c382ca92b2078b31a0e9c3a7`

The span `12345678901` is synthetic. The message lines `pii.phone`, `src/b.cs`, `08031234567`, `0` fingerprint to:

`79d1fade04a4d84a7c7b361aeee52791d6fbd3f1545e286bac9f8255816db879`

---

## 5. Evidence file

The evidence file is a JSON object. Its keys, in order, are:

`schemaVersion`, `evaluatedOn`, `engine`, `repository`, `profile`, `historyScanned`, `failOn`, `exitCode`, `packs`, `rules`, `findings`, `history`, `exceptions`, `baseline`, `inventory`, `warnings`.

`schemaVersion` MUST be the integer 1.

`engine` keys, in order: `product`, `version`. `product` is `gatekeeper`.

`repository` keys, in order: `id`, `revision`.

`exitCode` is 0 when no reported finding that is still able to fail meets `failOn`, and 1 when at least one does. Reported findings are the work-tree findings that were not suppressed and were not held back by the baseline, plus every history finding. A baselined finding does not meet the threshold. A suppressed finding is not reported.

### 5.1 Packs

`packs` lists every loaded pack, sorted by `packId` ordinal, then by `version` ordinal.

| Property | Required | Value |
|---|---|---|
| `packId` | yes | The pack identifier. |
| `version` | yes | The calendar version for an Invenio pack, or the decimal digits of a local pack's integer version. |
| `provenance` | yes | `bundled`, `plan-gated` or `local`. |
| `coverageDate` | Invenio only | The pack's `coverageDate`. A local pack omits it. |
| `contentDigest` | yes | 64 lowercase hex characters. SHA-256 of the pack content bytes, excluding any watermark (IQ-80). |

### 5.2 Rule changes

`rules` keys, in order: `overrides`, `disabled`.

`overrides` lists each severity override that was in force, sorted by rule identifier. Each entry's keys are `id`, then `severity`.

`disabled` lists each disabled rule, sorted by rule identifier. Each entry's keys are `id`, then `reason`. The reason is the configuration text, unchanged apart from the trimming the configuration specification requires.

### 5.3 Findings

`findings` lists reported work-tree findings, including baselined findings and findings whose severity an exception overrode. Suppressed findings are omitted.

Sort: `path`, `line`, `column`, `ruleId`, using ordinal comparison for strings and ascending order for numbers.

| Property | Required | Value |
|---|---|---|
| `ruleId` | yes | The rule identifier. |
| `severity` | yes | The severity after overrides and after a severity-bearing exception. |
| `category` | yes | The rule's category. |
| `path` | yes | Repository-relative path, `/` separators. |
| `line` | yes | 1-based line. |
| `column` | yes | 1-based column. |
| `locationKind` | yes | One of section 6. The rule's `location.kind` when the rule has one, otherwise `source`. |
| `fingerprint` | when a key was available | The `gatekeeper/v1` fingerprint. Omitted for every finding when the key is missing. |
| `baselineState` | yes | `unchanged` or `new`. |
| `citation` | yes | `title`, then `ndpa` when present, then `gaid` when present. |

### 5.4 History

`history` lists history findings. When `historyScanned` is false the array MUST be empty. A clean history scan is an empty array with `historyScanned` true, which is a different fact from not having scanned.

Sort: `commitId` ordinal, then `path`, then `line`, then `ruleId`, then `column`.

A history entry has the same properties as a finding except that it has `commitId` (40 lowercase hex characters) as its first property, and it has neither `fingerprint` nor `baselineState`. `commitId` is the commit that contained the blob. The matched value is not a property.

History findings are included in `exitCode`. They are not included in `baseline` or in `inventory`. Exit code 2 is not a value of `exitCode`, because a tool error does not write these files.

### 5.5 Applied exceptions

`exceptions` lists each time an active exception applied, not each exception file entry once. Two findings suppressed by one entry produce two records. Sort: `id`, `path`, `line`, `column`, `ruleId`.

| Property | Value |
|---|---|
| `id` | The exception `id`. |
| `entryHash` | The lowercase hex SHA-256 of the serialisation below. |
| `ruleId` | The finding's rule identifier. |
| `path` | The finding's path. |
| `line` | The finding's line. |
| `column` | The finding's column. |
| `outcome` | `suppressed` when the exception had no severity, or `overridden` when it set a severity. |

`entryHash` is SHA-256 over UTF-8 lines, each ended by LF, in this order: `id`; `rule` or an empty line; `category` or an empty line; each path glob in file order; `justification`; `owner`; `expires`; `severity` or an empty line. The hash is of the exception entry, so two applications of one unchanged entry share one hash.

The entry whose lines are `exc-fixture-bvn`, `pii.bvn`, an empty line, `tests/fixtures/**`, `Synthetic values from the corpus script.`, `dpo@example.com`, `2026-11-01`, and an empty line hashes to:

`f9de64c03afa66c3d6def1cb5d697dcb9e8f0f7c14824717ed5e1e31cbe6d8b0`

### 5.6 Baseline summary and warnings

`baseline` keys, in order: `matchedCount`, `oldestAgeDays`.

`matchedCount` is the number of work-tree findings with `baselineState` equal to `unchanged`. `oldestAgeDays` is the greatest age, in civil days from the entry's `addedOn` to `evaluatedOn`, among those findings. It is 0 when `matchedCount` is 0.

`warnings` is sorted by `code`, then by `message`, ordinal. Each entry's keys are `code`, then `message`. The code is one of:

| Code | Message |
|---|---|
| `baseline.not-applied` | `The baseline was not applied. Findings that the baseline would have hidden are reported.` |
| `config.unknown-key` | `ndpa-check.toml contains the unknown key {key}. It was ignored.` |
| `fingerprint.missing` | `No fingerprint key is available, so fingerprints are omitted from the results.` |
| `pack.stale` | `Rule pack {packId} was issued {n} days before the evaluation date, which is past the 90-day threshold.` |
| `repository.identity-empty` | `No repository identity could be derived. Set repository.id in ndpa-check.toml.` |
| `scan.history-without-git` | `History scanning was requested and this directory is not a git work tree, so history was not scanned.` |
| `scan.outside-git` | `This directory is not a git work tree. The scan walked the directory and applied the built-in exclusions.` |
| `scan.shallow-history` | `History scanning covered only the commits in this shallow clone.` |
| `submodule.not-checked-out` | `The submodule at {path} is not checked out, so its files were not scanned.` |

`{key}`, `{packId}`, `{n}` and `{path}` are replaced with the input values. `{n}` is a decimal integer with no leading zeros. A missing fingerprint key and a baseline file that could not be applied produce both `fingerprint.missing` and `baseline.not-applied`.

The sentence "A clean result does not mean git history was scanned." is required in the human-readable summary when history did not run. It is not a warning and it is not a property of either file.

---

## 6. Inventory

`inventory` counts reported work-tree findings, grouped by `category` and `locationKind`. A group whose count would be zero is omitted. Suppressed findings and history findings are not counted. Baselined findings are counted.

Each entry's keys, in order, are `category`, `locationKind`, `count`. `count` is an integer greater than or equal to 1. Entries are sorted by `category`, then by `locationKind`, ordinal, so the section is byte-identical (rule 6).

`locationKind` is one of `log`, `migration`, `api-model`, `configuration`, `fixture`, `source`. The section contains no matched text and no subject identifier.

P1-11 writes the section. Later audit reporting reads it.

---

## 7. What a clean scan still records

A scan with no findings still writes both files. The evidence file has empty `findings`, `history` (when history did not run), `exceptions`, `inventory` and, when nothing warned, `warnings`. `baseline.matchedCount` is 0 and `oldestAgeDays` is 0. `exitCode` is 0. The SARIF log has empty `rules` and empty `results`. Packs that loaded are still listed, so the evidence file shows which rule content ran.
