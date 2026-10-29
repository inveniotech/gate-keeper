# Schema artefact packer

Packs `schemas/` into `gate-keeper-schemas-<version>.zip` and checks that the committed manifest has not drifted.

The contract is the bytes of `manifest.json` inside the zip. Consumers pin those bytes. The zip container is transport only: its own hash is not stable across operating systems, so nothing pins it.

## Commands

Regenerate the committed manifest after a real change to `schemas/`:

```powershell
pwsh ./ci/schema-artefact/Pack-SchemaArtefact.ps1 -ManifestOutput ci/schema-artefact/expected-manifest.json
```

Check that a fresh pack still matches the committed manifest, and that a changed schema changes the manifest:

```powershell
pwsh ./ci/schema-artefact/Test-SchemaArtefact.ps1
```

Continuous integration runs the check. It fails if regeneration would change `expected-manifest.json`. Do not edit that file by hand.

## Manifest

UTF-8 without a byte-order mark, LF line endings, and a trailing newline. Keys are `name`, `version`, then `files`. Each file object has `path` then `sha256`. Paths are sorted by ordinal comparison. Digests are lowercase hexadecimal SHA-256 of the file bytes.

Version `0.0.0` is the scaffold: the folder has no schema files yet.
