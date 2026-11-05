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

## Severity and triage scoring

P2-07-02 (#99) extends the existing `CorpusMeasurements` scorer. Only `error` findings feed the five per cent gate; `warning` and `note` counts are reported separately. Matching an expected location counts as a true positive only when its triage is absent or `true-positive`. Rejected, unresolved and unmatched findings count as false positives at error severity. Recall uses positive synthetic expectations, excluding rejected/unresolved triage rows. Duplicate expected or finding keys fail instead of inflating counts.

The gate uses `20 × false positives <= true positives + false positives`, per rule and overall, with integer arithmetic. Reports keep deterministic rule/location ordering and display the gate as unmeasured when matching did not run. Run `dotnet test tests/Corpus.Tests -c Release --filter FullyQualifiedName~CorpusGateTests` for exact-boundary, severity, triage and duplicate-key checks. This scorer is test infrastructure with no dependency on production rule wording or owner triage being available; fabricated safe findings exercise the complete arithmetic now.

`CorpusTriage` defines the JSON triage labels once for structural validation, scoring and test inputs. Report markers and formatting use named constants in `CorpusMeasurements`, preserving the existing report text and policy values.
