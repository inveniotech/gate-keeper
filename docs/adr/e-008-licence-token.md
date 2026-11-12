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

**Draft — awaiting owner review and approval.** E-008 maps to the token
specification. IQ-07/35/37/38/39 and that specification take precedence over
Draft 6's older claim examples.

## Consequences

Token bytes remain portable. Parsing cannot promote unverified claims to trusted
data. Format approval does not change entitlement policy or production trust roots.

## Alternatives considered

Online activation would conflict with offline verification. Re-serialisation
would verify a different byte sequence. Neither is introduced as a fallback.

## Traces

- P0-02, [issue #1](https://github.com/inveniotech/gate-keeper/issues/1).
- P0-06, [issue #5](https://github.com/inveniotech/gate-keeper/issues/5).
- Engineering Plan Draft 6, sections 4.8 and 7, E-008.
- IQ-07/35/37/38/39; NFR-001/002.
