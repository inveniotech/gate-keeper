# Checks that a pull-request title or body contains at least one recognised
# trace identifier. Exits 0 when an identifier is found, 1 otherwise, and 2 on
# a tool error such as missing input. See ci/guardrails/README.md.
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string] $Title,

    [Parameter(Mandatory = $true)]
    [string] $Body
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$text = "$Title $Body"
$patterns = @(
    'P\d+-\d+',
    'NFR-\d+',
    'UC-\d+',
    'F-\d+',
    'E-\d+',
    'IQ-\d+',
    'ADR-\d+'
)

foreach ($pattern in $patterns) {
    if ($text -cmatch $pattern) {
        Write-Output "Trace identifier found: '$($Matches[0])'."
        exit 0
    }
}

Write-Error "Pull-request title and body must contain a recognised trace identifier (for example P0-12, NFR-026, UC-001, F-001, E-001, IQ-001 or ADR-001)."
exit 1
