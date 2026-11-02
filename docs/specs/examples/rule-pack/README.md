# Rule-pack examples

Four content documents for the rule-pack v1 format, validated against `schemas/rulepack.schema.json` by the tests in `tests/InvenioTech.GateKeeper.Tests`. Identifiers and sample values are synthetic (rule 10 of `AGENTS.md`); the citations are obvious placeholders because approved wording arrives in P2-02 (rule 15).

| File | What it demonstrates |
|---|---|
| `minimal.json` | One rule with the required fields only. |
| `full.json` | Every field: a bounded `shape`, a list matcher, a validator, boundary, keywords, window, location, exclusions, `redaction.optional`, `processor`, `baseVersion`, and both civil dates. |
| `invalid.json` | One deliberate schema failure: a shape part whose `max` is 65, above the bound of 64. |
| `invalid-correction.json` | Schema-valid but rejected by the civil-date consistency check: a correction whose `coverageDate` is after its `contentIssuedAt`. |

These files live under `docs/`, not `schemas/`, so they are not published in the `gate-keeper-schemas` artefact (every `*.json` under `schemas/` is).
