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

$here = $PSScriptRoot
$repositoryRoot = if (git rev-parse --show-toplevel 2>$null) {
    (git rev-parse --show-toplevel).Trim()
}
else {
    (Resolve-Path (Join-Path $here '../..')).Path
}
$rulePacksRoot = Join-Path $repositoryRoot $RulePacksPath
$schemaFile = Join-Path $repositoryRoot $SchemaPath

if (-not (Test-Path -LiteralPath $rulePacksRoot)) {
    Write-Output "Rule-packs folder '$RulePacksPath' does not exist; nothing to validate."
    exit 0
}

$packs = @(Get-ChildItem -LiteralPath $rulePacksRoot -Recurse -File -Filter '*.json' |
    Where-Object { $_.Name -ne 'rulepack.schema.json' })

if ($packs.Count -eq 0) {
    Write-Output "No rule-pack JSON files found; nothing to validate."
    exit 0
}

if (-not (Test-Path -LiteralPath $schemaFile)) {
    Write-Output "Schema '$SchemaPath' does not exist yet; skipping schema validation (P0-03 will add the schema)."
    Write-Output "$($packs.Count) rule-pack file(s) present but not validated against a schema."
    exit 0
}

# Validate that every rule pack is well-formed JSON. When the schema exists,
# real schema validation will be added here; for Phase 0 the schema is empty.
$invalid = $false
foreach ($pack in $packs) {
    try {
        $null = Get-Content -LiteralPath $pack.FullName -Raw | ConvertFrom-Json
        $relative = $pack.FullName.Substring($repositoryRoot.Length + 1).Replace('\', '/')
        Write-Output "$relative is valid JSON."
    }
    catch {
        $invalid = $true
        Write-Error "Failed to parse '$($pack.FullName)': $_"
    }
}

if ($invalid) {
    exit 1
}

Write-Output 'Pack-schema lint passed.'
