# Configuration, exceptions and baseline, version 1

**Status: draft — not accepted.** This specification is an AI draft for the owner to review and approve (rule 15 of `AGENTS.md`). It turns the decisions listed in section 0.1 into exact keys, defaults and lifecycle rules. Nothing in this document is final, accepted or approved until the owner says so.

This document is the written contract for work package P0-05 ([issue #4](https://github.com/inveniotech/gate-keeper/issues/4)). It does not implement the loader, the enumerator or the baseline command; those are P1-05, P1-08 and P1-09. The results file that records applied exceptions, baseline matches and warnings is specified by P0-04. The sink path that uses `repository.id` is specified by P0-07.

Settings and exceptions are TOML. The baseline is a small JSON document specified in full in section 12. This draft does not add a JSON Schema file: the schema artefact's next published schema is the results format (P0-04), and a TOML file is not a JSON Schema instance. A later change that adds a schema for any of these files updates that schema, this specification and the version field in the same pull request (rule 24 of `AGENTS.md`).

---

## 0. Document status

### 0.1 Decision status

Normative text in this specification comes from the recorded decisions listed here. The specification itself stays a draft, because rule 15 reserves final approval of specifications to the owner.

| Identifier | Status | What it settles |
|---|---|---|
| IQ-01 | Decided | Exception expiry and baseline age use `evaluatedOn`, an explicit date input. They do not use the commit timestamp. |
| IQ-02 | Decided | Everything read from a customer repository lives in `.ndpa-check/`. There is no root-level configuration file and no `.gk/` directory. |
| IQ-17 | Decided | Unknown keys in `ndpa-check.toml` produce a warning and do not fail the scan. |
| IQ-19 | Decided | `baseline` writes `.ndpa-check/baseline.json` containing fingerprints and the date each entry was added, never a matched value. Baselined findings are reported and do not fail continuous integration. `baseline.maxAgeDays` makes entries older than that fail again. |
| IQ-20 | Decided | A fingerprint is a hash-based message authentication code using SHA-256 (HMAC-SHA-256) over the rule identifier, the repository-relative path, the matched value and the occurrence index. The key is never written to any output. |
| IQ-21 | Decided | Work-tree enumeration, the built-in exclusions, the size limit, binary and encoding rules, submodules, Git LFS pointers, and `--changed-since`. |
| IQ-22 | Decided | Severities are `error`, `warning` and `note`. The default failure threshold is `fail-on = "error"`. A severity change is recorded in the evidence. |
| IQ-23 | Decided | Profiles `default` and `deep`. `deep` adds data files, staging configuration and a 10 MB file limit. A rule may be tagged so that it runs only in `deep`. |
| IQ-24 | Decided | Disabling a rule requires a written reason, which is recorded in the evidence. Pack merging itself is specified in the rule-pack format. |
| IQ-25 | Decided | `packs.local` lists unsigned local packs. Their rule identifiers start with `local.` and cannot override an Invenio rule. |
| IQ-29 | Decided | The repository identity is `repository.id` when that key is set, otherwise the normalised git remote URL. |
| IQ-80 | Decided | Every exception has a required `id`, unique within the file. Evidence records that `id` and an `entryHash` over a fixed serialisation of the entry. |
| IQ-83 | Decided | History scanning is off in `default` and on in `deep` unless `scan.history = false`. History findings are a separate result list. They are not fingerprinted and are not written into the baseline. A clean default-profile result does not claim that history was scanned. |
| IQ-86 | Decided | `init` gitignores the fingerprint key. `doctor` warns when git is tracking it. A missing key omits fingerprints and does not fail the scan. Continuous integration supplies the key as a secret file. |

The register entry for each identifier is an owner record. This specification cites the identifier and does not describe where that register is kept.

### 0.2 Normative keywords

The key words **MUST**, **MUST NOT**, **REQUIRED**, **SHALL**, **SHALL NOT**, **SHOULD**, **SHOULD NOT** and **MAY** are to be interpreted as described in RFC 2119. When this document writes "the binary", it means `gatekeeper-ndpa-check`. When it writes "a scan", it means an invocation of that binary that scans a tree. `init`, `baseline` and `doctor` are named subcommands.

### 0.3 Change log

| Version | Date | Change |
|---|---|---|
| 1 (draft) | 2 November 2026 | First draft. |

### 0.4 Details this draft fixes

The decisions name the behaviour and, in several places, the key. They do not always name the TOML shape, the byte count or the match order. This draft fixes the following so that an implementer has one reading. Each item is part of the draft for the owner to accept or amend.

1. **File size.** "1 MB" means 1,048,576 bytes and "10 MB" means 10,485,760 bytes. The keys hold integers, so the unit has to be exact.
2. **Keys the decisions describe but do not spell.** `scan.profile`, `scan.maxFileBytes`, `scan.exclude`, `scan.includeSubmodules`, `severity.overrides` and `rules.disabled`, plus the exception field names in section 11. Keys the decisions do spell are kept as spelled: `repository.id`, `scan.history`, `baseline.maxAgeDays`, `fail-on` and `packs.local`.
3. **Dates are strings** `YYYY-MM-DD`, the same civil-date form as the rule-pack format. A TOML date value is a type error.
4. **Glob matching.** The glob characters are the subset in the rule-pack specification (section 5.3 of [rule-pack-v1.md](rule-pack-v1.md)). `*` does not match `/`. Matching is ordinal and case-sensitive against the path the enumerator returns.
5. **Data files and staging configuration** are the path classes in section 5. That is the v1 reading of IQ-23.
6. **An exception without `severity` suppresses. An exception with `severity` reports the finding at that severity and does not suppress it.** Two matching exceptions of the same rank are a tool error.
7. **A missing fingerprint key does not apply the baseline.** Findings stay visible. IQ-86 says a missing key does not fail the scan; hiding findings without a key would do the opposite of what the baseline is for.
8. **Flag names** `--profile`, `--include-submodules`, `--changed-since` and `--evaluated-on`.
9. **The fingerprint message and the exception `entryHash` line order** in sections 11.4 and 13.2. The test vectors in those sections are computed from that order.
10. **The built-in exclusion list** in section 6, including the closed lock-file names, the minified suffixes, and the rule that `.ndpa-check/` is not scanned.
11. **`--changed-since` does not turn history off.** It only limits the work tree. History follows `scan.history` and the profile. A pull-request scan that also runs history is outside the 30-second budget.

---

## 1. Scope and non-goals

### 1.1 In scope

The directory `.ndpa-check/`, the keys and defaults of `ndpa-check.toml`, the two scan profiles, the exceptions file, the baseline file, the fingerprint key, and the scan-scope options that change which files are read. The lifecycle of an exception expiry and of a baseline entry that has passed `baseline.maxAgeDays`.

### 1.2 Not in scope

- Loading rule packs, merging two versions of the same pack, and the portable matcher (the rule-pack specification and P1-03, P1-06).
- The SARIF profile and the evidence JSON object shape (P0-04). This specification says which facts that object must be able to carry.
- The evidence-sink directory layout (P0-07). This specification defines the repository identity string that layout uses.
- Marking this specification accepted, and writing the architecture decision record for the directory name.

---

## 2. Directory and discovery

A scan reads customer configuration only from a directory named `.ndpa-check` at the root of the tree being scanned. The root is the git work-tree root when the tree is a git work tree, and the directory the user asked to scan otherwise. The binary MUST NOT walk parent directories to find a configuration file, and it MUST NOT read a configuration file at the repository root. The name `.gk` is not a configuration directory.

| Path | Role | Required |
|---|---|---|
| `.ndpa-check/ndpa-check.toml` | Settings. | No. When the file is absent, the defaults in section 4 apply. |
| `.ndpa-check/exceptions.toml` | Exceptions. | No. When the file is absent, there are no exceptions. |
| `.ndpa-check/baseline.json` | Baseline fingerprints. | No. When the file is absent, no finding is baselined. |
| `.ndpa-check/fingerprint.key` | The per-repository fingerprint key. | No. When the file is absent, section 13.4 applies. |
| `.ndpa-check/.gitignore` | Written by `init` so that git ignores the key. | No. |
| `.ndpa-check/packs/` | The conventional place for local packs. The loader reads only the paths in `packs.local`. | No. |

Files inside `.ndpa-check/` are configuration. A scan MUST NOT open them as files to be matched for findings, and it MUST NOT write the fingerprint key into any result.

Changes to this directory are how a repository accepts risk. Teams that want the Data Protection Officer to approve that risk protect `.ndpa-check/` with code owners. The binary does not read `CODEOWNERS`.

The settings file, the exceptions file, the baseline file and the key file, when present, MUST be UTF-8. A byte-order mark is a tool error. `ndpa-check.toml` and `exceptions.toml` MAY use LF or CRLF line endings (line feed, or carriage return followed by line feed). `baseline.json`, as written by `baseline`, uses LF only. The settings file, the exceptions file and the baseline file MUST each be at most 1,048,576 bytes. A larger file is a tool error.

---

## 3. The evaluation date

Exception expiry, baseline age and the date written by `baseline` all use one civil date, `evaluatedOn`. It is an input, not a timestamp taken from the commit.

The binary chooses `evaluatedOn` in this order:

1. `--evaluated-on YYYY-MM-DD`, when the flag is present.
2. Otherwise the UTC date of `SOURCE_DATE_EPOCH`, when that environment variable is set to a non-negative integer number of seconds.
3. Otherwise the UTC date of the injected clock. In a real run that is today's date in UTC. Tests supply the clock; they MUST NOT read the wall clock.

A flag value that is not a real civil date, or a `SOURCE_DATE_EPOCH` value that is not a non-negative integer, is a tool error. The flag wins over the environment variable when both are set.

The same files, packs, configuration and `evaluatedOn` MUST produce the same disposition of every finding. A later `evaluatedOn` MAY fail a scan that an earlier date passed, because an exception or a baseline entry may have expired. That dependence is intentional.

---

## 4. Settings file

`ndpa-check.toml` is a TOML document. `schemaVersion` MUST be the integer `1`. Any other value is a tool error that tells the user to use a build that supports the version, or to keep the file at version 1. A document the TOML parser rejects is a tool error naming the file and, when the parser reports it, the line.

A duplicate key is a tool error. Dates and severities are strings. A TOML date, a float, or a string where an integer is required is a tool error.

### 4.1 Keys

Every key is listed with its type, the default when the key is omitted, and an example value. Profile-dependent defaults are the value used when the key is omitted; an explicit value always replaces the profile default.

| Key | Type | Default | Example |
|---|---|---|---|
| `schemaVersion` | integer | Required when the file exists. An absent file behaves as version 1. | `1` |
| `repository.id` | string | The normalised git remote, or empty when no remote can be normalised (section 10). | `"github.com/acme/payments"` |
| `scan.profile` | string | `"default"` | `"deep"` |
| `scan.history` | boolean | `false` when the effective profile is `default`; `true` when it is `deep`. | `false` |
| `scan.maxFileBytes` | integer | `1048576` on `default`; `10485760` on `deep`. | `1048576` |
| `scan.exclude` | array of strings | `[]`. Built-in exclusions still apply. | `["scratch/**"]` |
| `scan.includeSubmodules` | boolean | `false` | `true` |
| `baseline.maxAgeDays` | integer | `0`, which means a baseline entry does not expire by age. | `90` |
| `fail-on` | string | `"error"` | `"warning"` |
| `severity.overrides` | table of rule identifier to severity | `{}` | `"pii.phone" = "warning"` |
| `rules.disabled` | array of `{ id, reason }` | `[]` | `id = "pii.email"`, with a reason |
| `packs.local` | array of strings | `[]` | `[".ndpa-check/packs/acme-customer-id.json"]` |

`scan.profile` MUST be `default` or `deep`. `fail-on` and every override value MUST be `error`, `warning` or `note`. `baseline.maxAgeDays` MUST be an integer greater than or equal to 0. `scan.maxFileBytes` MUST be an integer from 1 to 104,857,600. A value outside those sets is a tool error.

`scan.history` and `scan.maxFileBytes` follow the effective profile only when they are omitted. Setting `scan.history = false` in a `deep` profile turns history off (IQ-83). Setting `scan.history = true` in a `default` profile turns history on. A default-profile scan with history left off is the scan the 30-second continuous-integration budget is measured on. Turning history on, or selecting `deep`, is outside that budget.

Unknown keys produce a warning and do not fail the scan (IQ-17). The warning names the key. Known keys with the wrong type still fail, as above.

### 4.2 Invocation flags

Flags override the file for one run. They are not written back to the file.

| Flag | Default | Example | Effect |
|---|---|---|---|
| `--profile` | The effective `scan.profile`. | `--profile deep` | Sets the profile for this run. An explicit `scan.history` or `scan.maxFileBytes` in the file still wins over the new profile's default. |
| `--include-submodules` | Off, unless `scan.includeSubmodules` is true. | `--include-submodules` | Forces submodule enumeration on for this run. |
| `--changed-since <ref>` | Omitted, so the whole in-scope work tree is scanned. | `--changed-since origin/master` | Limits the work tree to files changed since that git reference (section 6). |
| `--evaluated-on <date>` | Section 3. | `--evaluated-on 2026-11-02` | Sets `evaluatedOn`. |

There is no flag for the fingerprint key and no environment variable that carries it. The only input is the file `.ndpa-check/fingerprint.key`. Continuous integration writes the secret to that path before the scan and MUST NOT print the secret.

### 4.3 Example that sets every key

```toml
schemaVersion = 1

fail-on = "warning"

[repository]
id = "github.com/acme/payments"

[scan]
profile = "deep"
history = false
maxFileBytes = 1048576
includeSubmodules = true
exclude = ["scratch/**"]

[baseline]
maxAgeDays = 90

[severity.overrides]
"pii.phone" = "warning"

[[rules.disabled]]
id = "pii.email"
reason = "Fixture addresses are synthetic and are reviewed each quarter."

[packs]
local = [".ndpa-check/packs/acme-customer-id.json"]
```

Rule identifiers used as `severity.overrides` keys MUST be quoted. An unquoted `pii.phone` is a nested TOML table, not a rule identifier.

### 4.4 Example that keeps every default

```toml
schemaVersion = 1
```

`init` writes this file when `ndpa-check.toml` is absent. It does not overwrite an existing file.

---

## 5. Profiles and file classes

The effective profile selects files and rules.

A rule runs when the effective profile is one of the profiles named in the rule's `profiles` array. An omitted `profiles` array means both `default` and `deep`, as the rule-pack specification says. A rule tagged only `deep` does not run in a `default` scan. A rule tagged only `default` does not run in a `deep` scan.

Each in-scope path is classified from its repository-relative form, using `/` separators:

1. **Staging configuration**, when any path segment equals `staging` under ordinal ignore-case comparison, or when the file name contains `.staging.` or `-staging.` under the same comparison. `appsettings.Staging.json` and `deploy/staging/app.yaml` are staging configuration.
2. **Data file**, otherwise, when the extension is `.csv`, `.tsv` or `.ipynb` under ordinal ignore-case comparison.
3. **Data file**, otherwise, when the extension is `.sql`, `.json`, `.jsonl` or `.ndjson` and any path segment equals `dump`, `dumps`, `seed` or `seeds` under ordinal ignore-case comparison.
4. **Ordinary file** otherwise. Source, configuration, test fixtures and migrations fall here.

`default` scans ordinary files. `deep` scans ordinary files, data files and staging configuration. A CSV fixture is a data file, so it is scanned in `deep` only. A SQL migration that is not under a dump or seed segment stays an ordinary file and is scanned in `default`.

The file limit is applied after classification, to the file's length in bytes.

---

## 6. Which files are enumerated

### 6.1 Git work tree

Inside a git work tree the scan enumerates tracked files with `git ls-files`. A shallow clone is a valid work tree. Untracked files are not scanned. Paths in every output use `/`, are relative to the repository root, and are ordered by ordinal comparison.

A gitlink (a submodule entry) is skipped unless submodules are included for this run. When they are included, each checked-out submodule is enumerated the same way, and its paths are prefixed with the submodule path. A submodule that is not checked out is skipped with a warning that names the path. Nested submodules are included when the parent is included.

A Git Large File Storage (LFS) pointer is skipped with a note. A pointer is a file whose bytes begin with the ASCII line `version https://git-lfs.github.com/spec/v1`. The scan MUST NOT fetch LFS objects.

`--changed-since <ref>` limits the work tree to tracked paths whose work-tree content differs from `<ref>` (`git diff --name-only <ref>`). The reference MUST resolve; if it does not, the scan is a tool error naming the reference. The flag does not change history scanning. Outside a git work tree the flag is a tool error.

### 6.2 Outside git

Outside a git work tree the scan walks the directory, applies `.gitignore` semantics for any `.gitignore` files in the tree, applies the built-in exclusions below, and writes a warning that the directory is not a git work tree. A `.gitignore` negation cannot bring a built-in exclusion back into the scan. History cannot run: if the effective `scan.history` is true, history is skipped and the warning says why. The work-tree scan still runs.

### 6.3 Built-in exclusions

These exclusions always apply. `scan.exclude` adds globs; it cannot remove a built-in exclusion. An empty `scan.exclude` means no extra globs.

A path is excluded when any segment equals one of these names under ordinal ignore-case comparison:

`.git`, `node_modules`, `bin`, `obj`, `dist`, `build`, `vendor`, `.ndpa-check`

A path is excluded when its file name equals one of these lock-file names under ordinal ignore-case comparison:

`package-lock.json`, `yarn.lock`, `pnpm-lock.yaml`, `bun.lock`, `packages.lock.json`, `Cargo.lock`, `composer.lock`, `Gemfile.lock`, `poetry.lock`, `Pipfile.lock`, `gradle.lockfile`, `pubspec.lock`

A path is excluded when its file name ends with `.min.js` or `.min.css` under ordinal ignore-case comparison.

`scan.exclude` entries use the same glob subset as section 5.3 of the rule-pack specification. A glob is relative to the repository root and uses `/` only. The only metacharacters are `*` and `**`. `?`, character classes and brace expansion are tool errors. `..`, a leading `/` and `\` are tool errors. `*` matches inside one path segment and does not match `/`. `**` matches across segments, including zero segments. A glob with no `/` matches a file in the repository root only, so `*.cs` does not match `src/a.cs` and `**/*.cs` does.

### 6.4 Binary files and encoding

The scan classifies the bytes of each remaining file in this order:

1. A leading UTF-16 LE byte-order mark (`FF FE`) or UTF-16 BE byte-order mark (`FE FF`) selects that encoding. The mark is not part of the text. This step happens before the binary check, because UTF-16 text contains NUL bytes.
2. A leading UTF-8 byte-order mark (`EF BB BF`) selects UTF-8, and the mark is not part of the text.
3. A NUL byte in the first 8,192 bytes selects a binary file. Binary files are skipped with a note.
4. Otherwise the file is UTF-8. Bytes that are not valid UTF-8 are skipped with a note.

UTF-16 without a byte-order mark is not decoded. Those files contain NUL bytes and are skipped as binary. Any other encoding is skipped with a note.

A file whose byte length is greater than the effective `scan.maxFileBytes` is skipped with a note. The comparison uses the raw byte length, before decoding.

---

## 7. History

History scanning reads git blobs reachable from `HEAD`. It does not read refs that `HEAD` does not reach. The scan takes the git blob object id of the bytes it actually read for each work-tree file. A historical blob with one of those ids is skipped, so the same bytes are not reported twice. A blob that exists only in an older commit, including a file that has since been deleted, is eligible.

Eligible blobs then pass the same exclusion, size, binary, encoding and profile class rules as work-tree files. The path used for those rules is the path in the commit that contained the blob.

History findings are emitted as a separate `history` list. P0-04 defines the object fields. This specification requires the list to be sorted by commit id (lowercase hex, ordinal), then path (ordinal), then line (ascending), then rule identifier (ordinal), then column (ascending). Matched values are never printed. History findings are not fingerprinted, are not written by `baseline`, and are not matched against the baseline. They are matched against exceptions and against `fail-on`.

The documentation for the default profile, and the human-readable summary of any scan that did not run history, MUST include this sentence: "A clean result does not mean git history was scanned." That sentence states the scope of the result. It is not a failure, and it is not an evidence warning. When history ran and the repository is shallow, the summary MUST include this sentence: "History scanning covered only the commits in this shallow clone." The shallow-clone sentence is a warning recorded for the evidence file. It does not change the exit code.

---

## 8. Severity and disabled rules

The three severities, from lowest to highest, are `note`, `warning` and `error`. They map one-to-one onto the SARIF levels of the same names. SARIF here means the Static Analysis Results Interchange Format.

`fail-on` names the lowest severity that fails the scan. `error` fails on `error` only. `warning` fails on `warning` and `error`. `note` fails on every reported finding that was not suppressed and not baselined.

`severity.overrides` replaces the pack severity for one rule identifier before exceptions are considered. The key is the rule identifier. An override whose rule identifier is not in a loaded pack is a tool error naming the identifier.

`rules.disabled` removes a rule before matching. Each entry has `id` (a rule identifier) and `reason` (a single-line string, trimmed, from 1 to 2,000 characters, with no carriage return or line feed). A missing or blank reason is a tool error naming the entry. A duplicate `id` is a tool error. An unknown rule identifier is a tool error. The reason is recorded in the evidence. A disabled rule produces no findings, so an exception for that rule does not apply.

Every override and every disabled rule is recorded in the evidence JSON (P0-04 carries the fields). Local packs are loaded before these identifiers are checked, so a `local.` rule may be overridden or disabled.

---

## 9. Local packs

`packs.local` is a list of paths to unsigned local rule packs. Paths are relative to the repository root, use `/`, and MUST NOT contain `..`, a leading `/` or `\`. A path that fails those rules, or that does not exist, is a tool error. There is no command-line flag to add a pack: adding a path is a configuration change. The pack loader opens these paths itself. The enumerator skips `.ndpa-check/`, and that skip does not stop the loader from reading a local pack stored there.

A local pack uses the rule-pack format with `provenance` of `local`. Every rule identifier MUST start with `local.`. A local rule identifier that equals any other loaded rule identifier is a load error, the same outcome as two packs defining one identifier. A local pack cannot replace an Invenio rule. Outputs that name the pack mark its provenance as `local`.

---

## 10. Repository identity

`repository.id`, when set, is the repository identity written into the evidence and used by the sink layout in P0-07. It is a string of 1 to 200 characters. It MUST NOT contain whitespace, `\`, a leading `/`, or a `..` segment. Those limits keep the value usable as a relative path under a sink root.

When the key is omitted, the identity is derived from the git remote:

1. Use the remote named `origin` when it exists. Otherwise use the only remote. If there is no remote, or more than one and none is named `origin`, the identity is empty and the scan records a warning. The scan still runs.
2. Drop user information, a port, a query and a fragment. A credential in a remote URL MUST NOT be copied into any output.
3. Lowercase the host only. Leave the path's case as given.
4. Strip one trailing `.git`.
5. Rewrite `git@host:path` and `ssh://git@host/path` to `host/path`. An HTTPS URL `https://host/path` becomes `host/path`.
6. Strip a trailing `/`.

A remote with no host, such as a local path, does not produce an identity. The repository sets `repository.id` when it needs one.

Examples:

| Remote | Identity |
|---|---|
| `git@github.com:Acme/payments.git` | `github.com/Acme/payments` |
| `https://user:secret@github.com/Acme/payments.git` | `github.com/Acme/payments` |
| `ssh://git@github.com:22/Acme/payments.git` | `github.com/Acme/payments` |

---

## 11. Exceptions

`exceptions.toml` is a TOML document with `schemaVersion = 1` and zero or more `[[exception]]` tables. A parser error, a duplicate key, an unknown key, or a `schemaVersion` other than 1 is a tool error. Unknown keys are rejected here, unlike in `ndpa-check.toml`, because a misspelled field would otherwise look like an accepted exception.

### 11.1 Fields

| Field | Type | Default | Example |
|---|---|---|---|
| `id` | string | None. Required. Unique in the file. | `"exc-fixture-bvn"` |
| `rule` | string | None. Required when `category` is omitted. | `"pii.bvn"` |
| `category` | string | None. Required when `rule` is omitted. | `"email"` |
| `paths` | array of strings | None. Required. One or more globs. | `["tests/fixtures/**"]` |
| `justification` | string | None. Required. | `"Synthetic values from the corpus script."` |
| `owner` | string | None. Required. | `"dpo@example.com"` |
| `expires` | string | None. Required. A civil date. | `"2026-11-01"` |
| `severity` | string | Omitted. The exception suppresses the finding. | `"note"` |

Exactly one of `rule` and `category` MUST be set. `id` matches `^[A-Za-z0-9][A-Za-z0-9._-]{0,63}$` and is unique under ordinal comparison. `justification` and `owner` are single-line strings with no carriage return or line feed. After trimming, `justification` is 1 to 2,000 characters and `owner` is 1 to 200 characters. `paths` uses the glob language from section 6.3. A duplicate glob inside one entry is a tool error. `expires` MUST be a real civil date. `severity`, when present, is `error`, `warning` or `note`.

A missing `id`, a missing or blank `justification`, a missing `owner`, a missing `expires`, a missing scope, or a duplicate `id` is a tool error. The message names the `id` when one was present, and otherwise names the entry's position in the file, and says what to add.

`rule` MUST name a loaded rule. `category` MUST equal the `category` of at least one loaded rule. Both comparisons are ordinal and case-sensitive. A value that matches nothing is a tool error.

### 11.2 Which exception applies

An exception is active on `evaluatedOn` when `evaluatedOn` is earlier than or equal to `expires`. Comparison is of civil dates. There is no time of day.

An active exception matches a finding when the path matches one of its globs and either its `rule` equals the finding's rule identifier or its `category` equals the finding's rule category.

When several active exceptions match, a `rule` entry outranks a `category` entry. If two matching entries have the same rank, the scan is a tool error naming both `id` values. The author narrows the globs or the rule identifier until one entry remains.

Disposition of a match:

- If the winning exception has no `severity`, the finding is suppressed. It does not fail the scan, it is not passed to the baseline, and the evidence records the exception as applied.
- If the winning exception has `severity`, the finding is reported at that severity. The exception is applied and recorded. The finding then goes through the baseline check in section 12.

An inactive (expired) exception does not match. The finding is a violation again, at the severity from the pack and any `severity.overrides` entry, and it can fail the scan.

### 11.3 Expiry example

This entry suppresses a Bank Verification Number rule inside fixtures until the end of 1 November 2026:

```toml
schemaVersion = 1

[[exception]]
id = "exc-fixture-bvn"
rule = "pii.bvn"
paths = ["tests/fixtures/**"]
justification = "Synthetic values from the corpus script."
owner = "dpo@example.com"
expires = "2026-11-01"
```

Take a finding of rule `pii.bvn` at `tests/fixtures/customer.cs`, pack severity `error`, and `fail-on = "error"`.

| `evaluatedOn` | What happens |
|---|---|
| 2026-11-01 | The exception is active. The finding is suppressed. The scan does not fail because of it. Evidence records `id` `exc-fixture-bvn` and the `entryHash` from section 11.4. |
| 2026-11-02 | The exception is expired. It does not apply. The finding is reported at `error` and the scan fails. The expired entry is not listed as an applied exception. |

The same dates with `severity = "note"` added to the entry behave differently while the entry is active: on 2026-11-01 the finding is reported at `note` and, with `fail-on = "error"`, does not fail the scan. On 2026-11-02 the override is gone with the exception, and the finding is an `error` again.

### 11.4 Entry hash

Evidence records `entryHash` for each applied exception: the lowercase hexadecimal SHA-256 of a fixed UTF-8 serialisation of the field values. The serialisation is not the file bytes, so comments and key order do not change the hash. Values are trimmed as section 11.1 requires. Each value is one line ended by a single LF (`U+000A`). The lines are, in order:

1. `id`
2. `rule`, or an empty line when the entry uses `category`
3. `category`, or an empty line when the entry uses `rule`
4. Each glob in `paths`, in the order written in the file
5. `justification`
6. `owner`
7. `expires`
8. `severity`, or an empty line when `severity` is omitted

The last line is followed by LF as well. For the entry in section 11.3 the lines are `exc-fixture-bvn`, `pii.bvn`, an empty line, `tests/fixtures/**`, `Synthetic values from the corpus script.`, `dpo@example.com`, `2026-11-01`, and an empty line. The hash is:

`f9de64c03afa66c3d6def1cb5d697dcb9e8f0f7c14824717ed5e1e31cbe6d8b0`

A second vector, with two globs and a severity, uses the lines `exc-log-email`, an empty line, `email`, `src/logs/**`, `src/legacy/**`, `Log samples are synthetic.`, `platform-team`, `2027-01-15`, and `note`. The hash is:

`b6fce505cd4b16001bcb22499e2adf5b6a0494058ada59d38070fe322bf8329e`

---

## 12. Baseline

`baseline.json` holds fingerprints of work-tree findings the team has accepted for now. It never holds a matched value, a path, a line or a rule identifier.

```json
{
  "schemaVersion": 1,
  "entries": [
    {
      "fingerprint": "00cca3a4746d65803db2fa8f18dfa1d9f05ca1c3c382ca92b2078b31a0e9c3a7",
      "addedOn": "2026-11-02"
    }
  ]
}
```

The fingerprint in the example is the first test vector in section 13.3. It shows the file shape and is not tied to a particular repository.

| Field | Type | Default | Example |
|---|---|---|---|
| `schemaVersion` | integer | None. Required, and it MUST be `1`. | `1` |
| `entries` | array | None. Required. It MAY be empty. | One entry, as above. |
| `entries[].fingerprint` | string | None. Required. 64 lowercase hex characters. | The fingerprint in section 13.3. |
| `entries[].addedOn` | string | None. Required. The civil date the fingerprint was first written. | `"2026-11-02"` |

An unknown field, a duplicate fingerprint, a bad date, a bad hex string, or a `schemaVersion` other than 1 is a tool error. The writer emits UTF-8 with no byte-order mark, LF line endings, a trailing newline, two-space indentation, and keys in the order `schemaVersion`, `entries`, then `fingerprint`, `addedOn`. Entries are sorted by fingerprint, ordinal. A second `baseline` run that sees the same findings on the same date MUST write the same bytes.

### 12.1 What `baseline` writes

`baseline` scans the work tree with the current configuration and does not scan history. It fingerprints findings that are still reported after disabled rules and after suppressing exceptions. A severity-only exception does not remove a finding from this set. For each fingerprint:

- If the file already contains it, `addedOn` is kept.
- If it is new, `addedOn` is `evaluatedOn`.
- A fingerprint that is no longer in the set is removed.

`baseline` requires a readable, well-formed key. A missing or malformed key is a tool error, because the command's only job is to write fingerprints. This is stricter than a scan, which continues without a key (section 13.4). The command MUST NOT print a matched value or the key.

### 12.2 What a scan does with the file

A work-tree finding that was not suppressed is baselined when its fingerprint equals an entry and the entry has not aged out. A baselined finding is still reported. Its SARIF `baselineState` is `unchanged`. It does not fail the scan. History findings are never baselined.

`baseline.maxAgeDays` of 0, or an omitted key, means entries do not age out. A positive value means the entry ages out when the number of civil days from `addedOn` to `evaluatedOn` is greater than `maxAgeDays`. The entry then does nothing: the finding is new again and can fail the scan. The file is not rewritten by a scan; only `baseline` rewrites it.

Example: `addedOn` is 2026-01-01 and `maxAgeDays` is 90. 1 April 2026 is 90 civil days later (31 + 28 + 31). On `evaluatedOn` 2026-04-01 the age is 90, which is not greater than 90, so the finding stays baselined. On 2026-04-02 the age is 91, the entry no longer applies, and the finding fails again when its severity meets `fail-on`.

Evidence reports the number of findings the baseline held back and the age, in civil days, of the oldest of those findings, so accepted debt stays visible. Stale entries that matched nothing are not part of that count. P0-04 defines the fields.

When the key is missing, the baseline is not applied, even if the file is present. Those findings are reported as new. The warning says that the baseline was not applied.

---

## 13. Fingerprints and the key file

### 13.1 Key file

`.ndpa-check/fingerprint.key` contains a cryptographic random 32-byte key, written as 64 lowercase hexadecimal characters followed by a single LF. `init` creates it when the file is absent. `baseline` creates it when the file is absent and `init` has not been run. Neither command overwrites an existing key.

Where the file system can express it, the file is readable and writable only by the user who created it. The key bytes MUST NOT appear in SARIF, evidence JSON, standard output, standard error, logs, a support bundle, or any other output.

A file that exists but is not exactly this shape, or that cannot be read, is a tool error. The error names the path and MUST NOT include the file contents. A file that is absent is not a tool error on a scan; section 13.4 applies.

### 13.2 Fingerprint bytes

The fingerprint is HMAC-SHA-256, as in RFC 2104, using SHA-256. The HMAC key is the 32 decoded bytes, not the hexadecimal text. The message is UTF-8, four lines, each ended by LF:

1. The rule identifier.
2. The repository-relative path, using `/`, with no leading `./` and no trailing `/`. Backslashes are replaced with `/`. The path is normalised to Unicode Normalization Form C. Its case is the case the enumerator returned.
3. The matched span, as text. Not the line, and not a redacted copy.
4. The occurrence index, as a decimal integer with no sign and no leading zeros.

The occurrence index counts earlier matches of the same rule, in the same file, whose matched span is ordinally equal to this span. The first match is `0`. Two different values in one file are both index `0` for their own value. Two rules do not share an index series.

Line numbers and line text are not inputs. A finding that moves to another line keeps its fingerprint. A rename changes the path and therefore the fingerprint.

The value emitted in SARIF `partialFingerprints` under the name `gatekeeper/v1` is the lowercase hexadecimal HMAC. History findings have no fingerprint.

### 13.3 Test vector

The file contents are the 64 hexadecimal characters below, followed by one LF. The HMAC key is the 32 bytes decoded from those characters. The LF is not part of the key.

`00112233445566778899aabbccddeeff00112233445566778899aabbccddeeff`

Message lines, each ended by LF: `pii.bvn`, `src/a.cs`, `12345678901`, `0`. The span `12345678901` is synthetic. The fingerprint is:

`00cca3a4746d65803db2fa8f18dfa1d9f05ca1c3c382ca92b2078b31a0e9c3a7`

The same message with occurrence index `1` fingerprints to:

`45a95165c37f218e00fb180ed710a9ac8aae15930204f1b4b483b9bae04442b6`

### 13.4 Missing key during a scan

A scan with no key file does not fail and does not change its exit code because of the missing key. Fingerprints are omitted, including SARIF `partialFingerprints`. If `baseline.json` is present it is not applied, and the warning says so. The warning text is:

"No fingerprint key is available, so fingerprints are omitted from the results."

When a baseline file is also present, the warning continues:

"The baseline was not applied. Findings that the baseline would have hidden are reported."

### 13.5 Git and continuous integration

`init` writes `.ndpa-check/.gitignore` if needed. The file is UTF-8, LF, and contains the line `fingerprint.key`. If the file already exists, `init` leaves every other line alone and appends `fingerprint.key` only when no existing line is exactly that text.

`doctor` warns when git tracks `.ndpa-check/fingerprint.key`. The warning does not by itself fail a scan. The warning text is:

"Git is tracking .ndpa-check/fingerprint.key. Anyone who can read the repository can reverse a fingerprint of a short identifier. Stop tracking the file, remove it from history, and replace the key."

Continuous integration supplies the same file as a secret file. The binary does not fetch it.

---

## 14. `init` and `doctor`

`gatekeeper-ndpa-check init` creates `.ndpa-check/` when it is absent, writes the key when it is absent, ensures `.gitignore` contains `fingerprint.key`, and writes the minimal `ndpa-check.toml` from section 4.4 when that file is absent. It does not write `exceptions.toml` or `baseline.json`. It does not print the key. It tells the user that continuous integration must receive the key as a secret file.

`doctor` performs the tracked-key check in section 13.5. The rest of `doctor` is P1-13. This specification adds the tracked-key warning to that command; it does not define `doctor`'s other checks.

---

## 15. Disposition and exit codes

For each match the engine produces, in this order:

1. Drop it when the file was not enumerated or the rule does not run in the effective profile.
2. Drop it when the rule is disabled. Record the disable.
3. Compute the severity: the override in `severity.overrides` when one exists, otherwise the pack severity.
4. If an active suppressing exception matches, record that exception and stop. The finding does not fail the scan.
5. If an active exception with `severity` matches, record that exception and replace the severity.
6. For a work-tree finding, when a key is available, compute the fingerprint. When that fingerprint matches a baseline entry that has not aged out, report the finding with `baselineState: unchanged`. It does not fail the scan.
7. Otherwise report the finding at the severity from step 3 or step 5. It fails the scan when that severity is the `fail-on` severity or a higher one.

History findings skip step 6.

Exit codes for a scan:

| Code | When |
|---|---|
| 0 | The load succeeded, and no reported finding that is still able to fail meets `fail-on`. |
| 1 | The load succeeded, and at least one such finding remains. |
| 2 | A tool error: the configuration, exceptions or baseline cannot be used, a pack failed to load, or the tree cannot be read. Exit 2 is never used because findings exist. |

Warnings do not change the exit code. They include an unknown settings key, a missing fingerprint key, a scan outside git, a shallow history, a submodule that is not checked out, and an empty derived repository identity. Each warning names the file or path it refers to and says what the user should do.

A tool error follows the same shape: what happened, why it matters, and what to do next, naming the file and the entry. A stack trace is shown only when `--verbose` is set.
