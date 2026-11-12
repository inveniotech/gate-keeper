# E-005: Exceptions format and lifecycle

## Context

A justified exception must be reviewable and time-bounded. Suppression without
a reason and owner would weaken the resulting evidence.

## Decision

Use the [configuration specification](../specs/config-v1.md) for exception
identifiers, scope, justification, ownership, expiry and matching. Use the
[results contract](../specs/results-format-v1.md) for applied exceptions and hashes.
A baseline is not an exception, and an expired exception no longer suppresses
a finding. This record adds no fields or workflow service.

## Status

**Draft — awaiting owner review and approval.** The source register maps E-005
to this contract. The referenced specifications retain their own approval status.

## Consequences

Exceptions travel with a repository and remain reviewable. Applied decisions
are visible in evidence. Invalid or unjustified entries cannot silently suppress
findings.

## Alternatives considered

Permanent blanket exclusions conceal reasons for accepting risk. Embedding
exception policy separately in each adapter would fragment the contract.

## Traces

- P0-02, [issue #1](https://github.com/inveniotech/gate-keeper/issues/1).
- P0-05, [issue #4](https://github.com/inveniotech/gate-keeper/issues/4).
- Engineering Plan Draft 6, sections 4.6 and 7, E-005.
- IQ-19/24/80; rules 6 and 24 of [AGENTS.md](../../AGENTS.md).
