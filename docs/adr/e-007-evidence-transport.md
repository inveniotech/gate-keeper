# E-007: Evidence transport and completion rules

## Context

A scan must run offline. Customers can separately copy evidence to storage they
control, where consumers must distinguish complete files from partial copies.

## Decision

Use the [evidence-sink contract](../specs/evidence-sink-v1.md) for local output,
pipeline copies, destinations, paths, integrity sidecars and copy failure
handling. Use the [results format](../specs/results-format-v1.md) for file content.

R-5 identifies destination families. IQ-27 moves uploading into a pipeline
step: the CLI contains no network code. Consumers require the matching SHA-256
sidecar before treating an object as complete. This defines no separate service
or credential protocol.

## Status

**Accepted in part — recorded on 13 November 2026.** The owner confirmed that
implemented decisions and decisions accepted elsewhere are approved. IQ-27/28/29/46/85
are recorded owner decisions in the [evidence-sink decision table](../specs/evidence-sink-v1.md#01-decision-status):
local offline output, pipeline copies, destination paths, digest logging and a matching
sidecar as the completion witness. [ScanCli](../../cli/ScanCli.cs) writes local results;
the [zero-egress harness](../../ci/zero-egress/README.md) checks the offline boundary.
These decisions no longer await acceptance even though the upload step is later work.

**Awaiting owner review and acceptance:** section 0.4 of the evidence-sink
specification adds details beyond those recorded decisions, including exact witness
bytes, `norev` naming, path validation and the three-attempt failure procedure.
The P4-04 pipeline implementation is still pending; examples alone are not an
implementation. This record does not accept those additional unimplemented details.

## Consequences

Scan exit codes describe scanning, not a later upload. Customers choose whether
to copy evidence and use existing rights. Object presence alone is insufficient
proof of completion.

## Alternatives considered

Uploading in the CLI adds network and credential handling to an offline binary.
Admitting files without a completion witness would accept partial copies.

## Traces

- P0-02, [issue #1](https://github.com/inveniotech/gate-keeper/issues/1).
- P0-07, [issue #6](https://github.com/inveniotech/gate-keeper/issues/6).
- Engineering Plan Draft 6, sections 4.7 and 7, E-007; R-5.
- IQ-27/28/29/46/85; NFR-001.
