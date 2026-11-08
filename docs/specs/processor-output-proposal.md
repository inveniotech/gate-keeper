# Processor catalogue output proposal

**Status: draft — not accepted.** P2-12 (#64), IQ-82; NFR-004, NFR-009,
NFR-020, NFR-027 and NFR-031. This document proposes the remaining user-facing
catalogue integration. It does not alter results-format v1 or its schemas.
Owner acceptance is required before implementing the public contract change
(rules 1, 15 and 24 of [AGENTS.md](../../AGENTS.md)).

## Problem and completed work

The authored catalogue and verified public NIBSS integrations are complete in
#114/#119 and #121/#122. Pack loading retains `processor.dataCategories` and
`processor.usualRegion`, but `Finding` and the results writers do not expose
them. The existing [results format](results-format-v1.md) fixes message text to
rule identifier plus citation title, and both JSON Schemas close unknown fields.
Adding a field to current v1 output would therefore break older validators.

The remaining work is output integration, not a second catalogue. Note severity,
the verbatim citation and the distinction between technical integration evidence
and an organisation's actual processing arrangement must remain intact.

## Proposed version boundary

Introduce evidence schema version **2**, with a new schema identifier ending in
`/gk-results/v2.json`, and a separately named SARIF profile version 2. SARIF's
standard version remains 2.1.0. Preserve the existing v1 files and writers for
consumers pinned to them. Introduce the output change in a product major as
required by rule 24. The owner may instead approve an explicit compatible
extension policy; this proposal does not assume that exception.

The CLI version-selection surface and default version must be accepted with
the version policy before code is written. No new flag is implemented here.

## Proposed data mapping

| Destination | Proposed value | Source and omission rule |
|---|---|---|
| Evidence finding `processor.dataCategories` | Array of category strings | Copy from the matched loaded rule, ordinal sort and remove duplicates |
| Evidence finding `processor.usualRegion` | Region caveat string | Copy verbatim from the matched loaded rule |
| SARIF result `properties.processor` | Same metadata object | Only when that result's rule has a processor block |
| Console detail after a finding | Categories, region caveat and confirmation request | Metadata from the matched rule; never the matched token or source |

The optional object is omitted for ordinary rules, never written as null. Its
keys are `dataCategories`, then `usualRegion`. Metadata must be resolved from
the same effective rule selected by pack loading; the writer must not search
the filesystem or guess from a rule identifier. Historical findings retain
their separate list and receive the same optional metadata in evidence v2.
No additional finding is created and no fingerprint input changes.

The proposal fragment below is illustrative v2 finding metadata, not a complete
evidence document or an assertion that the current v1 schema accepts it:

```json
{
  "processor": {
    "dataCategories": ["payment integration metadata"],
    "usualRegion": "Deployment-dependent; confirm the service region and transfer basis."
  }
}
```

Console wording is proposed legal-adjacent text for owner review:

> Integration reference detected. Confirm the categories of data actually sent,
> the service region and the applicable transfer basis for your deployment.

The output must not claim a transfer occurred, name a customer-specific host,
infer assignment or processing location from a public token, or state that a
transfer is unlawful. Actual package or host source tokens remain omitted from
findings, consistent with the current content-free output contract. Categories
describe the catalogue snapshot, not a verified inventory of the customer's data.

## Acceptance tests for the implementation card

1. A loaded processor rule produces one note with exact metadata in console,
   evidence v2 and SARIF profile v2; an ordinary rule omits the object.
2. The effective loaded rule wins when pack merge precedence selects a duplicate
   identifier. Shuffling pack and category inputs produces identical bytes.
3. Baselines retain metadata on reported findings. Suppressed findings remain
   absent; history stays out of SARIF and does not enter work-tree inventory.
4. Note findings do not fail the default error threshold or enter the error-level
   false-positive gate. Explicit user thresholds retain their existing meaning.
5. V2 documents validate against their schemas. Tests reject unknown metadata
   keys, invalid category types and missing region values. Existing v1 fixtures
   and schemas remain unchanged and pass their original checks.
6. A synthetic marker in matched input never appears in metadata, diagnostics,
   support bundles or documents. No new network code or dependency is added.
7. Generated reference material is regenerated from rule data. A linked website
   guide documents the accepted output version before release (IQ-65).

## Implementation handoff

After owner acceptance, split implementation into a public results-contract and
writer card plus a linked website guide card. Extend the existing writer and
source-generated serialisation; do not add a second result pipeline. Bump the
schema artefact version and add deterministic schema tests in the writer PR.
Reuse the existing processor corpus and combined synthetic report.

Do not close #64 on this proposal: output version selection, legal-adjacent
wording and the implementation remain pending. Production signing, catalogue
publication and actual customer transfer analysis remain outside this card.
