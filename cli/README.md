# Command-line interface

Placeholder for the `gatekeeper-ndpa-check` command-line interface. This build scans nothing, writes no results and makes no network access. The scan command arrives in Phase 1.

## Public contract

`CliModule.Status` is `not-started`. `CliModule.Run` returns zero and does nothing else. Running the project exits with that code.

## Dependencies

None.

## How to test

`dotnet test --filter PlaceholderTests.Cli`
