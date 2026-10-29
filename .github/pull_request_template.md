> Answer every section before asking for review. This template is read by the reviewer agent first and by the owner afterwards; where a section does not apply, write "None" rather than leaving it empty.

## Traces

- **Work package and card:** for example `P0-09`, card `docs/implementation/cards/p0-09/02-agents-tool-shims.md`.
- **Implements:** the `UC-`, `F-` and `NFR-` identifiers this change delivers.
- **Specifications and decisions:** links to the specification sections, the architecture decision records (ADRs) and any `IQ-nn` entries this pull request closes or depends on. Everything you cite must be readable in this repository.
- **Linked pull requests:** the website pull request that carries hand-written documentation, if this change alters user-visible behaviour (rule 12, IQ-65). Give its number only — never paste its content, or anything else from a private repository, into this pull request (rule 29).

## What changed

One or two sentences of plain en-GB prose describing the behaviour change. Say why the change is needed as well as what it does, and expand any acronym on first use.

## Tests

- The test that failed before this change and passes after it, named as it appears in the code (rule 2).
- The command that runs it.
- If this change carries no tests, explain why none are appropriate here.

## Known gaps

Skipped tests, TODOs, assumptions, and anything you could not verify (rule 16).

## Review checklist

Walk this list before asking for review (rule 33 and §7 of the execution model):

- [ ] Every changed behaviour has a test that fails without the change.
- [ ] The change stays inside the card; no drive-by refactoring, renaming or reformatting.
- [ ] No outbound network code, telemetry, analytics, crash reporting or update checks (rule 5). The zero-egress container test passes.
- [ ] Output is deterministic: stable ordering, no wall-clock times, no unordered collections (rule 6).
- [ ] AOT-safe for .NET code: no reflection-based serialisation, zero trim and AOT warnings (rule 7).
- [ ] No new dependency without owner approval (rule 8).
- [ ] No real personal data in logs, tests, fixtures or support bundles; synthetic identifiers only (rule 10).
- [ ] No reference to a private repository, and no private context pasted into the change, the commits or this description (rule 29). Anything written here may become public.
- [ ] Glossary used correctly: "rule pack" not "ruleset", "posture" not "compliance score", "updates plan" not "subscription" (rule 4).
- [ ] en-GB prose in full sentences, with acronyms expanded on first use (rules 17 and 18).
- [ ] Documentation updated, either as generated reference data here or named as a linked website pull request (rule 12).

## Handoff

- **Done:**
- **Not done:**
- **Decisions taken and why:**
- **Open questions:**
- **Follow-ups:**
