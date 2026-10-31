# Node.js / TypeScript middleware

**Status: not started.** The Node adapter arrives in Phase 3 (P3-03).

## Toolchain

| Concern | Choice |
|---|---|
| Runtime | Every Node LTS line under upstream support when the middleware major is released (IQ-79); at the time of writing Node 22, 24 and 26 |
| Web framework | Express middleware and a NestJS interceptor |
| Package | `@inveniotech/gate-keeper-*` on npm |
| Logging | pino and winston |
