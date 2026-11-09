# .NET middleware

**Status: executable reference core for P3-00.** Framework hooks and package
publication remain P3-02. The owner accepted the P3-01 contract on 9 November 2026.

`RuntimeMatcher` consumes an already verified signed `MergedRules` load from
the existing pack loader. It rejects empty and local-pack loads; it performs no
network request or entitlement check. `Matches`, `Classify` and `Redact` execute
every loaded severity, ignoring repository paths and descriptive runtime context.
The scanner configuration is not an input. The emitting shape part is the
finding/redaction span. Offsets are Unicode scalars, with the rule-pack 65,536
scalar cap on input and result. Literal masks use the pack identifier grammar
and are removed before matching; masks from no-longer-loaded rules remain
reserved. Selection is earliest, longest, then ordinal rule identifier.

Only optional rules may be disabled through the separate constructor parameter;
the constructor requires one safe start-up diagnostic when disablement is used.
`Versions()` reports the code version and exact loaded identities and digests.
`ConsentCheck` supplies stateless `CheckAsync` and `IsGrantedAsync` over an
application-owned provider, with safe type/purpose diagnostics and cancellation.
`ResponseFilter` atomically transforms a private JSON tree. A complete `*`
segment selects every array element; `~0`, `~1`, `~2` address literal tilde,
slash and star keys. The framework must bound parsing first and must abort an
already committed protected response when `AbortRequired` is true. No HTTP
framework hook is claimed by this tree-level core.

A missing path is ignored; a present null leaf or null intermediate container
fails as a type error. Protected responses with committed headers always abort:
the contract defines an already started body as streaming. Explicit null or
empty content types fail closed. Every matcher failure, including the engine's
regex deadline, becomes a content-free `ResponseFilterException`. Provider
timeouts become unknown with diagnostics unless the caller token was cancelled.

## Dependencies and validation

The only project dependency is the existing engine. There is no new package.

```powershell
dotnet test tests/Engine.Tests -c Release --filter 'FullyQualifiedName~RuntimeMatcherTests|FullyQualifiedName~ConsentCheckTests|FullyQualifiedName~ResponseFilterTests'
dotnet test tests/Corpus.Tests -c Release --filter FullyQualifiedName~ConformanceExportTests
dotnet publish tests/conformance/NativeProbe -c Release -r win-x64 -p:PublishAot=true -p:PublishTrimmed=true -p:NuGetLockFilePath=obj/packages.native.lock.json -o artifacts/middleware-native
./artifacts/middleware-native/MiddlewareNativeProbe.exe
```

The same native probe runs on Linux in CI with trim and ahead-of-time warnings
treated as errors. A verified signed-pack container loader, ASP.NET Core and
logging registrations remain the later adapter card; the reference core does
not treat arbitrary source pack JSON as a verified installation.

## Toolchain

| Concern | Choice |
|---|---|
| Runtime | .NET 10 |
| Web framework | ASP.NET Core: one middleware, an endpoint filter for minimal APIs, and a result filter for MVC controllers |
| Package | `InvenioTech.GateKeeper.*` on NuGet |
| Logging | `Microsoft.Extensions.Logging` through the `Microsoft.Extensions.Compliance.Redaction` abstractions, and Serilog |
