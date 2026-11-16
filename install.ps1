# SPDX-License-Identifier: Apache-2.0
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string] $Version,
    [Parameter(Mandatory)][string] $PublicKey,
    [Parameter(Mandatory)][string] $Destination,
    [string] $SourceDirectory
)
$ErrorActionPreference = 'Stop'
$arguments = @("$PSScriptRoot/ci/native-release/install.mjs", '--version', $Version, '--public-key', $PublicKey, '--destination', $Destination)
if ($SourceDirectory) { $arguments += @('--source-directory', $SourceDirectory) }
& node @arguments
exit $LASTEXITCODE
