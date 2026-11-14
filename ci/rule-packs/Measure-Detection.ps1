# Regenerates or verifies synthetic measurements, or writes the dated weekly
# report, through the real loader, matcher and scorer.
[CmdletBinding()]
param(
    [switch] $WriteReport,
    [string] $EvaluationDate = '',
    [string] $MeasurementRevision = '')

$ErrorActionPreference = 'Stop'
if (($EvaluationDate -ne '') -ne ($MeasurementRevision -ne '')) {
    Write-Error 'The weekly report needs its evaluation date and measurement revision together. Pass -EvaluationDate <yyyy-MM-dd> and -MeasurementRevision <40-hexadecimal commit>, or neither.'
    exit 2
}
if ($EvaluationDate -ne '' -and $EvaluationDate -notmatch '^\d{4}-\d{2}-\d{2}$') {
    Write-Error "The evaluation date '$EvaluationDate' is not a UTC date in the yyyy-MM-dd form. Pass the measurement's evaluation date from the run metadata, for example 2026-11-16."
    exit 2
}
if ($MeasurementRevision -ne '' -and $MeasurementRevision -notmatch '^[0-9a-f]{40}$') {
    Write-Error "The measurement revision '$MeasurementRevision' is not 40 lowercase hexadecimal characters. Pass the commit the measurement runs at, for example the output of git rev-parse HEAD."
    exit 2
}

$previousWrite = $env:GATEKEEPER_WRITE_SYNTHETIC_REPORT
$previousDate = $env:GATEKEEPER_WEEKLY_REPORT_DATE
$previousRevision = $env:GATEKEEPER_HARNESS_REVISION
try {
    $env:GATEKEEPER_WRITE_SYNTHETIC_REPORT = if ($WriteReport) { '1' } else { '0' }
    if ($EvaluationDate -ne '') {
        $env:GATEKEEPER_WEEKLY_REPORT_DATE = $EvaluationDate
        $env:GATEKEEPER_HARNESS_REVISION = $MeasurementRevision
    } else {
        Remove-Item env:GATEKEEPER_WEEKLY_REPORT_DATE, env:GATEKEEPER_HARNESS_REVISION -ErrorAction SilentlyContinue
    }
    $filter = 'FullyQualifiedName~DetectionReport_Should_MatchTheCommittedReportAndWriteTheWeeklyReport'
    $project = Join-Path $PSScriptRoot '../../tests/Corpus.Tests'
    dotnet test $project -c Release --filter $filter --logger 'console;verbosity=detailed'
    $result = $LASTEXITCODE
} finally {
    $env:GATEKEEPER_WRITE_SYNTHETIC_REPORT = $previousWrite
    if ($previousDate) { $env:GATEKEEPER_WEEKLY_REPORT_DATE = $previousDate } else { Remove-Item env:GATEKEEPER_WEEKLY_REPORT_DATE -ErrorAction SilentlyContinue }
    if ($previousRevision) { $env:GATEKEEPER_HARNESS_REVISION = $previousRevision } else { Remove-Item env:GATEKEEPER_HARNESS_REVISION -ErrorAction SilentlyContinue }
}
exit $result
