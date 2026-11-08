<#
.SYNOPSIS
  Update the upstream/ submodule to the latest nilbuild/tinyhand and show what changed,
  so Swift changes can be ported. Optionally copies new or changed hand images and sounds.

.EXAMPLE
  ./scripts/sync-upstream.ps1             # update and list changes
  ./scripts/sync-upstream.ps1 -CopyAssets # also copy changed hands/sounds into assets/
#>
param([switch]$CopyAssets)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
Set-Location $root

$before = (git -C upstream rev-parse HEAD).Trim()
git submodule update --init --remote upstream
$after = (git -C upstream rev-parse HEAD).Trim()

if ($before -eq $after) {
    Write-Host "upstream is already at the latest commit ($($after.Substring(0, 7)))."
    return
}

Write-Host "upstream moved $($before.Substring(0, 7)) -> $($after.Substring(0, 7))`n"
git -C upstream log --oneline "$before..$after"
Write-Host "`nChanged files:"
git -C upstream diff --stat "$before..$after"

if ($CopyAssets) {
    $changed = git -C upstream diff --name-only "$before..$after" |
        Where-Object { $_ -like 'assets/hand_*.png' -or $_ -like 'assets/sounds/*.wav' -or $_ -like 'assets/menubar_*.png' }
    foreach ($file in $changed) {
        $src = Join-Path 'upstream' $file
        if (Test-Path $src) {
            Copy-Item $src (Join-Path $root $file) -Force
            Write-Host "copied $file"
        }
    }
}

Write-Host "`nReview the Swift changes in upstream/Sources and port them to src/TinyHand, then commit the submodule bump."
