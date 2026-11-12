# Architecture Decision Records

This folder holds the engineering Architecture Decision Records for the open formats. Each record follows one fixed template: Context, Decision, Status, Consequences, Alternatives considered, Traces.

**Status: drafting; owner approval pending.** P0-02 is tracked by
[issue #1](https://github.com/inveniotech/gate-keeper/issues/1). Every record here
is a draft. An existing source decision does not approve a new ADR, and an ADR
does not change the approval status of the specification it references.

Use [TEMPLATE.md](TEMPLATE.md) for new records.

| Record | Subject | Record status |
|---|---|---|
| [E-001](e-001-public-licence.md) | Apache-2.0 licence | Draft; source cites D-006 rather than an R-number |
| [E-002](e-002-rule-pack-format.md) | Rule-pack format | Draft; owner approval pending |
| [E-003](e-003-engine-scan-depth.md) | Engine scan depth and upgrade path | Draft; owner approval pending |
| [E-004](e-004-results-format.md) | SARIF and evidence JSON | Draft; owner approval pending |
| [E-005](e-005-exceptions-lifecycle.md) | Exceptions format and lifecycle | Draft; owner approval pending |
| [E-007](e-007-evidence-transport.md) | Evidence transport and completion | Draft; owner approval pending |
| [E-008](e-008-licence-token.md) | Offline licence tokens and key rotation | Draft; owner approval pending |
| [E-010](e-010-configuration-layout.md) | CLI binary and configuration naming | Draft; owner approval pending |
| [E-011](e-011-stack-sequencing.md) | Middleware stacks and roadmap | Draft; owner approval pending |
| [E-015](e-015-nativeaot-cli.md) | C# and .NET NativeAOT CLI | Draft; source cites the dated owner decision rather than an R-number |
| [E-016](e-016-agent-rules.md) | Rules for implementing agents | Draft; owner approval pending |

Specification-backed records link to their contracts rather than duplicating
them. Engineering Plan Draft 6, section 7, supplies the identifier mapping.
Source decisions with R-numbers cite them. E-001 and E-015 use the source's
actual D-006 and dated-owner-decision references, with trace exceptions left
for owner confirmation. Later specifications and recorded naming and scope
amendments take precedence over older plan examples. Issue #1 stays open until
its register and owner-approval acceptance criteria are met.
