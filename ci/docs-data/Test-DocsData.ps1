# Proves the docs-data packer is deterministic and that the committed
# manifest still matches a fresh pack. Exits 0 on success and throws on drift.
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$here = $PSScriptRoot
$packer = Join-Path $here 'Pack-DocsData.ps1'
$expectedPath = Join-Path $here 'expected-manifest.json'

function Read-ManifestBytes([string] $ZipPath) {
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $archive = [System.IO.Compression.ZipFile]::OpenRead($ZipPath)
    try {
        $entry = $archive.GetEntry('manifest.json')
        if ($null -eq $entry) {
            throw "The artefact '$ZipPath' has no manifest.json entry."
        }

        $buffer = New-Object byte[] $entry.Length
        $entryStream = $entry.Open()
        try {
            $read = 0
            while ($read -lt $buffer.Length) {
                $count = $entryStream.Read($buffer, $read, $buffer.Length - $read)
                if ($count -eq 0) {
                    throw "The manifest entry in '$ZipPath' ended before its declared length."
                }

                $read += $count
            }
        }
        finally {
            $entryStream.Dispose()
        }

        return $buffer
    }
    finally {
        $archive.Dispose()
    }
}

function Read-EntryNames([string] $ZipPath) {
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $archive = [System.IO.Compression.ZipFile]::OpenRead($ZipPath)
    try {
        return @($archive.Entries | ForEach-Object { $_.FullName })
    }
    finally {
        $archive.Dispose()
    }
}

function Get-EntryHash([string] $ZipPath, [string] $EntryName) {
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $archive = [System.IO.Compression.ZipFile]::OpenRead($ZipPath)
    try {
        $entry = $archive.GetEntry($EntryName)
        if ($null -eq $entry) {
            throw "The docs-data artefact omits '$EntryName'."
        }

        $sha = [System.Security.Cryptography.SHA256]::Create()
        try {
            $entryStream = $entry.Open()
            try {
                $digest = $sha.ComputeHash($entryStream)
            }
            finally {
                $entryStream.Dispose()
            }
        }
        finally {
            $sha.Dispose()
        }

        return ([System.BitConverter]::ToString($digest)).Replace('-', '').ToLowerInvariant()
    }
    finally {
        $archive.Dispose()
    }
}

function Assert-SameBytes([byte[]] $Actual, [byte[]] $Expected, [string] $Message) {
    if ($Actual.Length -ne $Expected.Length) {
        throw "$Message Length $($Actual.Length) is not $($Expected.Length)."
    }

    for ($index = 0; $index -lt $Actual.Length; $index++) {
        if ($Actual[$index] -ne $Expected[$index]) {
            throw "$Message The first different byte is at offset $index."
        }
    }
}

if (-not (Test-Path -LiteralPath $expectedPath)) {
    throw 'ci/docs-data/expected-manifest.json is missing. Regenerate it with Pack-DocsData.ps1 -ManifestOutput.'
}

$expected = [System.IO.File]::ReadAllBytes($expectedPath)
$workspace = Join-Path ([System.IO.Path]::GetTempPath()) ("gate-keeper-docs-data-check-" + [guid]::NewGuid().ToString('n'))
New-Item -ItemType Directory -Force -Path $workspace | Out-Null
$firstManifestPath = Join-Path $workspace 'first-manifest.json'
$secondManifestPath = Join-Path $workspace 'second-manifest.json'
$firstZip = & $packer -Output (Join-Path $workspace 'first.zip') -ManifestOutput $firstManifestPath
$secondZip = & $packer -Output (Join-Path $workspace 'second.zip') -ManifestOutput $secondManifestPath
try {
    $first = Read-ManifestBytes $firstZip
    $second = Read-ManifestBytes $secondZip
    Assert-SameBytes $first $second 'Packing the same docs-data twice did not produce the same manifest.'
    Assert-SameBytes $first $expected 'The packed manifest has drifted from ci/docs-data/expected-manifest.json. Regenerate that file and review the diff; do not edit it by hand.'
    Assert-SameBytes ([System.IO.File]::ReadAllBytes($firstManifestPath)) $first 'The standalone manifest differs from the manifest inside the packed artefact.'
    Assert-SameBytes ([System.IO.File]::ReadAllBytes($secondManifestPath)) $second 'The second standalone manifest differs from the manifest inside its packed artefact.'

    $manifestText = [System.Text.Encoding]::UTF8.GetString($first)
    try {
        $manifest = $manifestText | ConvertFrom-Json
    }
    catch {
        throw "The packed manifest is not valid JSON: $($_.Exception.Message)"
    }

    $names = Read-EntryNames $firstZip
    foreach ($required in @('manifest.json', 'rules.json', 'citations.json', 'schemas/VERSION')) {
        if ($names -notcontains $required) {
            throw "The docs-data artefact omits '$required'."
        }
    }

    foreach ($pattern in @('^rules/.+\.md$', '^cli/.+\.md$', '^schemas/.+\.json$')) {
        if (-not ($names | Where-Object { $_ -match $pattern })) {
            throw "The docs-data artefact omits every entry matching '$pattern'."
        }
    }

    if (($names | Select-Object -Unique).Count -ne $names.Count) {
        throw 'The docs-data artefact lists a duplicate entry path.'
    }

    $listed = @($manifest.files)
    if ($names.Count - 1 -ne $listed.Count) {
        throw 'The manifest file list and the zip entries disagree.'
    }

    if ($manifest.name -ne 'gate-keeper-docs-data') {
        throw "The manifest name is '$($manifest.name)', not 'gate-keeper-docs-data'."
    }

    foreach ($version in @($manifest.version, $manifest.packVersion, $manifest.schemasVersion)) {
        if ($version -notmatch '^[0-9]+\.[0-9]+\.[0-9]+$') {
            throw "The manifest carries the invalid version '$version'."
        }
    }

    $previous = ''
    foreach ($file in $listed) {
        if ($previous -and [string]::CompareOrdinal($previous, $file.path) -ge 0) {
            throw "The manifest file list is not sorted ordinally at '$($file.path)'."
        }

        $previous = $file.path
        if ($file.sha256 -notmatch '^[0-9a-f]{64}$') {
            throw "The manifest digest for '$($file.path)' is not a lowercase SHA-256."
        }

        if ((Get-EntryHash $firstZip $file.path) -ne $file.sha256) {
            throw "The manifest digest for '$($file.path)' does not match the packed bytes."
        }
    }
}
finally {
    Remove-Item -LiteralPath $workspace -Recurse -Force -ErrorAction SilentlyContinue
}

Write-Output 'Docs-data artefact check passed.'
