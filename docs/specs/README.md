# Specifications

This folder holds the specifications for the open formats. Each is a written contract with a version, and each format change updates its specification, its JSON Schema and its version field in the same pull request (rule 24 of `AGENTS.md`).

| Format | Specification | JSON Schema | Status |
|---|---|---|---|
| Rule pack | [rule-pack-v1.md](rule-pack-v1.md) | [../../schemas/rulepack.schema.json](../../schemas/rulepack.schema.json) | Draft — not accepted (P0-03) |
| Results (evidence JSON and SARIF profile) | [results-format-v1.md](results-format-v1.md) | [../../schemas/gk-results.schema.json](../../schemas/gk-results.schema.json) and [../../schemas/sarif-profile.schema.json](../../schemas/sarif-profile.schema.json) | Draft — not accepted (P0-04) |

Still to arrive in their own cards: configuration, exceptions and baseline files (P0-05), the licence token (P0-06), the evidence-sink transport (P0-07) and the middleware API contract (P3-01).
