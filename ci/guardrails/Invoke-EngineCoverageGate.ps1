# P2-11 / NFR-026. Count engine lines, never the solution-wide average.
[CmdletBinding()]
param([Parameter(Mandatory)][string] $Report)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
try {
    $settings = [Xml.XmlReaderSettings]::new()
    $settings.DtdProcessing = [Xml.DtdProcessing]::Prohibit
    $settings.XmlResolver = $null
    $reader = [Xml.XmlReader]::Create([IO.Path]::GetFullPath($Report), $settings)
    try {
        $document = [Xml.XmlDocument]::new()
        $document.XmlResolver = $null
        $document.Load($reader)
    }
    finally { $reader.Dispose() }
    $packages = $document.SelectNodes('/coverage/packages/package[@name="InvenioTech.GateKeeper.Engine"]')
    if ($packages.Count -ne 1) { throw 'Expected exactly one engine package.' }
    $lines = [Collections.Generic.Dictionary[string, bool]]::new([StringComparer]::Ordinal)
    foreach ($class in $packages[0].SelectNodes('classes/class')) {
        $file = $class.GetAttribute('filename')
        if ([string]::IsNullOrWhiteSpace($file)) { throw 'Missing source path.' }
        foreach ($line in $class.SelectNodes('lines/line')) {
            $number = [long]::Parse($line.GetAttribute('number'), [Globalization.CultureInfo]::InvariantCulture)
            $hits = [long]::Parse($line.GetAttribute('hits'), [Globalization.CultureInfo]::InvariantCulture)
            if ($number -le 0 -or $hits -lt 0) { throw 'Invalid coverage count.' }
            $key = "$file`:$number"
            $lines[$key] = ($hits -gt 0) -or ($lines.ContainsKey($key) -and $lines[$key])
        }
    }
    if ($lines.Count -eq 0) { throw 'No measured engine lines.' }
    $covered = @($lines.Values | Where-Object { $_ }).Count
    Write-Output "Engine line coverage: $covered / $($lines.Count); required >= 80 per cent."
    if ([decimal]$covered * 100 -lt [decimal]$lines.Count * 80) { exit 1 }
    exit 0
}
catch {
    # Parser errors may contain source paths or report content; do not print them.
    Write-Output 'Engine coverage report is missing, invalid or unmeasured. Regenerate a Cobertura report for the complete test suite.'
    exit 2
}
