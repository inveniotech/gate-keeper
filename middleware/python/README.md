# Python middleware

**Status: not started.** The Python adapter arrives in Phase 3 (P3-05).

## Toolchain

| Concern | Choice |
|---|---|
| Runtime | Every Python version under upstream support when the middleware major is released (IQ-79); at the time of writing 3.11 to 3.15 |
| Web framework | Django middleware and FastAPI (Starlette) middleware |
| Package | `inveniotech-gate-keeper` on PyPI |
| Logging | The standard-library `logging` filter |
