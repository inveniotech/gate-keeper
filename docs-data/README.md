# docs-data

This folder holds the generator that produces the reference data published with each release: the rule reference, the CLI reference and the schema pages. The website consumes that data from a pinned release, never by hand-editing the pages.

P2-03 generates the rule reference under `rules/` with `node ci/rule-packs/build-detection.mjs --write`; `--check` and the existing Node authoring tests verify byte-exact regeneration. Descriptions, limitations and remediation are mandatory authoring inputs, and citations and rule identifiers come from the generated pack data. Never hand-edit generated pages. Release artefact publication, the CLI reference, schema pages and website consumption remain P2-10 and P0-20 work.

The [synthetic measurement report](synthetic-measurements.md) is generated with `./ci/rule-packs/Measure-Detection.ps1 -WriteReport`; omit the switch to verify its bytes. It records exact pack digests and synthetic precision/recall through the real loader and matcher. It does not claim owner-triaged real-repository measurements. The ordinary corpus test suite verifies freshness.
