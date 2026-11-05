# SPDX-License-Identifier: Apache-2.0
[CmdletBinding()]
param(
  [Parameter(Mandatory)][string] $Binary,
  [Parameter(Mandatory)][string] $RuntimeIdentifier,
  [Parameter(Mandatory)][string] $Destination,
  [Parameter(Mandatory)][string] $ProductVersion
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$path = (Resolve-Path -LiteralPath $Binary).Path
$size = (Get-Item -LiteralPath $path).Length
if ($size -gt 8MB) { throw "The $RuntimeIdentifier binary is $size bytes, above the unchanged 8 MiB budget (IQ-90)." }
New-Item -ItemType Directory -Path $Destination -Force | Out-Null
$destinationPath = (Resolve-Path -LiteralPath $Destination).Path
$name = "gatekeeper-ndpa-check-$RuntimeIdentifier"
if ($RuntimeIdentifier.StartsWith('win-', [StringComparison]::Ordinal)) {
  $archive = Join-Path $destinationPath "$name.zip"
  Compress-Archive -LiteralPath $path -DestinationPath $archive -Force
} else {
  $archive = Join-Path $destinationPath "$name.tar.gz"
  & tar -czf $archive -C (Split-Path -Parent $path) (Split-Path -Leaf $path)
  if ($LASTEXITCODE -ne 0) { throw 'The native binary archive could not be created.' }
}
$hash = (Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash.ToLowerInvariant()
[IO.File]::WriteAllText((Join-Path $destinationPath "$name.sha256"), "$hash  $([IO.Path]::GetFileName($archive))`n")
$record = [ordered]@{ product = 'gatekeeper'; productVersion = $ProductVersion; runtimeIdentifier = $RuntimeIdentifier; binaryBytes = $size; archive = [IO.Path]::GetFileName($archive); sha256 = $hash }
[IO.File]::WriteAllText((Join-Path $destinationPath "$name.json"), ($record | ConvertTo-Json) + "`n")
