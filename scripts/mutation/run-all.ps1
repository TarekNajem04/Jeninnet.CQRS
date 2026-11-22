#
# Copyright (c) Jeninnet.
# Part of the Jeninnet Platform.
# Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
# Licensed under the MIT License.
#
param(
    [string]$ArtifactsDirectory = "artifacts/mutation"
)

$ErrorActionPreference = "Stop"
$repo = Resolve-Path (Join-Path $PSScriptRoot "..\..")
$runs = @(
    @{ Directory = "tests/Jeninnet.CQRS.Tests"; Config = "stryker-config.core.json"; Name = "core" },
    @{ Directory = "tests/Jeninnet.CQRS.Tests"; Config = "stryker-config.resilience.json"; Name = "resilience" },
    @{ Directory = "tests/Jeninnet.CQRS.Tests"; Config = "stryker-config.idempotency.json"; Name = "idempotency" },
    @{ Directory = "tests/Jeninnet.CQRS.Tests"; Config = "stryker-config.testing.json"; Name = "testing" },
    @{ Directory = "tests/Jeninnet.CQRS.Sagas.Tests"; Config = "stryker-config.sagas.json"; Name = "sagas" }
)
$failures = [System.Collections.Generic.List[string]]::new()
$reports = [System.Collections.Generic.List[string]]::new()

foreach($run in $runs) {
    $output = Join-Path $repo (Join-Path $ArtifactsDirectory $run.Name)
    $report = Join-Path $output "reports/mutation-report.json"
    $reports.Add($report)

    Push-Location (Join-Path $repo $run.Directory)
    try {
        Write-Host "=== Mutation: $($run.Name) ==="
        & dotnet stryker -f $run.Config -O $output --skip-version-check -V info

        if($LASTEXITCODE -ne 0) {
            $failures.Add("$($run.Name) exited with $LASTEXITCODE (mutation score below its floor).")
        }
    }
    finally {
        Pop-Location
    }
}

$dashboard = Join-Path $repo (Join-Path $ArtifactsDirectory "dashboard.md")
& (Join-Path $PSScriptRoot "summarize.ps1") -Report $reports.ToArray() -Output $dashboard

if($failures.Count -gt 0) {
    $failures | ForEach-Object { Write-Error $_ }
    exit 1
}

Write-Host "All mutation runs passed their configured floors."
exit 0
