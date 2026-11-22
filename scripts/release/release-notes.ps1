#
# Copyright (c) Jeninnet.
# Part of the Jeninnet Platform.
# Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
# Licensed under the MIT License.
#
param(
    [Parameter(Mandatory = $true)]
    [string]$Version,

    [string]$Changelog = "CHANGELOG.md",
    [string]$Output = "artifacts/release-notes.md"
)

$ErrorActionPreference = "Stop"

if(-not (Test-Path $Changelog)) {
    Write-Error "Changelog not found: $Changelog"
    exit 1
}

$content = Get-Content $Changelog -Raw
$escaped = [regex]::Escape($Version)
$pattern = "(?ms)^## \[?$escaped\]?[^\r\n]*\r?\n(?<body>.*?)(?=^## |\z)"
$match = [regex]::Match($content, $pattern)

if(-not $match.Success) {
    # First release: the prepared section may still be named Unreleased.
    $match = [regex]::Match($content, "(?ms)^## Unreleased[^\r\n]*\r?\n(?<body>.*?)(?=^## |\z)")

    if(-not $match.Success) {
        Write-Error "No CHANGELOG section found for version $Version (and no Unreleased section)."
        exit 1
    }
}

$notes = "# v$Version`n`n" + $match.Groups["body"].Value.Trim() + "`n"
$directory = Split-Path $Output -Parent

if(-not [string]::IsNullOrEmpty($directory)) {
    New-Item -ItemType Directory -Force -Path $directory | Out-Null
}

Set-Content -Path $Output -Value $notes -Encoding utf8NoBOM
Write-Host "Release notes written to $Output"
exit 0
