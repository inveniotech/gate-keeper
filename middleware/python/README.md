# Python middleware

**Status: not in the first release.** The contract still describes the Python shape, so the adapter does not drift. The package is built after the .NET, Node and JVM adapters.

## Toolchain

| Concern | Choice |
|---|---|
| Runtime | Every Python version under upstream support when the middleware major is released (IQ-79); at the time of writing 3.11 to 3.15 |
| Web framework | Django middleware and FastAPI (Starlette) middleware |
| Package | `inveniotech-gate-keeper` on PyPI |
| Logging | The standard-library `logging` filter |
