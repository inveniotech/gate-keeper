# Cross-stack conformance kit

P3-00, IQ-14, NFR-004 and NFR-027. The C# engine generates `vectors.json`
from every authored source pack and every Phase 2 synthetic corpus file. Packs
load through the existing loader with in-memory development-only signatures.
This is not a claim that a production-signed bundled pack has been released.

Each classification case includes synthetic input, repository-relative path,
rule identifiers, severity, line, column and zero-based Unicode scalar spans.
Positive and near-miss list cases are included alongside pattern rules.
No matched-text field is emitted. Only committed synthetic corpus inputs enter
the vectors. Pack digests pin the exact authored source inputs. `runtimeVersions`
records actual .NET `Versions()` output from the successful signed test load;
its digests are checked against the source bytes before export. Node and JVM
compare that output and independently hash those source files. The one
`contentDigest` field replaces the duplicate hash fields. This proves reporting
parity at the loader boundary, not production Node/JVM signature verification.

The same file now contains the native rule models, complete runtime matches,
the selected classification and actual redacted output from the .NET core.
`syntheticRuntimeCases` pins overlap ordering, emitted spans after an astral
character, literal masks and removal before matching. `behaviour-vectors.json`
records actual .NET consent and atomic wildcard-response results. It covers
all provider states, invalid state, absent provider, exceptions, cancellation,
purpose boundaries, every array element, nested/empty arrays, escaped keys,
mixed types, content types, streaming and committed-response abort signalling.

`patterns.tsv` contains a family for every authored structured pattern, with
three positive and three negative samples where available. C# compiles shapes
through the production compiler and records complete-match and emitting-group
spans. The TSV spans use native UTF-16 indices deliberately: this family checks
regex candidate parity, before scalar conversion, boundaries, validators,
keywords, exclusions and location filters. It is not a second matcher.

Each named validator has an independent `validator-*.json` file. The C# registry
computes accepted/rejected results from the existing generated synthetic-value
set and explicit empty/text near-misses. Prefix parameters come from the current
phone pack; the digits-length family uses the documented eleven-digit parameter.
Adapters must consume these files with their native validator implementations.

## Generate and verify

From the repository root in PowerShell:

```powershell
./tests/conformance/Sync-RuntimeVersion.ps1 -Check
$env:GATEKEEPER_WRITE_CONFORMANCE = '1'
dotnet test tests/Corpus.Tests -c Release --filter FullyQualifiedName~ConformanceExportTests
Remove-Item Env:GATEKEEPER_WRITE_CONFORMANCE
dotnet test tests/Corpus.Tests -c Release --filter FullyQualifiedName~ConformanceExportTests
node tests/conformance/check-patterns.mjs
node tests/conformance/check-runtime.mjs
java tests/conformance/CheckPatterns.java tests/conformance/patterns.tsv
javac --release 15 -d artifacts/conformance-jvm middleware/jvm/src/africa/gatekeeper/*.java tests/conformance/VectorJson.java tests/conformance/ResponseFailureProbe.java tests/conformance/CheckRuntime.java
java -cp artifacts/conformance-jvm CheckRuntime
```

Never hand-edit generated JSON or TSV files. Verification recomputes expectations
with the current engine and compares bytes, failing for stale inputs, digests or
expectations. The workflow has a five-minute timeout. Node and Java use only
standard-library facilities; Java source-file execution needs Java 17 or later.
No production trust, new shipped dependency or matcher capability is introduced.

`middleware/version.txt` is the single adapter version source. Run
`./tests/conformance/Sync-RuntimeVersion.ps1` to regenerate the three native
constants and Node package metadata. The .NET project reads the same source;
CI rejects stale generated metadata. Repository path filtering belongs only to
the scanner fixture: no public runtime matcher accepts a repository path.

## Native conformance and release boundary

The kit executes actual .NET, Node and JVM matching/redaction cores and native
validator implementations, not just regex compilation. Node 22/24/26 and Java
17/21/25 run in independent CI jobs. The .NET probe publishes the actual core
with NativeAOT and trimming and executes it with reflection-based JSON disabled.
Boundary tests also cover invalid Unicode, scalar caps, output expansion,
optional disablement and errors in a later selected response element.

The owner accepted the P3-01 contract on 9 November 2026. P3-02 to P3-04 own framework
hooks, signed installation-container loading, complete application samples and
package publishing. Those are not claimed by a tree-level reference core.
Existing `PackLoaderTests` exercise the engine's signed-pack failures and
watermark behaviour; this kit does not pretend its test-fixture JSON reader is
a production JVM pack loader. Python joins with P3-05. Production keys, releases
and framework dependencies are not introduced here.

## Python adapter checks

P3-05 consumes the same generated expectations without rewriting them. Run
`check_patterns.py`, `check_runtime.py`, `check_python_behaviour.py`,
`check_python_packs.py` and `check_python_hooks.py` with Python from this directory
or the repository root. The first two verify regex spans and native matching;
the remaining suites verify consent, atomic responses, signed packs and host
hooks. `python_fixture.py` supplies source-tree imports only to these checks.
The installed wheel is checked separately with
`python -I tests/conformance/check_python_distribution.py` after installation.
See the [Python module guide](../../middleware/python/README.md) for framework
samples, strict types, formatting and package build commands.

## Observing runtime cost

Run `node tests/conformance/benchmark-runtime.mjs` for the real authored rules
at 1,000, 10,000 and 65,536 scalars. For the JVM, add
`tests/conformance/BenchmarkRuntime.java` to the compilation command above, then
run `java -cp artifacts/conformance-jvm BenchmarkRuntime`. Both scripts verify
the output and report elapsed time without timing assertions. Deterministic
conformance separately exercises the cap-sized adversarial inputs.

The review baseline on the local Node 26 runner was about 19 seconds for an
email near miss at the cap. Sharing shape suffix state across candidate starts,
preparing alternatives once, indexing keyword occurrences once, rejecting
impossible mandatory literals, and translating masks incrementally reduced
that case to about 23 milliseconds. Additional cap-sized email, keyword and
mask scenarios took 74–122 milliseconds in Node and 9–149 milliseconds on the
local JVM 21 runner. These observations are machine-dependent, include warm-up
effects and are not a per-call latency guarantee. .NET builds one scalar-offset
map instead of counting from the beginning for each finding.
