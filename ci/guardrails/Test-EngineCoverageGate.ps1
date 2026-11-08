# P2-11: exercise the exact boundary and fail-closed report handling offline.
$ErrorActionPreference = 'Stop'
$temporary = Join-Path ([IO.Path]::GetTempPath()) ('gk-coverage-gate-' + [guid]::NewGuid())
[IO.Directory]::CreateDirectory($temporary) | Out-Null
try {
    $cases = @(
        @{ Name = 'boundary'; Hits = 80; Total = 100; Expected = 0 },
        @{ Name = 'below'; Hits = 79; Total = 100; Expected = 1 },
        @{ Name = 'rounding'; Hits = 799; Total = 1000; Expected = 1 },
        @{ Name = 'empty'; Hits = 0; Total = 0; Expected = 2 }
    )
    foreach ($case in $cases) {
        $lines = for ($index = 1; $index -le $case.Total; $index++) {
            '<line number="{0}" hits="{1}" />' -f $index, [int]($index -le $case.Hits)
        }
        $report = Join-Path $temporary ($case.Name + '.xml')
        [IO.File]::WriteAllText($report, '<coverage><packages><package name="InvenioTech.GateKeeper.Engine"><classes><class filename="src/Engine/Example.cs"><lines>' + ($lines -join '') + '</lines></class></classes></package></packages></coverage>')
        & pwsh -NoProfile -File "$PSScriptRoot/Invoke-EngineCoverageGate.ps1" -Report $report
        if ($LASTEXITCODE -ne $case.Expected) { throw "Coverage case '$($case.Name)' returned $LASTEXITCODE, expected $($case.Expected)." }
    }
    foreach ($content in @('<coverage><packages /></coverage>', '<broken', '<!DOCTYPE coverage [<!ENTITY external SYSTEM "file:///missing">]><coverage>&external;</coverage>')) {
        $report = Join-Path $temporary 'invalid.xml'
        [IO.File]::WriteAllText($report, $content)
        & pwsh -NoProfile -File "$PSScriptRoot/Invoke-EngineCoverageGate.ps1" -Report $report
        if ($LASTEXITCODE -ne 2) { throw 'An invalid coverage report must fail closed.' }
    }
    & pwsh -NoProfile -File "$PSScriptRoot/Invoke-EngineCoverageGate.ps1" -Report (Join-Path $temporary 'missing.xml')
    if ($LASTEXITCODE -ne 2) { throw 'A missing coverage report must fail closed.' }
    Write-Output 'Engine coverage gate acceptance passed.'
}
finally {
    # Delete only the unique temporary directory created by this harness.
    if ((Split-Path $temporary -Parent) -ne [IO.Path]::GetTempPath().TrimEnd('\', '/')) { throw 'Unsafe temporary path.' }
    Remove-Item -LiteralPath $temporary -Recurse -Force
}
# Expected child failures must not become the workflow step's process status.
exit 0
