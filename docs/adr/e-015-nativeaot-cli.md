# E-015: C# and .NET NativeAOT for the command-line interface

## Context

The command-line interface (CLI) needs an offline, reproducible distribution.
The rule engine must also be reusable by .NET middleware. The
[feasibility spike](../../spikes/aot/report.md) measured the intended libraries,
signature verification and matching under native ahead-of-time compilation.

## Decision

Use the CLI and engine baseline as C# on .NET 10, with the CLI published as a
trimmed NativeAOT executable. Reuse the engine as a library rather than invoking
the CLI from middleware. Keep the signature implementation behind its existing
internal interface and use source-generated serialisation.

The pinned [SDK](../../global.json), [package versions](../../Directory.Packages.props)
and [build properties](../../Directory.Build.props) define the build inputs.
The spike is feasibility evidence, not a new permanent binary-size budget.

## Status

**Accepted — recorded on 13 November 2026.** The owner's confirmation covers
the implemented C#/.NET 10 and NativeAOT decision. The
[CLI project](../../cli/) references the shared engine;
the [native release workflow](../../.github/workflows/native-release.yml) publishes
trimmed NativeAOT binaries and treats trim and AOT warnings as errors.
Engineering Plan Draft 6 records the owner decision of 24 October 2026,
superseding proposed D-017. Its lack of a dedicated R-number remains a source-trace
question for issue #1, not a reason to leave this implemented decision unapproved.

## Consequences

The CLI does not require an installed managed runtime. Engine code remains shared
with .NET middleware. Reflection-based serialisation and dependencies that produce
trim or NativeAOT warnings cannot enter the shipping build. Platform publication
must be verified on supported operating systems and architectures.

## Alternatives considered

A managed-only CLI would depend on a runtime installation or a larger bundled
runtime. A separate engine implementation would duplicate matching behaviour.
The spike records dependency-size trade-offs without deciding a size budget.
These alternatives explain the implemented architecture decision.

## Traces

- P0-02, [issue #1](https://github.com/inveniotech/gate-keeper/issues/1).
- P0-16, [NativeAOT report](../../spikes/aot/report.md).
- [Engine module](../../src/Engine/README.md).
- Rules 7 and 30 of [AGENTS.md](../../AGENTS.md).
- Engineering Plan Draft 6, section 7: owner decision of 24 October 2026,
  superseding proposed D-017. No dedicated R-number is assigned in that table.
