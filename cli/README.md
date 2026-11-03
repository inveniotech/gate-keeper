# Command-line interface

The `gatekeeper-ndpa-check` command line. Work package P1-01 (issue #11) built the argument surface and the version output; the scan pipeline, its configuration and the results writers are Phase 1 cards that follow. This build writes no results, scans nothing and makes no network access.

## Public contract

- `CliModule.Run(string[] args, TextWriter output, TextWriter errors)` runs one invocation against the given writers and returns the process exit code: 0 pass, 1 violations, 2 tool error (rule 13). The no-writer overload writes to the console's standard output and standard error.
- `CliCommandParser.TryParse` is the argument surface: the bare invocation is the scan command, the commands are `scan`, `init`, `baseline`, `scaffold consent`, `doctor`, `support-bundle` and `version`, and the flags are `--version`, `--help` and `-h`. Every failure produces a sentence that says what happened and what to do next, and the tool-error exit is never used for "violations found".
- `version` and `--version` print the product, the product version and the bundled pack version to the output writer; the bundling arrives with the pack build step, so this build prints that no pack is bundled yet rather than an invented number.
- Every other command exits 2 with a message saying it is not in this build yet. The command line never pretends a scan ran.
- Results go to the output writer, diagnostics to the error-writer (rule 31).

## Dependencies

None. The argument parser is hand-written because the spike did not measure a parser library and a new dependency needs the owner's rule-8 approval; the parse surface is small, ordinal and reflection-free, so it is ahead-of-time safe. `System.CommandLine` can replace it later if the owner approves that dependency.

## How to test

From the repository root:

```powershell
dotnet test --filter FullyQualifiedName~Cli.Tests
dotnet run --project cli -- --version
```
