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
if ($size -gt 10MB) { throw "The $RuntimeIdentifier binary is $size bytes, above the owner-approved 10 MiB budget (IQ-90)." }
New-Item -ItemType Directory -Path $Destination -Force | Out-Null
$destinationPath = (Resolve-Path -LiteralPath $Destination).Path
$name = "gatekeeper-$RuntimeIdentifier"
$windows = $RuntimeIdentifier.StartsWith('win-', [StringComparison]::Ordinal)
$archive = Join-Path $destinationPath ($name + $(if ($windows) { '.zip' } else { '.tar.gz' }))
$stream = [IO.File]::Open($archive, [IO.FileMode]::Create, [IO.FileAccess]::Write, [IO.FileShare]::None)
try {
  if ($windows) {
    $zip = [IO.Compression.ZipArchive]::new($stream, [IO.Compression.ZipArchiveMode]::Create, $true)
    try {
      $entry = $zip.CreateEntry([IO.Path]::GetFileName($path), [IO.Compression.CompressionLevel]::Optimal)
      $entry.LastWriteTime = [DateTimeOffset]::new(1980, 1, 1, 0, 0, 0, [TimeSpan]::Zero)
      $entry.ExternalAttributes = 0
      $target = $entry.Open()
      $source = [IO.File]::OpenRead($path)
      try { $source.CopyTo($target) } finally { $source.Dispose(); $target.Dispose() }
    } finally { $zip.Dispose() }
  } else {
    Add-Type -AssemblyName System.Formats.Tar
    $gzip = [IO.Compression.GZipStream]::new($stream, [IO.Compression.CompressionLevel]::Optimal, $true)
    try {
      $tar = [System.Formats.Tar.TarWriter]::new($gzip, $true)
      try {
        $entry = [System.Formats.Tar.UstarTarEntry]::new([System.Formats.Tar.TarEntryType]::RegularFile, [IO.Path]::GetFileName($path))
        $entry.ModificationTime = [DateTimeOffset]::UnixEpoch
        $entry.Mode = [IO.UnixFileMode]493
        $entry.Uid = 0
        $entry.Gid = 0
        $entry.UserName = ''
        $entry.GroupName = ''
        $source = [IO.File]::OpenRead($path)
        try { $entry.DataStream = $source; $tar.WriteEntry($entry) } finally { $source.Dispose() }
      } finally { $tar.Dispose() }
    } finally { $gzip.Dispose() }
  }
} finally { $stream.Dispose() }
$hash = (Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash.ToLowerInvariant()
[IO.File]::WriteAllText((Join-Path $destinationPath "$name.sha256"), "$hash  $([IO.Path]::GetFileName($archive))`n")
$record = [ordered]@{ product = 'gatekeeper'; productVersion = $ProductVersion; runtimeIdentifier = $RuntimeIdentifier; binaryBytes = $size; binarySizeBudgetBytes = 10MB; archive = [IO.Path]::GetFileName($archive); sha256 = $hash }
[IO.File]::WriteAllText((Join-Path $destinationPath "$name.json"), ($record | ConvertTo-Json) + "`n")
