# Tests

Every test project uses xUnit for discovery and execution, and AwesomeAssertions for assertions. Use `.Should()` assertions instead of xUnit assertion calls; preserve sequence order with `.Equal()` and exact exception types with `.ThrowExactly()` when those are part of the expected behaviour. Do not add another test or assertion framework.

The test projects, one folder under `tests/` each:

| Project | What it covers |
|---|---|
| The specification project | The conformance vectors and the schema checks for the open formats (P0-03, P0-04, P0-06, P0-07). |
| The engine project, `tests/Engine.Tests` | The engine's domain model and its System.Text.Json source-generated serialisation (P1-01). |
| The command-line project, `tests/Cli.Tests` | The argument surface and the version output (P1-01). |
| The corpus project, `tests/Corpus.Tests` | The golden fixture corpus and its harness: every case's repository, expected findings, and the per-rule precision and recall report (P1-02). |

## How to test

From the repository root:

```powershell
dotnet test
```

## Measured corpus runs

P2-07-01 (#98) lets `CorpusScanRunner` accept already loaded rules and execute the existing matcher and scan orchestrator over the copied case. That run returns sorted, content-free findings and sets `MatcherStagePresent` to true, including a measured clean result. Omitting rules performs enumeration only and keeps that flag false; an explicitly empty rule list is rejected. The harness applies no baseline, exceptions or severity overrides. Temporary copies are removed after failures; successful runs are owned by `CorpusScanScavenger`. Links remain links when cases are copied.

Run `dotnet test tests/Corpus.Tests -c Release --filter FullyQualifiedName~CorpusMatcherTests` for live positive/near-miss, repeatability and failure-cleanup checks. These tests use a synthetic list rule with no legal claim. Production detection acceptance still requires the actual approved pack, but this harness capability has no dependency on that pack being authored.
