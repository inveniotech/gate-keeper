# SPDX-License-Identifier: Apache-2.0
[CmdletBinding()]
param(
  [Parameter(Mandatory)][string] $BaselineBinary,
  [Parameter(Mandatory)][string] $CandidateBinary,
  [Parameter(Mandatory)][string] $Destination,
  [string] $ReferencePack,
  [string] $ReleaseBinary
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
. "$PSScriptRoot/BenchmarkMath.ps1"
$baseline = (Resolve-Path -LiteralPath $BaselineBinary).Path
$candidate = (Resolve-Path -LiteralPath $CandidateBinary).Path
$release = if ($ReleaseBinary) { (Resolve-Path -LiteralPath $ReleaseBinary).Path } else { $candidate }
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
  $metricsPath = $Directory + '.metrics.json'
  if (Test-Path -LiteralPath $metricsPath) { [IO.File]::Delete($metricsPath) }
  $start.Environment['GATEKEEPER_BENCHMARK_OUTPUT'] = $metricsPath
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
      if (-not (Test-Path -LiteralPath $metricsPath)) { throw 'The measured binary omitted its matching-stage metrics. Build both arms with BenchmarkInstrumentation=true.' }
      $metrics = Get-Content -LiteralPath $metricsPath -Raw | ConvertFrom-Json
      $expectedCount = if ($Directory.EndsWith('tree-10000', [StringComparison]::Ordinal)) { 10000 } else { 500 }
      if ($metrics.schemaVersion -ne 1 -or $metrics.fileCount -ne $expectedCount -or @($metrics.fileMilliseconds).Count -ne $expectedCount) { throw 'The matching-stage sample did not cover every generated file.' }
    } elseif ($process.ExitCode -ne 0) { throw 'The benchmark version invocation failed.' }
    $version = if ($Scan) { $json.engine.version } else { [regex]::Match($output, '(?m)^product version: ([^\r\n]+)').Groups[1].Value }
    if (-not $version) { throw 'A measured invocation omitted its product version.' }
    return [pscustomobject]@{ Milliseconds = $watch.Elapsed.TotalMilliseconds; Version = $version; Metrics = $(if ($Scan) { $metrics } else { $null }); Packs = $(if ($Scan) { @($json.packs) } else { @() }) }
  } finally { $process.Dispose() }
}
try {
  $trees = @{}
  $manifests = @{}
  foreach ($files in @(500, 10000)) {
    $tree = Join-Path $root "tree-$files"
    & node "$PSScriptRoot/../../tests/corpus/New-CorpusValues.mjs" --benchmark $tree --files $files
    if ($LASTEXITCODE -ne 0) { throw 'The deterministic benchmark repository could not be generated.' }
    $folder = Join-Path $tree '.gatekeeper'
    New-Item -ItemType Directory -Path $folder | Out-Null
    $configuration = (@('schemaVersion = 1', 'profile = "default"', '[scan]', 'history = false', '[repository]', 'id = "synthetic/benchmark"', '') -join [char]10)
    if ($ReferencePack) {
      Copy-Item -LiteralPath $ReferencePack -Destination (Join-Path $folder 'reference.json')
      $configuration += (@('[packs]', 'local = [".gatekeeper/reference.json"]', '') -join [char]10)
    }
    [IO.File]::WriteAllText((Join-Path $folder 'gatekeeper.toml'), $configuration)
    $trees[$files] = $tree
    $manifests[$files] = Get-Content -LiteralPath "$tree.manifest.json" -Raw | ConvertFrom-Json
  }
  $measurements = [ordered]@{}
  $metadata = @{}
  $metricSamples = @{}
  foreach ($metricName in @('fileMedianMilliseconds', 'fileP95Milliseconds', 'allocatedBytesPerFile', 'regexInputBytesPerCall')) {
    $metricSamples[$metricName] = @{ baseline = [Collections.Generic.List[double]]::new(); candidate = [Collections.Generic.List[double]]::new() }
  }
  $regexRuns = @{ baseline = [Collections.Generic.List[object]]::new(); candidate = [Collections.Generic.List[object]]::new() }
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
        if ($pair -gt 0) {
          $values[$arm].Add($value.Milliseconds)
          if ($name -eq 'scan10000Milliseconds') {
            $data = $value.Metrics
            $times = @($data.fileMilliseconds | Sort-Object)
            $metricSamples.fileMedianMilliseconds[$arm].Add($times[4999])
            $metricSamples.fileP95Milliseconds[$arm].Add($times[9499])
            $metricSamples.allocatedBytesPerFile[$arm].Add($data.allocatedBytesPerFile)
            $calls = 0.0
            $bytes = 0.0
            foreach ($counter in @($data.regex)) { $calls += $counter.calls; $bytes += $counter.inputBytes }
            $metricSamples.regexInputBytesPerCall[$arm].Add($(if ($calls -gt 0) { $bytes / $calls } else { 0 }))
            $regexRuns[$arm].Add([pscustomobject]@{ Rules = @($data.regex) })
          }
        }
      }
    }
    $baseMedian = Get-Median $values.baseline.ToArray()
    $candidateMedian = Get-Median $values.candidate.ToArray()
    $measurements[$name] = [ordered]@{ baseline = $baseMedian; candidate = $candidateMedian; regression = (Test-Regression $baseMedian $candidateMedian) }
  }
  foreach ($metricName in @($metricSamples.Keys | Sort-Object)) {
    $baseMedian = Get-Median $metricSamples[$metricName].baseline.ToArray()
    $candidateMedian = Get-Median $metricSamples[$metricName].candidate.ToArray()
    $measurements[$metricName] = [ordered]@{ baseline = $baseMedian; candidate = $candidateMedian; regression = (Test-Regression $baseMedian $candidateMedian) }
  }
  $regexByRule = @()
  $identifiers = @(@($regexRuns.baseline.ToArray()) + @($regexRuns.candidate.ToArray()) | ForEach-Object { $_.Rules } | ForEach-Object { $_.ruleId } | Sort-Object -Unique)
  foreach ($identifier in $identifiers) {
    $row = [ordered]@{ ruleId = $identifier }
    foreach ($arm in @('baseline', 'candidate')) {
      $calls = [Collections.Generic.List[double]]::new()
      $bytesPerCall = [Collections.Generic.List[double]]::new()
      foreach ($run in $regexRuns[$arm]) {
        $rule = @($run.Rules | Where-Object ruleId -CEQ $identifier)
        $calls.Add($(if ($rule.Count -eq 1) { $rule[0].calls } else { 0 }))
        $bytesPerCall.Add($(if ($rule.Count -eq 1 -and $rule[0].calls -gt 0) { $rule[0].inputBytes / $rule[0].calls } else { 0 }))
      }
      $row[$arm + 'Calls'] = Get-Median $calls.ToArray()
      $row[$arm + 'BytesPerCall'] = Get-Median $bytesPerCall.ToArray()
    }
    $row['regression'] = Test-Regression $row.baselineBytesPerCall $row.candidateBytesPerCall
    $regexByRule += $row
  }
  $record = [ordered]@{
    schemaVersion = 1
    evaluatedOn = '2026-11-01'
    runnerImage = $env:ImageOS
    operatingSystem = [Runtime.InteropServices.RuntimeInformation]::OSDescription
    architecture = [Runtime.InteropServices.RuntimeInformation]::OSArchitecture.ToString()
    logicalProcessors = [Environment]::ProcessorCount
    runnerImageVersion = $env:ImageVersion
    cpuModel = $(if (Test-Path -LiteralPath '/proc/cpuinfo') { ((Get-Content /proc/cpuinfo | Where-Object { $_ -match '^model name\s*:' } | Select-Object -First 1) -split ':', 2)[1].Trim() } else { $env:PROCESSOR_IDENTIFIER })
    availableMemoryBytes = [GC]::GetGCMemoryInfo().TotalAvailableMemoryBytes
    baselineProductVersion = $metadata.baseline.Version
    candidateProductVersion = $metadata.candidate.Version
    loadedPacks = @($metadata.candidate.Packs)
    baselineSha256 = (Get-FileHash -LiteralPath $baseline -Algorithm SHA256).Hash.ToLowerInvariant()
    candidateSha256 = (Get-FileHash -LiteralPath $candidate -Algorithm SHA256).Hash.ToLowerInvariant()
    binarySizeBudgetBytes = 10MB
    candidateBytes = (Get-Item -LiteralPath $release).Length
    candidateInstrumentedBytes = (Get-Item -LiteralPath $candidate).Length
    releaseSha256 = (Get-FileHash -LiteralPath $release -Algorithm SHA256).Hash.ToLowerInvariant()
    referencePack = $(if ($ReferencePack) { 'provisional synthetic local pack' } else { 'embedded default pack' })
    referencePackSha256 = $(if ($ReferencePack) { (Get-FileHash -LiteralPath $ReferencePack -Algorithm SHA256).Hash.ToLowerInvariant() } else { $null })
    inputs = @($manifests[500], $manifests[10000])
    measurements = $measurements
    regexByRule = $regexByRule
    regexInputUnit = 'UTF-16 searchable suffix bytes per regex search, including unsuccessful searches.'
  }
  [IO.File]::WriteAllText((Join-Path $reportPath 'benchmark.json'), ($record | ConvertTo-Json -Depth 10) + "`n")
  $lines = @('# Provisional native benchmark report', '', 'One warm-up pair was discarded; values are medians of three fresh-process pairs on the same runner.', '', '| Measurement | Baseline | Candidate |', '|---|---:|---:|')
  foreach ($entry in $measurements.GetEnumerator()) { $lines += "| $($entry.Key) | $($entry.Value.baseline.ToString('F3', [Globalization.CultureInfo]::InvariantCulture)) | $($entry.Value.candidate.ToString('F3', [Globalization.CultureInfo]::InvariantCulture)) |" }
  $lines += @('', "Candidate binary: $($record.candidateBytes) bytes (budget 10,485,760). Whole-scan budget: 60,000 ms; added-CI budget: 30,000 ms.", '', 'Per-file timing and allocation counters come from the same instrumented whole-scan invocation. The size gate uses the release binary. Regex input bytes count UTF-16 searchable suffixes, including unsuccessful searches.')
  [IO.File]::WriteAllText((Join-Path $reportPath 'benchmark.md'), ($lines -join "`n") + "`n")
  if ($record.candidateBytes -gt 10MB -or $measurements.scan10000Milliseconds.candidate -gt 60000 -or $measurements.ci500Milliseconds.candidate -gt 30000 -or $measurements.fileMedianMilliseconds.candidate -gt 10 -or $measurements.fileP95Milliseconds.candidate -gt 50 -or @($measurements.Values | Where-Object regression).Count -gt 0 -or @($regexByRule | Where-Object regression).Count -gt 0) { throw 'A native size, absolute time or 20 per cent regression budget failed. Inspect benchmark.json.' }
} catch {
  $failure = [ordered]@{ status = 'Blocked (IQ-60) or measurement failure'; message = $_.Exception.Message }
  [IO.File]::WriteAllText((Join-Path $reportPath 'failure.json'), ($failure | ConvertTo-Json) + "`n")
  throw
} finally {
  $resolved = [IO.Path]::GetFullPath($root)
  if (-not $resolved.StartsWith([IO.Path]::GetFullPath([IO.Path]::GetTempPath()), [StringComparison]::OrdinalIgnoreCase)) { throw 'The benchmark cleanup path is outside the temporary directory.' }
  Remove-Item -LiteralPath $resolved -Recurse -Force
}
