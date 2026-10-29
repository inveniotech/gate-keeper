# Proves the schema artefact packer is deterministic and that the committed
# manifest still matches a fresh pack. Exits 0 on success and throws on drift.
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$here = $PSScriptRoot
$repositoryRoot = Resolve-Path (Join-Path $here '../..')
$packer = Join-Path $here 'Pack-SchemaArtefact.ps1'
$expectedPath = Join-Path $here 'expected-manifest.json'
$schemasDirectory = Join-Path $repositoryRoot 'schemas'

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
    throw "ci/schema-artefact/expected-manifest.json is missing. Regenerate it with Pack-SchemaArtefact.ps1 -ManifestOutput."
}

$expected = [System.IO.File]::ReadAllBytes($expectedPath)
$firstZip = & $packer -SchemasDirectory $schemasDirectory
$secondZip = & $packer -SchemasDirectory $schemasDirectory
try {
    $first = Read-ManifestBytes $firstZip
    $second = Read-ManifestBytes $secondZip
    Assert-SameBytes $first $second 'Packing the same schemas twice did not produce the same manifest.'
    Assert-SameBytes $first $expected 'The packed manifest has drifted from ci/schema-artefact/expected-manifest.json. Regenerate that file and review the diff; do not edit it by hand.'
}
finally {
    Remove-Item -LiteralPath $firstZip, $secondZip -Force -ErrorAction SilentlyContinue
}

$tempRoot = Join-Path ([System.IO.Path]::GetTempPath()) ("gate-keeper-schemas-" + [guid]::NewGuid().ToString('n'))
New-Item -ItemType Directory -Force -Path $tempRoot | Out-Null
try {
    Set-Content -LiteralPath (Join-Path $tempRoot 'VERSION') -Value "0.0.1`n" -NoNewline -Encoding utf8NoBOM
    $schemaPath = Join-Path $tempRoot 'example.schema.json'
    $schemaBytes = [System.Text.Encoding]::UTF8.GetBytes("{`n  `"id`": `"example`"`n}`n")
    [System.IO.File]::WriteAllBytes($schemaPath, $schemaBytes)
    $packed = & $packer -SchemasDirectory $tempRoot
    try {
        $manifestText = [System.Text.Encoding]::UTF8.GetString((Read-ManifestBytes $packed))
        $expectedHash = (Get-FileHash -LiteralPath $schemaPath -Algorithm SHA256).Hash.ToLowerInvariant()
        if (-not $manifestText.Contains('"version": "0.0.1"')) {
            throw 'A packed temporary schema directory did not record its VERSION.'
        }

        if (-not $manifestText.Contains('"path": "example.schema.json"')) {
            throw 'A packed temporary schema directory omitted the schema path.'
        }

        if (-not $manifestText.Contains($expectedHash)) {
            throw 'A packed temporary schema directory recorded the wrong SHA-256 digest.'
        }
    }
    finally {
        Remove-Item -LiteralPath $packed -Force -ErrorAction SilentlyContinue
    }

    $rejected = $false
    $badPath = Join-Path $tempRoot 'bad name.json'
    Set-Content -LiteralPath $badPath -Value '{}' -Encoding utf8NoBOM
    try {
        & $packer -SchemasDirectory $tempRoot | Out-Null
    }
    catch {
        $rejected = $true
    }

    if (-not $rejected) {
        throw 'The packer accepted a schema path that contains a space. It must reject that path.'
    }
}
finally {
    Remove-Item -LiteralPath $tempRoot -Recurse -Force -ErrorAction SilentlyContinue
}

Write-Output 'Schema artefact check passed.'
