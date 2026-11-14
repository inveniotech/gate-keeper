# E-008: Offline licence tokens and key rotation

## Context

A verifier needs a small signed format without online activation, a vendor
connection or machine fingerprinting.

## Decision

Use the [licence-token specification](../specs/licence-token-v1.md) for armour,
exact signed bytes, claims, source precedence and key identifiers. Verify the
signature before parsing claims and never re-serialise signed data.

A key identifier selects its trusted public key so a catalogue can retain
older verification keys during rotation. The [signing module](../../src/Signing/README.md)
owns the implementation boundary. Production issuance and key custody are
outside this format record. No key is generated or changed here.

## Status

**Accepted — owner decision, 13 November 2026.** This record accepts the format
and discovery details in section 0.4 of the [licence-token specification](../specs/licence-token-v1.md):
the readable line-based armour, restricted key identifier syntax, exact claim
names and bytes, ordered licence sources, fail-closed handling of an explicitly
requested missing file, and rejection of an `updatesUntil` date before
`purchasedOn`. The licence remains portable and offline; it does not call home,
fingerprint a machine, or merge claims from multiple tokens.

The product decision to identify the licensed legal entity with its CAC
registration number is accepted, but the field MUST NOT be used to issue a
production token until a lawyer confirms it may be carried there (IQ-39). The
[licence vector helper in the test suite](../../tests/)
is explicitly test-only and is not evidence of a production verifier.
Production token discovery, claim validation, issuance, key custody and key
rotation remain implementation and operations work; they do not reopen this
format decision. The [signing module](../../src/Signing/README.md) implements the
shared exact-byte Ed25519 primitive, not those production capabilities.

## Consequences

Token bytes remain portable. Parsing cannot promote unverified claims to trusted
data. Source precedence is deterministic, and an explicitly selected missing
file cannot silently select another organisation's token. Format approval does
not change entitlement policy or production trust roots. CAC field use remains
blocked on legal confirmation.

## Alternatives considered

Online activation and machine fingerprinting would conflict with offline,
portable licensing. A binary token envelope or a general-purpose JSON signature
format would add complexity without helping manual licence installation or
cross-stack verification. Re-serialisation would verify a different byte
sequence. Searching past an explicitly selected missing file could use the
wrong organisation's token, and allowing an updates end date before purchase
would create contradictory claims. None is introduced as a fallback.

## Traces

- P0-02, [issue #1](https://github.com/inveniotech/gate-keeper/issues/1).
- P0-06, [issue #5](https://github.com/inveniotech/gate-keeper/issues/5).
- Engineering Plan Draft 6, sections 4.8 and 7, E-008.
- IQ-07/35/37/38/39; NFR-001/002.
