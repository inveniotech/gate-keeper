# Regenerates or verifies synthetic measurements through the real loader, matcher and scorer.
[CmdletBinding()]
param([switch] $WriteReport)

$ErrorActionPreference = 'Stop'
$previous = $env:GATEKEEPER_WRITE_SYNTHETIC_REPORT
try {
    $env:GATEKEEPER_WRITE_SYNTHETIC_REPORT = if ($WriteReport) { '1' } else { '0' }
    $project = Join-Path $PSScriptRoot '../../tests/Corpus.Tests'
    dotnet test $project -c Release --filter 'FullyQualifiedName~DetectionReport_Should_MatchTheCommittedReport' --logger 'console;verbosity=detailed'
    $result = $LASTEXITCODE
} finally {
    $env:GATEKEEPER_WRITE_SYNTHETIC_REPORT = $previous
}
exit $result
