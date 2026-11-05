# SPDX-License-Identifier: Apache-2.0
[CmdletBinding()]
param(
  [Parameter(Mandatory)][string] $Binary,
  [string] $Corpus = "$PSScriptRoot/../../tests/corpus",
  [string] $ContainerImage
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$binaryPath = (Resolve-Path -LiteralPath $Binary).Path
$work = Join-Path ([IO.Path]::GetTempPath()) ('gate-keeper-native-corpus-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $work | Out-Null
$count = 0
try {
  foreach ($case in Get-ChildItem -LiteralPath $Corpus -Directory | Sort-Object Name) {
    $source = Join-Path $case.FullName 'repo'
    if (-not (Test-Path -LiteralPath $source)) { continue }
    $root = Join-Path $work $case.Name
    Copy-Item -LiteralPath $source -Destination $root -Recurse
    $folder = Join-Path $root '.ndpa-check'
    New-Item -ItemType Directory -Path $folder -Force | Out-Null
    Copy-Item -LiteralPath "$PSScriptRoot/../zero-egress/probe.pack.json" -Destination (Join-Path $folder 'probe.json')
    [IO.File]::WriteAllText((Join-Path $folder 'ndpa-check.toml'), (@('schemaVersion = 1', '[repository]', 'id = "synthetic/native-corpus"', '[packs]', 'local = [".ndpa-check/probe.json"]', '') -join [char]10))
    [IO.File]::WriteAllText((Join-Path $root 'egress-probe.txt'), "gatekeeper-corpus-probe`n")
    Push-Location $root
    try {
      if ($ContainerImage) {
        $result = & docker run --rm --network none --mount "type=bind,src=$root,dst=/scan,readonly" --mount "type=bind,src=$binaryPath,dst=/gatekeeper-ndpa-check,readonly" -w /scan $ContainerImage /gatekeeper-ndpa-check scan --json --evaluated-on 2026-11-01 2> (Join-Path $work 'diagnostics.txt')
      } else {
        $result = & $binaryPath scan --json --evaluated-on 2026-11-01 2> (Join-Path $work 'diagnostics.txt')
      }
      if ($LASTEXITCODE -ne 1) { throw "The published binary did not complete corpus case $($case.Name); inspect diagnostics in the retained test output." }
      $json = ($result -join "`n") | ConvertFrom-Json
      if (@($json.findings | Where-Object ruleId -CEQ 'local.egress.probe').Count -ne 1) { throw 'The native corpus probe was not reported exactly once.' }
      if ($json.engine.product -cne 'gatekeeper' -or $json.historyScanned) { throw 'The native corpus output has incorrect product or history state.' }
    } finally { Pop-Location }
    $count++
  }
  if ($count -eq 0) { throw 'No corpus repositories were found.' }
  Write-Output "The published binary completed $count corpus repositories."
} finally {
  $resolved = [IO.Path]::GetFullPath($work)
  $temporary = [IO.Path]::GetFullPath([IO.Path]::GetTempPath())
  if (-not $resolved.StartsWith($temporary, [StringComparison]::OrdinalIgnoreCase)) { throw 'The test cleanup path is outside the temporary directory.' }
  Remove-Item -LiteralPath $resolved -Recurse -Force
}
