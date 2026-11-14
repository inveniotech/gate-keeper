# Validates every rule-pack JSON file against the rule-pack JSON Schema.
# Exits 0 when there is nothing to validate or every file is valid, 1 when a
# file is invalid, and 2 on a tool error. See ci/guardrails/README.md.
[CmdletBinding()]
param(
    [string] $RulePacksPath = 'rule-packs',
    [string] $SchemaPath = 'schemas/rulepack.schema.json'
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repositoryRoot = [IO.Path]::GetFullPath("$PSScriptRoot/../..")
$rulePacksRoot = if ([IO.Path]::IsPathRooted($RulePacksPath)) { $RulePacksPath } else { Join-Path $repositoryRoot $RulePacksPath }
$schemaFile = if ([IO.Path]::IsPathRooted($SchemaPath)) { $SchemaPath } else { Join-Path $repositoryRoot $SchemaPath }

if (-not (Test-Path -LiteralPath $rulePacksRoot -PathType Container) -or -not (Test-Path -LiteralPath $schemaFile -PathType Leaf)) {
    Write-Output 'Pack-schema lint cannot run. Supply an existing pack directory and rule-pack schema file.'
    exit 2
}

try {
    $schema = Get-Content -LiteralPath $schemaFile -Raw -Encoding utf8 | ConvertFrom-Json -AsHashtable
    if (-not $schema.ContainsKey('$schema') -or -not $schema.ContainsKey('$defs')) { throw 'Not the rule-pack schema.' }
    [string[]] $packs = @(Get-ChildItem -LiteralPath $rulePacksRoot -Recurse -File -Filter '*.json' |
        Where-Object { $_.Name -cne 'rulepack.schema.json' -and $_.Name -cne 'citations.json' } |
        ForEach-Object FullName)
    [Array]::Sort($packs, [StringComparer]::Ordinal)
} catch {
    Write-Output 'Pack-schema lint could not read its inputs. Check the directory, schema JSON and file permissions.'
    exit 2
}

if ($packs.Count -eq 0) {
    Write-Output "No rule-pack JSON files found; nothing to validate."
    exit 0
}

$invalid = $false
foreach ($pack in $packs) {
    $relative = [IO.Path]::GetRelativePath($rulePacksRoot, $pack).Replace('\', '/')
    try {
        $valid = (Get-Content -LiteralPath $pack -Raw -Encoding utf8) | Test-Json -SchemaFile $schemaFile -ErrorAction SilentlyContinue
        if (-not $valid) {
            $invalid = $true
            Write-Output "$relative does not match the rule-pack schema. Correct its fields and shape before submitting it."
        } else {
            Write-Output "$relative matches the rule-pack schema."
        }
    }
    catch {
        $invalid = $true
        Write-Output "$relative could not be validated. Check its JSON and file access before submitting it."
    }
}

if ($invalid) {
    exit 1
}

Write-Output 'Pack-schema lint passed.'
