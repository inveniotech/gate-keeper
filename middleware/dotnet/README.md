# .NET middleware

**Status: the P3-02 adapter ships the framework host around this core.** The
owner accepted the P3-01 contract on 9 November 2026.

## The core

`RuntimeMatcher` consumes a verified signed `MergedRules` load from the existing
pack loader. It rejects empty and local-pack loads; it performs no network
request or entitlement check. `Matches`, `Classify` and `Redact` execute every
loaded severity, ignoring repository paths and descriptive runtime context. The
scanner configuration is not an input. The emitting shape part is the
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
slash and star keys. A missing path is ignored; a present null leaf or null
intermediate container fails as a type error. Explicit null or empty content
types fail closed. Every matcher failure, including the engine's regex deadline,
becomes a content-free `ResponseFilterException`. Provider timeouts become
unknown with diagnostics unless the caller token was cancelled.

## Verified pack loading

`RuntimePacks` is the middleware's loader (middleware API, section 9). It calls
the existing engine loader with an empty local-pack list and an admission policy
that admits `bundled` and `plan-gated` content **without consulting a licence**;
`BundledPacksOnlyPolicy` refuses plan-gated packs and is not used here. The
engine keeps the signature, digest, schema, watermark and pack-line checks.
Unsigned local packs never load.

The loader takes the `SignedPackInput` sequences and an `IPackKeyCatalogue` the
installation supplies, so nothing embeds unsigned `rule-packs/*.pack.json` as if
they were verified. `RuntimePacks.LoadFromDirectory` reads the existing on-disk
sequences: each `*.pack.json` pairs with `*.manifest.armour` and
`*.signature.hex`, plus the optional `*.watermark.armour` and
`*.watermark.signature.hex` pair. No new container is invented. The replacement
rules, which the engine merge does not implement by itself:

- A candidate that does not verify keeps the already verified embedded load. If
  nothing verified, start-up is refused.
- The same pack identifier with a strictly greater `YYYY.MM.P` triple replaces
  the embedded pack.
- An older or equal version, or a different pack identifier, throws
  `PackLoadException` and refuses start-up.
- A local or unsigned pack never loads.

`PackLoadException` names the pack and the recovery action and never carries
content bytes. `Versions()` stays side-effect-free and pack-ID sorted.

## The ASP.NET Core host

`AddGateKeeper` on `IServiceCollection` registers one verified
`RuntimeMatcher` (built from the load, with the disabled optional rules), the
compiled `ResponseFilter` over the protected JSON Pointers, the `ConsentCheck`
over the application's provider, the middleware and the two filters. A second
overload accepts a `RuntimeMatcher` the application already built. `UseGateKeeper`
installs the middleware; call it after exception handling and before the
endpoints. The minimal-API filter is `GateKeeperEndpointFilter`, added with
`AddEndpointFilter<GateKeeperEndpointFilter>()`; the MVC global result filter is
registered automatically.

One shared, bounded buffer backs all three hooks. It redirects the response body
into a private stream capped at the 65,536-scalar maximum (262,144 bytes),
decodes it strictly as UTF-8, parses the private copy with `JsonNode`, applies
the existing `ResponseFilter`, and writes the transformed body only after the
whole transform succeeds. Nothing in the hooks uses reflection, which is what
keeps them trim and ahead-of-time safe. A streaming or oversized response, a
missing, empty or non-JSON content type, a non-string selected value, a
non-UTF-8 body, or a redaction failure produces `ResponseFilterException` with
no original or partial body; committed headers abort the connection. An empty
body and a response without configured protected paths pass through untouched.
The inner filter sets an `HttpContext` item after a successful transform, and
the outer middleware passes its buffer through when that item is set, so a
response is redacted once.

Consent registration does not change redaction. Start-up diagnostics — disabled
optional rules and the absent consent provider — are buffered at registration
and written once through the host's logger when it starts; runtime consent faults
are written immediately. Every message names rule identifiers, purposes and
exception type names only.

## Logging integrations

`GateKeeperRedactor` (package `InvenioTech.GateKeeper.Compliance.Redaction`)
subclasses `Microsoft.Extensions.Compliance.Redaction.Redactor` and delegates to
`RuntimeMatcher.Redact`. `GetRedactedLength` returns the mask length after
redaction, because masks are longer than the matched span. Argument and limit
failures propagate; the redactor never returns the original text.
`AddGateKeeperRedaction` registers it for the `GateKeeperClassifications.Pii`
classification and as the fallback. Call it after `AddGateKeeper`, so the
redactor resolves the verified runtime.

> **Known gap.** The automatic `ILogger` message redaction switch
> (`LoggingRedactionExtensions.EnableRedaction`) lives in
> `Microsoft.Extensions.Telemetry`, a package rule 8 has not approved for this
> card. Applications that need automatic message redaction must wait for an
> owner-approved dependency, or consume the registered `IRedactorProvider`
> explicitly as the sample does. The classification annotations
> (`[LogProperties]`, tag providers) belong to the same unapproved package.

`GateKeeperEnricher` (package `InvenioTech.GateKeeper.Serilog`) replaces every
scalar string property of the event through `AddOrUpdateProperty` before any
sink renders it. `GateKeeperDestructuringPolicy` builds the structures for
`{@...}` captures itself, so it redacts the string keys and members of
dictionaries and the strings of string sequences, and recurses through nested
dictionaries. Types Serilog destructures by reflection — anonymous and record
types — are outside the policy, because the adapter adds no reflection and
stays trim and ahead-of-time safe. Neither integration logs the original value;
exception messages are out of scope.

## Dependencies and validation

The core's only project dependency is the existing engine. The approved
packages are `Microsoft.Extensions.Compliance.Redaction` (10.11.0, MIT) and
`Serilog` (4.4.0, Apache-2.0), under the owner's rule-8 approval dated
10 November 2026 for this card only. ASP.NET Core is a framework reference.

```powershell
dotnet test tests/Middleware.Tests -c Release
dotnet test tests/Engine.Tests -c Release --filter 'FullyQualifiedName~RuntimeMatcherTests|FullyQualifiedName~ConsentCheckTests|FullyQualifiedName~ResponseFilterTests'
dotnet publish tests/conformance/NativeProbe -c Release -r win-x64 -p:PublishAot=true -p:PublishTrimmed=true -p:ILLinkTreatWarningsAsErrors=true -p:IlcTreatWarningsAsErrors=true -p:NuGetLockFilePath=obj/packages.native.lock.json -o artifacts/middleware-native
./artifacts/middleware-native/MiddlewareNativeProbe.exe
dotnet publish samples/aspnetcore -c Release -r win-x64 -p:PublishAot=true -p:PublishTrimmed=true -p:ILLinkTreatWarningsAsErrors=true -p:IlcTreatWarningsAsErrors=true -o artifacts/sample-native
./artifacts/sample-native/InvenioTech.GateKeeper.Sample.AspNetCore.exe <pack-directory> <key-file>
```

The native probe runs on Linux in CI with trim and ahead-of-time warnings treated
as errors, and the sample must publish with the same flags and run the two redacted
lines without a warning.

## Toolchain

| Concern | Choice |
|---|---|
| Runtime | .NET 10 |
| Web framework | ASP.NET Core: one middleware, an endpoint filter for minimal APIs, and a result filter for MVC controllers |
| Package | `InvenioTech.GateKeeper.*` on NuGet |
| Logging | `Microsoft.Extensions.Logging` through the `Microsoft.Extensions.Compliance.Redaction` abstractions, and Serilog |
