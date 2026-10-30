# .NET middleware

**Status: not started.** The ASP.NET Core adapter arrives in Phase 3 (P3-02).

## Toolchain

| Concern | Choice |
|---|---|
| Runtime | .NET 10 |
| Web framework | ASP.NET Core: one middleware, an endpoint filter for minimal APIs, and a result filter for MVC controllers |
| Package | `InvenioTech.GateKeeper.*` on NuGet |
| Logging | `Microsoft.Extensions.Logging` through the `Microsoft.Extensions.Compliance.Redaction` abstractions, and Serilog |
