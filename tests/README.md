# Tests

Every test project uses xUnit for discovery and execution, and AwesomeAssertions for assertions. Use `.Should()` assertions instead of xUnit assertion calls; preserve sequence order with `.Equal()` and exact exception types with `.ThrowExactly()` when those are part of the expected behaviour. Do not add another test or assertion framework.

The test projects, one folder under `tests/` each:

| Project | What it covers |
|---|---|
| The specification project | The conformance vectors and the schema checks for the open formats (P0-03, P0-04, P0-06, P0-07). |
| The engine project, `tests/Engine.Tests` | The engine's domain model and its System.Text.Json source-generated serialisation (P1-01). |
| The command-line project, `tests/Cli.Tests` | The argument surface and the version output (P1-01). |
| The corpus project, `tests/Corpus.Tests` | The golden fixture corpus and its harness: every case's repository, expected findings, and the per-rule precision and recall report (P1-02). |

## How to test

The corpus runner explicitly disables the regular-expression deadline for
semantic acceptance, matching the existing engine conformance tests. Hosted
runner scheduling therefore cannot change a corpus result. Production keeps
its 100 ms default, verified separately by `ShapeCompilerTests`; no production
timeout, quality threshold or performance budget changes.

From the repository root:

```powershell
dotnet test
```


## Measured corpus runs

`DetectionPackTests` loads each authored detection source through `PackLoader` with a signature created only in memory from the existing labelled development seed. Every synthetic repository is scanned with the real matcher. Expectations are filtered to that pack's identifiers while every unexpected finding remains a failure. Exact locations, complete synthetic recall and the error-level false-positive gate are asserted; the test output prints the measured report. This does not replace owner triage on the pinned real-repository corpus. Run `dotnet test tests/Corpus.Tests -c Release --filter FullyQualifiedName~DetectionPackTests`.

P2-07-01 (#98) lets `CorpusScanRunner` accept already loaded rules and execute the existing matcher and scan orchestrator over the copied case. That run returns sorted, content-free findings and sets `MatcherStagePresent` to true, including a measured clean result. Omitting rules performs enumeration only and keeps that flag false; an explicitly empty rule list is rejected. The harness applies no baseline, exceptions or severity overrides. Temporary copies are removed after failures; successful runs are owned by `CorpusScanScavenger`. Links remain links when cases are copied.

Run `dotnet test tests/Corpus.Tests -c Release --filter FullyQualifiedName~CorpusMatcherTests` for live positive/near-miss, repeatability and failure-cleanup checks. These tests use a synthetic list rule with no legal claim. Production detection acceptance still requires the actual approved pack, but this harness capability has no dependency on that pack being authored.
## Severity and triage scoring

P2-07-02 (#99) extends the existing `CorpusMeasurements` scorer. Only `error` findings feed the five per cent gate; `warning` and `note` counts are reported separately. Matching an expected location counts as a true positive only when its triage is absent or `true-positive`. Rejected, unresolved and unmatched findings count as false positives at error severity. Recall uses positive synthetic expectations, excluding rejected/unresolved triage rows. Duplicate expected or finding keys fail instead of inflating counts.

The gate uses `20 × false positives <= true positives + false positives`, per rule and overall, with integer arithmetic. Reports keep deterministic rule/location ordering and display the gate as unmeasured when matching did not run. Run `dotnet test tests/Corpus.Tests -c Release --filter FullyQualifiedName~CorpusGateTests` for exact-boundary, severity, triage and duplicate-key checks. This scorer is test infrastructure with no dependency on production rule wording or owner triage being available; fabricated safe findings exercise the complete arithmetic now.

`CorpusTriage` defines the JSON triage labels once for structural validation, scoring and test inputs. Report markers and formatting use named constants in `CorpusMeasurements`, preserving the existing report text and policy values.

P2-04 extends `DetectionPackTests` with exact severity and inventory-kind checks for all five identifier families in every location, supported path aliases and call spellings, and BVN/NIN field handling without literal values. The complete corpus scan covers the location near-misses as well as existing golden cases. Base and location rules intentionally produce separate observations; measurements count findings rather than unique identifiers. See [location acceptance](../rule-packs/LOCATION-ACCEPTANCE.md).

## Weekly false-positive report

P2-07-04 adds the dated weekly report the false-positive policy commits under `docs/reports/` (the quality-budgets specification, section 5.5). `CorpusWeeklyReportWriter` renders the report from one shared measurement: `DetectionPackTests.MeasureEverySyntheticCase` produces the per-rule table through `CorpusMeasurements.WriteMarkdownTable`, so the weekly report and the committed synthetic report cannot disagree about a count. The report records the inputs section 5.5 fixes — harness revision, product version, rule-pack versions and digests, validation pins when `tests/corpus/validation/repos.json` exists — and names the policy sections its numbers follow. It never sees a matched value, so it cannot name one (rule 10). When the gate fails, the report names the top three rules by false positives, sorted by count descending and then rule identifier ordinal, and the measurement run fails.

The writer rejects a malformed evaluation date or measurement revision with an actionable message, so no wall-clock date or unverified revision enters a report (rule 22). Run `dotnet test tests/Corpus.Tests -c Release --filter FullyQualifiedName~CorpusWeeklyReportTests` for the writer's behaviour tests, and `pwsh ./ci/rule-packs/Measure-Detection.ps1 -EvaluationDate <yyyy-MM-dd> -MeasurementRevision <40-hexadecimal commit>` to produce a report through the real harness. The scheduled measurement job runs that command weekly and uploads the dated report as an artefact; the job never pushes commits.
