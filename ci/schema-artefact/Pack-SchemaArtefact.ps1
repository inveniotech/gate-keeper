# Packs schemas/ into a versioned artefact. The manifest bytes are the contract;
# the zip is only the transport, because zip metadata is not stable across
# operating systems. See README.md in this directory.
[CmdletBinding()]
param(
    [string] $SchemasDirectory,
    [string] $Output,
    [string] $ManifestOutput
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$RepositoryRoot = Resolve-Path (Join-Path $PSScriptRoot '../..')
if (-not $SchemasDirectory) {
    $SchemasDirectory = Join-Path $RepositoryRoot 'schemas'
}

$versionPath = Join-Path $SchemasDirectory 'VERSION'
if (-not (Test-Path -LiteralPath $versionPath)) {
    throw "schemas/VERSION is missing. That file is the artefact version."
}

$version = (Get-Content -LiteralPath $versionPath -Raw).Trim()
if ($version -notmatch '^[0-9]+\.[0-9]+\.[0-9]+$') {
    throw "schemas/VERSION must be major.minor.patch with digits only. Found '$version'."
}

$allowedPath = '^[A-Za-z0-9._/-]+$'
$entries = [System.Collections.Generic.List[object]]::new()
Get-ChildItem -LiteralPath $SchemasDirectory -Recurse -File -Filter *.json | ForEach-Object {
    $relative = [System.IO.Path]::GetRelativePath($SchemasDirectory, $_.FullName).Replace('\', '/')
    if ($relative -notmatch $allowedPath -or $relative.Contains('..')) {
        throw "Schema path '$relative' is not allowed. Use letters, digits, '.', '_' , '-' and '/' only, with no '..'."
    }

    $hash = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
    $entries.Add([pscustomobject]@{ Path = $relative; Sha256 = $hash; FullName = $_.FullName })
}

$sorted = @($entries | Sort-Object -Property Path)
$manifest = New-Object System.Text.StringBuilder
function Add-Line([string] $Line) {
    [void]$script:manifest.Append($Line).Append("`n")
}

Add-Line '{'
Add-Line '  "name": "gate-keeper-schemas",'
Add-Line "  `"version`": `"$version`","
if ($sorted.Count -eq 0) {
    Add-Line '  "files": []'
}
else {
    Add-Line '  "files": ['
    for ($index = 0; $index -lt $sorted.Count; $index++) {
        $comma = if ($index -lt ($sorted.Count - 1)) { ',' } else { '' }
        Add-Line '    {'
        Add-Line "      `"path`": `"$($sorted[$index].Path)`","
        Add-Line "      `"sha256`": `"$($sorted[$index].Sha256)`""
        Add-Line "    }$comma"
    }
    Add-Line '  ]'
}
Add-Line '}'

$utf8 = New-Object System.Text.UTF8Encoding $false
$manifestBytes = $utf8.GetBytes($manifest.ToString())

if ($ManifestOutput) {
    $manifestParent = Split-Path -Parent $ManifestOutput
    if ($manifestParent) {
        New-Item -ItemType Directory -Force -Path $manifestParent | Out-Null
    }

    [System.IO.File]::WriteAllBytes($ManifestOutput, $manifestBytes)
}

if (-not $Output) {
    $Output = Join-Path ([System.IO.Path]::GetTempPath()) "gate-keeper-schemas-$version.zip"
}

$outputParent = Split-Path -Parent $Output
if ($outputParent) {
    New-Item -ItemType Directory -Force -Path $outputParent | Out-Null
}

if (Test-Path -LiteralPath $Output) {
    Remove-Item -LiteralPath $Output -Force
}

Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem
$zipEpoch = [datetime]::new(1980, 1, 1, 0, 0, 0, [DateTimeKind]::Unspecified)
$stream = [System.IO.File]::Open($Output, [System.IO.FileMode]::CreateNew)
try {
    $archive = New-Object System.IO.Compression.ZipArchive(
        $stream,
        [System.IO.Compression.ZipArchiveMode]::Create,
        $false)
    try {
        $manifestEntry = $archive.CreateEntry(
            'manifest.json',
            [System.IO.Compression.CompressionLevel]::NoCompression)
        $manifestEntry.LastWriteTime = $zipEpoch
        $manifestStream = $manifestEntry.Open()
        try {
            $manifestStream.Write($manifestBytes, 0, $manifestBytes.Length)
        }
        finally {
            $manifestStream.Dispose()
        }

        foreach ($file in $sorted) {
            $entry = $archive.CreateEntry(
                "schemas/$($file.Path)",
                [System.IO.Compression.CompressionLevel]::NoCompression)
            $entry.LastWriteTime = $zipEpoch
            $input = [System.IO.File]::OpenRead($file.FullName)
            $outputStream = $entry.Open()
            try {
                $input.CopyTo($outputStream)
            }
            finally {
                $outputStream.Dispose()
                $input.Dispose()
            }
        }
    }
    finally {
        $archive.Dispose()
    }
}
finally {
    $stream.Dispose()
}

Write-Output $Output
