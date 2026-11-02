# Runs cspell with the repository's cspell.json configuration. Exits 0 when no
# spelling issues are found, 1 when issues are found, and 2 on a tool error. See
# ci/guardrails/README.md.
[CmdletBinding()]
param(
    [string] $CSpellVersion = '9.0.1'
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
$config = Join-Path $repositoryRoot 'cspell.json'

if (-not (Test-Path -LiteralPath $config)) {
    throw "cspell configuration '$config' does not exist."
}

$cspell = Get-Command cspell -ErrorAction SilentlyContinue
if ($cspell) {
    & $cspell.Source lint --config $config
}
else {
    Write-Output "cspell not found; running via npx cspell@$CSpellVersion ..."
    npx --yes "cspell@$CSpellVersion" lint --config $config
}
exit $LASTEXITCODE
