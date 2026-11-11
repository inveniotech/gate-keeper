# Gate-Keeper

Gate-Keeper scans source code for the personal data that its rule packs describe, and proves that a repository is safe to build. The engine, the command-line interface and the file formats carry no jurisdiction: each law is a set of rule packs. The bundled rule packs cover the Nigeria Data Protection Act 2023 (NDPA) and the General Application and Implementation Directive (GAID). This repository is the public monorepo: everything free ships here under the Apache-2.0 licence, including the command-line interface, the rule engine, the bundled rule packs, the schemas, the middleware and the CI integrations. The first release ships middleware for .NET, Node and the JVM. The Python adapter follows that release. The command-line interface ships for Windows, Linux and macOS.

The scan command now runs configured local rule packs and prints a console summary or evidence JSON. Baseline, installation diagnostics and a local masked support bundle are available. The bundled detection pack arrives in Phase 2.

## Layout

| Path | What it is |
|---|---|
| `cli/` | The `gatekeeper` command-line interface. Argument surface and version output (P1-01). |
| `src/Engine/` | The rule engine library, shared by the CLI and the .NET middleware. Domain model and JSON source generation (P1-01). |
| `src/Signing/` | The internal Ed25519 signature verification behind `ISignatureVerifier` (P1-04). |
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
