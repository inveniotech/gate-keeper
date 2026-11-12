# P3-05 implementation handoff

Refs #42. Implements the Python middleware operations in the accepted
[API contract](../../docs/specs/middleware-api-v1.md): UC-003, F-005,
NFR-001, NFR-004, NFR-027, NFR-028, NFR-029 and NFR-031.

## Done

The adapter supplies native bounded matching, all four validators, scalar offsets,
classification, redaction, literal masks, optional-rule disablement, independent
synchronous/asynchronous consent, atomic wildcard JSON filtering, pure Ed25519
verification, signed-pack verification, atomic same-identifier replacement,
bounded filesystem reads, logging, Django and FastAPI hooks. Runtime calls do
not require the scanner or network. There are no required runtime dependencies.

The Python package includes its licence and typing marker. The shared version
script generates both the Python constant and distribution version. CI adds
Python 3.11 to 3.15 conformance, framework samples, pinned type/lint/format checks,
source and wheel builds, strict metadata checks and an installed-wheel check.
No package is published by this change. The module guide documents the public
operations, framework registration, trust boundary and validation commands.

## Validation observed in the implementation session

- All five conformance scripts passed on Python 3.11.14 and 3.13.9. On Python
  3.15.0 the portability and native runtime checks passed, followed by all
  23 consent, pack/security and hook tests through the standard test runner.
- The real Django and FastAPI sample applications passed on Python 3.11.14
  with the pinned development requirements installed in a temporary environment.
- Pinned mypy 1.18.2 strict checking passed on all ten source files. Pinned Ruff
  0.14.4 lint and format checks passed.
- The pinned isolated build produced the source distribution and built the
  universal wheel from it. Twine 6.2.0 strict checks passed on both artefacts.
  Installing the wheel without dependencies and checking isolated imports,
  version metadata, typing marker and licence passed.
- Node conformance passed: 246 regex cases, 262 runtime cases, four validators,
  consent and 22 atomic response cases. JVM conformance produced the same
  successful runtime and regex results on the locally available Java 21.
- The full .NET test command completed without failing tests. The existing
  operating-system-dependent symbolic-link cases were skipped by their tests.
  The focused .NET middleware regression set passed all 33 tests.
- Shared runtime version regeneration check and dependency licence check passed.
- Spelling on committed files, glossary checks and workflow syntax validation
  passed. Python runtime conformance produced identical output in read-only
  containers with bridge networking and with networking disabled. Consent and
  host-hook tests also passed in the offline container.

## Release and review boundary

This is source implementation, not complete production installation media.
No production-signed embedded packs are supplied by this repository. The loader
accepts host-supplied signed embedded media and trusted public keys; it never
promotes unsigned authored packs or development keys into production trust.
Supply independently signed release media before claiming a distributable free
or paid installation. No production signing keys are needed for further work.

Hosted CI on all five Python interpreters must be observed before review is
complete. No full local Python 3.12 or 3.14 run is claimed. The independent check
phase required by AGENTS.md rule 37 still needs its own fresh session. This is
why the pull request is a draft and references, rather than closes, issue #42.
The linked website guide and production publication are outside this change.

## Continuation

Review the draft pull request in a fresh check session. Confirm every hosted
gate, including the unchanged zero-egress proof, and resolve any findings.
Complete release-media integration and linked website documentation before
closing #42. No merge, tag, release or package publication is authorised here.
Unrelated Node and JVM changes in the original working directory were excluded.
