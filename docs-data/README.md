# docs-data

This folder holds the generated reference data and the generator that produces the artefact published with each release: the rule reference, the rule metadata index, the CLI reference and the schema pages. The website consumes that data from a pinned release, never by hand-editing the pages.

P2-03 generates the rule reference under `rules/` with `node ci/rule-packs/build-detection.mjs --write`; `--check` and the existing Node authoring tests verify byte-exact regeneration. Descriptions, limitations and remediation are mandatory authoring inputs, and citations and rule identifiers come from the generated pack data. Never hand-edit generated pages.

P2-10 adds the per-rule metadata index [rules.json](rules.json) and the command-line reference under `cli/`, generated with `node ci/docs-data/build.mjs --write` after `dotnet build cli -c Release`; use `--check` to verify the committed bytes. A detection source that ships without a description, limits or remediation fails that build, so an undocumented rule cannot reach the website. The command-line pages embed the exact help text captured from the built binary, so the reference cannot drift from behaviour.

`ci/docs-data/Pack-DocsData.ps1` packs this folder, the citation register and `schemas/` into the `gate-keeper-docs-data` release artefact; `Test-DocsData.ps1` and [ci/docs-data/README.md](../ci/docs-data/README.md) document the manifest contract and the regeneration commands.

The [synthetic measurement report](synthetic-measurements.md) is generated with `./ci/rule-packs/Measure-Detection.ps1 -WriteReport`; omit the switch to verify its bytes. It records exact pack digests and synthetic precision/recall through the real loader and matcher. It does not claim owner-triaged real-repository measurements. The ordinary corpus test suite verifies freshness.
