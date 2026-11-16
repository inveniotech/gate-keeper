# Native release candidates and benchmarks

P1-15 builds the real CLI for Windows, Linux and macOS on x64 and arm64, including both Alpine/musl Linux variants (IQ-59 and IQ-88). Each job publishes with NativeAOT and trimming, treats their warnings as errors, runs every corpus repository with the existing synthetic local probe, and enforces the 10 MiB executable budget (10,485,760 bytes), amended by the owner on 5 November 2026 (IQ-90). Size optimisation is the supported .NET `OptimizationPreference=Size` setting, with stack-trace metadata removed from release binaries through `StackTraceSupport=false`. User-facing diagnostics still describe failures; release binaries do not carry runtime stack traces. Archives contain the binary with fixed timestamps and permissions, so the same binary produces identical archive bytes, with SHA-256 sidecars and a combined `SHA256SUMS` review artefact. The workflow prepares artefacts and measured release-note inputs; publishing a release remains an owner action.

macOS binaries are unsigned and are not notarised. macOS may display an operating-system warning when a downloaded binary is opened. Windows signing and the fresh Windows 11 Smart App Control test remain owner release checks; this workflow does not claim either has been completed (IQ-18).

## Publish an alpha for code-signing review (P4-01, #46)

The `Publish CLI alpha` workflow runs when an owner pushes a tag of the form `cli-v0.2.0-alpha.1`. It calls the existing native workflow at that same tagged commit, including all eight runtime identifiers, corpus smoke tests, size checks and paired benchmark gates. No production key or SignPath account is needed for this unsigned alpha. Release signing and signature-verifying installers remain outstanding under #46; this workflow cannot publish stable or beta releases.

After merging the workflow, an owner runs these commands from an up-to-date checkout of the intended release commit. The numeric version must match `EngineProduct.EngineVersion` in that commit. Choose a new positive alpha number for each release; never move a published tag.

```sh
git tag -a cli-v0.2.0-alpha.1 -m "Gate-Keeper 0.2.0 alpha 1 for code-signing review"
git push origin cli-v0.2.0-alpha.1
```

Watch `Publish CLI alpha` in GitHub Actions. It validates the tag before starting native builds. Once the builds and benchmarks succeed, it verifies every archive against both its metadata and checksum sidecar, and rejects missing platforms, unexpected files, mismatched engine versions and damaged archives. It creates a fresh `SHA256SUMS` with ordinally sorted filenames, generates GitHub build attestations for the eight archives and manifest, creates a draft containing all nine assets and the measured release notes, then publishes it as a prerelease without making it the latest release. The owner pushing the tag authorises that publication. Repository Actions policy must permit the job's `contents: write`, `id-token: write` and `attestations: write` permissions; no personal access token is required.

The public review link is `https://github.com/inveniotech/gate-keeper/releases/tag/cli-v0.2.0-alpha.1`. Send that link to the signing provider after the workflow finishes. Windows executables are unsigned; SmartScreen may warn and Smart App Control may block them. macOS executables are unsigned and are not notarised. The release notes explicitly state that `SHA256SUMS` has no detached Ed25519 signature. The archive checksums detect changed bytes; they do not authenticate the publisher. Customers can verify the workflow provenance separately:

```sh
gh attestation verify gatekeeper-win-x64.zip --repo inveniotech/gate-keeper
```

The alpha suffix identifies the release; the CLI and evidence retain their existing numeric engine version. The CLI does not yet bundle a rule pack. Use configured local rule packs for evaluation. This alpha does not claim completed clean-machine installation checks or production readiness.

If any build, validation or attestation fails, the workflow does not publish a release. If asset upload or publication fails after draft creation, inspect the draft and run logs before retrying. The workflow never overwrites an existing release or its assets. A complete draft can be reviewed and published manually; an incomplete draft needs owner recovery. Releases created with the workflow token do not trigger other release-event workflows, so the separate docs-data attachment workflow will not run automatically.

Run the deterministic assembly and rejection tests with `node --test ci/native-release/alpha-release.test.mjs`. They run in the native workflow on pull requests and before an alpha build. They use synthetic archive bytes and do not publish anything. Existing native archive-integrity tests separately check real ZIP packaging.

## Verified installers (P4-01, #46)

The owner approved Node.js 26 or later as an installation prerequisite on 16 November 2026. `install.ps1` and `install.sh` use its built-in Ed25519 and SHA-256 operations with no third-party packages. They also require `tar`, supplied by Windows 11 and ordinarily installed on Linux and macOS. Use these scripts from a reviewed repository checkout; retain `ci/native-release/install.mjs` beside them in its repository location. They download only the explicitly requested CLI version, with no latest-version lookup. The installed native CLI itself does not require Node.js.

The release signer is not configured yet. The current unsigned alpha deliberately cannot pass these installers. Before a production installation can succeed, the release must include `SHA256SUMS.sig`: a raw 64-byte pure Ed25519 signature over the exact UTF-8 bytes of `SHA256SUMS`, including its LF line endings and final newline. It is not a signature of the hexadecimal checksum or of a re-serialised document. The installer requires an independently obtained, public-only PEM SubjectPublicKeyInfo verification key. It never downloads a public key from the release, requests a private key, or permits unsigned installation. Provisioning the production signer and distributing its trusted public key remain owner work. SignPath Windows code signing is separate from this detached manifest signature.

From the repository root, use a future release that actually has the detached signature:

```powershell
./install.ps1 -Version 0.2.0-alpha.3 -PublicKey C:/trusted/release-public.pem -Destination C:/tools/gatekeeper-alpha3
```

```sh
sh ./install.sh --version 0.2.0-alpha.3 --public-key /trusted/release-public.pem --destination "$HOME/.local/gatekeeper-alpha3"
```

The example version is illustrative, not a claim that alpha.3 exists. Linux defaults to the glibc archive; use `--musl` only on a musl-based Linux distribution. x64 and arm64 are selected from the running Node process architecture. Use native Node.js on the target operating system and architecture. To verify a previously downloaded release without network access, add `-SourceDirectory DIRECTORY` on PowerShell or `--source-directory DIRECTORY` on the shell. That directory must contain the archive, manifest and detached signature; all checks still apply.

The installer verifies the signature before reading manifest entries, rejects duplicate or malformed checksum entries, verifies the selected archive hash, checks that the archive contains exactly one regular executable within the 10 MiB size budget, and installs without replacing an existing executable. Downloads and extraction have size and time bounds. No executable from the archive is run during installation. There is no PATH or security-setting change. Add the chosen directory to PATH yourself, or invoke the installed executable by its full path and check `--version`.

To uninstall, remove only the installed `gatekeeper.exe` or `gatekeeper` file from the directory you selected, remove that directory if it is empty, and remove any PATH entry you added. No service or background process is installed. Windows binaries still need Windows code signing and macOS binaries are not notarised; manifest verification does not bypass operating-system security policies.

Run `node --test ci/native-release/install.test.mjs` to test signed development-only packages, exact extraction and rejection of missing signatures, tampering, wrong keys, duplicate entries, unpinned versions, unexpected archive contents and existing executables. These tests run in each native operating-system job. Production-key use, a production-signed release and clean-machine Windows, Linux and macOS acceptance are not claimed by these tests.

## Reproduce native validation

```powershell
dotnet publish cli -c Release -r win-x64 -p:PublishAot=true -p:PublishTrimmed=true -p:OptimizationPreference=Size -p:StackTraceSupport=false -o artifacts/native
./ci/native-release/Test-PublishedCorpus.ps1 -Binary artifacts/native/gatekeeper.exe
./ci/native-release/Pack-Native.ps1 -Binary artifacts/native/gatekeeper.exe -RuntimeIdentifier win-x64 -Destination artifacts/release -ProductVersion 0.1.0
```

The corpus smoke test verifies the native scanner and its JSON output. It does not claim recall against the future bundled detection rules. The original corpus is copied before test configuration and a mandatory synthetic probe are added.

## Performance reference (IQ-60)

The existing corpus generator creates exactly 500 or 10,000 eligible files with pinned seed 20261104, a recorded generator version and parameter hash, fixed file sizes, and UTF-8 and UTF-16 encodings. The 500-file tree stays below 10 MiB. The generated trees are outside Git, use the default profile with history off, and have no exceptions, baseline, fingerprint key or disabled rules. Existing directories are never overwritten.

```powershell
node --test ci/native-release/benchmark-generator.test.mjs
./ci/native-release/Test-BenchmarkMath.ps1
./ci/native-release/Measure-Native.ps1 -BaselineBinary <baseline-native-binary> -CandidateBinary <candidate-native-binary> -Destination artifacts/benchmarks
```

The workflow compares the pull request's base commit with its candidate in the same job. After one discarded warm-up pair, three alternating pairs measure fresh-process version start, the complete 10,000-file scan and the 500-file continuous-integration scan. Each statistic is a median of three. On 15 November 2026, the owner replaced the cold-start relative gate with an absolute limit: its candidate median must be strictly below 10 ms; exactly 10 ms fails. The cold-start relative regression flag remains in the JSON report for information and does not block publication. Every other measurement retains the 20 per cent regression gate. A scan above 60 seconds, a 500-file scan above 30 seconds or an executable above 10 MiB still fails. Reports retain binary digests, generator inputs and runner details. Fresh-process start does not flush the operating system's disk cache.

The owner selected provisional synthetic-pack benchmarks for the initial run. The workflow explicitly supplies the pinned synthetic regex-and-validator workload through `-ReferencePack`, records its digest and labels the report provisional. The embedded default-pack reference remains unavailable before Phase 2; these measurements do not claim the performance of that future pack. Omitting the explicit pack preserves the original embedded-pack contract and fails clearly when it is unavailable. Both benchmark arms enable `BenchmarkInstrumentation=true`, which records anonymous per-file matching times, current-thread allocation bytes and per-rule regex input counters during the same scan invocation. The matching operation is synchronous within each worker, so the allocation boundaries stay on one thread. The report includes nearest-rank per-file median and 95th percentile, allocations per file, and regex input bytes per call by rule and overall. Regex input bytes are the UTF-16 searchable suffix length passed to each search, including unsuccessful searches. All four metrics retain the same 20 per cent regression gate, and per-file times also retain the 10 ms and 50 ms absolute gates. Release builds compile out the instrumentation and do not recognise its metrics-output environment variable. A separate normal release binary supplies the size measurement. No specification or budget is changed here.

## Instrumented baseline compatibility

For a baseline that predates these counters, `Enable-BaselineInstrumentation.ps1` applies the committed measurement-only overlay after exact source-blob checks and `git apply --check`. A baseline that already has the instrumentation is left alone. The new counter types are copied from their ordinary C# source file after the overlay succeeds. A mismatched older source tree fails explicitly instead of being replaced with candidate implementation code. The overlay is generated from the instrumentation source change, not edited by hand:

```powershell
git diff --unified=0 707df6d -- Directory.Build.props cli/ScanCli.cs src/Engine/MatcherEngine.cs src/Engine/ScanOrchestrator.cs > ci/native-release/baseline-instrumentation.patch
$paths = @('Directory.Build.props', 'cli/ScanCli.cs', 'src/Engine/MatcherEngine.cs', 'src/Engine/ScanOrchestrator.cs')
$paths | ForEach-Object { "$(git rev-parse "707df6d:$_") $_" } > ci/native-release/baseline-instrumentation-base.txt
```

A baseline that predates the jurisdiction-neutral names (IQ-92) still builds `gatekeeper-ndpa-check` and reads `.ndpa-check/`. `Rename-BaselineNames.ps1` renames those string constants in the baseline's `cli/` and `src/` sources, so both arms scan the same generated tree. It changes no matching or scanning code, and it does nothing to a baseline that already uses `gatekeeper`, `.gatekeeper/` and `gatekeeper.toml`.

The synthetic reference pack drives repeated shape matching and named-validator rejection over the generated alphabetic text, with no findings or matched values emitted. This provides measurable per-file work and live regex counters without changing either comparison arm's input. Counter tests assert exact search counts and input-byte sums and prove that findings are unchanged. Run them with `dotnet test tests/Engine.Tests -p:BenchmarkInstrumentation=true --filter FullyQualifiedName~BenchmarkInstrumentationTests`.
