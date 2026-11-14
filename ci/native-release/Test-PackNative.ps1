# SPDX-License-Identifier: Apache-2.0
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$root = Join-Path ([IO.Path]::GetTempPath()) ('gate-keeper-package-test-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $root | Out-Null
try {
  $binary = Join-Path $root 'gatekeeper.exe'
  [IO.File]::WriteAllBytes($binary, @(1, 2, 3))
  $destination = Join-Path $root 'package'
  & "$PSScriptRoot/Pack-Native.ps1" -Binary $binary -RuntimeIdentifier win-x64 -Destination $destination -ProductVersion 0.1.0
  $record = Get-Content (Join-Path $destination 'gatekeeper-win-x64.json') -Raw | ConvertFrom-Json
  if ($record.binaryBytes -ne 3 -or $record.productVersion -cne '0.1.0') { throw 'The package measurement or product stamp is incorrect.' }
  $archive = Join-Path $destination $record.archive
  if ((Get-FileHash $archive -Algorithm SHA256).Hash.ToLowerInvariant() -cne $record.sha256) { throw 'The package checksum is incorrect.' }
  $unpacked = Join-Path $root 'unpacked'
  Expand-Archive -LiteralPath $archive -DestinationPath $unpacked
  if ([Convert]::ToBase64String([IO.File]::ReadAllBytes((Join-Path $unpacked 'gatekeeper.exe'))) -cne [Convert]::ToBase64String([byte[]]@(1, 2, 3))) { throw 'The package changed the executable bytes.' }
  [IO.File]::SetLastWriteTimeUtc($binary, [DateTime]::new(2025, 1, 1, 0, 0, 0, [DateTimeKind]::Utc))
  $second = Join-Path $root 'second-package'
  & "$PSScriptRoot/Pack-Native.ps1" -Binary $binary -RuntimeIdentifier win-x64 -Destination $second -ProductVersion 0.1.0
  $secondRecord = Get-Content (Join-Path $second 'gatekeeper-win-x64.json') -Raw | ConvertFrom-Json
  if ($secondRecord.sha256 -cne $record.sha256) { throw 'Archive bytes must not depend on the input file timestamp.' }
  $stream = [IO.File]::OpenWrite($binary)
  try { $stream.SetLength(10MB) } finally { $stream.Dispose() }
  & "$PSScriptRoot/Pack-Native.ps1" -Binary $binary -RuntimeIdentifier win-x64 -Destination (Join-Path $root 'boundary') -ProductVersion 0.1.0
  $stream = [IO.File]::OpenWrite($binary)
  try { $stream.SetLength(10MB + 1) } finally { $stream.Dispose() }
  $rejected = $false
  try { & "$PSScriptRoot/Pack-Native.ps1" -Binary $binary -RuntimeIdentifier win-x64 -Destination (Join-Path $root 'oversize') -ProductVersion 0.1.0 } catch { $rejected = $true }
  if (-not $rejected) { throw 'An executable above the owner-approved 10 MiB budget must fail.' }
  Write-Output 'Native archive integrity, version stamp, checksum and size-budget tests passed.'
} finally {
  $resolved = [IO.Path]::GetFullPath($root)
  if (-not $resolved.StartsWith([IO.Path]::GetFullPath([IO.Path]::GetTempPath()), [StringComparison]::OrdinalIgnoreCase)) { throw 'The package-test cleanup path is outside the temporary directory.' }
  Remove-Item -LiteralPath $resolved -Recurse -Force
}
