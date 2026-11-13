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

Hosted CI passed on all five Python interpreters on commit 7961938, including
framework samples, strict types and package validation. The initial Python 3.15
build-tool failure was fixed by updating the existing build pin to 1.6.1.
No full local Python 3.14 run is claimed. The independent check required by
AGENTS.md rule 37 has since completed; its outcome is recorded below. This
pull request remains a draft and references, rather than closes, issue #42.
Companion documentation is tracked in linked website PR #42. Production
publication remains an owner step.

## Continuation

Complete production release-media integration and merge linked website PR #42
before a release. No merge, tag, release or package publication is authorised
here.
Unrelated Node and JVM changes in the original working directory were excluded.

## SDK restore follow-up

The owner requested the SDK/lock-file mismatch fix after the Python jobs passed.
Broad SDK roll-forward selected 10.0.404 even though CI installed 10.0.403. The
newer SDK implicitly required linker tools 10.0.15 while the committed lock files
required 10.0.14, causing NU1004 during locked restore. Exact SDK 10.0.403 selection
is restored with roll-forward disabled. The build guide explains that future
SDK, CI version and generated lock-file updates must be made together.

The mismatch was reproduced with SDK 10.0.404 before the change. With the exact
10.0.403 SDK in the official Linux container, locked restore passed without
changing any committed lock file, and the solution built with zero warnings or
errors. All 1,684 .NET tests passed; one existing symbolic-link corpus test was
skipped on the mounted filesystem. The middleware probe also published with
NativeAOT and trimming warnings treated as errors and passed when executed.
The first container restore hit a dependency download timeout; the successful
run used the existing local NuGet cache mounted read-only.

## 13 November 2026 source completion

This follow-up fixes the Python consent contract in section 12 of the accepted
middleware specification. Synchronous and asynchronous provider objects now
use `get_state`; callable providers remain compatible, and `get_state` takes
precedence when an object offers both. The public `ConsentProvider` and
`AsyncConsentProvider` protocols describe the application-owned methods.
The regression test failed before the change and passes now. Framework samples
exercise provider objects.

`Installation.from_package` reads signed media staged under the installed
package's `embedded/` directory. It shares the bounded read and verification
path with signed filesystem replacement. The package-data setting includes
the JSON document and all adjacent signature and watermark sidecars, while
the staging directory is ignored in the default source checkout. Public trust
anchors remain external to the candidate media. No production-signed media,
development fixtures or signing keys were added to the distributed source.

The reproducible `tests/conformance/check_python_media_installation.py` stages
existing development-only signed vectors in a temporary package copy, builds
the source distribution and wheel offline, checks both archives and strict
metadata, installs the wheel outside the checkout, and verifies identity and
redaction. The existing pack tests cover absent media, incomplete sidecars,
orphan recognised sidecars beside a valid pack, directory-shaped watermark
sidecars, a valid content stem ending in `.watermark`, tampering and an
untrusted signer. The package data includes only
recognised media suffixes; the installation check proves an unrelated staged
file is excluded. This source validation cannot establish
production release-media availability. Companion documentation is tracked in
linked website PR #42.

Replacement reads use direct bounded opens of the known JSON and sidecar
paths. Only a missing optional watermark file counts as absent; permission
errors and directory-shaped sidecars fail. A deterministic test blocks
directory listing while leaving the known files readable and verifies that
replacement still activates the newer signed pack.

Local Python 3.12 validation passed all five conformance scripts, both real
framework samples, strict mypy, Ruff lint and format, and the staged-media
package installation check. The parent also observed a passing locked .NET
restore and 1,684 passing tests, with one existing symbolic link test skipped.
The fresh independent rule-37 check found no remaining actionable findings.
It verified the no-list directory replacement, directory-shaped watermark
rejection and all 15 pack tests. Production-signed media and publication remain
owner steps; no release or publication is claimed.
