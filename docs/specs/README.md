# Specifications

This folder holds the specifications for the open formats. Each is a written contract with a version, and each format change updates its specification, its JSON Schema and its version field in the same pull request (rule 24 of `AGENTS.md`).

| Format | Specification | JSON Schema | Status |
|---|---|---|---|
| Rule pack | [rule-pack-v1.md](rule-pack-v1.md) | [../../schemas/rulepack.schema.json](../../schemas/rulepack.schema.json) | Draft — not accepted (P0-03) |
| Results (evidence JSON and SARIF profile) | [results-format-v1.md](results-format-v1.md) | [../../schemas/gk-results.schema.json](../../schemas/gk-results.schema.json) and [../../schemas/sarif-profile.schema.json](../../schemas/sarif-profile.schema.json) | Draft — not accepted (P0-04) |
| Configuration, exceptions and baseline | [config-v1.md](config-v1.md) | None in this draft. Settings and exceptions are TOML; the baseline document is specified in the text. | Draft — not accepted (P0-05) |
| Licence token | [licence-token-v1.md](licence-token-v1.md) | None. The token is armoured text. The signed line is JSON, and it is verified before it is parsed. | Accepted by owner (13 November 2026); production CAC claim use awaits legal confirmation (P0-06) |
| Evidence sink | [evidence-sink-v1.md](evidence-sink-v1.md) | None in this draft. The contract is a path, a sidecar and a pipeline step. | Draft — not accepted (P0-07) |
| Quality budgets: false positives and performance | [quality-budgets.md](quality-budgets.md) | None in this draft. The gate and the budgets are measured, not serialised; the arithmetic is fixed in the text. | Draft — not accepted (P0-17) |
| Processor catalogue output | [processor-output-proposal.md](processor-output-proposal.md) | Existing v1 schemas unchanged; implementation follows separately. | Additive v1 policy approved by the owner, 9 November 2026; remaining proposal draft (P2-12) |

| Middleware API | [middleware-api-v1.md](middleware-api-v1.md) | None. The API contract defines native stack types. | Accepted by the owner, 9 November 2026 (P3-01; #123) |
