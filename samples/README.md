# Sample applications

P3-07 (#44), F-005, UC-003 and NFR-001/004/029. The four MVP examples use real
ASP.NET Core, Express, NestJS and Spring Boot applications. Each demonstrates
log redaction, atomic JSON response filtering and an application-owned consent
provider. Tests run actual framework request pipelines in process, without
opening a listening socket or making a network request.

| Framework | Application | Logging integration |
| --- | --- | --- |
| ASP.NET Core | [aspnetcore](aspnetcore/README.md) | Compliance redaction and Serilog |
| Express | [express](express/README.md) | pino |
| NestJS | [nestjs](nestjs/README.md) | pino |
| Spring Boot | [spring-boot](spring-boot/README.md) | Logback |

The synthetic Bank Verification Number is the existing corpus value. It is
always paired with its field label so the authored rule's keyword window is
satisfied. Both logs and the `/customer/bvn` response field must contain
`[REDACTED:pii.bvn]`, with no matched digits. The controller reports the consent
answer independently. Redaction still runs when consent is denied. The fixed
provider grants only the synthetic subject and purpose used in the tests;
unknown subjects are denied. It is an example, not a consent store or an
authentication system. The consent helper applies only to processing whose
lawful basis is consent.

## Signed installation

Samples require explicitly supplied signed media and a public-only key
catalogue. No production signing key, implicit development trust, licence
enforcement or runtime CLI invocation is included. To try the repository's
labelled development media, run this from the repository root:

```powershell
node samples/prepare-test-media.mjs
```

This copies the committed signed vectors to ignored `artifacts/sample-media/`
and produces a public-only catalogue. It neither signs a pack nor copies seed
material. Production installations supply their own signed release media and
independently trusted public catalogue.

## Build and test

From the repository root, with .NET 10, Node 26 and Gradle 8.14.3 available:

```powershell
dotnet test tests/Middleware.Tests --filter FullyQualifiedName~SampleAcceptanceTests
dotnet test tests/Cli.Tests --filter FullyQualifiedName~SampleScanTests
npm ci --prefix middleware/node --ignore-scripts
npm run build --prefix middleware/node
npm ci --prefix samples --ignore-scripts
npm test --prefix samples
node samples/prepare-test-media.mjs
gradle -p middleware/jvm :sample-spring-boot:verifySample
```

The Spring build uses Java 21; its sample targets Java 17 and the existing
adapter matrix runs it on Java 17, 21 and 25. The Node conformance matrix also
runs both applications on Node 22, 24 and 26. The .NET build-and-test matrix runs
the ASP.NET Core request checks and all four CLI checks. Existing timeouts and
thresholds are unchanged.

## CLI in continuous integration

`SampleScanTests` copies each sample's actual source into an isolated scan
repository and runs the real CLI entry point with a fixed evaluation date.
It uses the authored BVN rule, with its unchanged matcher and approved citation,
in an explicitly unsigned local test pack. Local identifiers and integer
versions follow the local-pack schema. Each scan must return exit code 1 and
report the intentional synthetic value's source location without printing its
contents. This is an expected finding, not a tool error or a blanket exclusion.
Runtime redaction uses verified signed packs and never consumes scanner settings.

## Sample-only dependencies

The private Node sample manifest pins Express 5.3.0, NestJS 12.5.0 and pino
10.4.0 (MIT), and reflect-metadata 0.2.2 and RxJS 7.8.2 (Apache-2.0).
These are the established upstream framework and logging packages required by
the sample integrations named in #44. Express supplies the HTTP framework;
NestJS supplies its controller and interceptor pipeline; pino exercises the
real logging hook; reflect-metadata and RxJS supply NestJS's required host
runtime. They are application dependencies only, add no middleware dependencies
and have no .NET ahead-of-time compilation cost. The npm-generated lockfile
pins their transitive dependencies. The Spring sample reuses the adapter's
existing framework versions and keeps its own Gradle-generated dependency lock.
Nothing in this card publishes packages or changes release permissions.
