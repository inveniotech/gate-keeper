# SPDX-License-Identifier: Apache-2.0
[CmdletBinding()]
param(
  [Parameter(Mandatory)][string] $BaselineBinary,
  [Parameter(Mandatory)][string] $CandidateBinary,
  [Parameter(Mandatory)][string] $Destination,
  [string] $ReferencePack
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
. "$PSScriptRoot/BenchmarkMath.ps1"
$baseline = (Resolve-Path -LiteralPath $BaselineBinary).Path
$candidate = (Resolve-Path -LiteralPath $CandidateBinary).Path
New-Item -ItemType Directory -Path $Destination -Force | Out-Null
$reportPath = (Resolve-Path -LiteralPath $Destination).Path
$root = Join-Path ([IO.Path]::GetTempPath()) ('gate-keeper-benchmark-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $root | Out-Null
function Invoke-Measurement([string] $Binary, [string] $Directory, [string[]] $Arguments, [bool] $Scan) {
  $start = [Diagnostics.ProcessStartInfo]::new($Binary)
  $start.WorkingDirectory = $Directory
  $start.UseShellExecute = $false
  $start.RedirectStandardOutput = $true
  $start.RedirectStandardError = $true
  $start.CreateNoWindow = $true
  foreach ($argument in $Arguments) { $start.ArgumentList.Add($argument) }
  $process = [Diagnostics.Process]::new()
  $process.StartInfo = $start
  $watch = [Diagnostics.Stopwatch]::StartNew()
  try {
    if (-not $process.Start()) { throw 'A benchmark process could not start.' }
    $outputTask = $process.StandardOutput.ReadToEndAsync()
    $errorTask = $process.StandardError.ReadToEndAsync()
    if (-not $process.WaitForExit(65000)) { $process.Kill($true); throw 'A benchmark invocation exceeded 65 seconds.' }
    $watch.Stop()
    $output = $outputTask.GetAwaiter().GetResult()
    $null = $errorTask.GetAwaiter().GetResult()
    if ($Scan) {
      if ($process.ExitCode -notin @(0, 1)) { throw 'A benchmark scan failed. Check that the selected build has a bundled pack or supply an approved reference pack.' }
      $json = $output | ConvertFrom-Json
      if ($json.historyScanned -or $json.profile -cne 'default') { throw 'A benchmark invocation used the wrong profile or scanned history.' }
      if (@($json.packs).Count -eq 0) { throw 'A benchmark scan loaded no rule packs.' }
    } elseif ($process.ExitCode -ne 0) { throw 'The benchmark version invocation failed.' }
    $version = if ($Scan) { $json.engine.version } else { [regex]::Match($output, '(?m)^product version: ([^\r\n]+)').Groups[1].Value }
    if (-not $version) { throw 'A measured invocation omitted its product version.' }
    return [pscustomobject]@{ Milliseconds = $watch.Elapsed.TotalMilliseconds; Version = $version; Packs = $(if ($Scan) { @($json.packs) } else { @() }) }
  } finally { $process.Dispose() }
}
try {
  $trees = @{}
  $manifests = @{}
  foreach ($files in @(500, 10000)) {
    $tree = Join-Path $root "tree-$files"
    & node "$PSScriptRoot/../../tests/corpus/New-CorpusValues.mjs" --benchmark $tree --files $files
    if ($LASTEXITCODE -ne 0) { throw 'The deterministic benchmark repository could not be generated.' }
    $folder = Join-Path $tree '.ndpa-check'
    New-Item -ItemType Directory -Path $folder | Out-Null
    $configuration = (@('schemaVersion = 1', 'profile = "default"', '[scan]', 'history = false', '[repository]', 'id = "synthetic/benchmark"', '') -join [char]10)
    if ($ReferencePack) {
      Copy-Item -LiteralPath $ReferencePack -Destination (Join-Path $folder 'reference.json')
      $configuration += (@('[packs]', 'local = [".ndpa-check/reference.json"]', '') -join [char]10)
    }
    [IO.File]::WriteAllText((Join-Path $folder 'ndpa-check.toml'), $configuration)
    $trees[$files] = $tree
    $manifests[$files] = Get-Content -LiteralPath "$tree.manifest.json" -Raw | ConvertFrom-Json
  }
  $measurements = [ordered]@{}
  $metadata = @{}
  foreach ($name in @('coldStartMilliseconds', 'scan10000Milliseconds', 'ci500Milliseconds')) {
    $values = @{ baseline = [Collections.Generic.List[double]]::new(); candidate = [Collections.Generic.List[double]]::new() }
    # One warm-up pair is discarded; three alternating pairs are measured.
    foreach ($pair in 0..3) {
      foreach ($arm in @('baseline', 'candidate')) {
        $binary = if ($arm -eq 'baseline') { $baseline } else { $candidate }
        $isScan = $name -ne 'coldStartMilliseconds'
        $tree = if ($name -eq 'scan10000Milliseconds') { $trees[10000] } else { $trees[500] }
        $arguments = if ($isScan) { @('scan', '--json', '--evaluated-on', '2026-11-01') } else { @('--version') }
        $value = Invoke-Measurement $binary $tree $arguments $isScan
        $metadata[$arm] = $value
        if ($pair -gt 0) { $values[$arm].Add($value.Milliseconds) }
      }
    }
    $baseMedian = Get-Median $values.baseline.ToArray()
    $candidateMedian = Get-Median $values.candidate.ToArray()
    $measurements[$name] = [ordered]@{ baseline = $baseMedian; candidate = $candidateMedian; regression = (Test-Regression $baseMedian $candidateMedian) }
  }
  $record = [ordered]@{
    schemaVersion = 1
    evaluatedOn = '2026-11-01'
    runnerImage = $env:ImageOS
    operatingSystem = [Runtime.InteropServices.RuntimeInformation]::OSDescription
    architecture = [Runtime.InteropServices.RuntimeInformation]::OSArchitecture.ToString()
    logicalProcessors = [Environment]::ProcessorCount
    runnerImageVersion = $env:ImageVersion
    baselineProductVersion = $metadata.baseline.Version
    candidateProductVersion = $metadata.candidate.Version
    loadedPacks = @($metadata.candidate.Packs)
    baselineSha256 = (Get-FileHash -LiteralPath $baseline -Algorithm SHA256).Hash.ToLowerInvariant()
    candidateSha256 = (Get-FileHash -LiteralPath $candidate -Algorithm SHA256).Hash.ToLowerInvariant()
    candidateBytes = (Get-Item -LiteralPath $candidate).Length
    referencePack = $(if ($ReferencePack) { 'provisional synthetic local pack' } else { 'embedded default pack' })
    referencePackSha256 = $(if ($ReferencePack) { (Get-FileHash -LiteralPath $ReferencePack -Algorithm SHA256).Hash.ToLowerInvariant() } else { $null })
    inputs = @($manifests[500], $manifests[10000])
    measurements = $measurements
    perFileInstrumentation = 'Not available; no per-file budget claim is made.'
    machineIndependentCounters = 'Not available; allocation and regular-expression counters require instrumentation.'
  }
  [IO.File]::WriteAllText((Join-Path $reportPath 'benchmark.json'), ($record | ConvertTo-Json -Depth 10) + "`n")
  $lines = @('# Provisional native benchmark report', '', 'One warm-up pair was discarded; values are medians of three fresh-process pairs on the same runner.', '', '| Measurement | Baseline | Candidate |', '|---|---:|---:|')
  foreach ($entry in $measurements.GetEnumerator()) { $lines += "| $($entry.Key) | $($entry.Value.baseline.ToString('F3', [Globalization.CultureInfo]::InvariantCulture)) | $($entry.Value.candidate.ToString('F3', [Globalization.CultureInfo]::InvariantCulture)) |" }
  $lines += @('', "Candidate binary: $($record.candidateBytes) bytes (budget 8,388,608). Whole-scan budget: 60,000 ms; added-CI budget: 30,000 ms.", '', 'Per-file times and machine-independent counters are unavailable and are not claimed by this report.')
  [IO.File]::WriteAllText((Join-Path $reportPath 'benchmark.md'), ($lines -join "`n") + "`n")
  if ($record.candidateBytes -gt 8MB -or $measurements.scan10000Milliseconds.candidate -gt 60000 -or $measurements.ci500Milliseconds.candidate -gt 30000 -or @($measurements.Values | Where-Object regression).Count -gt 0) { throw 'A native size, absolute time or 20 per cent regression budget failed. Inspect benchmark.json.' }
} catch {
  $failure = [ordered]@{ status = 'Blocked (IQ-60) or measurement failure'; message = $_.Exception.Message }
  [IO.File]::WriteAllText((Join-Path $reportPath 'failure.json'), ($failure | ConvertTo-Json) + "`n")
  throw
} finally {
  $resolved = [IO.Path]::GetFullPath($root)
  if (-not $resolved.StartsWith([IO.Path]::GetFullPath([IO.Path]::GetTempPath()), [StringComparison]::OrdinalIgnoreCase)) { throw 'The benchmark cleanup path is outside the temporary directory.' }
  Remove-Item -LiteralPath $resolved -Recurse -Force
}
