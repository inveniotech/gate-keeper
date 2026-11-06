# Rule packs

This folder holds the bundled rule packs shipped with each release. A rule pack is data, not code: detection logic lives here as JSON with a citation and corpus cases, never in the engine (rule 9 of `AGENTS.md`).

**Status: no bundled detection pack yet.** The first bundled pack arrives in Phase 2 (P2-03). Each pack is validated against the rule-pack JSON Schema before it ships. The [draft authoring guide](AUTHORING.md) and [synthetic local template](templates/local.pack.json) demonstrate the existing contract; the template is not shipped as a detection rule. Its real-CLI acceptance tests run with `dotnet test tests/Corpus.Tests -c Release --filter FullyQualifiedName~RuleAuthoringTests`. This module adds no dependency.

The owner approved the current [citation register](CITATIONS.md) and its [JSON data](citations.json) in principle on 5 November 2026. Legal counsel's review remains pending and the owner will introduce any recommended changes. The register is metadata, not a bundled pack. Run `node --test ci/rule-packs/citations.test.mjs` for completeness and approval-aware lookup checks; no new dependency is added.
