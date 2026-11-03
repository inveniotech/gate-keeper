# Tests

xUnit is the test framework for the repository. Do not add another.

The test projects, one folder under `tests/` each:

| Project | What it covers |
|---|---|
| The specification project | The conformance vectors and the schema checks for the open formats (P0-03, P0-04, P0-06, P0-07). |
| The engine project, `tests/Engine.Tests` | The engine's domain model and its System.Text.Json source-generated serialisation (P1-01). |
| The command-line project, `tests/Cli.Tests` | The argument surface and the version output (P1-01). |

## How to test

From the repository root:

```powershell
dotnet test
```
