# Node.js / TypeScript middleware

Gate-Keeper supplies offline classification, redaction, consent checks and atomic
response filtering. `@inveniotech/gate-keeper` supports Node 22.18.0 and later,
with ECMAScript modules (ESM), CommonJS and TypeScript declarations. It has no
runtime dependencies. Express, NestJS, pino, winston and RxJS remain application
dependencies; the hooks use structural interfaces and import none of them.

This implements P3-03, issue #40, against middleware API version 1 (UC-003,
F-005, NFR-001/004/028/029/031, IQ-09/31/76/79). The IQ-31 framework selection
still awaits interview validation. Production signed pack bytes and an npm
release remain human release steps. No production pack or trusted development
key is embedded. The source package is private; only the manually dispatched
publication workflow removes that flag in its release workspace.

## Public entry points

The root exports `loadInstallation`, `verifySignedPack`, `RuntimeMatcher`,
`ConsentCheck`, `ResponseFilter`, their models and named errors `PackLoadError`,
`InputLimitError` and `ResponseFilterError`. Subpaths `/express`, `/nest`, `/pino`
and `/winston` export their corresponding factories. Both module formats expose
the same operations.

`RuntimeMatcher` is the existing execution core for verified rule models.
`classify(value, context)` returns a finding without matched text; `redact(text)`
preserves unrelated Unicode scalars and reserves existing redaction masks. Both
enforce the 65,536-scalar cap and reject invalid Unicode. Scanner profiles, path
scopes, severity overrides, `packs.local` and consent state are not redaction
inputs. Only identifiers marked `redaction.optional` may be disabled, with a
start-up diagnostic. `versions()` reports adapter version and verified pack
identities, calendar versions and content SHA-256 digests in pack-ID order.

`ConsentCheck` uses an application-owned asynchronous provider. Providers must
be safe for concurrent calls. Answers are never cached. Missing providers or
provider failures return `unknown` with safe metadata diagnostics; the caller's
`AbortSignal` propagates cancellation. Consent does not influence redaction.

## Verified installation

The application supplies its release's approved raw 32-byte Ed25519 public keys
and signed embedded byte sequences. These variables come from the application's
release packaging, rather than unverified source JSON:

```js
import { loadInstallation } from '@inveniotech/gate-keeper';
const runtime = loadInstallation({
  embedded: [{ name: 'release-pack', contentBytes, manifestArmour, manifestSignature }],
  keys: { pack: approvedPackPublicKeys, watermark: approvedWatermarkPublicKeys },
  replacementDirectory: '/application/replacement', // Optional; omit when unused.
  disabled: [],
  diagnostic: (message) => applicationLogger.warn(message),
});
```

Verification checks the exact manifest payload signature using `node:crypto`,
both content hashes, strict UTF-8, schema 1, calendar facts, engine compatibility
(engine 0.2.0), rule shapes and manifest identity. Content is capped at 16 MiB
and JSON nesting at 64 levels. Only `bundled` and `plan-gated` provenance is
accepted. A detached watermark is optional; when present its signature and
content-digest binding must verify with the separate watermark catalogue.
No licence check or network access occurs.

A replacement directory contains these named inputs; other entries are ignored
and directories are never walked:

```text
pack.json             Exact UTF-8 content bytes, at most 16 MiB.
manifest.armour       Four-line manifest armour, at most 8 KiB.
manifest.signature    Raw 64-byte Ed25519 signature, not hexadecimal text.
watermark.armour      Optional four-line detached block, at most 8 KiB.
watermark.signature   Optional raw 64-byte signature; both watermark files are required together.
```

Absent or invalid embedded packs refuse start-up. Verified replacements must
have the same identifier as an embedded pack and a strictly greater numeric
year/month/patch triple. Older, equal or different-ID replacements refuse
start-up. Unsupported schemas or engine requirements also refuse start-up.
Unreadable, tampered or otherwise invalid replacement bytes are discarded,
retaining the already verified embedded packs, with a diagnostic when supplied.
No unverified candidate becomes active. Errors name the pack and recovery action,
without including pack bytes or application content.

## Express and NestJS

Protected paths are JSON Pointers, with explicit array wildcards such as
`/customers/*/phone`; use `~2` for a literal `*` object property. Missing object
properties are ignored. Selected nulls and other non-string values fail.
Filtering uses the existing `ResponseFilter` and completes before a body is sent.

Both factories require a positive `maxBodyBytes` chosen by the host. This bounds
the UTF-8 serialisation before and after redaction. Only inert JSON trees up to
64 levels deep are accepted; cycles, accessors and custom serialisers fail.
Set a JSON content type before invoking the hook. Missing, empty and non-JSON
types fail; the hooks never infer JSON. Express raw `send` calls, buffers and
streams passed to `send` fail closed with configured paths. Protected routes must use
`json`. When present, native `write` and `end` are intercepted: direct calls and
piped streams fail before a body write. Only synchronous delegation from filtered
`json` to the host body methods is allowed, including empty HEAD or no-body
completion. After rejection, further body calls remain closed. Committed protected responses abort
through `destroy`; otherwise Express calls `next(error)` and Nest errors its
observable. No configured paths means unchanged pass-through.

```js
import { createExpressMiddleware } from '@inveniotech/gate-keeper/express';
app.use(createExpressMiddleware(runtime, ['/customers/*/phone'], 1024 * 1024));
app.get('/customers', (_request, response) => {
  response.type('application/json');
  response.json(customerResult);
});
```

```js
import { createNestInterceptor } from '@inveniotech/gate-keeper/nest';
// The controller or an earlier interceptor must set the actual JSON content type.
app.useGlobalInterceptors(createNestInterceptor(runtime, ['/customers/*/phone'], 1024 * 1024));
```

The Nest interceptor delegates cancellation to the upstream cancellation handle. Its
minimal observable supports `subscribe` and `pipe`; real RxJS sources retain
their own `lift`/`pipe` implementation so outer interceptors can compose operators.
Hosts must handle errors without sending original protected content.

## Logging hooks

String arguments and string properties, including nested properties, pass through
`redact`; the top-level `level` is preserved. Records are copied. A redaction
failure drops the whole original record and emits a fixed metadata message.
Install the winston transform before formats that produce final rendered text.

```js
import pino from 'pino';
import { createPinoHooks } from '@inveniotech/gate-keeper/pino';
const logger = pino({ hooks: createPinoHooks(runtime) });
```

```js
import winston from 'winston';
import { createWinstonFormat } from '@inveniotech/gate-keeper/winston';
const logger = winston.createLogger({
  format: winston.format.combine(
    winston.format(createWinstonFormat(runtime))(),
    winston.format.json(),
  ),
  transports: [new winston.transports.Console()],
});
```

## Build, test and release

From the repository root:

```sh
node tests/conformance/check-patterns.mjs
node tests/conformance/check-runtime.mjs
node --test middleware/node/*.test.ts
```

From `middleware/node`:

```sh
npm ci --ignore-scripts
npm run build
npm run test:package
npm pack --dry-run
```

TypeScript 5.9.3 is a pinned Apache-2.0 development-only compiler, used to emit
strict builds into `dist/esm` and `dist/cjs` with declarations. It adds no runtime
or .NET ahead-of-time compatibility cost. `build.mjs` also copies the root licence
and writes the CommonJS package marker. Build outputs are ignored and regenerated.
The narrow `node-builtins.d.ts` declarations are build-only and are not packaged.
Version metadata remains generated by `tests/conformance/Sync-RuntimeVersion.ps1`.
CI runs conformance, adapter tests and package builds on Node 22, 24 and 26 without
changing the existing five-minute budget or vector generator.

A person configures npm trusted publishing for `.github/workflows/publish-node.yml`
and manually dispatches it for a reviewed release. That workflow validates the
package and uses `npm publish --provenance` with OpenID Connect identity; it has
no pull-request or scheduled trigger. This implementation does not publish.
