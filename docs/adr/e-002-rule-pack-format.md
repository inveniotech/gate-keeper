# E-002: Rule-pack format

## Context

The command-line interface and middleware need a shared, versioned rule format.
Detection changes must travel as data with citations and corpus cases rather
than being copied into each engine (NFR-027).

## Decision

Use the [rule-pack specification](../specs/rule-pack-v1.md) and its
[JSON Schema](../../schemas/rulepack.schema.json) as the single source of truth.
This record does not define another format or change the specification's status.

Sections 2 and 6 define signed bytes, verification order and producer discipline.
Sections 4, 5, 7 and 8 define rule models, portable structured matching and named
validators. Sections 9 and 10 define version lines, dates and provenance. Format
examples and validation cases remain alongside the specification and schema.

## Status

**Draft — awaiting owner review and approval.** Approval of individual decisions
recorded by the specification is not approval of this record or the whole
specification. No approval is granted by this draft.

## Consequences

All implementations share one interpretation of rule data and verify the exact
signed bytes before using content. Changes to the format update its specification,
schema and version together. Matcher capabilities require cross-stack conformance
cases; a rule author cannot introduce arbitrary executable matching code.

## Alternatives considered

- Separate formats per stack would duplicate contracts and risk different findings
  for the same rule pack.
- Detection rules embedded in engine code would require engine releases for data
  changes and conflict with NFR-027.

These are draft rationale, not a claim that a separate alternatives decision has
already been approved.

## Traces

- P0-02, [issue #1](https://github.com/inveniotech/gate-keeper/issues/1).
- P0-03, [issue #2](https://github.com/inveniotech/gate-keeper/issues/2).
- NFR-027 and rules 9, 24 and 28 of [AGENTS.md](../../AGENTS.md).
- IQ-06/07/14/24/25/35/36/76/80/91, as recorded in the
  [specification's decision table](../specs/rule-pack-v1.md#01-decision-status).
