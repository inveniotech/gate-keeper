# Proves the banned-terms lint flags banned vocabulary and name variants in
# prose and in identifiers, leaves conforming fixtures clean, and rejects a
# malformed glossary. Exits 0 on success and throws on any failure.
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$here = $PSScriptRoot
$lint = Join-Path $here 'Invoke-GlossaryLint.ps1'
$fixtures = Join-Path $here 'fixtures'

function Get-LintResult {
    param([hashtable] $Parameters)

    $output = @(& $lint @Parameters)
    return [pscustomobject]@{
        Output   = $output
        ExitCode = $LASTEXITCODE
    }
}

function Assert-Contains([string] $Text, [string] $Expected, [string] $Message) {
    if (-not $Text.Contains($Expected)) {
        throw "$Message The output did not contain '$Expected'."
    }
}

function Assert-Empty([string] $Text, [string] $Message) {
    if ($Text.Length -ne 0) {
        throw "$Message The output was not empty:`n$Text"
    }
}

$prose = Join-Path $fixtures 'banned-terms.md'
$identifiers = Join-Path $fixtures 'banned-terms.cs'
$conformingProse = Join-Path $fixtures 'conforming.md'
$conformingIdentifiers = Join-Path $fixtures 'conforming.cs'

# Banned vocabulary and name variants in prose.
$proseResult = Get-LintResult -Parameters @{ Path = $prose }
if ($proseResult.ExitCode -ne 1) {
    throw "The lint exited with code $($proseResult.ExitCode) for the prose fixture; expected 1."
}

$proseText = $proseResult.Output -join "`n"
Assert-Contains $proseText 'ruleset' 'The prose fixture should flag the banned term "ruleset".'
Assert-Contains $proseText 'compliance score' 'The prose fixture should flag the banned term "compliance score".'
Assert-Contains $proseText 'subscription' 'The prose fixture should flag the banned term "subscription".'
Assert-Contains $proseText 'Gatekeeper' 'The prose fixture should flag the banned name variant "Gatekeeper".'
Assert-Contains $proseText 'Gate Keeper' 'The prose fixture should flag the banned name variant "Gate Keeper".'
Assert-Contains $proseText 'gate_keeper' 'The prose fixture should flag the banned name variant "gate_keeper".'
Assert-Contains $proseText 'GateKeeper' 'The prose fixture should flag the Pascal name form "GateKeeper", which is reserved for identifiers.'
Assert-Contains $proseText 'use "rule pack"' 'The prose finding should name the approved term "rule pack".'

# Banned vocabulary and name variants in identifiers.
$identifierResult = Get-LintResult -Parameters @{ Path = $identifiers }
if ($identifierResult.ExitCode -ne 1) {
    throw "The lint exited with code $($identifierResult.ExitCode) for the identifier fixture; expected 1."
}

$identifierText = $identifierResult.Output -join "`n"
Assert-Contains $identifierText 'ruleset' 'The identifier fixture should flag the banned term "ruleset".'
Assert-Contains $identifierText 'subscription' 'The identifier fixture should flag the banned term "subscription".'
Assert-Contains $identifierText 'Gatekeeper' 'The identifier fixture should flag the banned name variant "Gatekeeper".'
Assert-Contains $identifierText 'Gate Keeper' 'The identifier fixture should flag the banned name variant "Gate Keeper".'
Assert-Contains $identifierText 'gate_keeper' 'The identifier fixture should flag the banned name variant "gate_keeper".'

# Conforming fixtures are clean.
$conformingResult = Get-LintResult -Parameters @{ Path = @($conformingProse, $conformingIdentifiers) }
if ($conformingResult.ExitCode -ne 0) {
    throw "The lint exited with code $($conformingResult.ExitCode) for the conforming fixtures; expected 0."
}

Assert-Empty ($conformingResult.Output -join "`n") 'The conforming fixtures produced findings:'

# Directory recursion plus an exclude list keeps the lint usable over a tree
# without self-flagging the fixtures that intentionally contain banned terms.
$excludedResult = Get-LintResult -Parameters @{
    Path    = $fixtures
    Exclude = @('ci/glossary/fixtures/banned-terms.md', 'ci/glossary/fixtures/banned-terms.cs')
}
if ($excludedResult.ExitCode -ne 0) {
    throw "The lint exited with code $($excludedResult.ExitCode) after the banned fixtures were excluded; expected 0."
}

Assert-Empty ($excludedResult.Output -join "`n") 'The excluded directory scan produced findings:'

# A malformed glossary is a tool error, not a silent pass.
$tempRoot = Join-Path ([System.IO.Path]::GetTempPath()) ("gate-keeper-glossary-" + [guid]::NewGuid().ToString('n'))
New-Item -ItemType Directory -Force -Path $tempRoot | Out-Null
try {
    $badGlossary = Join-Path $tempRoot 'glossary.json'
    Set-Content -LiteralPath $badGlossary -Value '{ "name": "broken", "version": "1.0.0", "terms": [] }' -Encoding utf8NoBOM
    $rejected = $false
    try {
        & $lint -Path $prose -GlossaryPath $badGlossary | Out-Null
    }
    catch {
        $rejected = $true
    }

    if (-not $rejected) {
        throw 'The lint accepted a glossary that is missing the required nameForms and bannedVariants arrays.'
    }
}
finally {
    Remove-Item -LiteralPath $tempRoot -Recurse -Force -ErrorAction SilentlyContinue
}

Write-Output 'Banned-terms lint check passed.'
