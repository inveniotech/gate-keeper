# Deterministic pack assembly

P2-08-01 (#100) assembles valid content documents into one release-candidate pack. It takes explicit identity, calendar version, provenance, minimum engine version, coverage date, issue date and public signing-key identifier. It sorts rule identifiers and object keys ordinally, preserves semantically ordered arrays, refuses duplicate/local rule identifiers, validates source and output against the existing JSON Schema and emits UTF-8 content with LF and a final newline. Civil dates are validated without consulting a clock. Patch-zero dates must agree; a correction requires a pinned base and passes the existing correction comparison.

`assemblePack(inputs, metadata, base?)` returns `content` and `manifest` byte buffers. The unsigned manifest uses the exact specified property order and SHA-256/SHA-512 digests of the final content bytes. Its payload has no trailing newline: those exact bytes are what an external signer signs. Content is limited to 16 MiB and the manifest to 4,096 bytes. Schema validation does not replace the loader's semantic checks or signature verification; release verification must still use the loader before a candidate is accepted.

```sh
node --test ci/pack-release/assembly/build.test.mjs
node ci/pack-release/assembly/build.mjs --metadata /path/to/release-metadata.json --output /path/to/new-directory /path/to/source-one.json /path/to/source-two.json
```

The metadata JSON keys are `id`, `version`, `provenance`, `minEngineVersion`, `coverageDate`, `contentIssuedAt`, `keyId` and, for a correction, `baseVersion`. Pass `--base /path/to/pinned-base.json` for corrections. The new destination receives `pack.json` and `manifest.payload.json`. All validation happens before destination creation. Existing directories are refused and no existing artefact is overwritten. Success exits 0; input, validation or file errors exit 2 with a fixed diagnostic that does not repeat input content. Source files are selected explicitly; citation metadata is not a content document and is rejected if supplied as one.

The producer uses Node.js 26 built-ins and the existing PowerShell `Test-Json` facility, with no package dependency. It never reads a private key, signs a pack, publishes an artefact, approves citations or changes the CLI's embedded pack. The unsigned candidate is for external review and signing under the owner's process.

## Development acceptance

`fixture.mjs` is test-only. It feeds two synthetic rules through the real producer, applies a signature using the existing clearly labelled development key and generates `assembly.*` under `tests/vectors/pack/`. That key must never sign a released pack. The production producer does not import this generator.

```sh
node ci/pack-release/assembly/fixture.mjs --write
node ci/pack-release/assembly/fixture.mjs --check
dotnet test tests/Engine.Tests -c Release --filter FullyQualifiedName~PackAssemblyTests
```

CI verifies byte-exact fixture regeneration and runs the loader test in the normal platform matrix. That test verifies the generated manifest and signature through the existing loader, matches both rules and rejects changed content. This completes the engineering acceptance without production keys, production rule wording or a production signing rehearsal; those remain separate parent-release responsibilities.
