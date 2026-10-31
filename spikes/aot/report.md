# NativeAOT feasibility spike — report

Work package P0-16 (issue #9). This report records the measured answers to the five questions in the card, with the numbers the owner needs to set the binary-size budget. It is the input to work package P0-03 (the rule-pack format's regular-expression profile) and to engineering Architecture Decision Record E-015 (the decision to build the command-line interface on C# / .NET 10 with Native ahead-of-time compilation).

## Method and environment

- **Toolchain:** .NET SDK 10.0.302, pinned by `global.json`.
- **Publish command:** `dotnet publish -c Release -r <rid> -p:PublishAot=true` with the trimming and ahead-of-time (AOT) analysers enabled by `Directory.Build.props`.
- **Where each number came from:** every size and timing figure in this report was measured on a local Windows x64 machine (the `win-x64` runtime identifier). The other runtime identifiers, and the cost of the build matrix, are produced by the workflow `.github/workflows/aot-matrix.yml`, which runs when this pull request opens; the per-identifier sizes appear in its job logs. A figure marked "pending" has not yet run in continuous integration and is stated honestly as such.
- **Warning handling:** the spike projects deliberately do not promote trim or AOT warnings to errors, so that a package that warns still yields a measurable binary. "Zero warnings" means the publish step emitted no `ILxxxx` warning lines.

## Question 1 — Ed25519 under NativeAOT (IQ-11)

| Library | Version | Zero trim/AOT warnings | RFC 8032 test vectors | Binary size added |
|---|---|---|---|---|
| `BouncyCastle.Cryptography` | 2.7.0 | Yes | Pass (TEST 1, 2, 3 and 1024) | about 76 KiB |
| `Rebex.Elliptic.Ed25519` | 1.2.2 | Yes | Pass (TEST 1, 2, 3 and 1024) | about 253 KiB |

Both libraries sign and verify the four pure-Ed25519 test vectors from RFC 8032 section 7.1, and both pass a sign-then-verify round trip, with zero trim and AOT warnings.

The surprising result is size: `BouncyCastle.Cryptography` adds roughly 76 KiB, while the "small managed fallback" `Rebex.Elliptic.Ed25519` adds roughly 253 KiB, because Rebex carries a .NET Standard compatibility dependency that trims less cleanly than Bouncy Castle's zero-dependency assembly.

**Answer.** `BouncyCastle.Cryptography` 2.7.x verifies and signs under NativeAOT with zero warnings, adds about 76 KiB, and needs no fallback. The Rebex fallback pre-approved under IQ-11 is not required. Recommendation: keep `BouncyCastle.Cryptography` behind the planned `ISignatureVerifier` interface; RFC 8032 vectors remain the acceptance test (rule 14 of `AGENTS.md`).

## Question 2 — Runtime regular expressions (IQ-14)

Measured over a synthetic corpus of 10,000 files (about 7.7 MiB total, generated with a fixed seed). Patterns follow the portable profile: ASCII character classes and bounded quantifiers only.

| Pattern | Interpreter | `RegexOptions.NonBacktracking` |
|---|---|---|
| `[0-9]{11}` (11-digit value) | 63.1 ms total, 0.0063 ms median per file | 49.7 ms total, 0.0042 ms median per file |
| `[Bb][Vv][Nn][0-9]{11}` (keyword plus value) | 4.2 ms total | 5.3 ms total |

Both engines are far inside the performance budget of 10 ms median per file and 60 s for 10,000 files (NFR-010/011). `NonBacktracking` is about 21 % faster on the match-heavy pattern; the interpreter is marginally faster on the pattern that never matches, but the difference is under 2 ms in absolute terms.

The pathological-pattern result is the deciding one:

| Pattern `(a+)+$` on `'a'` x 30 plus `!` (no match) | Result |
|---|---|
| Interpreter (with a 1 second timeout) | **TIMEOUT** after about 1 s (catastrophic backtracking) |
| `RegexOptions.NonBacktracking` | no match in about 0.02 ms |

`NonBacktracking` is immune to catastrophic backtracking by construction, which matters because pack patterns arrive as data and cannot be pre-vetted by hand.

**Answer.** Use `RegexOptions.NonBacktracking` in the engine, with the pack-schema lint enforcing the IQ-14 portable profile (ASCII classes, bounded quantifiers, no look-around or back-references), so every pattern stays inside what `NonBacktracking` supports. No keyword pre-filter is needed at the 10,000-file scale; `.NET`'s `SearchValues<string>` remains available if the Phase 2 benchmark ever needs one.

**Input to P0-03.** The rule-pack specification should state that patterns are compiled with `RegexOptions.NonBacktracking` (and `CultureInvariant`), and that the schema's pattern profile is the one recorded against IQ-14. This report confirms that profile runs at full speed and safely.

## Question 3 — TOML and SARIF under NativeAOT (IQ-17)

| Library | Version | Zero trim/AOT warnings | Result | Binary size added |
|---|---|---|---|---|
| `Tomlyn` (source-generated `TomlSerializerContext`) | 2.10.1 | Yes | Parses a representative `ndpa-check.toml` (nested tables, arrays, a dictionary) and round-trips it | about 270 KiB |
| `SarifKit` | 1.0.0 | Yes | Writes a SARIF 2.1.0 log and reads it back to the same model | about 2.9 MiB |

`Tomlyn` 2.10.x with a `[TomlSerializable]` root and a `TomlSerializerContext` parses the configuration file with zero trim warnings, confirming IQ-17's recommendation. The round trip through the source-generated writer also works.

`SarifKit` writes and reads a SARIF 2.1.0 log with zero trim warnings. Its cost is the headline size finding: the complete SARIF 2.1.0 object model plus its `System.Text.Json` source-generated serialiser metadata adds about 2.9 MiB, which is by far the largest single component in the binary.

**Answer.** Both libraries are AOT-clean and functional. `Tomlyn` is confirmed. `SarifKit` works but dominates the binary size; see the E-015 note below for the budget consequence.

## Question 4 — Binary size and cold start

All figures are `win-x64`, NativeAOT, measured as the published executable's byte length.

| Component | Size added |
|---|---|
| Empty console application (baseline) | about 1.06 MiB |
| `System.Text.Json` source generation | about 0.95 MiB |
| Regular expressions (interpreter plus `NonBacktracking`) | about 1.2 MiB |
| Ed25519 (`BouncyCastle.Cryptography`) | about 76 KiB |
| TOML (`Tomlyn`) | about 270 KiB |
| SARIF (`SarifKit`) | about 2.9 MiB |
| **Everything together** | **about 5.43 MiB** (5,691,392 bytes) |

Cold start on Windows, measured as the wall-clock time to launch and finish the "everything" binary over 15 runs: minimum 18.7 ms, median 19.6 ms, maximum 26.9 ms. This is dominated by Windows process creation and console attachment — an empty NativeAOT binary takes about 16 ms the same way — so the ahead-of-time runtime itself adds essentially no start-up cost.

**Answer.** A realistic `gatekeeper-ndpa-check` binary is about 5.4 MiB on `win-x64`. Cold start is negligible. The per-runtime-identifier sizes for `win-arm64`, `linux-x64`, `linux-arm64` and the two musl variants come from the matrix workflow (pending first run).

## Question 5 — Build matrix on GitHub-hosted runners (IQ-18)

The workflow `.github/workflows/aot-matrix.yml` publishes the "everything" project for six runtime identifiers:

| Runtime identifier | Runner | Approach |
|---|---|---|
| `win-x64` | `windows-latest` | native |
| `win-arm64` | `windows-11-arm` | native |
| `linux-x64` | `ubuntu-latest` | native |
| `linux-arm64` | `ubuntu-24.04-arm` | native |
| `linux-musl-x64` | Alpine container on `ubuntu-latest` | native inside the container |
| `linux-musl-arm64` | Alpine container on `ubuntu-24.04-arm` | native inside the container |

The arm64 identifiers build on the arm64-hosted runners, and the musl identifiers build inside an Alpine container on the matching architecture, because NativeAOT does not cross-compile between C libraries in practice. The matrix cost — the per-job duration and the number of runner-minutes — is measured from the first run of that workflow, which happens when this pull request opens. The results are recorded in the workflow logs and can be copied here once available.

**Answer.** The matrix is expressible on GitHub-hosted runners with no same-OS cross-compilation beyond what NativeAOT supports. The observed matrix cost is pending the first continuous-integration run.

## Input to E-015 (NativeAOT feasibility)

The spike confirms the assumption the command-line interface rests on:

- Every planned dependency (`BouncyCastle.Cryptography`, `Tomlyn`, `SarifKit`) is AOT-clean with zero trim and AOT warnings, and the fallback candidate (`Rebex.Elliptic.Ed25519`) is too.
- RFC 8032 vectors pass under NativeAOT.
- The full Windows and Linux matrix, including the musl variants, is expressible on GitHub-hosted runners.
- Cold start is negligible.

The decision the owner now makes is the **binary-size budget**. The realistic binary is about 5.4 MiB, and `SarifKit` contributes about 2.9 MiB of that. If the budget is tighter than roughly 5.4 MiB, the options, in order of effort, are: (a) accept the size; (b) write SARIF through a narrower hand-rolled `System.Text.Json` model covering only the SARIF members the CLI emits, dropping `SarifKit`; or (c) reconsider whether SARIF output is written by the trimmed CLI or by a separate, untrimmed step. This report records the numbers for that decision but does not make it.

## Honest gaps

- Only `win-x64` is measured locally. The five other runtime identifiers and the matrix cost are produced by the workflow on GitHub-hosted runners and are pending its first run.
- The sizes are point-in-time for the package versions pinned in `Directory.Packages.props`; a future package version changes them.
- The regex benchmark uses one representative corpus shape. The definitive corpus is built in Phase 1 (P1-02); this spike answers feasibility, not the final performance gate.
- This is a spike: the folder `spikes/aot/` is deleted after Phase 1, so none of this is a shipping dependency.
