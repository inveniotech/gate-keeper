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
