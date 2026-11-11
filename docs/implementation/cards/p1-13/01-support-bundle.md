# P1-13 support-bundle engineering scope

Implementation note for issue #23. IQ-55 concerns support and service-level agreements; channels, response times, retention and support policy remain open there. The engineering bundle follows the card without deciding those policies.

## First engineering version

Create a local ZIP file at `.gatekeeper/support-bundle.zip`, with an optional repository-relative `--output` filename. Nothing is uploaded automatically. The user reviews the archive and chooses whether to send it through their support channel.

The archive contains exactly three UTF-8 JSON entries, in ordinal entry-name order. Entry names are fixed, so no source path can enter an archive name. ZIP entry timestamps use a fixed date, and documents use the scan's declared evaluation date.

| Entry | Contents |
|---|---|
| `configuration.json` | Effective typed settings. Profile, severity, booleans and numeric limits are retained. Every free-text setting is replaced with `[masked]`, including repository identity, exclusion paths, local-pack paths, disabled-rule identifiers and reasons. |
| `findings.json` | Work-tree and history finding counts grouped by rule identifier, plus overall counts. No finding locations, fingerprints, excerpts or matched text. |
| `versions.json` | Bundle schema version, evaluation date, engine product and version, and loaded pack identifiers, versions, provenance and content digests. |

Never include source files, matched content, source paths, Git remotes, raw configuration, exception entries or reasons, fingerprint keys, licence files or tokens, environment variables, logs, machine names, user names or absolute paths.

## Privacy checks

Build documents from this allow-list rather than copying and then redacting files. Use the loaded rules as an additional content check on retained identifiers and metadata. For this diagnostic check only, remove path and keyword restrictions, retaining shapes, validators and exclusions, so a source-specific rule cannot accidentally exempt metadata. Replace a metadata string that matches with `[masked]`. Check the final serialised documents again; a remaining match or a check failure prevents the archive from being written. Write the ZIP atomically and retain an existing archive on failure.

The acceptance tests scan every corpus repository through the real scan and bundle path, open every ZIP entry and assert that none of the values from the documented synthetic-value generator occurs. Additional tests cover hostile configuration strings, path traversal, deterministic bytes, fixed entry names, absence of keys and source content, and failure without a partial archive.

## Trade-off and follow-ups

The bundle gives support enough information to identify version mismatches, configuration shape and which rules are noisy while keeping repository content local. It cannot reproduce a location-specific detection by itself. If that is necessary, ask the user to create a minimal synthetic reproduction as a separate deliberate action; never silently enlarge the bundle.

The remaining support triage document can later define the receiving channel, retention and deletion process, and any additional fields. Each added field needs its own privacy and regression tests. This first version does not make a retention promise or define terms of support.
