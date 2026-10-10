$ErrorActionPreference = "Stop"
$root = $PSScriptRoot

& (Join-Path $root "pack.ps1")

$propsText = Get-Content -LiteralPath (Join-Path $root "Directory.Build.props") -Raw -Encoding UTF8
if ($propsText -notmatch '<Version>([^<]+)</Version>') { throw "Version is missing from Directory.Build.props" }
$version = $Matches[1].Trim()
$stage = Join-Path $root "dist\ptcgl-fast-startup-$version"
if (-not (Test-Path -LiteralPath $stage)) { throw "release layout was not built" }

$game = Get-ChildItem "C:\Users\yangyuhan" -Directory | Where-Object { $_.Name -like "The Pok*" } | ForEach-Object {
    Get-ChildItem -LiteralPath $_.FullName -Directory | Where-Object { $_.Name -like "Pok*" }
} | Select-Object -First 1 -ExpandProperty FullName
if (-not $game) { throw "game directory not found" }
Write-Host "Installing into $game"

Get-Process | Where-Object { $_.ProcessName -like "*Pokemon TCG*" } | Stop-Process -Force
Start-Sleep -Seconds 1

Get-ChildItem -LiteralPath $stage -Force | Where-Object { $_.Name -ne "README.txt" } | ForEach-Object {
    Copy-Item -LiteralPath $_.FullName -Destination (Join-Path $game $_.Name) -Recurse -Force
}

# CodeRedeem's doorstop loads FastStartupBootstrap, then starts BepInEx.
# Point Doorstop at it when that DLL is already in the game folder.
$redeemDoorstop = Join-Path $game "FastStartup\PtcglCodeRedeem.Doorstop.dll"
if (Test-Path -LiteralPath $redeemDoorstop) {
    $iniPath = Join-Path $game "doorstop_config.ini"
    $ini = [System.IO.File]::ReadAllText($iniPath)
    $updated = [regex]::Replace($ini, '(?m)^target_assembly\s*=.*$', 'target_assembly = FastStartup\PtcglCodeRedeem.Doorstop.dll')
    if ($updated -eq $ini) { throw "doorstop_config.ini has no target_assembly setting" }
    $utf8 = New-Object System.Text.UTF8Encoding $false
    [System.IO.File]::WriteAllText($iniPath, $updated, $utf8)
    Write-Host "Doorstop target is PtcglCodeRedeem.Doorstop.dll so FastStartup and CodeRedeem both load."
}

Write-Host "Installed $version. Launch the game and read FastStartup.log next to the exe."
