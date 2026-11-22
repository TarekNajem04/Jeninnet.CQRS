#
# Copyright (c) Jeninnet.
# Part of the Jeninnet Platform.
# Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
# Licensed under the MIT License.
#
param(
    [Parameter(Mandatory = $true)]
    [string[]]$Report,

    [string]$Output = "artifacts/mutation/dashboard.md"
)

$ErrorActionPreference = "Stop"
$rows = [System.Collections.Generic.List[object]]::new()
$summary = [System.Collections.Generic.List[object]]::new()

foreach($path in $Report) {
    if(-not (Test-Path $path)) {
        continue
    }

    $document = Get-Content $path -Raw | ConvertFrom-Json
    $name = Split-Path (Split-Path (Split-Path $path -Parent) -Parent) -Leaf
    $counts = @{ Killed = 0; Survived = 0; NoCoverage = 0; Timeout = 0; Ignored = 0; CompileError = 0 }

    foreach($fileProperty in $document.files.PSObject.Properties) {
        $file = $fileProperty.Name
        $shortFile = $file -replace [regex]::Escape((Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path + [IO.Path]::DirectorySeparatorChar), ""

        foreach($mutant in @($fileProperty.Value.mutants)) {
            if($counts.ContainsKey($mutant.status)) {
                $counts[$mutant.status]++
            }

            if($mutant.status -in @("Survived", "NoCoverage", "Timeout")) {
                $rows.Add([pscustomobject]@{
                    Config = $name
                    File = $shortFile
                    Line = $mutant.location.start.line
                    Mutator = $mutant.mutatorName
                    Status = $mutant.status
                })
            }
        }
    }

    # Stryker counts a timeout as a detected mutant (the test hung on the mutation).
    $detected = $counts.Killed + $counts.Timeout
    $tested = $detected + $counts.Survived + $counts.NoCoverage
    $score = if($tested -eq 0) { 0 } else { [math]::Round(($detected * 100.0) / $tested, 2) }

    $summary.Add([pscustomobject]@{
        Config = $name
        Killed = $counts.Killed
        Survived = $counts.Survived
        NoCoverage = $counts.NoCoverage
        Timeout = $counts.Timeout
        Ignored = $counts.Ignored
        CompileError = $counts.CompileError
        Score = $score
    })
}

$directory = Split-Path $Output -Parent

if($directory -and -not (Test-Path $directory)) {
    New-Item -ItemType Directory -Path $directory -Force | Out-Null
}

$grouped = $rows | Group-Object Mutator | Sort-Object Name
$lines = [System.Collections.Generic.List[string]]::new()
$lines.Add("# Mutation testing dashboard")
$lines.Add("")
$lines.Add("Generated: $([DateTimeOffset]::UtcNow.ToString("u"))")
$lines.Add("")
$lines.Add("## Mutation score")
$lines.Add("")
$lines.Add("| Config | Killed | Survived | NoCoverage | Timeout | Ignored | CompileError | Score |")
$lines.Add("|---|---:|---:|---:|---:|---:|---:|---:|")

foreach($entry in $summary) {
    $lines.Add("| $($entry.Config) | $($entry.Killed) | $($entry.Survived) | $($entry.NoCoverage) | $($entry.Timeout) | $($entry.Ignored) | $($entry.CompileError) | $($entry.Score)% |")
}

$lines.Add("")
$lines.Add("## Survivors by mutator")
$lines.Add("")
$lines.Add("| Mutator | Total |")
$lines.Add("|---|---:|")

foreach($group in $grouped) {
    $lines.Add("| $($group.Name) | $($group.Count) |")
}

$lines.Add("")
$lines.Add("## Survivor details")
$lines.Add("")

foreach($group in $grouped) {
    $lines.Add("### $($group.Name)")
    $lines.Add("")

    foreach($row in $group.Group | Sort-Object Config, File, Line) {
        $lines.Add("- ``$($row.Config)`` ``$($row.File):$($row.Line)`` — $($row.Status)")
    }

    $lines.Add("")
}

$target = if([IO.Path]::IsPathRooted($Output)) { $Output } else { Join-Path (Get-Location) $Output }
$jsonTarget = $target -replace '\.md$', '.json'
[IO.File]::WriteAllText($target, ($lines -join [Environment]::NewLine) + [Environment]::NewLine, [Text.UTF8Encoding]::new($true))
[IO.File]::WriteAllText($jsonTarget, ($summary | ConvertTo-Json -Depth 4), [Text.UTF8Encoding]::new($true))
Write-Host "Mutation dashboard written to $Output"
