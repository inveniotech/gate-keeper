# E-003: Engine scan depth and upgrade path

## Context

The first engine needs bounded, precise detection with consistent findings
across stacks. Whole-program analysis introduces a separate cost per language.

## Decision

Use the [rule-pack contract](../specs/rule-pack-v1.md) for structured shapes,
validators, context, location constraints and list matching. Use
[configuration](../specs/config-v1.md) for profiles and history scope, and the
[quality contract](../specs/quality-budgets.md) for measurement.

This record introduces neither abstract syntax tree or data-flow analysis nor
secret-scanning capabilities. Later capabilities need specifications and corpus
cases. Middleware follows its [accepted contract](../specs/middleware-api-v1.md),
without repository scanning or request gating.

## Status

**Draft — awaiting owner review and approval.** Engineering Plan Draft 6,
sections 4.4 and 7, establishes E-003. R-1 constrains middleware. Current
specifications take precedence over older examples in the plan.

## Consequences

Detection stays in rule data. New matcher capabilities require cross-stack
conformance. Results must describe only the scope actually evaluated.

## Alternatives considered

General data-flow analysis needs per-language contracts and tests. Arbitrary
rule expressions would undermine shared safety and performance guarantees.

## Traces

- P0-02, [issue #1](https://github.com/inveniotech/gate-keeper/issues/1).
- Engineering Plan Draft 6, sections 4.4 and 7, E-003; R-1.
- IQ-14/23/64/83; NFR-010/011/012/013/027.
- [Engine contract](../../src/Engine/README.md).
