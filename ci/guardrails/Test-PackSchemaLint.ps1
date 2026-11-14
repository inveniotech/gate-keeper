# SPDX-License-Identifier: Apache-2.0
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$work = Join-Path ([IO.Path]::GetTempPath()) ('gate-keeper-schema-' + [Guid]::NewGuid().ToString('N'))
$schema = [IO.Path]::GetFullPath("$PSScriptRoot/../../schemas/rulepack.schema.json")
New-Item -ItemType Directory -Path $work | Out-Null
function Assert-Lint([int] $Expected, [string] $SchemaFile = $schema) {
    $output = & pwsh -NoProfile -File "$PSScriptRoot/Invoke-PackSchemaLint.ps1" -RulePacksPath $work -SchemaPath $SchemaFile 2>&1
    if ($LASTEXITCODE -ne $Expected) { throw "Expected schema lint exit $Expected, received $LASTEXITCODE. Output: $output" }
    if (($output -join "`n").Contains('synthetic-sensitive-marker')) { throw 'Schema lint printed input content.' }
    return ($output -join "`n")
}
try {
    $valid = [IO.File]::ReadAllText("$PSScriptRoot/../../docs/specs/examples/rule-pack/minimal.json")
    [IO.File]::WriteAllText((Join-Path $work 'valid.json'), $valid)
    Copy-Item -LiteralPath "$PSScriptRoot/../zero-egress/probe.pack.json" -Destination (Join-Path $work 'valid-local.json')
    $null = Assert-Lint 0
    [IO.File]::WriteAllText((Join-Path $work 'invalid.json'), '{"synthetic-sensitive-marker":true}')
    [IO.File]::WriteAllText((Join-Path $work 'malformed.json'), '{')
    $output = Assert-Lint 1
    if (-not $output.Contains('invalid.json') -or -not $output.Contains('malformed.json')) { throw 'Schema lint did not report every invalid file.' }
    Remove-Item -LiteralPath (Join-Path $work 'invalid.json'),(Join-Path $work 'malformed.json')
    $invalidShape = [IO.File]::ReadAllText("$PSScriptRoot/../../docs/specs/examples/rule-pack/invalid.json")
    [IO.File]::WriteAllText((Join-Path $work 'invalid-shape.json'), $invalidShape)
    $null = Assert-Lint 1
    Remove-Item -LiteralPath (Join-Path $work 'invalid-shape.json')
    $unknown = $valid | ConvertFrom-Json -AsHashtable
    $unknown['unexpected'] = 'synthetic-sensitive-marker'
    [IO.File]::WriteAllText((Join-Path $work 'unknown-field.json'), ($unknown | ConvertTo-Json -Depth 100))
    $null = Assert-Lint 1
    Remove-Item -LiteralPath (Join-Path $work 'unknown-field.json')
    [IO.File]::WriteAllText((Join-Path $work 'citations.json'), '{"schemaVersion":1,"status":"draft","entries":[]}')
    $null = Assert-Lint 0
    $null = Assert-Lint 2 (Join-Path $work 'missing-schema.json')
    [IO.File]::WriteAllText((Join-Path $work 'bad-schema.json'), '{}')
    $null = Assert-Lint 2 (Join-Path $work 'bad-schema.json')
    Remove-Item -LiteralPath (Join-Path $work 'bad-schema.json'),(Join-Path $work 'valid.json'),(Join-Path $work 'valid-local.json')
    $null = Assert-Lint 0
    Write-Output 'Pack-schema lint acceptance checks passed.'
} finally {
    $resolved = [IO.Path]::GetFullPath($work)
    $temporary = [IO.Path]::GetFullPath([IO.Path]::GetTempPath())
    if (-not $resolved.StartsWith($temporary, [StringComparison]::OrdinalIgnoreCase)) { throw 'The test cleanup path is outside the temporary directory.' }
    Remove-Item -LiteralPath $resolved -Recurse -Force
}
