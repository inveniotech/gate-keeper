# Development pack vectors

The public rule-pack loader vectors (card P1-03). Each signed scenario is a set of the byte sequences the pack loader receives: the content bytes, the manifest armour with the signature over its exact payload line, and, for the watermark cases, the detached block with its signature.

The vectors are generated from two **development-only** Ed25519 keys (rule 14): `gkpk-dev-1` signs the manifests and `gkwm-dev-1` signs the watermark. They must never sign a released pack. `development-public-key.json` records the seeds and the public keys, labelled `development-only`.

Regenerate them with Node 22 or later, from the repository root:

```powershell
node tests/vectors/pack/New-PackVectors.mjs
```

| Stem | Expected outcome |
|---|---|
| `pack` | Loads: the valid bundled 2027.1.0 pack with its one rule. |
| `tampered-content` | One content byte changed after signing; the digests refuse it. |
| `tampered-signature` | Content and manifest intact; the signature byte was flipped. |
| `unknown-key` | The manifest names `gkpk-unknown`, which no key catalogue carries. |
| `key-id-mismatch` | The armour header and the payload line name different keys. |
| `next-line` | Loads; version 2027.2.0 of the same pack, one rule more. |
| `conflict` | Same pack id and version as `pack` but different content: a digest conflict. |
| `rival` | A different pack that carries `pii.bvn`: a duplicate rule identifier (IQ-24). |
| `schema-major` | Content document at schema version 2: the upgrade sentence. |
| `newer-engine` | `minEngineVersion` 100.0.0 against a 0.1.0 engine: refused. |
| `plan-gated` | Provenance `plan-gated`: the MVP bundled-only policy refuses it (IQ-10). |
| `coverage-after-issued` | Coverage after the issue date on a correction: refused. |
| `base-version-wrong` | A correction whose `baseVersion` sits off its line: refused. |
| `local-rule-in-bundled` | A bundled pack carrying a `local.` rule identifier (IQ-25): refused. |
| `watermark` | The detached block verifies against `gkwm-dev-1` and matches the digest. |
| `watermark-digest-mismatch` | The block's copied digest differs from the manifest's. |
| `watermark-tampered-signature` | The block's signature byte was flipped. |

The malformed-armour and oversized-manifest cases are constructed in the tests, because they depend on variations the generator would sign dishonestly otherwise.

Local pack vectors are not committed: local packs are customer-authored unsigned documents, and the tests write small synthetic ones (`Engine.Tests/PackLoaderTests.cs`).
