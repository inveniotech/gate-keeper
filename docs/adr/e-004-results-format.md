# E-004: SARIF and evidence JSON results

## Context

Code review needs findings, while automated consumers need a stable evidence
document. Both outputs must agree on the scan facts.

## Decision

Use the [results specification](../specs/results-format-v1.md) for the Static
Analysis Results Interchange Format (SARIF) 2.1.0 profile and evidence JSON.
Keep the [SARIF schema](../../schemas/sarif-profile.schema.json) and
[evidence schema](../../schemas/gk-results.schema.json) aligned with it.

The current profile targets GitHub code scanning; Draft 6's broader platform
rendering claim is not carried forward. Ordering, input dates and version
stamps follow the current specification.

## Status

**Accepted — recorded on 13 November 2026.** The owner's confirmation covers
the implemented results decision. [ResultsWriter](../../src/Engine/ResultsWriter.cs)
produces both outputs from one completed scan, with deterministic ordering,
version stamps and no matched values. The [writer and consistency test suites](../../tests/)
cover the contract. This acceptance applies to the current implemented profile.

## Consequences

Consumers validate published formats rather than parsing console text.
Evidence contains metadata and locations without matched personal-data content.
Format changes update the specification, schema and version together.

## Alternatives considered

Console text is not a stable machine contract. Producing separate scan facts
for each output could create contradictory records.

## Traces

- P0-02, [issue #1](https://github.com/inveniotech/gate-keeper/issues/1).
- P0-04, [issue #3](https://github.com/inveniotech/gate-keeper/issues/3).
- Engineering Plan Draft 6, sections 4.5 and 7, E-004.
- IQ-01/20/61/80/83/92; NFR-004/018/020/031.
