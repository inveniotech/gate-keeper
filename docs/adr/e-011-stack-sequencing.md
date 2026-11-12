# E-011: Middleware stacks and roadmap

## Context

Thin adapters need consistent rule behaviour without committing the first
release to every language or framework.

## Decision

Use the [accepted middleware contract](../specs/middleware-api-v1.md):
.NET, Node/TypeScript and Kotlin/JVM ship first; Python follows that release.
Reserved PHP, Go and Rust directories do not promise implementation.

Use that contract and [AGENTS.md](../../AGENTS.md) for supported runtime lines,
framework surfaces and tests. CLI scan coverage is separate from runtime
adapter availability.

## Status

**Draft — awaiting owner review and approval.** R-2 is the original source.
IQ-79 and IQ-89 amend its runtime and launch sequencing. The accepted middleware
contract records the current scope; the old four-stack launch list is not restored.

## Consequences

Adapters share signed rules and conformance cases. New stack commitments need
their own scope and demand validation rather than following from reserved paths.

## Alternatives considered

Shipping every reserved stack at once expands support before demand is known.
Independent rules per stack risk different findings for the same data.

## Traces

- P0-02, [issue #1](https://github.com/inveniotech/gate-keeper/issues/1).
- Engineering Plan Draft 6, sections 4.12 and 7, E-011; R-2.
- IQ-31/34/78/79/89; rules 9, 30 and 32 of [AGENTS.md](../../AGENTS.md).
