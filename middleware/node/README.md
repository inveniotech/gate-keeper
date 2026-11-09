# Node.js / TypeScript middleware

**Status: executable native core for P3-00.** Express/NestJS and logging hooks,
signed installation loading and package publication remain P3-03. This module
does not claim that unsigned source JSON is a verified installation.

`runtime.ts` is strict TypeScript with no runtime dependency. Current supported
Node releases execute its erasable syntax directly. `RuntimeMatcher` consumes
already validated, verified rule models. Its bounded, memoised shape interpreter
implements all four validators, exclusions, keyword windows, per-rule resume
semantics and exact list tokens without native regex backtracking. `matches`,
`classify` and `redact` have no repository-path argument and never select rules
by paths, surface or field name. Scanner scopes are applied only in test fixtures.
Scalar offsets, emitted spans,
mask reservation, overlap selection and the 65,536-scalar cap match .NET.

`consent.ts` supplies the uncached asynchronous tri-state helper.
`response.ts` atomically transforms an already parsed, host-bounded JSON tree
using explicit array wildcards and pointer escapes `~0`, `~1`, `~2`. Hosts must
honour `abortRequired`; the pure core never writes response bytes. Safe metadata
diagnostics contain no subjects or provider messages. Scanner configuration and
consent state cannot alter redaction. `versions()` reports supplied verified
pack identities without application inputs.

Version constants and package metadata are generated from `middleware/version.txt`
by `tests/conformance/Sync-RuntimeVersion.ps1`. CI checks regeneration. Conformance
compares version reporting with actual .NET loader metadata and independently
hashes the source pack files. Supplied metadata is a loader trust boundary;
reporting it does not verify a signature or establish rule provenance.

Hosts must supply the actual `contentType`; absent, null and empty values fail
closed on protected responses. A present null leaf or intermediate container is
a type error; missing properties are ignored. Committed protected responses
always abort because an already started body is streaming under the contract.
A provider's own abort returns unknown with diagnostics; only the caller's
aborted signal propagates cancellation.

## Validate

```sh
node tests/conformance/check-patterns.mjs
node tests/conformance/check-runtime.mjs
```

The generated .NET expectations cover every authored rule and synthetic Phase 2
case plus explicit runtime overlap, astral-character, mask, consent and response
families. CI runs both commands on Node 22, 24 and 26. The declaration surface
has also been checked locally with TypeScript strict mode. No npm package is
published by this card.

## Toolchain

| Concern | Choice |
|---|---|
| Runtime | Every Node LTS line under upstream support when the middleware major is released (IQ-79); at the time of writing Node 22, 24 and 26 |
| Web framework | Express middleware and a NestJS interceptor |
| Package | `@inveniotech/gate-keeper-*` on npm |
| Logging | pino and winston |
