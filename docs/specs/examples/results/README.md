# Results examples

Evidence documents and SARIF logs for the results format, version 1. The test project validates the evidence documents against `schemas/gk-results.schema.json` and the SARIF logs against `schemas/sarif-profile.schema.json`. Identifiers and sample values are synthetic (rule 10 of `AGENTS.md`). The citations are obvious placeholders because approved wording arrives in P2-02 (rule 15).

| File | What it demonstrates |
|---|---|
| `minimal.gk.json` | A clean scan: packs loaded, no findings, history not scanned. |
| `full.gk.json` | Every evidence section: two packs, an override, a disabled rule, a new finding, a baselined finding, one history finding, one applied exception, the inventory and a warning. |
| `invalid.gk.json` | A finding with a `match` property. The schema rejects it, because a matched value is not an evidence field. |
| `minimal.sarif.json` | The same clean scan as a SARIF log with no results. |
| `full.sarif.json` | The work-tree findings from `full.gk.json`, with fingerprints and no source snippet. |
| `invalid.sarif.json` | A region `snippet`, which the profile rejects because it would copy a matched value into the log. |

These files live under `docs/`, not `schemas/`, so they are not published in the `gate-keeper-schemas` artefact (every `*.json` under `schemas/` is).

The content digests in the evidence examples are the SHA-256 of the ASCII strings `example-pack-bytes` and `example-local-pack-bytes`. They are not digests of a released pack.
