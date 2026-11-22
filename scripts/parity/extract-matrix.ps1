#
# Copyright (c) Jeninnet.
# Part of the Jeninnet Platform.
# Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
# Licensed under the MIT License.
#
param(
    [string]$MatrixPath,
    [string]$IndexPath,
    [int]$ExpectedRows = 78,
    [switch]$SelfTest
)

$ErrorActionPreference = "Stop"

# The authoritative matrix and task ledger are maintainer-only files and are not
# part of the public snapshot. Pass their paths explicitly; the self-test uses
# in-memory fixtures and needs no repository data.
if(-not $SelfTest) {
    if(-not $MatrixPath) {
        throw "Supply -MatrixPath with the local parity matrix to audit."
    }

    if(-not $IndexPath) {
        throw "Supply -IndexPath with the local task ledger to audit."
    }
}

function Read-MatrixRows {
    param([string]$Path)

    if(-not (Test-Path -LiteralPath $Path)) {
        throw "Parity matrix not found: $Path"
    }

    $rows = @{}
    $inMatrix = $false
    $lineNumber = 0

    foreach($line in Get-Content -LiteralPath $Path) {
        $lineNumber++

        if($line -match '^##\s+1\.') {
            $inMatrix = $true
            continue
        }

        if($line -match '^##\s+2\.') {
            $inMatrix = $false
        }

        if(-not $inMatrix -or $line -notmatch '^\|\s*(?<number>\d+)\s*\|') {
            continue
        }

        $parts = $line -split '\|'

        # Section 1 rows have #, feature, ours, and four peer columns. Section 2 gap
        # tables have fewer columns and are intentionally ignored.
        if($parts.Count -lt 9) {
            continue
        }

        $number = [int]$Matches["number"]

        if($rows.Contains($number)) {
            throw "Duplicate matrix row $number at line $lineNumber."
        }

        $rows[$number] = [pscustomobject]@{
            Number = $number
            Feature = $parts[2].Trim()
            Status = $parts[3].Trim()
            Line = $lineNumber
        }
    }

    return $rows
}

function Read-TaskRows {
    param([string]$Path)

    if(-not (Test-Path -LiteralPath $Path)) {
        throw "Task ledger not found: $Path"
    }

    $tasks = [System.Collections.Generic.List[object]]::new()

    foreach($line in Get-Content -LiteralPath $Path) {
        if($line -notmatch '^\|\s*`(?<task>CQRS-\d+)`\s*\|') {
            continue
        }

        $parts = $line -split '\|'

        if($parts.Count -lt 7) {
            continue
        }

        $tasks.Add([pscustomobject]@{
            Task = $Matches["task"]
            Title = $parts[2].Trim()
            Status = $parts[4].Trim()
            MatrixCell = $parts[5].Trim()
        })
    }

    return @($tasks)
}

function Expand-MatrixCell {
    param([string]$Cell)

    $numbers = [System.Collections.Generic.HashSet[int]]::new()

    # Parenthesized notes (for example "34 (superseded by 53/54)") describe history,
    # not additional covered rows.
    $normalized = [regex]::Replace($Cell, '\([^)]*\)', '')

    foreach($match in [regex]::Matches($normalized, '(?<start>\d+)\s*[–—-]\s*(?<end>\d+)|(?<single>\d+)')) {
        if($match.Groups["single"].Success) {
            [void]$numbers.Add([int]$match.Groups["single"].Value)
            continue
        }

        $start = [int]$match.Groups["start"].Value
        $end = [int]$match.Groups["end"].Value

        for($number = [Math]::Min($start, $end); $number -le [Math]::Max($start, $end); $number++) {
            [void]$numbers.Add($number)
        }
    }

    return $numbers
}

function Measure-ParityCoverage {
    param(
        [string]$Matrix,
        [string]$Ledger,
        [int]$Rows
    )

    $matrixRows = Read-MatrixRows -Path $Matrix
    $tasks = Read-TaskRows -Path $Ledger

    if($matrixRows.Count -ne $Rows) {
        throw "Expected $Rows matrix rows, found $($matrixRows.Count)."
    }

    $coveredByTask = @{}
    $invalidReferences = [System.Collections.Generic.List[string]]::new()

    foreach($task in $tasks) {
        $numbers = Expand-MatrixCell -Cell $task.MatrixCell

        foreach($number in $numbers) {
            if($number -lt 1 -or $number -gt $Rows) {
                $invalidReferences.Add("$($task.Task) references matrix row $number outside 1..$Rows.")
                continue
            }

            if(-not $coveredByTask.ContainsKey($number)) {
                $coveredByTask[$number] = [System.Collections.Generic.List[string]]::new()
            }

            $coveredByTask[$number].Add($task.Task)
        }
    }

    $shipped = 0
    $tracked = [System.Collections.Generic.List[object]]::new()
    $uncovered = [System.Collections.Generic.List[object]]::new()

    for($number = 1; $number -le $Rows; $number++) {
        if(-not $matrixRows.Contains($number)) {
            throw "Matrix row $number is missing."
        }

        $row = $matrixRows[$number]

        if($row.Status -match '●') {
            $shipped++
            continue
        }

        if($coveredByTask.ContainsKey($number)) {
            $tracked.Add([pscustomobject]@{
                Row = $row
                Tasks = @($coveredByTask[$number])
            })
            continue
        }

        $uncovered.Add($row)
    }

    return [pscustomobject]@{
        Total = $matrixRows.Count
        Shipped = $shipped
        Tracked = $tracked
        Uncovered = $uncovered
        InvalidReferences = $invalidReferences
    }
}

function Write-CoverageResult {
    param($Result)

    Write-Host "Parity matrix: $($Result.Total) rows"
    Write-Host "Shipped (●): $($Result.Shipped)"
    Write-Host "Tracked gaps (non-● with task): $($Result.Tracked.Count)"

    foreach($tracked in $Result.Tracked) {
        Write-Host "  row $($tracked.Row.Number): $($tracked.Tasks -join ', ') — $($tracked.Row.Feature)"
    }

    if($Result.InvalidReferences.Count -gt 0) {
        $Result.InvalidReferences | ForEach-Object { Write-Host "INVALID: $_" -ForegroundColor Red }
        Write-Host "$($Result.InvalidReferences.Count) invalid task reference(s)." -ForegroundColor Red
        exit 1
    }

    if($Result.Uncovered.Count -gt 0) {
        $Result.Uncovered | ForEach-Object {
            Write-Host "UNCOVERED row $($_.Number): $($_.Feature) (line $($_.Line))" -ForegroundColor Red
        }

        Write-Host "$($Result.Uncovered.Count) uncovered rows" -ForegroundColor Red
        exit 1
    }

    Write-Host "Uncovered rows: 0"
    Write-Host "0 uncovered rows" -ForegroundColor Green
}

function Invoke-SelfTest {
    $temporaryRoot = Join-Path ([System.IO.Path]::GetTempPath()) ("jeninnet-parity-" + [Guid]::NewGuid().ToString("N"))
    New-Item -ItemType Directory -Path $temporaryRoot | Out-Null

    try {
        $matrixHeader = @(
            "## 1. Full feature matrix (3 features, fixtures)"
            "| # | Feature | Jeninnet.CQRS | A | B | C | D |"
            "|---|---|---|---|---|---|---|"
        )
        $matrixFooter = @(
            ""
            "## 2. Closure ledger"
        )
        $ledgerHeader = @(
            "| Task | Title | Group | Status | Matrix rows |"
            "|---|---|---|---|---|"
        )

        $allShipped = Join-Path $temporaryRoot "all-shipped.md"
        @($matrixHeader + @("| 1 | One | ● | ● | ● | ● | ● |", "| 2 | Two | ● | ● | ● | ● | ● |", "| 3 | Three | ● | ● | ● | ● | ● |") + $matrixFooter) |
            Set-Content -LiteralPath $allShipped -Encoding utf8
        $emptyLedger = Join-Path $temporaryRoot "empty-ledger.md"
        $ledgerHeader | Set-Content -LiteralPath $emptyLedger -Encoding utf8

        $uncovered = Join-Path $temporaryRoot "uncovered.md"
        @($matrixHeader + @("| 1 | One | ● | ● | ● | ● | ● |", "| 2 | Two | ● | ● | ● | ● | ● |", "| 3 | Three | ○ | ● | ● | ● | ● |") + $matrixFooter) |
            Set-Content -LiteralPath $uncovered -Encoding utf8

        $coveredLedger = Join-Path $temporaryRoot "covered-ledger.md"
        @($ledgerHeader + @('| `CQRS-99` | Fixture gap | `07-Quality` | `TODO` | 3 |')) |
            Set-Content -LiteralPath $coveredLedger -Encoding utf8

        $result = Measure-ParityCoverage -Matrix $allShipped -Ledger $emptyLedger -Rows 3

        if($result.Uncovered.Count -ne 0 -or $result.Shipped -ne 3) {
            throw "Self-test failed: an all-shipped matrix must have 0 uncovered rows."
        }

        $result = Measure-ParityCoverage -Matrix $uncovered -Ledger $emptyLedger -Rows 3

        if($result.Uncovered.Count -ne 1 -or $result.Uncovered[0].Number -ne 3) {
            throw "Self-test failed: an untracked non-● row must be reported as uncovered."
        }

        $result = Measure-ParityCoverage -Matrix $uncovered -Ledger $coveredLedger -Rows 3

        if($result.Uncovered.Count -ne 0 -or $result.Tracked.Count -ne 1) {
            throw "Self-test failed: a non-● row with a task must be tracked, not uncovered."
        }

        Write-Host "Parity checker self-test passed: shipped, uncovered, and tracked fixtures." -ForegroundColor Green
    }
    finally {
        Remove-Item -LiteralPath $temporaryRoot -Recurse -Force -ErrorAction SilentlyContinue
    }
}

if($SelfTest) {
    Invoke-SelfTest
    exit 0
}

$result = Measure-ParityCoverage -Matrix $MatrixPath -Ledger $IndexPath -Rows $ExpectedRows
Write-CoverageResult -Result $result