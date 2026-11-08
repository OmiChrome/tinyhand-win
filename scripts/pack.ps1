<#
.SYNOPSIS
  Build TinyHand and package it with Velopack into Releases/ (Setup.exe, portable zip, update packages).

.EXAMPLE
  ./scripts/pack.ps1                 # version from VERSION
  ./scripts/pack.ps1 -Version 1.1.0  # override
#>
param(
    [string]$Version,
    [string]$Runtime = 'win-x64'
)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
Set-Location $root

if (-not $Version) { $Version = (Get-Content VERSION -Raw).Trim() }
$publish = Join-Path $root "publish/$Runtime"
$releases = Join-Path $root 'Releases'

Write-Host "Building TinyHand $Version ($Runtime)..."
if (Test-Path $publish) { Remove-Item $publish -Recurse -Force }
dotnet publish src/TinyHand -c Release -r $Runtime --self-contained false `
    -p:Version=$Version -p:PublishReadyToRun=true -o $publish
if ($LASTEXITCODE -ne 0) { throw 'dotnet publish failed' }

dotnet tool restore | Out-Null
Write-Host "Packing with Velopack..."
# The installer offers to install the .NET 10 Desktop Runtime if it's missing.
dotnet vpk pack `
    --packId TinyHand `
    --packVersion $Version `
    --packDir $publish `
    --mainExe TinyHand.exe `
    --packTitle TinyHand `
    --packAuthors 'OmiChrome' `
    --icon assets/AppIcon.ico `
    --framework net10.0-x64-desktop `
    --outputDir $releases
if ($LASTEXITCODE -ne 0) { throw 'vpk pack failed' }

Get-ChildItem $releases | Select-Object Name, @{ n = 'MB'; e = { [math]::Round($_.Length / 1MB, 1) } } | Format-Table
