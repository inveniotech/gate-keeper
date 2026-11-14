# Zero-egress proof

P1-16 implements NFR-001 using the published `gatekeeper` Linux NativeAOT executable. The pull-request workflow runs every golden corpus repository twice in the same runtime image: once with Docker's normal network and once with `--network none`. Standard output, standard error and exit codes must agree byte for byte. A tool error, a network-related diagnostic, an empty corpus or a missing probe finding fails the proof. Containers are unprivileged and the binary and input mounts are read-only.

Run on a Linux Docker host:

```sh
dotnet publish cli -c Release -r linux-x64 -p:PublishAot=true -p:PublishTrimmed=true -o artifacts/native
bash ci/zero-egress/Test-ZeroEgress.sh artifacts/native/gatekeeper
```

The public script is the reproducible proof behind “verifiable, not promised”. It records both evidence documents and diagnostics under `artifacts/zero-egress`. Every invocation uses the same explicit evaluation date. A synthetic local pack adds one mandatory finding to every copied case, proving that the real matcher and results writer ran successfully. Original corpus files are copied without changing their bytes, including UTF-16, binary, ignored and minified cases. No source content enters the proof artefacts. The corpus's semantic detection expectations still depend on the Phase 2 bundled rules; this test establishes network independence, not detection recall. Images are prepared before the isolated runs; only the scanner's execution is isolated.
