# Middleware package dry runs

P3-08 builds packages for the three MVP ecosystems without uploading to a
registry. It implements IQ-50 packaging and NFR-031 version traceability.
No new runtime dependency is introduced. Gradle publication and signing use
built-in plugins. Existing conformance and NativeAOT checks remain in place.

## Reproduce

Use the repository's pinned .NET SDK, Node.js 26, JDK 21 and Gradle 8.14.3:

```powershell
./tests/conformance/Sync-RuntimeVersion.ps1 -Check
./ci/middleware-release/nuget.ps1
npm ci --prefix middleware/node --ignore-scripts
npm run build --prefix middleware/node
npm run test:package --prefix middleware/node
node --test ci/middleware-release/release.test.mjs
node ci/middleware-release/npm.mjs
gradle -p middleware/jvm --no-daemon publishAllPublicationsToDryRunRepository
gradle -p ci/middleware-release/maven-consumer --no-daemon run
```

The workflow retains NuGet and npm archives and a local Maven repository for
review. No pull-request job has publication credentials or write permissions.
NuGet restores a separate consumer against the packaged Engine, Signing and
four middleware libraries, without project references. npm installs the real
archive in a temporary project and checks every ESM and CommonJS entry point;
the existing package test checks declarations. Maven resolves all four modules
from the local repository and executes the core consent helper. Sources and
Javadoc jars accompany each Maven module. The Java documentation tool documents
Java sources; Kotlin hook documentation remains in the source jar and READMEs.

## Versions and release handoff

The existing middleware version source is `middleware/version.txt`; regenerate
runtime stamps with `Sync-RuntimeVersion.ps1`. These packages currently share
that version. The CLI tag and engine product stamp do not set the middleware
package version. IQ-50 permits independent versions and requires Semantic
Versioning for middleware. This card does not introduce release-please or change
the version policy; separate version streams need matching runtime stamps.

The owner confirmed on 16 November 2026 that Maven Central verification of the
`africa.gatekeeper` namespace completed successfully. This records the owner's
confirmation; the package dry run does not independently inspect the portal.

Actual registry publication belongs to P4-09 and requires human review. Before
publication, confirm the npm scope and trusted publisher for `publish-node.yml`,
NuGet ownership and any adopted package-signing policy. The local dry run
does not prove that these other accounts are configured. npm's existing human-triggered
workflow publishes with provenance; this workflow does not dispatch it.

Maven publication emits Central metadata, dependency POMs, sources and Javadoc
jars. A human can enable Gradle's external GPG signatory with `-P signRelease=true`
in the approved release environment; it then requires signatures and fails if
the signatory is unavailable. Never place production keys in the repository or
pass them to this dry run. The reviewed local repository must be signed and
submitted through the Central portal at release time. No remote Maven upload
endpoint or credentials are configured here. NuGet packages are unsigned until
the owner adopts and configures package signing. CLI Windows code signing and release
checksum signatures are separate from Maven and NuGet package signing.

## Recovery

Build outputs live under ignored `artifacts/` and Gradle build directories.
Re-run after correcting a failed validation. A dry run cannot overwrite a
registry release because it never contacts a publication endpoint. Resolve
namespace or signing setup before attempting a production release.
