# SPDX-License-Identifier: Apache-2.0
$ErrorActionPreference = 'Stop'
. "$PSScriptRoot/BenchmarkMath.ps1"
if ((Get-Median @(9, 1, 4)) -ne 4) { throw 'The paired-run median is incorrect.' }
if (Test-Regression 100 120) { throw 'An exact 20 per cent change must pass.' }
if (-not (Test-Regression 100 120.001)) { throw 'A change above 20 per cent must fail.' }
if (Test-Regression 100 80) { throw 'An improvement must pass.' }
if (-not (Test-Regression 0 1)) { throw 'A positive candidate with a zero baseline must fail.' }
if (-not (Test-MeasurementBudget 'coldStartMilliseconds' 6.3454 7.621)) { throw 'Cold start below 10 ms must pass even above the relative regression band.' }
if (-not (Test-MeasurementBudget 'coldStartMilliseconds' 1 9.999)) { throw 'Cold start just below 10 ms must pass.' }
if (Test-MeasurementBudget 'coldStartMilliseconds' 10 10) { throw 'Cold start exactly at 10 ms must fail.' }
if (Test-MeasurementBudget 'coldStartMilliseconds' 100 10.001) { throw 'Cold start above 10 ms must fail even when faster than the baseline.' }
foreach ($name in @('scan10000Milliseconds', 'ci500Milliseconds', 'fileMedianMilliseconds', 'fileP95Milliseconds', 'allocatedBytesPerFile', 'regexInputBytesPerCall')) {
  if (-not (Test-MeasurementBudget $name 100 120)) { throw "The exact 20 per cent boundary must still pass for $name." }
  if (Test-MeasurementBudget $name 100 120.001) { throw "A regression above 20 per cent must still fail for $name." }
}
foreach ($invalid in @(-1, [double]::NaN, [double]::PositiveInfinity, [double]::NegativeInfinity)) {
  $rejected = $false
  try { Test-MeasurementBudget 'coldStartMilliseconds' 1 $invalid | Out-Null } catch { $rejected = $true }
  if (-not $rejected) { throw 'An invalid cold-start measurement must be rejected.' }
}
$rejected = $false
try { Get-Median @(1, 2) | Out-Null } catch { $rejected = $true }
if (-not $rejected) { throw 'An incomplete measurement set must fail.' }
Write-Output 'Benchmark median and regression boundary tests passed.'
