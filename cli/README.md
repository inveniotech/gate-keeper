# Command-line interface

The `gatekeeper-ndpa-check` command line. Work package P1-01 (issue #11) built the command surface on System.CommandLine and the version output; the scan pipeline, its configuration and the results writers are Phase 1 cards that follow. The baseline command scans the work tree with configured local packs. The bare invocation and `scan` run that pipeline too. The binary makes no network access.

## Public contract

- `CliModule.Run(string[] args, TextWriter output, TextWriter errors)` runs one invocation against the given writers and returns the process exit code: 0 pass, 1 violations, 2 tool error (rule 13). The no-writer overload writes to the console's standard output and standard error.
- The command surface is a System.CommandLine tree: the bare invocation is the scan command, the commands are `scan`, `init`, `baseline`, `scaffold consent`, `doctor`, `support-bundle` and `version`, and the built-in help answers `-h`, `--help`, `-?` and `/?`. Every parse failure produces a sentence written by this module — the library only parses; the wording stays in our hands (rule 23).
- `version` and `--version` print the product, the product version and the bundled pack version to the output writer; the bundling arrives with the pack build step, so this build prints that no pack is bundled yet rather than an invented number. The version flag owns its invocation: any other token beside it is a tool error.
- `baseline [--evaluated-on YYYY-MM-DD]` scans the current repository using its configured local packs, applies exceptions, creates a missing fingerprint key and writes `.ndpa-check/baseline.json`. With no bundled pack in this build, an absent local pack is a tool error. Supply the key to continuous integration as a secret file; it never appears in output.
- `CliModule.RunAsync` is the asynchronous entry point, with an optional repository root and cancellation token for hosts and tests.
- `scan [--json] [--evaluated-on YYYY-MM-DD]` and the bare invocation load configured local packs, apply the effective profile, exceptions, baseline and severity threshold, then print findings grouped by ordinal file path, line, column and rule identifier. The plain summary always uses uncoloured text, including when `NO_COLOR` is present, and includes the history-scan caveat. `--json` writes the existing version-stamped Gate-Keeper evidence document, including rule identifiers, severities, paths, lines and citations. Diagnostics remain on standard error. A failed scan prints no partial result. No bundled pack is present yet; configure `packs.local`.
- Every other command exits 2 with a message saying it is not in this build yet. The command line never pretends a scan ran.
- Results go to the output writer, diagnostics to the error-writer (rule 31).

## Dependencies

- `System.CommandLine` 2.0.14 (MIT), pinned in `Directory.Packages.props` and registered in `ci/guardrails/allowed-dependencies.json`. The P0-16 spike did not measure it, so the owner approved it under rule 8 for this repository on 3 November 2026, conditional on the library being ahead-of-time safe and offering a real advantage; both were verified in this pull request: the native publish carries zero trim and ahead-of-time warnings under the repository's analysers, and the library provides the standard grammar, the standard help rendering and shell-completion integration that the hand-written parser could not grow without a second bespoke component.
- The parse-law sentences (what the user sees when an argument is wrong) are written by `CliModule`, so a library version change cannot silently reword our errors.

## How to test

From the repository root:

```powershell
dotnet test --filter FullyQualifiedName~Cli.Tests
dotnet run --project cli -- --version
dotnet run --project cli -- --help
```
