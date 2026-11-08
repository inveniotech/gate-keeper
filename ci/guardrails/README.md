# Pull-request guard-rails

## Detection quality (P2-11, NFR-013 and NFR-026)

The detection-quality workflow measures the complete test suite with the pinned,
test-only `dotnet-coverage` tool, version 18.12.0. The tool is not referenced by
the engine or the published binary. Its adoption is proposed for owner review
with this card; it adds no shipped dependency. The existing matrix remains in
place. Coverage counts only the engine assembly, deduplicating source lines
across classes. It includes generated engine code and does not remove uncovered
lines. Missing, malformed, empty or ambiguous reports fail closed. Exactly
80 per cent passes; 79.9 per cent fails without rounding.

```powershell
./ci/guardrails/Test-EngineCoverageGate.ps1
dotnet-coverage collect 'dotnet test -c Release --no-restore' -f cobertura -o artifacts/coverage/solution.cobertura.xml
./ci/guardrails/Invoke-EngineCoverageGate.ps1 -Report artifacts/coverage/solution.cobertura.xml
```

The same run executes `DetectionPackTests` and `CorpusGateTests`: per-rule and
overall error-level false positives must remain at or below five per cent;
unexpected synthetic findings, missed expectations and stale reports fail.
Warning and note findings remain separate. Real public-repository precision
remains unmeasured until owner triage is complete under P2-06/P2-07. Mutation
testing is optional and is not introduced here.

The first local full-suite measurement on Windows covered 4,118 of 7,371 distinct
engine source lines (about 55.9 per cent). The proposed gate correctly fails
that baseline. This PR must remain a draft until the coverage deficit is fixed
and the test-only tool is reviewed; no threshold reduction is proposed.

This folder contains the scripts and small tools that enforce the CI guard-rails defined in P0-12. The GitHub Actions workflow in `.github/workflows/pr-guardrails.yml` wires them into the pull-request gate.

## Gates

| Gate | Script / tool | What it checks |
|---|---|---|
| Trace-ID lint | `Invoke-TraceIdLint.ps1` | The pull-request title or body contains a recognised trace identifier (WP, NFR, UC, F, E, IQ, ADR). |
| Build and test matrix | `dotnet build` / `dotnet test` | The solution builds and tests pass on Windows and Linux, x64 and arm64. |
| AOT and trim warnings | `dotnet publish -p:PublishAot=true` | The CLI publishes under NativeAOT with zero trim/AOT warnings. |
| Zero-egress banned-API check | `BannedApiCheck` | No built assembly references `System.Net.*` types except in an allowed sink assembly. |
| Pack-schema lint | `Invoke-PackSchemaLint.ps1` | Every content pack, including nested templates, validates against the complete rule-pack JSON Schema. The named citation metadata file and schema itself are not content packs. |
| Banned-terms lint | `../glossary/Invoke-GlossaryLint.ps1` | Prose and identifiers conform to `docs/glossary.json`. |
| en-GB spelling lint | `Invoke-SpellingLint.ps1` | Markdown, source and configuration files spell-check with an en-GB dictionary. |
| Dependency licence check | `Invoke-DependencyLicenceCheck.ps1` | Every package declared in `Directory.Packages.props` is in the allow-list and does not carry a denied licence. |

## Running locally

From the repository root:

```powershell
pwsh ./ci/guardrails/Invoke-TraceIdLint.ps1 -Title 'feat(p0-12): add guard-rails' -Body 'Implements P0-12.'
pwsh ./ci/guardrails/Invoke-PackSchemaLint.ps1
pwsh ./ci/guardrails/Invoke-SpellingLint.ps1
pwsh ./ci/guardrails/Invoke-DependencyLicenceCheck.ps1
dotnet run --project ci/guardrails/BannedApiCheck/BannedApiCheck.csproj -- src cli tests --exclude "Microsoft.TestPlatform.*.dll" --exclude "xunit.*.dll" --exclude "testhost*.dll" --exclude "JsonPointer.Net.dll"
```

## Exit codes

All scripts follow the convention used elsewhere in the repository:

- `0` — check passed.
- `1` — check failed (a rule was broken).
- `2` — tool error (missing file, malformed input, etc.).

P2-11-01 (#102) replaces the pack lint's syntax-only placeholder with PowerShell's existing `Test-Json` schema validation, without a new dependency. Absolute or repository-relative pack/schema paths work from any current directory. Pack paths are checked in ordinal order. Invalid packs produce exit 1, with every invalid file reported by relative path; input content and parser exceptions are never printed. Missing directories, missing schemas and unreadable or malformed schema inputs fail with exit 2. An empty existing pack directory is valid. `citations.json` is explicitly metadata and is checked by its own authoring tests rather than misclassified as a rule pack.

Run `pwsh ./ci/guardrails/Test-PackSchemaLint.ps1` for the offline acceptance harness covering valid content, schema-invalid JSON, malformed JSON, over-bound shapes, metadata exclusion, empty directories and missing schemas. It runs before the real pack lint in the existing CI job. This strengthens the existing gate; it changes no coverage or performance threshold.
