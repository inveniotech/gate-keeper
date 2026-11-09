# Processor catalogue output proposal

**Status: version policy approved by the owner on 9 November 2026; remaining
proposal is draft.** P2-12 (#64), IQ-82; NFR-004, NFR-009, NFR-020, NFR-027
and NFR-031. The owner selected the additive amendment path in pull request
#124. This records that decision; it does not claim approval of the remaining
data mapping or legal-adjacent console wording. The current writers and schemas
are unchanged (rules 1, 15 and 24 of [AGENTS.md](../../AGENTS.md)).

## Problem and completed work

The authored catalogue and verified public NIBSS integrations are complete in
#114/#119 and #121/#122. Pack loading retains `processor.dataCategories` and
`processor.usualRegion`, but `Finding` and the results writers do not expose
them. The existing [results format](results-format-v1.md) fixes message text to
rule identifier plus citation title, and both JSON Schemas close unknown fields.
A document that adds a field fails a validator pinned to the schema files that
predate that field.

That constraint has a precedent inside v1. On 5 November 2026 the owner approved
amendment 1 of the results format. Schema artefact 1.2.0 added the optional
SARIF run property `packs`. New producers always write it. Logs written before
1.2.0 stay valid because the field is optional. A validator pinned to artefact
1.1.0 rejects a log that contains it. The evidence schema version stayed 1, the
SARIF standard version stayed 2.1.0, and the change needed no CLI version switch
and no product major. The precedent covered a SARIF field. This change also adds
a field to the evidence file. The effect on a pinned validator is the same for
both files.

`processor` is the same kind of addition: one optional object, omitted when the
matched rule has no processor block, and never written as null. The remaining
work is output integration, not a second catalogue. Note severity, the verbatim
citation and the distinction between technical integration evidence and an
organisation's actual processing arrangement must remain intact.

## Owner-approved version policy

On 9 November 2026 the owner approved the additive amendment of v1. The version
2 alternative is not selected. The compatibility boundary remains explicit:
validators pinned to older closed schema artefacts reject the new field, while
existing documents that omit it remain valid against the amended schemas.

Keep evidence schema version 1 and the SARIF profile identifier ending in
`/sarif-profile/v1.json`. Keep the SARIF standard version at 2.1.0. Publish the
optional `processor` object in schema artefact 1.3.0, the next minor after
1.2.0. New producers write the object whenever the effective rule has a
processor block, and omit it otherwise. Documents that omit it stay valid.
There is no new CLI flag and no product major. Existing v1 fixtures that omit
`processor` keep validating. The writer card updates both schemas, this
proposal's successor text in the results format, and the artefact version in
the same pull request.

## Proposed data mapping

Processor metadata describes the rule, not one occurrence. The two documents
already carry citations in different places, and this proposal follows that
split.

The evidence file has no rule catalogue. Its `rules` object lists severity
overrides and disabled rules only (results format, section 5.2), and each
finding carries its own citation. `processor` therefore goes on the evidence
finding and on the history finding, after `citation`. Repeating it has the same
cost as repeating the citation.

SARIF already has one entry per rule in `tool.driver.rules`, and each result
points at that entry with `ruleIndex`. Citations already live on the rule
entry (section 4.3). `processor` goes there, once. The current rule
`properties` schema allows only `gaid` and `ndpa`. The chosen version policy
adds an optional `processor` object beside those keys. Write `properties` when
the citation has an NDPA or GAID string or the rule has a processor block.
Key order stays alphabetical: `gaid`, `ndpa`, then `processor`. A result keeps
its current keys and gains no `properties` field.

| Destination | Proposed value | Source and omission rule |
|---|---|---|
| Evidence finding `processor.dataCategories` | Array of category strings | Copy from the effective loaded rule, in authored order, dropping a later repeat of an earlier value |
| Evidence finding `processor.usualRegion` | Region caveat string | Copy verbatim from the effective loaded rule |
| Evidence history finding `processor` | The same object | Same rule and omission as a work-tree finding |
| SARIF rule `properties.processor` | The same object | Once, on the `driver.rules` entry for a rule that has a processor block |
| Console detail after a finding | Categories, region caveat and confirmation request | Metadata from the matched rule; the matched token and the source line stay omitted |

The object is omitted for ordinary rules, never written as null. Its keys are
`dataCategories`, then `usualRegion`. Metadata comes from the effective rule
selected by pack loading. The writer must not search the filesystem or guess
from a rule identifier. Historical findings keep their separate list. No
additional finding is created.

Category order is the order on that loaded rule. The writer does not sort the
categories, because sorting would discard the order the catalogue author wrote.
The rule-pack schema does not require unique categories, and this proposal does
not change the loader. A repeated category is dropped by the writer, keeping
the first occurrence. Identical bytes come from writing that loaded rule. A
second run of the same load matches. Reordering the categories on the winning
rule reorders the output.

The additive amendment changes neither fingerprint inputs nor the fingerprint key name. SARIF
`partialFingerprints` keeps its only name, `gatekeeper/v1`. The evidence
`fingerprint` field stays that same value. A schema or profile version in a
file name does not rename the key.

The object below is copied from the authored rule `transfer.processor.aws`. It
shows the metadata object only. It is not a complete evidence document, and the
current schema artefact does not accept it:

```json
{
  "processor": {
    "dataCategories": ["application-defined stored data"],
    "usualRegion": "Deployment-dependent; confirm the contracted processing and storage regions."
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

1. A loaded processor rule produces one note. The evidence finding carries
   `processor` after `citation`, and a history finding for that rule carries the
   same object. SARIF carries the object once, on that rule's `driver.rules`
   entry, and the result has no `properties` field. An ordinary rule omits the
   object in both documents. Console shows the categories, the region caveat
   and the confirmation sentence.
2. When pack merge selects one of two rules that share an identifier, the
   written metadata is the winning rule's, in that rule's category order. A
   second run of the same load produces identical bytes. Reordering categories
   on the winning rule reorders the output, and a repeated category is written
   once. The fingerprint of that finding, and the SARIF name `gatekeeper/v1`,
   match the same span written without processor metadata.
3. Baselines retain metadata on reported findings. Suppressed findings remain
   absent; history stays out of SARIF and does not enter work-tree inventory.
4. The existing threshold tests still pass. These rules are notes, and this
   card leaves the default error threshold and the error-level false-positive
   gate on their current definitions.
5. Documents validate against schema artefact 1.3.0 under the approved additive policy. Tests
   reject an unknown metadata key, a category that is not a string, an empty
   category array and a missing `usualRegion`. Under the approved policy,
   current fixtures that omit `processor` still validate, and the v1 schemas
   gain only the optional object. Evidence schema version remains 1 and the
   SARIF standard version remains 2.1.0. No CLI version flag is introduced.
6. A synthetic marker in matched input never appears in metadata, diagnostics,
   support bundles or documents. No new network code or dependency is added.
7. Generated reference material is regenerated from rule data. A linked website
   guide documents the accepted output before release (IQ-65).

## Implementation handoff

The additive version policy is approved. After the owner accepts or amends the
remaining mapping and console sentence, split implementation into a public results-contract and
writer card plus a linked website guide card. Extend the existing writer and
source-generated serialisation; do not add a second result pipeline. Publish
schema artefact 1.3.0 and add deterministic schema tests in the writer PR. Reuse the
existing processor corpus and combined synthetic report.

Do not close #64 on this proposal. Owner acceptance of the remaining mapping and
console sentence, the writer and schema work, and the linked website guide
remain pending. Production signing, catalogue publication and actual customer
transfer analysis remain outside this card.
