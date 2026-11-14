# Consent scaffold acceptance checks

P3-06 (#43), F-005 and UC-003. These checks exercise the template source against
the existing consent adapters. They use fixed clocks and synthetic subjects.
They verify missing consent, grant, withdrawal, re-grant, separation by subject
and purpose, and complete event metadata. All events intentionally have the
same timestamp, proving that append order resolves ties.

From the repository root:

```powershell
dotnet test tests/Cli.Tests --filter FullyQualifiedName~ConsentScaffoldCliTests
dotnet test tests/Middleware.Tests --filter FullyQualifiedName~ConsentScaffoldTests
node --test tests/scaffold/consent.test.mjs
python tests/scaffold/check_consent.py
```

The .NET test project compiles the actual template as linked source. The Node
test resolves the package import to the local adapter source and runs the
template using Node's built-in TypeScript support. Python loads the actual
template with the local adapter on its import path. No package installation is
required. The generated README files contain the host-framework registration
examples; these checks exercise the provider rather than an HTTP server.

For Java, compile the template with the existing core sources, using Java 17 or
later. The temporary output stays under the ignored `artifacts/` directory:

```powershell
New-Item -ItemType Directory -Force artifacts/consent-scaffold/consent | Out-Null
Copy-Item cli/ConsentTemplates/jvm/ConsentProvider.java artifacts/consent-scaffold/consent/ConsentProvider.java
$coreSources = Get-ChildItem middleware/jvm/src/africa/gatekeeper/*.java | Select-Object -ExpandProperty FullName
javac --release 17 -d artifacts/consent-scaffold/classes $coreSources artifacts/consent-scaffold/consent/ConsentProvider.java tests/scaffold/CheckConsent.java
java -cp artifacts/consent-scaffold/classes CheckConsent
```

CLI tests also verify all four stacks, byte-identical output across application
roots, UTF-8 and LF output, invalid arguments, cancellation and preservation of
existing files and directories. Run `node ci/docs-data/build.mjs --write` after
a Release CLI build to regenerate the command reference.
