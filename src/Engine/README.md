# Rule engine

The rule engine library shared by the command-line interface and the .NET middleware. Work package P1-01 (issue #11) added the immutable domain types the later Phase 1 work builds on: the pack loader (P1-03) fills rules with matchers, the matcher engine (P1-06) produces findings, and the scan orchestrator (P1-07) assembles scan results. Work package P1-05 (issue #15, IQ-21 and IQ-83) added the file enumerator, so the scan input is now the engine's too.

## Public contract

- The immutable domain types in this folder's `.cs` files: `RulePack`, `Rule`, `Citation`, `RedactionOptions`, `ProcessorInfo`, `Finding`, `Location`, `Fingerprint`, `ScanResult`, plus the closed enums `Severity`, `BaselineState`, `LocationKind`, `PackProvenance` and `ExitOutcome`. Every type is a `sealed` record, its lists are read-only, and its constructor validations match the results format and the rule-pack format (1-based lines and columns, 64-character lowercase hexadecimal fingerprints).
- `EngineProduct` stamps the facts every artefact carries (NFR-031): the product name `gatekeeper`, the engine version, and the bundled rule pack version, which is `null` until the pack build step bundles one, so the version output states the absence rather than inventing a number.
- `EngineJsonSerializerContext` is the System.Text.Json source-generation context for every serialised type. There is no reflection-based serialisation anywhere in the engine or the interface (rule 7).
- A finding never carries the matched value as data. Outputs name the rule and the location (rule 10).
- `FileEnumerator` is the one enumeration of the scan input (sections 6 and 7 of the configuration specification). Inside a git work tree it lists the files the index tracks (`git ls-files`), and outside one it walks the directory, applies the `.gitignore` semantics for every `.gitignore` in the tree and records the `scan.outside-git` warning. The built-in exclusions (the excluded directory segments, the lock-file names and the minified-file names), the `scan.exclude` globs (`PathGlob`, sections 6.3 and 5.3), the per-file byte limit, the binary probe (a NUL byte in the first 8,192 bytes), the encodings UTF-8 and UTF-16 with a byte-order mark, the Git LFS pointer skip, the un-checked-out submodule warning, the `--changed-since` work-tree limit and, on request, the history blobs reachable from `HEAD` are all the enumeration's work. When `git` is not installed at all, the enumeration degrades to the directory walk and records the `scan.outside-git` warning, because git is guaranteed on the runners the product targets and silent absence at a customer site must still produce an honest determinable result.
- The enumeration hands `FileEnumerationResult` to the caller: `Files` (the scanned work-tree files with their decoded text, in ordinal path order), `History` (the history candidates with the 40-character commit identifier of the newest commit whose tree still contains the blob, the path, and the decoded text, in commit identifier then path order), `Warnings` (the closed-set notices the evidence file records, in code then message order) and `Notes` (the skip sentences for binary, oversized, LFS pointer, non-UTF-8 and unreadable candidates, in ordinal path order). All paths are forward-slash, relative to the repository root and Unicode-normalised to Normalisation Form C (rule 22).
- `HistoryCandidate` and the history list exist so the scan orchestrator (P1-07) can run the matcher over historical bytes and emit the `history` array the results format defines: they are a scan input, never a finding, and they carry no fingerprint and no baseline state (IQ-83).
- `EngineToolException` is the one tool error the engine throws: the message is the complete en-GB sentence the console shows, and the exit-code mapping stays in the CLI (rule 13, rule 23).
- `PathGlob` compiles and matches the `scan.exclude` glob subset; `FileEnumerationOptions` validates its values at construction, before any file is read (rule 28).

## Dependencies

None. `System.Text.Json` ships in the .NET shared framework, so the engine adds no package. The matcher grammar and the pack content document's exact serialisation arrive with P1-03. The enumeration shells out to the `git` executable, which is required on every runner the product targets; there is no network access anywhere in the engine (rule 5), and `git` is asked for objects the local object database already holds, never to fetch (IQ-21).

## How to test

From the repository root:

```powershell
dotnet test --filter FullyQualifiedName~Engine.Tests
```

The enumerator's tests build real throw-away git repositories under the machine's temporary directory with fixed author identities and fixed commit dates, so the object identifiers are byte-stable across machines and runs (rule 26). One test skips itself when the machine does not grant the symbolic-link privilege the fixture needs.
