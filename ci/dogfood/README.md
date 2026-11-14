# Repository dogfood integration

P1-17 scans each target work tree twice with the existing CLI, explicit evaluation date and local detection pack. Findings return 1; tool errors return 2. Byte-identical evidence, diagnostics and exit codes are required. Only schema-validated evidence is retained for seven days; diagnostics and source are never uploaded. History is disabled. The owner reviews findings locally and converts confirmed false positives to synthetic corpus cases before changing detection rules.

## Before publication

The reusable workflow builds the scanner from its checked-out source using the pinned SDK and locked dependencies. No published release or production signing key is needed. This is engineering dogfood validation, not acceptance of a distributed native release. Callers select scanner-ref and the reusable workflow from the same reviewed revision. During review they reference wp/p1-17-01-repository-dogfood; replace both references with the resulting full commit identifier before enabling the integration on the default branch.

The committed .gatekeeper/gatekeeper.toml selects .gatekeeper/dogfood.pack.json. This explicitly unsigned local pack is generated from all six authored detection packs, retaining matchers, severities, citations and metadata. Only rule identifiers receive the local. prefix and versions become local integers. No detection claim is based on the harness probe.

Regenerate from the public repository with node ci/dogfood/build-pack.mjs --write .gatekeeper/dogfood.pack.json. Copy that exact file to each caller repository. The workflow checks it against the selected scanner revision and fails if stale. Advance scanner references and regenerate packs together.

Run node --test ci/dogfood/scan.test.mjs ci/dogfood/build-pack.test.mjs. Build cli in Release and run node --test ci/dogfood/scan.integration.test.mjs. For a real target run node ci/dogfood/scan.mjs cli/bin/Release/net10.0/gatekeeper.dll <repository> <YYYY-MM-DD> <new-output-directory-outside-target>.

## Handoff

The harness, current configuration, generated detection pack and source-build workflow are implemented. Deploy the reviewed public integration branch before enabling callers, then pin its full commit. Actual three-repository CI scans, owner triage and any resulting corpus additions remain acceptance work. Native publication and production signing remain separate release work.
