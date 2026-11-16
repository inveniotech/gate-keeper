# SPDX-License-Identifier: Apache-2.0
[CmdletBinding()]
param([string] $GradleCommand = 'gradle', [string] $GpgCommand = 'gpg')
$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
function Convert-GpgPath([string] $Path) {
    if ($IsWindows -and $GpgCommand.Replace('\', '/') -match '/Git/usr/bin/') {
        return '/' + $Path.Substring(0, 1).ToLowerInvariant() + $Path.Substring(2).Replace('\', '/')
    }
    return $Path
}
$temporaryRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath())
# Keep agent socket paths short on Git for Windows.
$keyring = Join-Path $temporaryRoot ('gk-dev-' + [guid]::NewGuid().ToString('N').Substring(0, 16))
New-Item -ItemType Directory -Path $keyring | Out-Null
$gpgHome = Convert-GpgPath $keyring
$previousGpgHome = $env:GNUPGHOME
try {
    # Gradle streams signing bytes to GPG; the environment avoids native path rewriting.
    $env:GNUPGHOME = $gpgHome
    # This isolated, unprotected, one-day key is synthetic development material only.
    $parameters = @'
Key-Type: RSA
Key-Length: 3072
Key-Usage: sign
Name-Real: Gate-Keeper DEVELOPMENT ONLY
Name-Email: development@example.invalid
Expire-Date: 1d
%no-protection
%commit
'@
    $parameterFile = Join-Path $keyring 'development-key.txt'
    [IO.File]::WriteAllText($parameterFile, $parameters)
    & $GpgCommand --homedir $gpgHome --batch --generate-key (Convert-GpgPath $parameterFile)
    if ($LASTEXITCODE) { throw 'Development key generation failed.' }
    $listing = & $GpgCommand --homedir $gpgHome --with-colons --list-secret-keys
    if ($LASTEXITCODE) { throw 'Development key lookup failed.' }
    $fingerprint = (($listing | Where-Object { $_ -like 'fpr:*' } | Select-Object -First 1) -split ':')[9]
    if (!$fingerprint) { throw 'The isolated development key has no fingerprint.' }
    & $GradleCommand -p (Join-Path $root 'middleware/jvm') --no-daemon --rerun-tasks --project-prop 'signRelease=true' --project-prop "signing.gnupg.keyName=$fingerprint" --project-prop "signing.gnupg.executable=$GpgCommand" publishAllPublicationsToDryRunRepository
    if ($LASTEXITCODE) { throw 'Signed local Maven publication failed.' }
    $version = [IO.File]::ReadAllText((Join-Path $root 'middleware/version.txt')).Trim()
    $repository = Join-Path $root 'middleware/jvm/build/publication'
    $verified = 0
    foreach ($module in @('core', 'logback', 'log4j2', 'spring-boot')) {
        $name = "gate-keeper-$module"
        $directory = Join-Path $repository "africa/gatekeeper/$name/$version"
        foreach ($suffix in @('.pom', '.jar', '-sources.jar', '-javadoc.jar')) {
            $artifact = Join-Path $directory "$name-$version$suffix"
            if (!(Test-Path -LiteralPath $artifact) -or !(Test-Path -LiteralPath "$artifact.asc")) {
                throw "Required Maven artefact or signature is missing: $artifact."
            }
            & $GpgCommand --homedir $gpgHome --batch --verify (Convert-GpgPath "$artifact.asc") (Convert-GpgPath $artifact)
            if ($LASTEXITCODE) { throw "Signature verification failed: $artifact." }
            $verified++
        }
    }
    # Prove that verification detects changed bytes rather than merely finding a signature.
    $tampered = Join-Path $keyring 'tampered.jar'
    [IO.File]::WriteAllText($tampered, 'Deliberately changed development test bytes.')
    & $GpgCommand --homedir $gpgHome --batch --verify (Convert-GpgPath "$artifact.asc") (Convert-GpgPath $tampered) 2>$null
    if ($LASTEXITCODE -eq 0) { throw 'The verifier accepted a tampered artefact.' }
    & $GpgCommand --homedir $gpgHome --batch --yes -a --output (Convert-GpgPath (Join-Path $repository 'DEVELOPMENT-ONLY-public-key.asc')) --export $fingerprint
    if ($LASTEXITCODE) { throw 'Development public-key export failed.' }
    & $GradleCommand -p (Join-Path $PSScriptRoot 'maven-consumer') --no-daemon run
    if ($LASTEXITCODE) { throw 'The independent signed-package consumer failed.' }
    Write-Output "Verified $verified required Maven signatures; rejected tampered bytes; independent consumer passed. Development key only."
} finally {
    $gpgExecutable = (Get-Command $GpgCommand).Source
    $configurationName = [IO.Path]::GetFileNameWithoutExtension($gpgExecutable) + 'conf'
    if ($IsWindows) { $configurationName += '.exe' }
    $gpgConfiguration = Join-Path (Split-Path $gpgExecutable) $configurationName
    if (Test-Path -LiteralPath $gpgConfiguration) {
        & $gpgConfiguration --homedir $gpgHome --kill gpg-agent
    }
    $env:GNUPGHOME = $previousGpgHome
    $resolved = [IO.Path]::GetFullPath($keyring)
    if (!$resolved.StartsWith($temporaryRoot, [StringComparison]::OrdinalIgnoreCase) -or
        !(Split-Path $resolved -Leaf).StartsWith('gk-dev-', [StringComparison]::Ordinal)) {
        throw 'Refusing cleanup outside the temporary development keyring.'
    }
    Remove-Item -LiteralPath $resolved -Recurse -Force
}
