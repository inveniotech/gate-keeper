# Architecture Decision Records

This folder holds the engineering Architecture Decision Records for the open formats. Each record follows one fixed template: Context, Decision, Status, Consequences, Alternatives considered, Traces.

**Status: nine accepted records; two partially accepted records.** P0-02 is tracked
by [issue #1](https://github.com/inveniotech/gate-keeper/issues/1).
On 13 November 2026 the owner confirmed that decisions already implemented in
code, or documented as accepted elsewhere, are approved. Each record below links
the implementation or acceptance evidence. Pending status applies only to details
with neither basis; incomplete implementation alone does not undo a recorded decision.

The audit compared this pull request with master at `3adb9d6` and the existing
owner decision tables. Acceptance of an implemented decision does not claim that
every future capability or every detail of its referenced specification is accepted.

Use [TEMPLATE.md](TEMPLATE.md) for new records.

| Record | Subject | Record status |
|---|---|---|
| [E-001](e-001-public-licence.md) | Apache-2.0 licence | Accepted |
| [E-002](e-002-rule-pack-format.md) | Rule-pack format | Accepted |
| [E-003](e-003-engine-scan-depth.md) | Engine scan depth and upgrade path | Accepted |
| [E-004](e-004-results-format.md) | SARIF and evidence JSON | Accepted |
| [E-005](e-005-exceptions-lifecycle.md) | Exceptions format and lifecycle | Accepted |
| [E-007](e-007-evidence-transport.md) | Evidence transport and completion | Partially accepted; additional unimplemented details await review |
| [E-008](e-008-licence-token.md) | Offline licence tokens and key rotation | Accepted; CAC registration number use awaits legal confirmation |
| [E-010](e-010-configuration-layout.md) | CLI binary and configuration naming | Accepted |
| [E-011](e-011-stack-sequencing.md) | Middleware stacks and roadmap | Accepted |
| [E-015](e-015-nativeaot-cli.md) | C# and .NET NativeAOT CLI | Accepted |
| [E-016](e-016-agent-rules.md) | Rules for implementing agents | Accepted |

Specification-backed records link to their contracts rather than duplicating
them. Engineering Plan Draft 6, section 7, supplies the identifier mapping.
Source decisions with R-numbers cite them. E-001 and E-015 use the source's
actual D-006 and dated-owner-decision references, with source-trace questions retained
for issue #1. Those questions do not reopen the accepted decisions. Later specifications and recorded naming and scope
amendments take precedence over older plan examples. Issue #1 stays open until
its full register and traceability acceptance criteria are met. E-007 identifies
the remaining details awaiting owner acceptance. E-008 is accepted; production
use of the CAC registration number still awaits legal confirmation.
