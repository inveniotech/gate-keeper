# Rule engine

The rule engine library shared by the command-line interface and the .NET middleware. Work package P1-01 (issue #11) added the immutable domain types the later Phase 1 work builds on: the pack loader (P1-03) fills rules with matchers, the matcher engine (P1-06) produces findings, and the scan orchestrator (P1-07) assembles scan results.

Work package P1-08 (issue #18) added the configuration loader: it locates and reads the settings file `.ndpa-check/ndpa-check.toml` (IQ-02), parses it through Tomlyn under NativeAOT, validates every key the configuration specification lists, and resolves the effective `ScanSettings` with the profile defaults of `default` and `deep` (IQ-23).

## Public contract

- The immutable domain types in this folder's `.cs` files: `RulePack`, `Rule`, `Citation`, `RedactionOptions`, `ProcessorInfo`, `Finding`, `Location`, `Fingerprint`, `ScanResult`, plus the closed enums `Severity`, `BaselineState`, `LocationKind`, `PackProvenance` and `ExitOutcome`. Every type is a `sealed` record, its lists are read-only, and its constructor validations match the results format and the rule-pack format (1-based lines and columns, 64-character lowercase hexadecimal fingerprints).
- `ConfigurationLoader.Load(rootPath)` reads `.ndpa-check/ndpa-check.toml` and returns `ConfigurationLoadResult`: the effective `ScanSettings` or null, with warnings (such as an unknown key, IQ-17) and tool-error sentences in en-GB. When the file is absent every section 4 default applies. An unknown key warns and never fails; a known key with the wrong value, a wrong `schemaVersion`, a byte-order mark, an oversized file or invalid TOML is a tool error. The loader never walks parent directories and never writes the file back.
- `ScanSettings` carries every key of the settings file, resolved: `Profile`, `ScanHistory`, `MaxFileBytes`, `Exclude`, `IncludeSubmodules`, `RepositoryId`, `BaselineMaxAgeDays`, `FailOn`, `SeverityOverrides`, `DisabledRules` and `LocalPacks`. `ScanProfile` is the closed set `default` and `deep` (IQ-23); `DisabledRule` carries the written reason IQ-24 requires.
- `ScanSettingsToml` is the typed model of the settings file and the Tomlyn source-generated `TomlSerializerContext` for it (IQ-17): reflection stays disabled under AOT (rule 7).
- `EngineProduct` stamps the facts every artefact carries (NFR-031): the product name `gatekeeper`, the engine version, and the bundled rule pack version, which is `null` until the pack build step bundles one, so the version output states the absence rather than inventing a number.
- `EngineJsonSerializerContext` is the System.Text.Json source-generation context for every serialised type. There is no reflection-based serialisation anywhere in the engine or the interface (rule 7).
- A finding never carries the matched value as data. Outputs name the rule and the location (rule 10).

## Dependencies

`Tomlyn` (BSD-2-Clause, approved under rule 8 of `AGENTS.md`; pinned in `Directory.Packages.props`): the settings file is TOML, and Tomlyn parses it through a source-generated Serialiser context, so the CLI stays NativeAOT-clean. Its system-text-json-style attribute set also maps `fail-on`, the one settings key the camelCase naming policy cannot spell. `System.Text.Json` ships in the .NET shared framework, so no package is added for it.

## How to test

From the repository root:

```powershell
dotnet test --filter FullyQualifiedName~Engine.Tests
```
