# Native release candidates and benchmarks

P1-15 builds the real CLI for Windows, Linux and macOS on x64 and arm64, including both Alpine/musl Linux variants (IQ-59 and IQ-88). Each job publishes with NativeAOT and trimming, treats their warnings as errors, runs every corpus repository with the existing synthetic local probe, and enforces the unchanged 8 MiB executable budget (IQ-90). Size optimisation is the supported .NET `OptimizationPreference=Size` setting, with stack-trace metadata removed from release binaries through `StackTraceSupport=false`. User-facing diagnostics still describe failures; release binaries do not carry runtime stack traces. Archives contain the binary with fixed timestamps and permissions, so the same binary produces identical archive bytes, with SHA-256 sidecars and a combined `SHA256SUMS` review artefact. The workflow prepares artefacts and measured release-note inputs; publishing a release remains an owner action.

macOS binaries are unsigned and are not notarised. macOS may display an operating-system warning when a downloaded binary is opened. Windows signing and the fresh Windows 11 Smart App Control test remain owner release checks; this workflow does not claim either has been completed (IQ-18).

## Reproduce native validation

```powershell
dotnet publish cli -c Release -r win-x64 -p:PublishAot=true -p:PublishTrimmed=true -p:OptimizationPreference=Size -p:StackTraceSupport=false -o artifacts/native
./ci/native-release/Test-PublishedCorpus.ps1 -Binary artifacts/native/gatekeeper-ndpa-check.exe
./ci/native-release/Pack-Native.ps1 -Binary artifacts/native/gatekeeper-ndpa-check.exe -RuntimeIdentifier win-x64 -Destination artifacts/release -ProductVersion 0.1.0
```

The corpus smoke test verifies the native scanner and its JSON output. It does not claim recall against the future bundled detection rules. The original corpus is copied before test configuration and a mandatory synthetic probe are added.

## Performance reference (IQ-60)

The existing corpus generator creates exactly 500 or 10,000 eligible files with pinned seed 20261104, a recorded generator version and parameter hash, fixed file sizes, and UTF-8 and UTF-16 encodings. The 500-file tree stays below 10 MiB. The generated trees are outside Git, use the default profile with history off, and have no exceptions, baseline, fingerprint key or disabled rules. Existing directories are never overwritten.

```powershell
node --test ci/native-release/benchmark-generator.test.mjs
./ci/native-release/Test-BenchmarkMath.ps1
./ci/native-release/Measure-Native.ps1 -BaselineBinary <baseline-native-binary> -CandidateBinary <candidate-native-binary> -Destination artifacts/benchmarks
```

The workflow compares the pull request's base commit with its candidate in the same job. After one discarded warm-up pair, three alternating pairs measure fresh-process version start, the complete 10,000-file scan and the 500-file continuous-integration scan. Each statistic is a median of three. A change above 20 per cent, a scan above 60 seconds, a 500-file scan above 30 seconds or an executable above 8 MiB fails. Reports retain binary digests, generator inputs and runner details. Fresh-process start does not flush the operating system's disk cache.

The owner selected provisional synthetic-pack benchmarks for the initial run. The workflow explicitly supplies the pinned synthetic regex-and-validator workload through `-ReferencePack`, records its digest and labels the report provisional. The embedded default-pack reference remains unavailable before Phase 2; these measurements do not claim the performance of that future pack. Omitting the explicit pack preserves the original embedded-pack contract and fails clearly when it is unavailable. Both benchmark arms enable `BenchmarkInstrumentation=true`, which records anonymous per-file matching times, current-thread allocation bytes and per-rule regex input counters during the same scan invocation. The matching operation is synchronous within each worker, so the allocation boundaries stay on one thread. The report includes nearest-rank per-file median and 95th percentile, allocations per file, and regex input bytes per call by rule and overall. Regex input bytes are the UTF-16 searchable suffix length passed to each search, including unsuccessful searches. All four metrics retain the same 20 per cent regression gate, and per-file times also retain the 10 ms and 50 ms absolute gates. Release builds compile out the instrumentation and do not recognise its metrics-output environment variable. A separate normal release binary supplies the size measurement. No specification or budget is changed here.

## Instrumented baseline compatibility

For a baseline that predates these counters, `Enable-BaselineInstrumentation.ps1` applies the committed measurement-only overlay after exact source-blob checks and `git apply --check`. A baseline that already has the instrumentation is left alone. The new counter types are copied from their ordinary C# source file after the overlay succeeds. A mismatched older source tree fails explicitly instead of being replaced with candidate implementation code. The overlay is generated from the instrumentation source change, not edited by hand:

```powershell
git diff --unified=0 707df6d -- Directory.Build.props cli/ScanCli.cs src/Engine/MatcherEngine.cs src/Engine/ScanOrchestrator.cs > ci/native-release/baseline-instrumentation.patch
$paths = @('Directory.Build.props', 'cli/ScanCli.cs', 'src/Engine/MatcherEngine.cs', 'src/Engine/ScanOrchestrator.cs')
$paths | ForEach-Object { "$(git rev-parse "707df6d:$_") $_" } > ci/native-release/baseline-instrumentation-base.txt
```

The synthetic reference pack drives repeated shape matching and named-validator rejection over the generated alphabetic text, with no findings or matched values emitted. This provides measurable per-file work and live regex counters without changing either comparison arm's input. Counter tests assert exact search counts and input-byte sums and prove that findings are unchanged. Run them with `dotnet test tests/Engine.Tests -p:BenchmarkInstrumentation=true --filter FullyQualifiedName~BenchmarkInstrumentationTests`.