# E-015: C# and .NET NativeAOT for the command-line interface

## Context

The command-line interface (CLI) needs an offline, reproducible distribution.
The rule engine must also be reusable by .NET middleware. The
[feasibility spike](../../spikes/aot/report.md) measured the intended libraries,
signature verification and matching under native ahead-of-time compilation.

## Decision

Draft the CLI and engine baseline as C# on .NET 10, with the CLI published as a
trimmed NativeAOT executable. Reuse the engine as a library rather than invoking
the CLI from middleware. Keep the signature implementation behind its existing
internal interface and use source-generated serialisation.

The pinned [SDK](../../global.json), [package versions](../../Directory.Packages.props)
and [build properties](../../Directory.Build.props) define the build inputs.
The spike is feasibility evidence, not a new permanent binary-size budget.

## Status

**Draft — awaiting owner review and approval.** The source decision is described
as decided by P0-02, but its original R-number cannot be verified from the
available public sources. That trace must be supplied before this record can
satisfy acceptance. No approval of this record is implied by existing code.

## Consequences

The CLI does not require an installed managed runtime. Engine code remains shared
with .NET middleware. Reflection-based serialisation and dependencies that produce
trim or NativeAOT warnings cannot enter the shipping build. Platform publication
must be verified on supported operating systems and architectures.

## Alternatives considered

A managed-only CLI would depend on a runtime installation or a larger bundled
runtime. A separate engine implementation would duplicate matching behaviour.
The spike records dependency-size trade-offs without deciding a size budget.
These alternatives are draft rationale for owner review.

## Traces

- P0-02, [issue #1](https://github.com/inveniotech/gate-keeper/issues/1).
- P0-16, [NativeAOT report](../../spikes/aot/report.md).
- [Engine module](../../src/Engine/README.md).
- Rules 7 and 30 of [AGENTS.md](../../AGENTS.md).
- Original R-number: pending source verification.
