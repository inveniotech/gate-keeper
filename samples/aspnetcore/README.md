# ASP.NET Core sample

The application registers the service and response middleware with verified packs,
protects `/customer/bvn`, and resolves `ConsentCheck` through dependency injection.
The compliance redactor and Serilog enricher both protect the same synthetic
value. The terminal request handler returns bounded JSON; middleware filters the
complete body before it reaches the caller. No CLI is invoked at runtime.

From the repository root, after preparing the development media described in
[the sample guide](../README.md):

```powershell
$media = (Resolve-Path artifacts/sample-media).Path
dotnet run --project samples/aspnetcore -- $media (Join-Path $media public-keys.json)
dotnet test tests/Middleware.Tests --filter FullyQualifiedName~SampleAcceptanceTests
```

The executable prints two redacted log lines and one filtered JSON response.
`Create` builds the same application used by the acceptance tests. The tests
invoke its real request delegate with an in-memory response stream and check
both granted and denied answers. No listening socket is opened. A missing,
tampered or incomplete pack, or a catalogue containing seed material, refuses
start-up before any application content is printed. The legacy `Run` entry
point remains a logging-only check for compatibility with its existing tests.
