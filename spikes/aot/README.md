# NativeAOT feasibility spike (P0-16)

Throwaway experiments that answer the questions in work package P0-16 before Phase 1 starts. Nothing in this folder is a product dependency; the whole folder is deleted after Phase 1.

Each sub-project is a small console application that exercises one assumption and prints deterministic pass/fail lines. Binary-size and cold-start measurements are produced by publishing each project with NativeAOT and reading the resulting executable, exactly as the shipping command-line interface will be built.

## Questions and projects

| Question | Project | What it measures |
|---|---|---|
| 1. Ed25519 (IQ-11) | `Ed25519Spike/` | Whether `BouncyCastle.Cryptography` and `Rebex.Elliptic.Ed25519` sign and verify the RFC 8032 test vectors under NativeAOT with zero trim and AOT warnings, and what each adds to the binary size. |
| 2. Runtime regex (IQ-14) | `RegexSpike/` | Throughput of `RegexOptions.NonBacktracking` versus the interpreter over a synthetic 10,000-file repository, and behaviour with a pathological pattern. |
| 3. TOML and SARIF (IQ-17) | `TomlSarifSpike/` | Whether `Tomlyn` with a source-generated `TomlSerializerContext` parses `ndpa-check.toml` with zero trim warnings, and whether `Sarif.Sdk` writes a SARIF log under NativeAOT with zero warnings. |
| 4. Binary size and cold start | `EverythingSpike/` | The combined binary size per runtime identifier with System.Text.Json source generation, regex, Ed25519 and TOML included, and the cold-start time. |
| 5. Build matrix (IQ-18) | `.github/workflows/aot-matrix.yml` | Whether `win-x64`, `win-arm64`, `linux-x64`, `linux-arm64` and the two musl variants publish on GitHub-hosted runners, and the matrix cost. |

## Prerequisites

The .NET 10 SDK pinned in `global.json` (10.0.302). On Windows, NativeAOT needs the Visual Studio C++ toolchain; on Linux it needs `clang` and `zlib1g-dev`.

## Run a single spike

From the repository root:

```powershell
dotnet run --project spikes/aot/Ed25519Spike
dotnet run --project spikes/aot/RegexSpike
dotnet run --project spikes/aot/TomlSarifSpike
```

`dotnet run` uses the just-in-time compiler. To measure the AOT behaviour — the point of the spike — publish with NativeAOT:

```powershell
dotnet publish spikes/aot/Ed25519Spike -c Release -r win-x64 -p:PublishAot=true
```

Trim and AOT warnings are deliberately **not** promoted to errors inside these projects, so that a package that produces warnings still yields a binary whose size can be measured. The shipping projects keep the strict `TreatWarningsAsErrors` setting. Warnings appear in the publish output; a "zero warnings" result means the output for that step contains no `ILxxxx` warning lines.

## Measure a binary

```powershell
# Size in bytes.
(Get-Item spikes/aot/Ed25519Spike/bin/Release/net10.0/win-x64/publish/Ed25519Spike.exe).Length

# Cold start, median of several runs (Windows PowerShell).
1..9 | ForEach-Object { $sw = [System.Diagnostics.Stopwatch]::StartNew(); & spikes/aot/Ed25519Spike/bin/Release/net10.0/win-x64/publish/Ed25519Spike.exe | Out-Null; $sw.Stop(); $sw.ElapsedMilliseconds }
```

The measured numbers for this machine are recorded in `report.md`.
