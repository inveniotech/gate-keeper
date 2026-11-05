# SPDX-License-Identifier: Apache-2.0
[CmdletBinding()]
param([Parameter(Mandatory)][string] $BaselineRoot)
$ErrorActionPreference = 'Stop'
$root = (Resolve-Path -LiteralPath $BaselineRoot).Path
if (Test-Path -LiteralPath (Join-Path $root 'src/Engine/BenchmarkMetrics.cs')) { return }
# The overlay adds counters only; exact blob checks prevent a different baseline from being replaced.
$expected = @(Get-Content -LiteralPath "$PSScriptRoot/baseline-instrumentation-base.txt")
if ($expected.Count -ne 4) { throw 'The measurement overlay has an incomplete baseline manifest.' }
foreach ($line in $expected) {
  $parts = $line.Split(' ', 2)
  $actual = & git -C $root hash-object -- $parts[1]
  if ($LASTEXITCODE -ne 0 -or $actual -cne $parts[0]) { throw 'The baseline source does not match the measurement-only overlay. Review and regenerate the overlay for this baseline.' }
}
& git -C $root apply --check --unidiff-zero "$PSScriptRoot/baseline-instrumentation.patch"
if ($LASTEXITCODE -ne 0) { throw 'The measurement-only overlay does not match the baseline. Adapt and review the overlay before comparing this baseline.' }
& git -C $root apply --unidiff-zero "$PSScriptRoot/baseline-instrumentation.patch"
if ($LASTEXITCODE -ne 0) { throw 'The baseline measurement overlay could not be applied.' }
Copy-Item -LiteralPath "$PSScriptRoot/../../src/Engine/BenchmarkMetrics.cs" -Destination (Join-Path $root 'src/Engine/BenchmarkMetrics.cs')
