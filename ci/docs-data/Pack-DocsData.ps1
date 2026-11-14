# Packs the docs-data reference into gate-keeper-docs-data-<version>.zip and
# writes its manifest. The contract is the bytes of manifest.json inside the
# zip; the zip container is transport only, because zip metadata is not stable
# across operating systems. See README.md.
[CmdletBinding()]
param(
    [string] $Output,
    [string] $ManifestOutput
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$RepositoryRoot = Resolve-Path (Join-Path $PSScriptRoot '../..')

# The product version is read from the same constant the native release workflow reads,
# so every artefact stamps one version (NFR-031).
$engineSource = Get-Content -LiteralPath (Join-Path $RepositoryRoot 'src/Engine/EngineProduct.cs') -Raw
$productVersion = [regex]::Match($engineSource, 'EngineVersion = "([^"]+)"').Groups[1].Value
if ($productVersion -notmatch '^[0-9]+\.[0-9]+\.[0-9]+$') {
    throw "The product version in src/Engine/EngineProduct.cs must be major.minor.patch. Found '$productVersion'."
}

$rulesIndex = Get-Content -LiteralPath (Join-Path $RepositoryRoot 'docs-data/rules.json') -Raw | ConvertFrom-Json
$packVersion = $rulesIndex.packVersion
if ($packVersion -notmatch '^[0-9]+\.[0-9]+\.[0-9]+$') {
    throw "docs-data/rules.json must carry a major.minor.patch pack version. Found '$packVersion'."
}

$schemasVersion = (Get-Content -LiteralPath (Join-Path $RepositoryRoot 'schemas/VERSION') -Raw).Trim()
if ($schemasVersion -notmatch '^[0-9]+\.[0-9]+\.[0-9]+$') { throw "schemas/VERSION must be major.minor.patch. Found '$schemasVersion'." }

# Each entry is the artefact-internal path plus the repository file it is copied from.
$sections = @(
    @{ Prefix = ''; Directory = 'docs-data'; Filter = 'rules.json' },
    @{ Prefix = ''; Directory = 'rule-packs'; Filter = 'citations.json' },
    @{ Prefix = 'rules'; Directory = 'docs-data/rules'; Filter = '*.md' },
    @{ Prefix = 'cli'; Directory = 'docs-data/cli'; Filter = '*.md' },
    @{ Prefix = 'schemas'; Directory = 'schemas'; Filter = '*.json' },
    @{ Prefix = 'schemas'; Directory = 'schemas'; Filter = 'VERSION' }
)

$allowedPath = '^[A-Za-z0-9._/-]+$'
$entries = [System.Collections.Generic.List[object]]::new()
foreach ($section in $sections) {
    $directory = Join-Path $RepositoryRoot $section.Directory
    if (-not (Test-Path -LiteralPath $directory)) {
        throw "The docs-data source folder '$($section.Directory)' is missing. Regenerate the reference data first."
    }

    $files = @(Get-ChildItem -LiteralPath $directory -File -Filter $section.Filter | Sort-Object Name)
    if ($files.Count -eq 0) {
        throw "The docs-data source folder '$($section.Directory)' carries no '$($section.Filter)' file. Regenerate the reference data first."
    }

    foreach ($file in $files) {
        $relative = if ($section.Prefix) { "$($section.Prefix)/$($file.Name)" } else { $file.Name }
        if ($relative -notmatch $allowedPath -or $relative.Contains('..')) {
            throw "The artefact path '$relative' is not allowed. Use letters, digits, '.', '_', '-' and '/' only, with no '..'."
        }

        $hash = (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
        $entries.Add([pscustomobject]@{ Path = $relative; Sha256 = $hash; FullName = $file.FullName })
    }
}

$entries.Sort([System.Comparison[object]] {
    param($left, $right)
    [string]::CompareOrdinal($left.Path, $right.Path)
})
$sorted = @($entries)
if (($sorted | ForEach-Object { $_.Path } | Select-Object -Unique).Count -ne $sorted.Count) {
    throw 'Two docs-data artefact files map to the same path.'
}

$manifest = New-Object System.Text.StringBuilder
function Add-Line([string] $Line) {
    [void]$script:manifest.Append($Line).Append("`n")
}

Add-Line '{'
Add-Line '  "name": "gate-keeper-docs-data",'
Add-Line "  `"version`": `"$productVersion`","
Add-Line "  `"packVersion`": `"$packVersion`","
Add-Line "  `"schemasVersion`": `"$schemasVersion`","
Add-Line '  "files": ['
for ($index = 0; $index -lt $sorted.Count; $index++) {
    $comma = if ($index -lt ($sorted.Count - 1)) { ',' } else { '' }
    Add-Line '    {'
    Add-Line "      `"path`": `"$($sorted[$index].Path)`","
    Add-Line "      `"sha256`": `"$($sorted[$index].Sha256)`""
    Add-Line "    }$comma"
}
Add-Line '  ]'
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
    $Output = Join-Path ([System.IO.Path]::GetTempPath()) "gate-keeper-docs-data-$productVersion.zip"
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
                $file.Path,
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
