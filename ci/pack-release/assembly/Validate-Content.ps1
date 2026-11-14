# SPDX-License-Identifier: Apache-2.0
$ErrorActionPreference = 'Stop'
try {
    $schema = [IO.Path]::GetFullPath("$PSScriptRoot/../../../schemas/rulepack.schema.json")
    $valid = [Console]::In.ReadToEnd() | Test-Json -SchemaFile $schema -ErrorAction SilentlyContinue
    if (-not $valid) { exit 1 }
    exit 0
} catch { exit 2 }
