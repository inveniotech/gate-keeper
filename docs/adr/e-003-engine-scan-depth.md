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

**Accepted — recorded on 13 November 2026.** The owner's confirmation covers
the implemented scan-depth decision. [MatcherEngine](../../src/Engine/MatcherEngine.cs)
implements shapes, validators, context, location constraints and list matching;
[ConfigurationLoader](../../src/Engine/ConfigurationLoader.cs) resolves profiles
and history scope. [Matcher tests](../../tests/Engine.Tests/MatcherEngineTests.cs)
and [history tests](../../tests/Engine.Tests/FileEnumeratorHistoryTests.cs) cover
the behaviour. The accepted middleware contract separately excludes repository
scanning and request gating. Future analysis capabilities still need their own decisions.

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
