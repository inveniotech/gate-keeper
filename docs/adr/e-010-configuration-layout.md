# E-010: CLI binary and configuration naming

## Context

A repository needs one discoverable location for scan settings, exceptions and
baseline state. Reviewers must be able to identify configuration changes and
understand which settings produced the evidence.

## Decision

Use `gatekeeper` as the command-line interface (CLI) binary and `.gatekeeper/`
as its configuration directory, with `gatekeeper.toml` inside that directory.
These are the current names in the [CLI contract](../../cli/README.md) and
configuration specification. Historic jurisdiction-specific names are superseded.

Use the [configuration specification](../specs/config-v1.md) as the single
source of truth for discovery, file names, profiles, exceptions and baselines.
Its current jurisdiction-neutral paths take precedence over historic path names.
Use the [results specification](../specs/results-format-v1.md) for the evidence
of applied settings and exceptions. This record does not introduce extra keys.

## Status

**Draft — awaiting owner review and approval.** R-6 is the historical layout
revision; IQ-02 and IQ-92 amend its naming. The specification remains at its own
recorded status. This draft does not accept either specification.

## Consequences

Settings, exceptions and baselines follow one discovery rule. Exclusions and
disabled rules remain auditable, with reasons recorded in evidence. Fingerprint
keys stay out of version control. Public path names remain jurisdiction-neutral.

## Alternatives considered

Scattering exceptions and settings across unrelated locations makes discovery
and review harder. Maintaining both historic and current path conventions would
create competing contracts. These are draft rationale for owner review.

## Traces

- P0-02, [issue #1](https://github.com/inveniotech/gate-keeper/issues/1).
- R-6, amended by IQ-02 and IQ-92.
- Engineering Plan Draft 6, sections 4.13 and 7, E-010.
- P0-05, [issue #4](https://github.com/inveniotech/gate-keeper/issues/4).
- [Configuration decision table](../specs/config-v1.md#01-decision-status).
- Rules 6, 10 and 24 of [AGENTS.md](../../AGENTS.md).
