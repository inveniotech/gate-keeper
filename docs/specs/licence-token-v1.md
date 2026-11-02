# Licence token, version 1

**Status: draft — not accepted.** This specification is an AI draft for the owner to review and approve (rule 15 of `AGENTS.md`). It records the public token format. Nothing in this document is final, accepted or approved until the owner says so.

This document is the written contract for work package P0-06 ([issue #5](https://github.com/inveniotech/gate-keeper/issues/5)). It does not implement the verifier or the `licence install` command; those are later work. The vectors in [`tests/vectors/licence/`](../../tests/vectors/licence/) are the public cases, produced by [`New-LicenceVectors.mjs`](../../tests/vectors/licence/New-LicenceVectors.mjs) from the development key in section 6.

This draft does not add a JSON Schema file. The signed line is JSON, but a verifier MUST check the signature before it parses that line, so the schema artefact is not the contract. A later change that adds a schema updates that schema, this specification and the version field in the same pull request (rule 24 of `AGENTS.md`).

---

## 0. Document status

### 0.1 Decision status

Normative text in this specification comes from the recorded decisions listed here. The specification itself stays a draft, because rule 15 reserves final approval of specifications to the owner.

| Identifier | Status | What it settles |
|---|---|---|
| IQ-07 | Decided | The payload is one line of JSON. The signature covers those exact bytes. Nothing is re-serialised. `keyId` is in the armour header and again inside the payload, and the two MUST match after verification. A trailing CR on an armour line is stripped before verifying. |
| IQ-35 | Decided | The signed payload is at most 4,096 bytes. |
| IQ-37 | Decided | One token carries `purchasedOn` and an optional `updatesUntil`. The claims `licensedMajor`, `plan` and `expiresAt` are not part of version 1. |
| IQ-38 | Decided | The first source that is found wins: a command-line flag, then an environment variable holding the token text, then an environment variable holding a file path, then the user configuration folder, then a mounted file. `doctor` reports which source was used. `licence install` writes to the configuration folder, so a token is not committed. The binary needs a token only for a plan-gated pack. |
| IQ-39 | Decided | `orgId` is an opaque customer number for one legal entity. The token also carries that entity's Corporate Affairs Commission registration number. A lawyer still has to confirm that the registration number may appear in the token. |

The register entry for each identifier is an owner record. This specification cites the identifier and does not describe where that register is kept.

### 0.2 Normative keywords

The key words **MUST**, **MUST NOT**, **REQUIRED**, **SHALL**, **SHALL NOT**, **SHOULD**, **SHOULD NOT** and **MAY** are to be interpreted as described in RFC 2119. When this document writes "the binary", it means `gatekeeper-ndpa-check`. When it writes "the token", it means one licence file in the format below. Ed25519 means the pure Ed25519 signature scheme, with a 32-byte public key and a 64-byte signature.

### 0.3 Change log

| Version | Date | Change |
|---|---|---|
| 1 (draft) | 2 November 2026 | First draft. |

### 0.4 Details this draft fixes

The decisions name the signed bytes, the two date claims, and the kinds of place a token is read from. They do not spell the armour, the other claim names, or the flag and path strings. This draft fixes the following so that an issuer and a verifier agree. Each item is part of the draft for the owner to accept or amend.

1. **Armour and signed bytes.** Section 2 is the file layout. The signed bytes are the UTF-8 of the single payload line after one trailing CR has been removed. The line feed that ends the line in the file is not signed.
2. **Claim names and the example order.** Section 4 names every claim. Verifiers MUST NOT require a particular key order and MUST NOT re-serialise the payload. The examples use the order in section 4 only so the vectors have one byte string.
3. **`keyId` shape.** A key identifier is `gklic-` followed by 1 to 32 characters. The first of those characters is a letter or a digit. The rest are letters, digits or `-`. The development key is `gklic-dev-1`.
4. **`orgId` and `cacRegistrationNumber`.** Section 4 gives their character rules. IQ-39 says to carry the registration number and records that a lawyer has not yet confirmed the field. The field is in this draft so the format has a place for it. The owner can remove it before acceptance.
5. **Source names.** The flag is `--licence <path>`. The token text is `GATEKEEPER_LICENCE`. The file path is `GATEKEEPER_LICENCE_FILE`. The configuration file is `licence.gklic` inside the configuration folder. The mounted file is `/var/lib/gatekeeper/licence.gklic`.
6. **A requested file that is missing stops the search.** `--licence` or `GATEKEEPER_LICENCE_FILE`, when set, is the only source consulted. A missing file is reported as missing. Later sources are not used. An empty `GATEKEEPER_LICENCE` is unset, not a token.
7. **`updatesUntil` is on or after `purchasedOn`.** When both dates are present, a verifier rejects the token if `updatesUntil` is earlier than `purchasedOn`. This is a check on the claims after the signature has verified. It does not say which packs or features a date allows.
8. **There is no organisation-name claim.** `orgId` is not a name. The token still MUST NOT be committed to a repository, because it identifies the customer.

---

## 1. Scope and non-goals

### 1.1 In scope

The bytes of a token, the signature check, the public claims, the 4,096-byte limit, and the order in which the binary looks for a token. The development key and the public vectors.

### 1.2 Not in scope

- Which packs or features a pair of dates allows. This specification defines the claims. It does not define that decision.
- The verifier library, `licence install`, and the `doctor` text beyond the source name (later licensing work).
- Production signing keys. Only the labelled development key appears here.
- Marking this specification accepted.

---

## 2. Armour and signed bytes

A token is a text file, UTF-8, with no byte-order mark. The file uses LF between lines. A verifier also accepts CRLF, because each line has at most one trailing CR removed before any other check. No other character is stripped.

The lines, in order, are:

1. `-----BEGIN GATEKEEPER LICENCE-----`
2. `Key-Id: <keyId>`
3. `Signature: <signature>`
4. The payload, one JSON object on one line.
5. `-----END GATEKEEPER LICENCE-----`

The file ends with a single LF after the end line. There are no blank lines and no other header lines. `<keyId>` is the key identifier from section 4. `<signature>` is the Ed25519 signature of the payload line, written as 128 lowercase hexadecimal characters.

Any other layout is malformed armour. A verifier reports malformed armour and does not verify a signature and does not parse the payload. That includes a missing end line, a second payload line, a signature that is not 128 lowercase hex characters, and a payload line that does not start with `{`.

The signed bytes are the payload line after the trailing-CR rule, encoded as UTF-8, with no line feed and no CR. The signature does not cover the header. A verifier selects the public key named by the header, verifies those bytes, and only then parses the payload. Parsing a payload whose signature did not verify is not permitted.

After a successful verification:

1. Parse the payload as one JSON object.
2. Reject the token if the payload's `keyId` is not the same string as the header `Key-Id`.
3. Reject the token if the claims do not match section 4.

A header `keyId` that is not in the embedded public-key set is an unknown key. The verifier reports that outcome and does not need a matching public key to do so.

The signed payload, which is those UTF-8 bytes, MUST be at most 4,096 bytes. A longer line is rejected.

---

## 3. How a token is found

The binary looks for a token only when it is about to load a plan-gated pack. A scan that loads bundled packs only does not look for a token. Finding no token, a missing file, malformed armour, an unknown key, a bad signature, or rejected claims is not exit code 2 by itself. Exit codes stay the scan's own codes.

The first source that applies is the one that is used. `doctor` reports that source and does not print the token.

| Order | Source | When it applies | Name `doctor` reports |
|---|---|---|---|
| 1 | `--licence <path>` | The flag is present. | `flag` |
| 2 | `GATEKEEPER_LICENCE` | The variable is set and not empty. The value is the token text, not a path. | `environment-text` |
| 3 | `GATEKEEPER_LICENCE_FILE` | The variable is set and not empty. The value is a file path. | `environment-file` |
| 4 | Configuration folder | The file below exists. | `config-folder` |
| 5 | Mounted file | `/var/lib/gatekeeper/licence.gklic` exists. | `mounted-file` |

If none of these applies, `doctor` reports `none`.

On Windows the configuration folder is `%APPDATA%\gatekeeper`. On any other system it is `$XDG_CONFIG_HOME/gatekeeper` when `XDG_CONFIG_HOME` is set and not empty, and `$HOME/.config/gatekeeper` otherwise. The file inside that folder is `licence.gklic`.

When `--licence` or `GATEKEEPER_LICENCE_FILE` applies and the named file does not exist, the result is a missing file. The search stops. A later source MUST NOT be used, even when that later source has a token.

`licence install` writes the token bytes to the configuration-folder file and creates the folder if it is missing. It MUST NOT write the token into the repository being scanned.

---

## 4. Public claims

The payload is one JSON object. After the signature has verified, the object MUST contain the required claims below and MUST NOT contain any other claim. `updatesUntil` is optional. Dates are strings `YYYY-MM-DD` that are real civil dates, not JSON date values. The claims `licensedMajor`, `plan` and `expiresAt` MUST NOT appear.

| Claim | Required | Rule |
|---|---|---|
| `schemaVersion` | yes | The integer `1`. |
| `product` | yes | The string `gatekeeper`. |
| `keyId` | yes | The same string as the header `Key-Id`, matching `^gklic-[a-z0-9][a-z0-9-]{0,31}$`. |
| `orgId` | yes | 1 to 64 characters matching `^[A-Za-z0-9][A-Za-z0-9._-]{0,63}$`. An opaque customer number for one legal entity. It is not the entity's name. A separate legal entity needs its own token. |
| `cacRegistrationNumber` | yes | 1 to 32 characters matching `^[A-Z0-9][A-Z0-9-]{0,31}$`. The licensed entity's Corporate Affairs Commission registration number. See section 0.4 item 4. |
| `purchasedOn` | yes | The civil date of the one-time purchase. |
| `updatesUntil` | no | When present, the last civil date named by the updates plan on this token. It MUST be on or after `purchasedOn`. When the token does not carry an updates plan, the claim is omitted. It is not written as null. |

`purchasedOn` and `updatesUntil` are the public date claims. This specification does not say which software or which rule packs those dates allow.

Example payload line, with an updates plan. The signature in section 6 covers this exact line:

```json
{"schemaVersion":1,"product":"gatekeeper","keyId":"gklic-dev-1","orgId":"ORG-EXAMPLE-001","cacRegistrationNumber":"RC0000000","purchasedOn":"2026-11-01","updatesUntil":"2027-11-01"}
```

The same claims with `updatesUntil` omitted are the one-time example in `claims-one-time.gklic`. `ORG-EXAMPLE-001` and `RC0000000` are synthetic.

---

## 5. Public vectors

[`New-LicenceVectors.mjs`](../../tests/vectors/licence/New-LicenceVectors.mjs) rewrites the files below from the development seed. Run it from the repository root with `node tests/vectors/licence/New-LicenceVectors.mjs`. The test project checks the committed files against this specification.

| File | Expected outcome |
|---|---|
| `claims-with-updates.gklic` | Signature verifies. Both date claims are present. |
| `claims-one-time.gklic` | Signature verifies. `updatesUntil` is absent. |
| `tampered-payload.gklic` | The payload line was changed after signing. The signature does not verify. |
| `tampered-signature.gklic` | The payload is intact and the signature hex was changed. The signature does not verify. |
| `unknown-key.gklic` | The header key identifier is `gklic-unknown`, which is not the development key. |
| `malformed-armour.gklic` | The end line is missing and a second payload line is present. |
| `key-id-mismatch.gklic` | The signature verifies and the payload `keyId` is not the header `Key-Id`. |
| `claims-dates-out-of-order.gklic` | The signature verifies and `updatesUntil` is earlier than `purchasedOn`. |
| `discovery.json` | A `--licence` path that does not exist is a missing file even when every later source is set. When no earlier source is set, a mounted file is the source that is used. |

A verifier's CR handling is checked in the test project by taking `claims-with-updates.gklic` and replacing each LF with CRLF. That file is not stored on its own, because the repository stores text with LF.

---

## 6. Development key

The vectors are signed with a development key. This key MUST NOT be used to issue a customer token. Production keys are not in this repository.

| Item | Value |
|---|---|
| `keyId` | `gklic-dev-1` |
| Seed | `00112233445566778899aabbccddeeff00112233445566778899aabbccddeeff` |
| Public key | `3ccd241cffc9b3618044b97d036d8614593d8b017c340f1dee8773385517654b` |

The seed is 32 bytes. The public key is the 32-byte Ed25519 public key, lowercase hex. The same values are in `development-public-key.json`, with `"label": "development-only"`.

Worked token for the payload in section 4. The signature hex is the development key's signature over that line:

```text
-----BEGIN GATEKEEPER LICENCE-----
Key-Id: gklic-dev-1
Signature: cc0cc347f5d3d84dc8cb4accc0b8cfb9d60031452282b1b72afb283974141ef9df906b8c1bd12848e03230c2cd145772f2d6db24fc7daf1d53c98ea7e1089c05
{"schemaVersion":1,"product":"gatekeeper","keyId":"gklic-dev-1","orgId":"ORG-EXAMPLE-001","cacRegistrationNumber":"RC0000000","purchasedOn":"2026-11-01","updatesUntil":"2027-11-01"}
-----END GATEKEEPER LICENCE-----
```
