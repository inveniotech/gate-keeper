# Rule engine

The rule engine library shared by the command-line interface and the .NET middleware. Work package P1-01 (issue #11) added the immutable domain types the later Phase 1 work builds on: the pack loader (P1-03) fills rules with matchers, the matcher engine (P1-06) produces findings, and the scan orchestrator (P1-07) assembles scan results.

## Public contract

- The immutable domain types in this folder's `.cs` files: `RulePack`, `Rule`, `Citation`, `RedactionOptions`, `ProcessorInfo`, `Finding`, `Location`, `Fingerprint`, `ScanResult`, plus the closed enums `Severity`, `BaselineState`, `LocationKind`, `PackProvenance` and `ExitOutcome`. Every type is a `sealed` record, its lists are read-only, and its constructor validations match the results format and the rule-pack format (1-based lines and columns, 64-character lowercase hexadecimal fingerprints).
- `EngineProduct` stamps the facts every artefact carries (NFR-031): the product name `gatekeeper`, the engine version, and the bundled rule pack version, which is `null` until the pack build step bundles one, so the version output states the absence rather than inventing a number.
- `EngineJsonSerializerContext` is the System.Text.Json source-generation context for every serialised type. There is no reflection-based serialisation anywhere in the engine or the interface (rule 7).
- A finding never carries the matched value as data. Outputs name the rule and the location (rule 10).

## Dependencies

None. `System.Text.Json` ships in the .NET shared framework, so the engine adds no package. The matcher grammar and the pack content document's exact serialisation arrive with P1-03.

## How to test

From the repository root:

```powershell
dotnet test --filter FullyQualifiedName~Engine.Tests
```
