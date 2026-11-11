# SPDX-License-Identifier: Apache-2.0
[CmdletBinding()]
param([Parameter(Mandatory)][string] $BaselineRoot)
$ErrorActionPreference = 'Stop'
$root = (Resolve-Path -LiteralPath $BaselineRoot).Path
$project = Join-Path $root 'cli/InvenioTech.GateKeeper.Cli.csproj'
if (-not (Select-String -LiteralPath $project -SimpleMatch '<AssemblyName>gatekeeper-ndpa-check</AssemblyName>' -Quiet)) { return }
# A baseline from before IQ-92 still uses the jurisdiction-specific names. Renaming only the
# string constants lets both arms scan the same generated tree, so timings stay comparable.
$encoding = [Text.UTF8Encoding]::new($false)
foreach ($file in Get-ChildItem -LiteralPath (Join-Path $root 'cli'), (Join-Path $root 'src') -Recurse -File -Include '*.cs', '*.csproj') {
  $text = [IO.File]::ReadAllText($file.FullName)
  $updated = $text.Replace('gatekeeper-ndpa-check', 'gatekeeper').Replace('ndpa-check.toml', 'gatekeeper.toml').Replace('.ndpa-check', '.gatekeeper')
  if ($updated -cne $text) { [IO.File]::WriteAllText($file.FullName, $updated, $encoding) }
}
