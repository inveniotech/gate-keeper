# Schema artefact

This folder is the source of the versioned `gate-keeper-schemas` artefact. The version is the contents of `VERSION` (major.minor.patch). Every `*.json` file under this folder, and no other file, is part of the artefact.

The artefact is a zip archive named `gate-keeper-schemas-<version>.zip`. Its contract is the bytes of `manifest.json`, not the zip container: zip metadata is not stable across operating systems, so consumers pin the manifest. The manifest lists each schema path and the SHA-256 digest of that file's bytes. Paths use forward slashes, are relative to this folder, and are sorted by ordinal comparison.

Version `0.0.0` means the contract exists and no schema file has been published yet. Schema files arrive with the rule-pack and results-format specifications.

## Regenerate

From the repository root:

```powershell
pwsh ./ci/schema-artefact/Pack-SchemaArtefact.ps1 -ManifestOutput ci/schema-artefact/expected-manifest.json
```

The check fails if that command would change `ci/schema-artefact/expected-manifest.json`. See `ci/schema-artefact/README.md`.
