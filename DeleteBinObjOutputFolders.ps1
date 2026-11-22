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

$foldersToDelete = @("bin", "obj", "output")

# Get the current folder path
$currentFolder = Get-Location

# Delete matching folders in current directory and subdirectories
Get-ChildItem -Path $currentFolder -Directory -Recurse | Where-Object { $foldersToDelete -contains $_.Name } | ForEach-Object {
    Write-Host "Deleting folder: $($_.FullName)"
    Remove-Item -Path $_.FullName -Recurse -Force
}

Write-Host "Cleanup complete."
