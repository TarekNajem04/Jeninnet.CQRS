#
# Copyright (c) Jeninnet.
# Part of the Jeninnet Platform.
# Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
# Licensed under the MIT License.
#
param(
    [string]$Report = "artifacts/coverage/Cobertura.xml",
    [double]$Floor = -1,
    [string]$ConfigurationPath = (Join-Path $PSScriptRoot "coverage.config.json")
)

$ErrorActionPreference = "Stop"

if($Floor -lt 0) {
    $configuration = Get-Content $ConfigurationPath -Raw | ConvertFrom-Json
    $Floor = [double]$configuration.coverage.floor
}

if(-not (Test-Path $Report)) {
    Write-Error "Coverage report not found: $Report"
    exit 1
}

[xml]$coverage = Get-Content $Report -Raw
$rate = [double]$coverage.coverage.'line-rate'
$covered = [int]$coverage.coverage.'lines-covered'
$valid = [int]$coverage.coverage.'lines-valid'
$percent = [math]::Round($rate * 100, 2)
$floorPercent = [math]::Round($Floor * 100, 2)

Write-Host "Line coverage: $percent% ($covered/$valid), floor $floorPercent%"

if($valid -eq 0) {
    Write-Error "Coverage report contains no coverable lines; refusing to pass."
    exit 1
}

if($rate -lt $Floor) {
    Write-Error "Coverage $percent% is below the $floorPercent% floor."
    exit 1
}

Write-Host "Coverage gate passed."
exit 0
