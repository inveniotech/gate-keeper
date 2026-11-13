# Command-line interface

The `gatekeeper` command line. Work package P1-01 (issue #11) built the command surface on System.CommandLine and the version output; the scan pipeline, its configuration and the results writers are Phase 1 cards that follow. The baseline command scans the work tree with configured local packs. The bare invocation and `scan` run that pipeline too. The binary makes no network access.

## Public contract

- `CliModule.Run(string[] args, TextWriter output, TextWriter errors)` runs one invocation against the given writers and returns the process exit code: 0 pass, 1 violations, 2 tool error (rule 13). The no-writer overload writes to the console's standard output and standard error.
- The command surface is a System.CommandLine tree: the bare invocation is the scan command, the commands are `scan`, `init`, `baseline`, `scaffold consent`, `doctor`, `support-bundle` and `version`, and the built-in help answers `-h`, `--help`, `-?` and `/?`. Every parse failure produces a sentence written by this module — the library only parses; the wording stays in our hands (rule 23).
- `version` and `--version` print the product, the product version and the bundled pack version to the output writer; the bundling arrives with the pack build step, so this build prints that no pack is bundled yet rather than an invented number. The version flag owns its invocation: any other token beside it is a tool error.
- `baseline [--evaluated-on YYYY-MM-DD]` scans the current repository using its configured local packs, applies exceptions, creates a missing fingerprint key and writes `.gatekeeper/baseline.json`. With no bundled pack in this build, an absent local pack is a tool error. Supply the key to continuous integration as a secret file; it never appears in output.
- `CliModule.RunAsync` is the asynchronous entry point, with an optional repository root and cancellation token for hosts and tests.
- `scan [--json] [--evaluated-on YYYY-MM-DD]` and the bare invocation load configured local packs, apply the effective profile, exceptions, baseline and severity threshold, then print findings grouped by ordinal file path, line, column and rule identifier. The plain summary always uses uncoloured text, including when `NO_COLOR` is present, and includes the history-scan caveat. `--json` writes the existing version-stamped Gate-Keeper evidence document, including rule identifiers, severities, paths, lines and citations. Diagnostics remain on standard error. A failed scan prints no partial result. No bundled pack is present yet; configure `packs.local`.
- `init` and `scaffold consent` exit 2 with a message saying they are not in this build yet. The command line never pretends a scan ran.
- Results go to the output writer, diagnostics to the error-writer (rule 31).

Processor findings (P2-12, #64) also show the effective rule's categories in
authored order with later repeats removed, its verbatim region caveat and the
owner-approved request to confirm the data actually sent, service region and
applicable transfer basis. Both work-tree and history console findings include
these details. JSON evidence carries the optional `processor` object after the
citation, including history; ordinary rules omit it. The catalogue describes an
integration reference and does not establish actual processing or a transfer.
Matched values and source lines remain omitted. Default note severity does not
fail the default error threshold.

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

Pack-age diagnostics use the declared evaluation date. When the newest dated loaded pack is more than 90 days old, the existing pack.stale warning appears on standard error and in the evidence warnings array; it never changes the exit code. Local packs have no content issue date and do not raise this warning.

A developer benchmark build can opt into matching-stage measurements with BenchmarkInstrumentation=true. In that build only, GATEKEEPER_BENCHMARK_OUTPUT supplies the separate anonymous metrics artefact path. The ordinary release binary compiles this path out, and neither console nor evidence output changes.
## Doctor (P1-13)

The doctor command checks configuration and policy validity, loads packs through the existing verifier, reports local packs as unsigned, checks declared pack ages, checks Git availability and a tracked fingerprint key, and probes write access to a repository-relative --output directory (default .gatekeeper). It does not scan source files. Configuration failures do not stop the independent Git and output checks. A missing Git executable permits the documented directory-walk scan mode; a tracked key produces the configuration specification's warning. Licence verification is explicitly reported as unavailable until its implementation exists. Diagnostic checks return 0 when usable and 2 for tool or file-access failures.

## Local support bundle (P1-13)

`support-bundle [--evaluated-on YYYY-MM-DD] [--output repository-relative.zip]` runs the existing scan and writes a local ZIP, defaulting to `.gatekeeper/support-bundle.zip`. A successful bundle returns 0 even when the scan has findings; input, privacy or write failures return 2 and preserve an existing archive. Nothing is uploaded. The user reviews the archive before deciding whether to share it.

The fixed entries are `configuration.json` (typed effective settings with all free text masked), `findings.json` (counts grouped by rule, without locations or fingerprints), and `versions.json` (engine and pack metadata). Source files, matched values, paths, Git remotes, exception reasons, keys, licence tokens and environment variables are excluded. Retained metadata is checked with every loaded rule, with path and keyword restrictions removed for this diagnostic check. A remaining match in the serialised documents fails closed before a file is written. ZIP entries have fixed names, timestamps and attributes, use UTF-8 JSON with LF, and are written atomically. The bundle does not define support channels, response commitments or retention policy; those remain IQ-55 support work.

Run `dotnet test tests/Cli.Tests --filter FullyQualifiedName~SupportBundleCliTests` and `dotnet test tests/Corpus.Tests --filter FullyQualifiedName~CorpusSupportBundleTests` for command behaviour and the corpus-wide privacy acceptance test.

The corpus privacy acceptance test supplies real matchers through an internal
composition seam for both source scanning and final-document checks. Its shape
cache has no elapsed-time deadline so that busy runners cannot turn a semantic
privacy assertion into a timing test. Public CLI entry points retain the
100 millisecond production deadline; no flag or environment override is exposed.
Deterministic fault-injection tests prove that a matcher failure in either stage
returns exit code 2, preserves a previous archive and leaves no temporary file.
