# Python middleware

The P3-05 adapter implements the accepted [middleware API v1](../../docs/specs/middleware-api-v1.md)
for Python 3.11 to 3.15. It classifies values, redacts text and log records,
filters Django and FastAPI response fields, checks application-owned consent
providers and verifies signed installation packs. It does not access the
network, require the command-line interface or enforce licences.

The distribution is `inveniotech-gate-keeper`; imports use
`inveniotech.gatekeeper`. The core has no required runtime dependencies.
Django and FastAPI are supplied by the host application. Tool and framework
pins in `requirements-dev.txt` are development-only dependencies.

## Installation and trust

This source build contains no production-signed embedded packs and is not
complete release installation media. A release host supplies its embedded
`SignedPack` objects and trusted public-key mapping to `Installation`. Never
use development fixture keys as production trust. Private signing keys are
neither accepted nor needed by this API.

For an offline distribution build, stage each independently signed content
document and its adjacent sidecars in
`src/inveniotech/gatekeeper/embedded/` in a temporary copy of this package.
Use a common stem, such as `free.json`, `free.manifest.armour` and
`free.signature.hex`. A paid pack may also have
`free.watermark.manifest.armour` and `free.watermark.signature.hex`; supply
both watermark files or neither. The staged directory is ignored by Git and
must never contain development fixtures in a release build. Build the source
distribution and wheel from that temporary copy, then verify the installed
wheel with trust anchors obtained independently of those candidate media.
No private signing key belongs in the package or application.

```python
from inveniotech.gatekeeper import Installation

# Obtain these public keys from an independently trusted release channel.
installation = Installation.from_package(trusted_public_keys)
runtime = installation.runtime
```

`from_package` uses bounded reads and the same signature, digest, schema and
version verifier as `Installation`. An absent pack, incomplete sidecars,
untrusted signer or tampered document raises `PackLoadError` before a runtime
is exposed. This does not make the source checkout a production installation.

Each `SignedPack` carries exact content bytes, the armoured manifest and detached
signature bytes. Optional watermark armour and signature must be supplied
together. `Installation` verifies all embedded packs before exposing `runtime`.
Invalid signatures, schema, digests or versions raise `PackLoadError` without
activating the candidate.

`replace_from_path(Path(...))` accepts an explicit replacement JSON file and
adjacent `.manifest.armour` and `.signature.hex` files. Optional watermark
files use `.watermark.manifest.armour` and `.watermark.signature.hex`. The loader
replaces the JSON suffix with these suffixes. Replacement must have the same
embedded pack identifier and a strictly newer numeric calendar version.
After replacement, acquire `installation.runtime` when registering hooks;
previously acquired snapshots retain their verified rules. No automatic reload
or download is performed.

## Core operations

```python
from inveniotech.gatekeeper import Installation

# The application supplies signed embedded media and trusted public keys.
installation = Installation(embedded_packs, trusted_public_keys)
runtime = installation.runtime
safe_text = runtime.redact(application_text)
finding = runtime.classify(application_text, {"surface": "value"})
versions = runtime.versions()
```

Findings contain rule metadata and Unicode scalar offsets, never matched text.
Invalid text or context raises `ValueError`; input or redacted output exceeding
65,536 scalars raises `InputLimitError`. Reserved literal masks remain unchanged.
Runtime objects copy their rule inputs and calls keep match state local.
Only pack-marked optional rules may be disabled at start-up.

`RuntimeMatcher` also accepts already verified rule models; it does not replace
signed installation verification. `versions()` reports the adapter version and
pack identities sorted by identifier. Regenerate version constants and package
metadata with `tests/conformance/Sync-RuntimeVersion.ps1`.

## Logging and framework hooks

Attach `inveniotech.gatekeeper.logging.RedactingFilter(runtime)` to each handler
requiring message redaction. It redacts the fully formatted message and clears
its arguments. Exception payloads and arbitrary extra record fields are outside
this filter; do not log sensitive data there.

Register `DjangoMiddleware(get_response, runtime, paths)` through an application
middleware factory, or call
`app.add_middleware(FastAPIMiddleware, runtime=runtime, paths=paths)` in FastAPI.
Imports are `inveniotech.gatekeeper.django.DjangoMiddleware` and
`inveniotech.gatekeeper.fastapi.FastAPIMiddleware`.

Paths use JSON Pointer with `*` for every array element and `~2` for a literal
asterisk property. `/customers/*/phone` protects every selected phone. Invalid
paths fail at registration. Protected responses must be bounded JSON; missing
content type, streaming, selected non-string values and redaction failures raise
`ResponseFilterError` before transformed headers or bytes are sent. The ASGI
hook requires a declared content length and one complete body message. The
default host byte bound is 1,048,576, independent of the scalar cap. Unprotected
responses pass through unchanged. The host must abort a failed response after
transmission has begun.

Runnable [Django](../../samples/django/README.md) and
[FastAPI](../../samples/fastapi/README.md) samples exercise real framework objects
using the synthetic conformance corpus, without a network server.

## Consent

`ConsentCheck(provider, async_provider, diagnostic)` takes independent synchronous
and asynchronous providers. Implement `ConsentProvider.get_state` and
`AsyncConsentProvider.get_state` respectively. Existing callable providers
remain supported; an object's `get_state` takes precedence over `__call__`.
Use `check` / `is_granted` for the synchronous provider and `acheck` /
`ais_granted` for the asynchronous provider. Providers
must be safe for concurrent calls. No consent state is cached. Missing providers,
invalid states and provider failures return `unknown`; only `granted` returns
true. Invalid arguments raise `ValueError` before calling the provider.
Caller task cancellation propagates. A provider's own cancellation is a provider
failure when the caller task is not cancelling. Diagnostics contain only purpose
and exception type, never subject, exception message or stack trace. Consent
does not alter redaction or classification.

## Validation and packaging

From the repository root:

```sh
python -m pip install -r middleware/python/requirements-dev.txt
python tests/conformance/check_patterns.py
python tests/conformance/check_runtime.py
python tests/conformance/check_python_behaviour.py
python tests/conformance/check_python_packs.py
python tests/conformance/check_python_hooks.py
python samples/django/app.py
python samples/fastapi/app.py
```

From `middleware/python`:

```sh
python -m mypy
python -m ruff check src
python -m ruff format --check src
python -m build
python -m twine check --strict dist/*
```

CI runs these checks on Python 3.11 to 3.15, installs the wheel without dependencies
and checks an isolated import. The source distribution builds the wheel;
`py.typed` and the Apache-2.0 licence are included. Build outputs are ignored.
Package publication and production release-media signing remain human actions.
