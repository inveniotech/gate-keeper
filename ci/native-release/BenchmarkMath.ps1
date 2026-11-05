# SPDX-License-Identifier: Apache-2.0
function Get-Median([double[]] $Values) {
  if ($Values.Count -ne 3) { throw 'A benchmark median requires exactly three measured values.' }
  return @($Values | Sort-Object)[1]
}
function Test-Regression([double] $Baseline, [double] $Candidate) {
  if ($Baseline -lt 0 -or $Candidate -lt 0) { throw 'Benchmark measurements must be non-negative.' }
  return $Candidate -gt ($Baseline * 1.2)
}
