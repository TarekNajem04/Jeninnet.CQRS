#
# Copyright (c) Jeninnet.
# Part of the Jeninnet Platform.
# Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
# Licensed under the MIT License.
#
param(
    [Parameter(Mandatory = $true)]
    [string]$Version,

    [string]$PackagesDirectory = "artifacts/packages",
    [int]$ExpectedPackageCount = 31,
    [int]$ExpectedSymbolCount = 0
)

$ErrorActionPreference = "Stop"

if(-not (Test-Path $PackagesDirectory)) {
    Write-Error "Packages directory not found: $PackagesDirectory"
    exit 1
}

$packages = @(Get-ChildItem $PackagesDirectory -Filter "*.nupkg" -File)
$symbols = @(Get-ChildItem $PackagesDirectory -Filter "*.snupkg" -File)

if($packages.Count -ne $ExpectedPackageCount) {
    Write-Error "Expected $ExpectedPackageCount packages, found $($packages.Count)."
    exit 1
}

if($ExpectedSymbolCount -gt 0 -and $symbols.Count -ne $ExpectedSymbolCount) {
    Write-Error "Expected $ExpectedSymbolCount symbol packages, found $($symbols.Count)."
    exit 1
}

if($symbols.Count -eq 0) {
    Write-Error "No symbol packages were produced."
    exit 1
}

$empty = @($packages + $symbols | Where-Object { $_.Length -eq 0 })
if($empty.Count -gt 0) {
    $empty | ForEach-Object { Write-Host "EMPTY: $($_.Name)" -ForegroundColor Red }
    Write-Error "$($empty.Count) empty package(s)."
    exit 1
}

$expected = $Version.Trim()
$expectedNormalized = if($expected -match '^(\d+\.\d+\.\d+)\.0$') { $matches[1] } else { $expected }
$failures = [System.Collections.Generic.List[string]]::new()

foreach($package in $packages) {
    # NuGet file names are <id>.<version>.nupkg; the version can be normalized
    # (1.0.0.0 becomes 1.0.0) and may carry a prerelease suffix. Compare the
    # version suffix, not the id segments.
    $match = [regex]::Match($package.BaseName, '\.(?<version>\d+\.\d+\.\d+(?:\.\d+)?(?:-[0-9A-Za-z.-]+)?)$')

    if(-not $match.Success) {
        $failures.Add("$($package.Name) has no recognizable version suffix.")
        continue
    }

    $fileVersion = $match.Groups["version"].Value
    $fileNormalized = if($fileVersion -match '^(\d+\.\d+\.\d+)\.0$') { $matches[1] } else { $fileVersion }

    if($fileNormalized -ne $expectedNormalized) {
        $failures.Add("$($package.Name) has version $fileVersion, expected $Version.")
    }
}

if($failures.Count -gt 0) {
    $failures | ForEach-Object { Write-Host "VERSION: $_" -ForegroundColor Red }
    Write-Error "$($failures.Count) package version mismatch(es)."
    exit 1
}

Write-Host "Verified $($packages.Count) packages at version $Version ($($symbols.Count) symbol packages)."
exit 0
