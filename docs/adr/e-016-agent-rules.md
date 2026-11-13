# E-016: Rules for implementing agents

## Context

Agent-produced changes need written limits, traceability and human review.
Architecture, legal wording, production keys and releases need explicit controls.

## Decision

Keep [AGENTS.md](../../AGENTS.md) as the authoritative repository rules. Apply
its requirements for written specifications, tests, privacy, determinism,
dependencies, public contracts and handoff.

Do not reproduce the policy here. Changes remain owner-controlled, and more
specific owner instructions apply within their authorised scope.

## Status

**Accepted — recorded on 13 November 2026.** [AGENTS.md](../../AGENTS.md)
explicitly records owner approval on 27 October 2026 and later owner amendments.
The owner's confirmation recognises this existing acceptance. R-12 supplies the
engineering-plan trace. This record documents the governing policy without
amending or waiving it.

## Consequences

Agents can identify the governing policy before changing code. Reviewers can
trace changes to requirements and evidence. This record does not delegate
human-only decisions to agents.

## Alternatives considered

Unrecorded instructions can differ between sessions. Duplicating the complete
policy here would allow the copies to drift.

## Traces

- P0-02, [issue #1](https://github.com/inveniotech/gate-keeper/issues/1).
- Engineering Plan Draft 6, sections 6 and 7, E-016; R-12.
- Rules 1, 3, 15, 16 and 36 of [AGENTS.md](../../AGENTS.md).
