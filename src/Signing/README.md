# Signature verification

The internal Ed25519 verification module (work package P1-04, issue #14). The licence verifier that Phase 1 builds uses it to check the Ed25519 signature over a licence token's payload line (the licence-token draft, section 2 of `docs/specs/licence-token-v1.md`). The module exposes one assembly-internal interface, `ISignatureVerifier.Verify(publicKey, message, signature)`, and every caller obtains the verifier from the one factory `SignatureVerifiers.Default`, so the signature package is swappable in this module alone and no caller ever references the package directly (IQ-11).

## Public contract

- `ISignatureVerifier.Verify(ReadOnlySpan<byte> publicKey, ReadOnlySpan<byte> message, ReadOnlySpan<byte> signature)` returns `true` when the 64-byte signature verifies over the exact message bytes against the 32-byte public key. Nothing is re-encoded before the check, which matches the licence-token rule that the signed bytes are never re-serialised (IQ-07).
- A wrong length — the public key is not 32 bytes or the signature is not 64 bytes — and every other malformed input is a returned `false`, never a thrown error. Malformed signature material is input an attacker chose, so it is a failed verification and not a tool error (rules 23 and 28 of `AGENTS.md`).
- The whole surface is `internal`. Consumers and test assemblies are listed in the `InternalsVisibleTo` item of the project file; the licence verifier adds itself there in its own card. Nothing here is a public API, so nothing here is a contract under rule 24.
- The pure-mode Ed25519 shape only: a 32-byte public key, a 64-byte signature and one exact message. The module has no signing and no key-parsing API, because the licence verifier receives keys as bytes and never signs.

## Dependencies

`BouncyCastle.Cryptography` (MIT, pinned in `Directory.Packages.props`, approved under rule 8 of `AGENTS.md`). The NativeAOT feasibility spike (P0-16, IQ-11) verified the package signs and verifies every pure-Ed25519 RFC 8032 vector under NativeAOT with zero trim and AOT warnings and adds about 76 KiB, so the pre-approved fallback (`Rebex.Elliptic.Ed25519`) is not referenced. The package appears only in this project file and in the test helper of the licence-vector project.

## How to test

From the repository root:

```powershell
dotnet test --filter FullyQualifiedName~Signing.Tests
```

The vector run is the full test-vector run rule 14 requires for any change here: the four pure-Ed25519 vectors of RFC 8032 section 7.1 (TEST 1, TEST 2, TEST 3 and TEST 1024) verify, forged inputs — a signature over a different message, a different signer's key, a flipped signature bit, swapped signature halves — do not, and the licence-token vectors under `tests/vectors/licence/` run in the same repository test suite.

## Performance and AOT

Measured on a NativeAOT `win-x64` build of the module on the development machine (Windows 11 x64, .NET 10.0.30x), with a harness in the machine's temporary folder; owners re-measure per release rather than trusting these figures, and the harness is not committed:

| Measurement | Value | Budget |
|---|---|---|
| First verification of a licence-shaped payload (181 bytes), cold | 0.050 ms | 10 ms (NFR-002) |
| Warm median over 5,000 verifications, licence payload | 0.044 ms | 10 ms (NFR-002) |
| Warm 95th percentile over 5,000 verifications, licence payload | 0.049 ms | 10 ms (NFR-002) |
| Cold and warm verification of a 1,024-byte message | 0.052 ms / 0.044 ms median | 10 ms (NFR-002) |

Verification leaves about 200× headroom under the 10 ms licence-verification budget, so the headroom requirement of the card is met. Timing figures are never asserted in tests (rule 26): tests prove the behaviour, this table records the measurement.
