# False-positive reports

Dated records of the false-positive measurement, named from the report's UTC evaluation date (`false-positive-<yyyy-mm-dd>.md`, the quality-budgets specification, sections 5.4 and 5.5). A report is a repository artefact, not product output: its evaluation date comes from the continuous-integration run metadata, so the ban on wall-clock times in product outputs is not at stake.

The scheduled measurement job (`.github/workflows/measurement-report.yml`) runs the harness every Monday at 09:00 UTC and on demand, passes the run's UTC date and the checked-out commit to the harness, and uploads the dated report as a workflow artefact and a job summary. The job never pushes commits: the owner commits each week's report here, and the same report is attached to release notes from the first release (P4-01).

Each report records its inputs so a reader can reproduce the numbers — the harness revision, the product version, the rule-pack versions and digests, and the validation-repository pins once the validation corpus merges — and names the sections of `docs/specs/quality-budgets.md` the counts and arithmetic follow. Reports are generated, never hand-edited (rule 25). Regenerate one with:

```powershell
pwsh ./ci/rule-packs/Measure-Detection.ps1 -EvaluationDate <yyyy-MM-dd> -MeasurementRevision <40-hexadecimal commit>
```

Regeneration reproduces a report only at its recorded revision, because a report is a historical record of the measurement that ran that week. The current synthetic picture lives in [docs-data/synthetic-measurements.md](../../docs-data/synthetic-measurements.md), which is verified on every build.
