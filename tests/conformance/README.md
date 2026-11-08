# Cross-stack conformance kit

P3-00, IQ-14, NFR-004 and NFR-027. The C# engine generates `vectors.json`
from every authored source pack and every Phase 2 synthetic corpus file. Packs
load through the existing loader with in-memory development-only signatures.
This is not a claim that a production-signed bundled pack has been released.

Each classification case includes synthetic input, repository-relative path,
rule identifiers, severity, line, column and zero-based Unicode scalar spans.
Positive and near-miss list cases are included alongside pattern rules.
No matched-text field is emitted. Only committed synthetic corpus inputs enter
the vectors. Pack digests pin the exact authored source inputs.

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
$env:GATEKEEPER_WRITE_CONFORMANCE = '1'
dotnet test tests/Corpus.Tests -c Release --filter FullyQualifiedName~ConformanceExportTests
Remove-Item Env:GATEKEEPER_WRITE_CONFORMANCE
dotnet test tests/Corpus.Tests -c Release --filter FullyQualifiedName~ConformanceExportTests
node tests/conformance/check-patterns.mjs
java tests/conformance/CheckPatterns.java tests/conformance/patterns.tsv
```

Never hand-edit generated JSON or TSV files. Verification recomputes expectations
with the current engine and compares bytes, failing for stale inputs, digests or
expectations. The workflow has a five-minute timeout. Node and Java use only
standard-library facilities; Java source-file execution needs Java 17 or later.
No production trust, new shipped dependency or matcher capability is introduced.

## Remaining acceptance

This is the executable classification and regex-portability slice of P3-00.
Redacted-output, consent and pack-failure middleware vectors require the accepted
P3-01 contract and a reference redactor. The generated status field records that
gap rather than returning fabricated masks. Full adapter classification and
validator parity must run on every supported runtime when P3-02 to P3-04 land;
Python joins with P3-05. The current Node/JVM commands prove regex candidates,
not complete adapter parity. Keep #37 open until those acceptance items pass.
