# Flags banned vocabulary and banned name variants from docs/glossary.json.
# Exits 0 when nothing is flagged, 1 when something is flagged, and throws on a
# tool error such as a malformed glossary or a missing path. See README.md.
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string[]] $Path,
    [string] $GlossaryPath,
    [string[]] $Exclude = @()
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$here = $PSScriptRoot
$repositoryRoot = (Resolve-Path (Join-Path $here '../..')).Path

if (-not $GlossaryPath) {
    $GlossaryPath = Join-Path $repositoryRoot 'docs/glossary.json'
}

if (-not (Test-Path -LiteralPath $GlossaryPath -PathType Leaf)) {
    throw "Glossary '$GlossaryPath' does not exist."
}

$glossary = Get-Content -LiteralPath $GlossaryPath -Raw | ConvertFrom-Json

$validScopes = @('prose', 'identifiers', 'both')

function Assert-NotNull([object] $Value, [string] $Message) {
    if ($null -eq $Value) {
        throw $Message
    }
}

function Assert-Scope([string] $Value, [string] $Message) {
    if ($validScopes -notcontains $Value) {
        throw "$Message Found '$Value'; expected one of: $($validScopes -join ', ')."
    }
}

Assert-NotNull $glossary.name 'glossary.json is missing the top-level "name" field.'
Assert-NotNull $glossary.version 'glossary.json is missing the top-level "version" field.'
Assert-NotNull $glossary.terms 'glossary.json is missing the top-level "terms" array.'
Assert-NotNull $glossary.nameForms 'glossary.json is missing the top-level "nameForms" array.'
Assert-NotNull $glossary.bannedVariants 'glossary.json is missing the top-level "bannedVariants" array.'

$proseExtensions = @('.md', '.markdown', '.mdown', '.mkd', '.txt', '.rst', '.adoc', '.html', '.htm')
$identifierExtensions = @(
    '.cs', '.fs', '.vb', '.ts', '.tsx', '.js', '.jsx', '.mjs', '.cjs',
    '.java', '.kt', '.kts', '.py', '.pyi', '.go', '.rs', '.php', '.rb',
    '.swift', '.c', '.h', '.cc', '.cpp', '.hpp', '.json', '.toml', '.yaml',
    '.yml', '.xml', '.ps1', '.psm1', '.sh', '.bat', '.cmd', '.css', '.scss',
    '.sass', '.sql', '.proto', '.graphql', '.tf', '.csproj', '.fsproj',
    '.sln', '.props', '.targets'
)

# One rule object per checkable entry. Vocabulary terms are always matched
# case-insensitively; name variants honour their caseSensitive flag.
$rules = New-Object System.Collections.Generic.List[object]

foreach ($term in $glossary.terms) {
    Assert-NotNull $term.term 'A glossary term entry is missing its "term" field.'
    Assert-NotNull $term.definition 'A glossary term entry is missing its "definition" field.'
    Assert-NotNull $term.banned 'A glossary term entry is missing its "banned" array.'
    Assert-Scope $term.appliesTo "Term '$($term.term)' has an invalid appliesTo."
    foreach ($banned in $term.banned) {
        $rules.Add([pscustomobject]@{
            Text           = [string] $banned
            AppliesTo      = [string] $term.appliesTo
            CaseSensitive  = $false
            AllowedSuffixes = @()
            Kind           = 'term'
            Approved       = [string] $term.term
        })
    }
}

foreach ($variant in $glossary.bannedVariants) {
    Assert-NotNull $variant.variant 'A banned name variant entry is missing its "variant" field.'
    Assert-Scope $variant.appliesTo "Banned name variant '$($variant.variant)' has an invalid appliesTo."
    $caseSensitive = $false
    if ($null -ne $variant.PSObject.Properties['caseSensitive']) {
        $caseSensitive = [bool] $variant.caseSensitive
    }

    $suffixes = @()
    if ($null -ne $variant.PSObject.Properties['allowedSuffixes'] -and $null -ne $variant.allowedSuffixes) {
        $suffixes = @($variant.allowedSuffixes | ForEach-Object { [string] $_ })
    }

    $rules.Add([pscustomobject]@{
        Text           = [string] $variant.variant
        AppliesTo      = [string] $variant.appliesTo
        CaseSensitive  = $caseSensitive
        AllowedSuffixes = $suffixes
        Kind           = 'variant'
        Approved       = 'an allowed name form'
    })
}

function Get-RelativePath([string] $FullName, [string] $Root) {
    return [System.IO.Path]::GetRelativePath($Root, $FullName).Replace('\', '/')
}

function Test-Excluded([string] $Relative, [string[]] $Exclusions) {
    foreach ($entry in $Exclusions) {
        $normalised = ([string] $entry).Replace('\', '/').TrimEnd('/')
        if (-not $normalised) {
            continue
        }

        if ($Relative -eq $normalised) {
            return $true
        }

        if ($Relative.StartsWith($normalised + '/', [System.StringComparison]::Ordinal)) {
            return $true
        }
    }

    return $false
}

function Get-Context([string] $Extension) {
    $lower = $Extension.ToLowerInvariant()
    if ($proseExtensions -contains $lower) {
        return 'prose'
    }

    if ($identifierExtensions -contains $lower) {
        return 'identifiers'
    }

    return 'both'
}

function Test-RuleApplies([string] $AppliesTo, [string] $Context) {
    if ($AppliesTo -eq 'both' -or $Context -eq 'both') {
        return $true
    }

    return $AppliesTo -eq $Context
}

$findings = New-Object System.Collections.Generic.List[object]

foreach ($inputPath in $Path) {
    $resolved = (Resolve-Path -LiteralPath $inputPath).Path
    $files = New-Object System.Collections.Generic.List[object]
    if (Test-Path -LiteralPath $resolved -PathType Container) {
        Get-ChildItem -LiteralPath $resolved -Recurse -File | ForEach-Object {
            $files.Add([pscustomobject]@{ FullName = $_.FullName; Extension = $_.Extension })
        }
    }
    else {
        $files.Add([pscustomobject]@{ FullName = $resolved; Extension = [System.IO.Path]::GetExtension($resolved) })
    }

    foreach ($file in $files) {
        $relative = Get-RelativePath $file.FullName $repositoryRoot
        if (Test-Excluded $relative $Exclude) {
            continue
        }

        $context = Get-Context $file.Extension
        $lines = [System.IO.File]::ReadAllLines($file.FullName)

        for ($lineIndex = 0; $lineIndex -lt $lines.Count; $lineIndex++) {
            $line = $lines[$lineIndex]

            foreach ($rule in $rules) {
                if (-not (Test-RuleApplies $rule.AppliesTo $context)) {
                    continue
                }

                $comparison = if ($rule.CaseSensitive) { [System.StringComparison]::Ordinal } else { [System.StringComparison]::OrdinalIgnoreCase }
                $searchFrom = 0
                while ($searchFrom -le $line.Length) {
                    $index = $line.IndexOf($rule.Text, $searchFrom, $comparison)
                    if ($index -lt 0) {
                        break
                    }

                    $allowed = $false
                    if ($rule.AllowedSuffixes.Count -gt 0) {
                        $after = $index + $rule.Text.Length
                        $tail = if ($after -lt $line.Length) { $line.Substring($after) } else { '' }
                        foreach ($suffix in $rule.AllowedSuffixes) {
                            if ($tail.StartsWith($suffix, [System.StringComparison]::Ordinal)) {
                                $allowed = $true
                                break
                            }
                        }
                    }

                    if (-not $allowed) {
                        $findings.Add([pscustomobject]@{
                            Path   = $relative
                            Line   = $lineIndex + 1
                            Column = $index + 1
                            Text   = $rule.Text
                            Kind   = $rule.Kind
                            Approved = $rule.Approved
                        })
                    }

                    $searchFrom = $index + 1
                }
            }
        }
    }
}

$sorted = @($findings | Sort-Object -Property Path, Line, Column, Text)

foreach ($finding in $sorted) {
    if ($finding.Kind -eq 'term') {
        Write-Output "$($finding.Path):$($finding.Line):$($finding.Column): banned `"$($finding.Text)`"; use `"$($finding.Approved)`""
    }
    else {
        Write-Output "$($finding.Path):$($finding.Line):$($finding.Column): banned name variant `"$($finding.Text)`"; use $($finding.Approved)"
    }
}

if ($sorted.Count -gt 0) {
    exit 1
}

exit 0
