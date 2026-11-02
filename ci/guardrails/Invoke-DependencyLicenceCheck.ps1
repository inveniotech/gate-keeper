# Checks .NET package references against an allow-list of known-permissive
# dependencies. Exits 0 when every package is allowed, 1 when a package is not
# in the allow-list or carries a denied licence, and 2 on a tool error. See
# ci/guardrails/README.md.
[CmdletBinding()]
param(
    [string] $PackagesPropsPath = 'Directory.Packages.props',
    [string] $AllowListPath = 'ci/guardrails/allowed-dependencies.json'
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
$propsFile = Join-Path $repositoryRoot $PackagesPropsPath
$allowListFile = Join-Path $repositoryRoot $AllowListPath

if (-not (Test-Path -LiteralPath $propsFile)) {
    throw "Package properties file '$PackagesPropsPath' does not exist."
}

if (-not (Test-Path -LiteralPath $allowListFile)) {
    throw "Allow-list '$AllowListPath' does not exist."
}

$allowList = Get-Content -LiteralPath $allowListFile -Raw | ConvertFrom-Json
$allowed = @{}
foreach ($property in $allowList.dotnet.PSObject.Properties) {
    $allowed[$property.Name] = $property.Value
}

$denied = New-Object System.Collections.Generic.List[string]
foreach ($licence in $allowList.deniedLicences) {
    $denied.Add($licence)
}

[xml] $props = Get-Content -LiteralPath $propsFile -Raw
$packageNodes = $props.SelectNodes('//PackageVersion')
$unknown = New-Object System.Collections.Generic.List[string]

foreach ($node in $packageNodes) {
    $id = $node.GetAttribute('Include')
    if ([string]::IsNullOrWhiteSpace($id)) {
        continue
    }

    if ($allowed.ContainsKey($id)) {
        $licence = $allowed[$id]
        if ($denied -contains $licence) {
            Write-Error "Package '$id' is listed with a denied licence '$licence'."
            exit 1
        }
        Write-Output "Allowed: $id ($licence)"
    }
    else {
        $unknown.Add($id)
    }
}

if ($unknown.Count -gt 0) {
    foreach ($id in ($unknown | Sort-Object)) {
        Write-Error "Package '$id' is not in the dependency allow-list. Add it with its SPDX licence identifier."
    }
    exit 1
}

Write-Output 'Dependency licence check passed.'
