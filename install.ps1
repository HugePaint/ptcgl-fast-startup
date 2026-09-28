$ErrorActionPreference = "Stop"
$root = $PSScriptRoot
$zipUrl = "https://builds.bepinex.dev/projects/bepinex_be/788/BepInEx-Unity.Mono-win-x64-6.0.0-be.788%2B5b766a3.zip"
$dist = Join-Path $root "dist"
$zip = Join-Path $dist "BepInEx-Unity.Mono-win-x64.zip"
$stage = Join-Path $dist "bepinex"
New-Item -ItemType Directory -Force -Path $dist | Out-Null

if (-not (Test-Path $zip)) {
    Write-Host "Downloading Doorstop from the BepInEx 6.0.0-be.788 package"
    Invoke-WebRequest -Uri $zipUrl -OutFile $zip
}

if (-not (Test-Path (Join-Path $stage "winhttp.dll"))) {
    if (Test-Path $stage) {
        Remove-Item -LiteralPath $stage -Recurse -Force
    }
    Expand-Archive -LiteralPath $zip -DestinationPath $stage -Force
}

dotnet build (Join-Path $root "PtcglFastStartup\PtcglFastStartup.csproj") -c Release
if ($LASTEXITCODE -ne 0) { throw "plugin build failed" }
dotnet build (Join-Path $root "FastStartupBootstrap\FastStartupBootstrap.csproj") -c Release
if ($LASTEXITCODE -ne 0) { throw "bootstrap build failed" }

$game = Get-ChildItem "C:\Users\yangyuhan" -Directory | Where-Object { $_.Name -like "The Pok*" } | ForEach-Object {
    Get-ChildItem -LiteralPath $_.FullName -Directory | Where-Object { $_.Name -like "Pok*" }
} | Select-Object -First 1 -ExpandProperty FullName
if (-not $game) { throw "game directory not found" }
Write-Host "Installing into $game"

Get-Process | Where-Object { $_.ProcessName -like "*Pokemon TCG*" } | Stop-Process -Force
Start-Sleep -Seconds 1

Copy-Item -LiteralPath (Join-Path $stage "winhttp.dll") -Destination (Join-Path $game "winhttp.dll") -Force
Copy-Item -LiteralPath (Join-Path $stage ".doorstop_version") -Destination (Join-Path $game ".doorstop_version") -Force

@"
# General options for Unity Doorstop
[General]

enabled = true

# Unity 6.0.3 mono has no imageOpen export, so this must not be the BepInEx preloader.
target_assembly = FastStartup\FastStartupBootstrap.dll

redirect_output_log = false
boot_config_override =
ignore_disable_switch = false

[UnityMono]

dll_search_path_override = "FastStartup"
debug_enabled = false
debug_address = 127.0.0.1:10000
debug_suspend = false
"@ | Set-Content -LiteralPath (Join-Path $game "doorstop_config.ini") -Encoding ASCII

$fast = Join-Path $game "FastStartup"
New-Item -ItemType Directory -Force -Path $fast | Out-Null
Copy-Item -LiteralPath (Join-Path $root "PtcglFastStartup\bin\Release\netstandard2.1\PtcglFastStartup.dll") -Destination (Join-Path $fast "PtcglFastStartup.dll") -Force
Copy-Item -LiteralPath (Join-Path $root "FastStartupBootstrap\bin\Release\netstandard2.1\FastStartupBootstrap.dll") -Destination (Join-Path $fast "FastStartupBootstrap.dll") -Force

$core = Join-Path $stage "BepInEx\core"
foreach ($name in @(
    "0Harmony.dll",
    "MonoMod.RuntimeDetour.dll",
    "MonoMod.Utils.dll",
    "Mono.Cecil.dll",
    "Mono.Cecil.Mdb.dll",
    "Mono.Cecil.Pdb.dll",
    "Mono.Cecil.Rocks.dll"
)) {
    Copy-Item -LiteralPath (Join-Path $core $name) -Destination (Join-Path $fast $name) -Force
}

Write-Host "Installed. Launch the game and read FastStartup.log next to the exe."
