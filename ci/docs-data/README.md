# Docs-data generator and packer

Generates and packs the reference data published with each release: the per-rule metadata index, the command-line reference and the schema pages. The website repository consumes the pinned release artefact, never hand-edited pages.

## What is generated

- `docs-data/rules.json` — one self-contained entry per rule, built from the generated pack documents, the authored detection sources in `ci/rule-packs/rules/` and the citation register `rule-packs/citations.json`. Each entry carries the identifier, what it detects (`detects`), why it matters (`whyItMatters`, the register's explanation), the citation, the limits, how to fix a finding (`remediation`), how to raise a justified exception (`exceptionGuidance`, the rule's suppression hints), processor metadata when the rule carries it, and synthetic examples (`examples`, each `positive` or `near-miss`). Authored cases supply most examples; the generated location acceptance corpus in `tests/corpus/New-LocationCases.mjs` supplies the examples for the location family, associating a near-miss with the family and inventory kind in its name. Examples carry the case name and path, never a matched value (rule 10). Entries are sorted by identifier and every property bag is written in sorted key order.
- `docs-data/cli/*.md` — one page per command, embedding the exact help text captured from the built command line itself. The capture walks the real command tree by reading the `Commands:` section of each help text, so a new command appears automatically and the reference cannot drift from behaviour. Line endings are normalised to LF so the pages are byte-identical on every platform.
- The generated rule pages under `docs-data/rules/` are produced by `ci/rule-packs/build-detection.mjs`; this module verifies and packs them but does not regenerate them.

A detection source that ships without a description, limits or remediation fails this build, so an undocumented rule cannot reach the website.

## Commands

Build the command line first, then regenerate the committed reference data and verify it:

```powershell
dotnet build cli -c Release
node ci/docs-data/build.mjs --write   # regenerates docs-data/rules.json and docs-data/cli/
node ci/docs-data/build.mjs --check   # verifies the committed bytes; continuous integration runs this
node --test ci/docs-data/*.test.mjs   # unit tests for the builders
./ci/docs-data/Test-DocsData.ps1      # verifies the packed manifest and determinism
```

Pass `--cli=<path>` when the binary is not at `cli/bin/Release/net10.0/gatekeeper` (`gatekeeper.exe` on Windows).

## Artefact

`Pack-DocsData.ps1` packs `gate-keeper-docs-data-<product version>.zip` containing `manifest.json`, `rules.json`, `citations.json`, `rules/*.md`, `cli/*.md` and `schemas/*`. The contract is the bytes of `manifest.json` inside the zip: `name`, then the product, rule-pack and schemas versions, then `files` with each path and its lowercase SHA-256 digest, sorted by ordinal comparison. The zip container is transport only, because zip metadata is not stable across operating systems. The website pins the manifest bytes of a released artefact.

The committed `expected-manifest.json` pins the current artefact contents; continuous integration fails if a fresh pack would change it. Regenerate it after any real change with:

```powershell
./ci/docs-data/Pack-DocsData.ps1 -ManifestOutput ci/docs-data/expected-manifest.json
```

## Dependencies

Node.js 26 with no third-party packages, PowerShell 7 for packing, the .NET 10 command-line project for the help capture, and the existing authoring and corpus modules (`ci/rule-packs/`, `ci/pack-release/assembly/`, `tests/corpus/`). No new dependency is added.
