# Pull-request guard-rails

This folder contains the scripts and small tools that enforce the CI guard-rails defined in P0-12. The GitHub Actions workflow in `.github/workflows/pr-guardrails.yml` wires them into the pull-request gate.

## Gates

| Gate | Script / tool | What it checks |
|---|---|---|
| Trace-ID lint | `Invoke-TraceIdLint.ps1` | The pull-request title or body contains a recognised trace identifier (WP, NFR, UC, F, E, IQ, ADR). |
| Build and test matrix | `dotnet build` / `dotnet test` | The solution builds and tests pass on Windows and Linux, x64 and arm64. |
| AOT and trim warnings | `dotnet publish -p:PublishAot=true` | The CLI publishes under NativeAOT with zero trim/AOT warnings. |
| Zero-egress banned-API check | `BannedApiCheck` | No built assembly references `System.Net.*` types except in an allowed sink assembly. |
| Pack-schema lint | `Invoke-PackSchemaLint.ps1` | Every file in `rule-packs/` is valid JSON and, once `schemas/rulepack.schema.json` exists, validates against it. |
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
