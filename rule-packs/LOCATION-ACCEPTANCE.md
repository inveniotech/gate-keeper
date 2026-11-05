# Location rule acceptance

P2-04 (issue #31), card 01, adds corpus-first coverage for every identifier in logging, fixtures, configuration, migrations and response models. It also adds BVN/NIN field-handling inputs in .NET, Node.js and JVM code for logging, plain string columns and explicit response serialisation. Those field cases contain no literal identifier; shape matching of digit values cannot satisfy them.

The literal-value cases reuse the documented generator through the P2-03 case builder. The location generator owns its files and marked corpus README section, using the shared synchronisation function rather than a second writer. Proposed field-rule identifiers use `pii.bvn.field.log`, `pii.bvn.field.migration`, `pii.bvn.field.api` and the corresponding NIN identifiers. These remain acceptance candidates until the rule cards confirm them; no published rule contract is changed.

```sh
node tests/corpus/New-LocationCases.mjs --write
node --test ci/rule-packs/identifier-cases.test.mjs ci/rule-packs/location-cases.test.mjs
dotnet test tests/Corpus.Tests -c Release
```

## Severity and implementation limits

IQ-12 already fixes high-confidence labelled BVN/NIN values and unsafe handling of those fields at `error`, and unlabelled eleven-digit values at `note`. Other location-sensitive severities must be recorded alongside the production rules and measured false-positive results before acceptance; this preparation does not invent a final severity mapping. The generic identifier and specialised location rules must not silently inflate findings for the same risk.

Use pack shapes, keywords and path scopes for the heuristics. `location.kind` labels a match; it does not recognise syntax by itself. Path names alone cannot establish that a log call executed, that a response was sent or that storage lacks encryption. In particular, the plain-column cases ask for a safeguard review; they cannot prove whether database or application encryption exists. The citation draft records that limit.

Production rule data remains pending P2-02 human approval and the P2-03 base rules. Before completing the location cards, confirm how specialised rules coexist with base rules: the current matcher only deduplicates identical rule identifiers and spans, so different rule identifiers at the same span both survive. Do not quietly change this engine behaviour or the public schema in a rule-only card. Record the chosen rule composition explicitly, then assert exact findings, severities and inventory kinds through the real matcher. Structural corpus tests alone do not establish detection quality or satisfy this acceptance.
