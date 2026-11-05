# SPDX-License-Identifier: Apache-2.0
$ErrorActionPreference = 'Stop'
. "$PSScriptRoot/BenchmarkMath.ps1"
if ((Get-Median @(9, 1, 4)) -ne 4) { throw 'The paired-run median is incorrect.' }
if (Test-Regression 100 120) { throw 'An exact 20 per cent change must pass.' }
if (-not (Test-Regression 100 120.001)) { throw 'A change above 20 per cent must fail.' }
if (Test-Regression 100 80) { throw 'An improvement must pass.' }
if (-not (Test-Regression 0 1)) { throw 'A positive candidate with a zero baseline must fail.' }
$rejected = $false
try { Get-Median @(1, 2) | Out-Null } catch { $rejected = $true }
if (-not $rejected) { throw 'An incomplete measurement set must fail.' }
Write-Output 'Benchmark median and regression boundary tests passed.'
