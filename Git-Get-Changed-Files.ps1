#
# Copyright (c) Jeninnet.
# Part of the Jeninnet Platform.
# Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
# Licensed under the MIT License.
#
#=======================
#Author:	Tarek Najem
#Date:		26-07-2024
#=======================

$TotalNumberVersionsToKeep = 100
$BackupFolder = "..\DevOps-Git-Stages\Jeninnet.CQRS"
$ProjectFolder = [System.Environment]::CurrentDirectory
$ProjectFolderName = [System.IO.Path]::GetFileName($ProjectFolder)

# Ensure backup folder exists
If (!(Test-Path $BackupFolder)) {
    New-Item -ItemType Directory -Force -Path $BackupFolder | Out-Null
}

Push-Location -Path $ProjectFolder

# Get changed or untracked files
$changes = CMD.EXE /C git ls-files --modified --others --exclude-standard | Out-String
# Get deleted files
$deletedFiles = CMD.EXE /C git ls-files --deleted | Out-String

Write-Host "Changed files raw output:`n$changes"
Write-Host "Deleted files raw output:`n$deletedFiles"

Write-Host "Backup folder:$BackupFolder"

$changedFilesList = $changes -split '[\r\n]' | Where-Object { $_.Trim() }
$deletedFilesList = $deletedFiles -split '[\r\n]' | Where-Object { $_.Trim() }
# Remove deleted files from the list of changed files
$changedFilesList = $changedFilesList | Where-Object { $deletedFilesList -notcontains $_ }

Write-Host "`nChanged files after remove deleted files raw output:`n$changes"

Pop-Location

if ($changedFilesList.Count -eq 0 -and $deletedFilesList.Count -eq 0) {
    Write-Host "No changed or deleted files detected. Exiting."
    Exit
}

Push-Location -Path $BackupFolder

# Find backup directories matching the version pattern
$dirs = Get-ChildItem -Directory | Where-Object { $_.Name -match "^$ProjectFolderName\.\d{4}$" } | Sort-Object -Property Name -Descending

foreach ($d in $dirs) {
    $ext = [int32]::Parse([System.IO.Path]::GetExtension($d.Name).Substring(1))
    if ($ext -ge $TotalNumberVersionsToKeep) {
        Remove-Item -Recurse -Force $d
        continue
    }
    $newName = $d.Name.Substring(0, $d.Name.Length - 4) + "{0:D4}" -f ($ext + 1)
    Rename-Item $d -NewName $newName
}

# Create new backup directory for this version
#$newBackupDir = Join-Path $BackupFolder ("$ProjectFolderName.0000")
$newBackupDir = "$ProjectFolderName.0000"
Write-Host "New Backup folder: $newBackupDir"

if (!(Test-Path $newBackupDir)) {
    Write-Host "Backup folder created."
    New-Item -ItemType Directory -Path $newBackupDir | Out-Null
}

Write-Host "Start copy each changed file to the new backup directory process: " $changedFilesList.Count

# Copy each changed file to the new backup directory
foreach ($file in $changedFilesList) {
    $from = Join-Path $ProjectFolder $file
    $to = Join-Path $newBackupDir $file
    $toDir = [System.IO.Path]::GetDirectoryName($to)

    if (!(Test-Path $toDir)) {
        New-Item -ItemType Directory -Path $toDir -Force | Out-Null
    }

    Write-Host "Copy-Item " $from " Destination " $toDir
    Copy-Item $from -Destination $to -Force
}

# Create a batch file to delete deleted files from the backup
if ($deletedFilesList.Count -gt 0) {
    $batchFile = Join-Path $newBackupDir "delete_deleted_files.bat"
    Set-Content -Path $batchFile -Value "@echo off"
    foreach ($file in $deletedFilesList) {
        $backupPath = "$ProjectFolder\$file" -replace "/", "\"
        Add-Content -Path $batchFile -Value "del `"$backupPath`""
    }
}

Pop-Location