# Gate-Keeper

Gate-Keeper scans source code for personal data that the Nigeria Data Protection Act 2023 protects, and proves that a repository is safe to build. This repository is the public monorepo: everything free ships here under the Apache-2.0 licence, including the command-line interface, the rule engine, the bundled rule packs, the schemas, the middleware for four stacks and the CI integrations.

This is the scaffold. The scan command does not exist yet; the placeholder projects build and pass their tests.

## Layout

| Path | What it is |
|---|---|
| `cli/` | The `gatekeeper-ndpa-check` command-line interface. Placeholder. |
| `src/Engine/` | The rule engine library, shared by the CLI and the .NET middleware. Placeholder. |
| `rule-packs/` | The bundled rule packs. Empty until Phase 2. |
| `schemas/` | The JSON Schemas for the open formats, published as a versioned artefact. |
| `middleware/` | One folder per stack: `dotnet`, `node`, `jvm`, `python`, plus reserved placeholders for `php`, `go` and `rust`. Each folder states its status and toolchain. |
| `ci/` | Scripts the pipelines use. |
| `docs/` | Specifications and Architecture Decision Records for the open formats. |
| `docs-data/` | The generator for the reference data published with each release. |
| `tests/` | The test projects, including the golden fixture corpus under `tests/corpus/`. |
| `samples/` | One sample application per stack. Empty until Phase 3. |

## Build and test

.NET 10 is pinned in `global.json`. From the repository root:

```powershell
dotnet build
dotnet test
```

NuGet versions are pinned in `packages.lock.json`. Restore with `dotnet restore --locked-mode`.

## Licence

Apache-2.0. See `LICENSE`. Every source file begins with the SPDX identifier `SPDX-License-Identifier: Apache-2.0`; the policy is recorded in `.editorconfig`.
