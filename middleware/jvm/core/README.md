# Core artefact

`gate-keeper-core` packages the dependency-free Java sources in `../src` at
Java 15 bytecode. `Installation.load` verifies signed packs before constructing
a matcher. Public constructors over rule models remain available for low-level
conformance tests and do not mark a matcher as a verified installation.

Run the `CheckRuntime` and `CheckPackLoad` commands in the parent README.
`gradle -p middleware/jvm :core:build` builds the core jar without Kotlin.
