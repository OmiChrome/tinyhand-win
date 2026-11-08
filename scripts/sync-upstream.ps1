<#
.SYNOPSIS
  Update the tinyhand/ submodule (the original nilbuild/tinyhand) to its latest commit and show
  what changed, so the changes can be ported. -CopyAssets also copies new or changed hands and sounds.

.EXAMPLE
  ./scripts/sync-upstream.ps1
  ./scripts/sync-upstream.ps1 -CopyAssets
#>
param([switch]$CopyAssets)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
Set-Location $root

$before = (git -C tinyhand rev-parse HEAD).Trim()
git submodule update --init --remote tinyhand
$after = (git -C tinyhand rev-parse HEAD).Trim()

if ($before -eq $after) {
    Write-Host "tinyhand is already at the latest commit ($($after.Substring(0, 7)))."
    return
}

Write-Host "tinyhand moved $($before.Substring(0, 7)) -> $($after.Substring(0, 7))`n"
git -C tinyhand log --oneline "$before..$after"
Write-Host "`nChanged files:"
git -C tinyhand diff --stat "$before..$after"

if ($CopyAssets) {
    $changed = git -C tinyhand diff --name-only "$before..$after" |
        Where-Object { $_ -like 'assets/hand_*.png' -or $_ -like 'assets/sounds/*.wav' -or $_ -like 'assets/menubar_*.png' }
    foreach ($file in $changed) {
        # The original's menu bar icons are the tray icons here.
        $dest = Join-Path $root ($file -replace 'menubar_', 'tray_')
        Copy-Item (Join-Path 'tinyhand' $file) $dest -Force
        Write-Host "copied $file"
    }
}

Write-Host "`nPort the changes in tinyhand/Sources to src/TinyHand, then commit the submodule bump."
