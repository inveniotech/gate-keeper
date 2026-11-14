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

**Accepted — recorded on 13 November 2026.** The middleware contract was
accepted by the owner on 9 November 2026 in [pull request #123](https://github.com/inveniotech/gate-keeper/pull/123).
It establishes the current first-release stacks and defers Python. Implementations
exist for [.NET](../../middleware/dotnet/README.md),
[Node/TypeScript](../../middleware/node/README.md) and
[Kotlin/JVM](../../middleware/jvm/README.md). The owner's confirmation recognises
both that prior acceptance and the implemented scope. Future stacks remain future
work, not decisions awaiting acceptance of the current sequencing.

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
