# Specifications

This folder holds the specifications for the open formats. Each is a written contract with a version, and each format change updates its specification, its JSON Schema and its version field in the same pull request (rule 24 of `AGENTS.md`).

| Format | Specification | JSON Schema | Status |
|---|---|---|---|
| Rule pack | [rule-pack-v1.md](rule-pack-v1.md) | [../../schemas/rulepack.schema.json](../../schemas/rulepack.schema.json) | Draft — not accepted (P0-03) |
| Configuration, exceptions and baseline | [config-v1.md](config-v1.md) | None in this draft. Settings and exceptions are TOML; the baseline document is specified in the text. | Draft — not accepted (P0-05) |
| Licence token | [licence-token-v1.md](licence-token-v1.md) | None in this draft. The token is armoured text. The signed line is JSON, and it is verified before it is parsed. | Draft — not accepted (P0-06) |

Still to arrive in their own cards: the results format (P0-04), the evidence-sink transport (P0-07) and the middleware API contract (P3-01).
