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

**Accepted in part — recorded on 13 November 2026.** IQ-07/35/37/38/39 are
recorded owner decisions in the [licence-token decision table](../specs/licence-token-v1.md#01-decision-status).
They settle exact signed bytes, the payload limit, date claims, source precedence
and entity identification. The owner's confirmation recognises those decisions as
approved. [The signing module](../../src/Signing/README.md) implements the shared
exact-byte Ed25519 primitive; production token discovery, claim validation and
rotation are not established by that primitive alone.

**Awaiting owner review and acceptance:** the additional format and discovery
details in section 0.4 of the token specification that are neither implemented in
production nor separately accepted, including armour, identifier character rules,
source strings, missing-file handling and claim-date validation. The
[licence vector helper](../../tests/InvenioTech.GateKeeper.Tests/LicenceVectorSupport.cs)
explicitly identifies itself as test-only, so it is not evidence of a production
verifier. The lawyer confirmation recorded by IQ-39 also remains outstanding.
Production key custody and issuance remain outside this record.

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
