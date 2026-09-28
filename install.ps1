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

Write-Host "Installed $version. Launch the game and read FastStartup.log next to the exe."
