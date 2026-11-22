#
# Copyright (c) Jeninnet.
# Part of the Jeninnet Platform.
# Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
# Licensed under the MIT License.
#
param(
    [string]$Baseline = "docs/benchmarks/baseline.json",
    [string]$Current = "docs/benchmarks/current.json",
    [double]$Threshold = 0.20
)

$ErrorActionPreference = "Stop"

if(-not (Test-Path $Baseline)) {
    Write-Error "Baseline file not found: $Baseline"
    exit 1
}

if(-not (Test-Path $Current)) {
    Write-Error "Current benchmark file not found: $Current"
    exit 1
}

$baselineDocument = Get-Content $Baseline -Raw | ConvertFrom-Json
$currentDocument = Get-Content $Current -Raw | ConvertFrom-Json
$failures = [System.Collections.Generic.List[string]]::new()
$compared = 0

foreach($property in $baselineDocument.benchmarks.PSObject.Properties) {
    $name = $property.Name
    $baselineEntry = $property.Value
    $currentProperty = $currentDocument.benchmarks.PSObject.Properties[$name]

    if($null -eq $currentProperty) {
        continue
    }

    $currentEntry = $currentProperty.Value
    $compared++
    $limit = 1 + $Threshold

    if($baselineEntry.allocatedBytes -gt 0 -and $currentEntry.allocatedBytes -gt ($baselineEntry.allocatedBytes * $limit)) {
        $failures.Add("$name allocated $($currentEntry.allocatedBytes) B, baseline $($baselineEntry.allocatedBytes) B (> $([math]::Round($Threshold * 100))%).")
    }

    if($baselineEntry.ratioToBaseline -gt 0 -and $currentEntry.ratioToBaseline -gt ($baselineEntry.ratioToBaseline * $limit)) {
        $failures.Add("$name ratio $([math]::Round($currentEntry.ratioToBaseline, 3)), baseline $([math]::Round($baselineEntry.ratioToBaseline, 3)) (> $([math]::Round($Threshold * 100))%).")
    }
}

if($compared -eq 0) {
    Write-Error "No benchmarks were present in both files; the benchmark suite or filters are broken."
    exit 1
}

if($failures.Count -gt 0) {
    $failures | ForEach-Object { Write-Host "REGRESSION: $_" -ForegroundColor Red }
    Write-Error "$($failures.Count) benchmark regression(s) exceeded the $([math]::Round($Threshold * 100))% threshold."
    exit 1
}

Write-Host "Benchmark comparison passed for $compared benchmark(s) at a $([math]::Round($Threshold * 100))% threshold."