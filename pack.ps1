$ErrorActionPreference = "Stop"
$root = $PSScriptRoot
$zipUrl = "https://builds.bepinex.dev/projects/bepinex_be/788/BepInEx-Unity.Mono-win-x64-6.0.0-be.788%2B5b766a3.zip"
$dist = Join-Path $root "dist"
$doorstopZip = Join-Path $dist "BepInEx-Unity.Mono-win-x64.zip"
$doorstop = Join-Path $dist "bepinex"

$propsText = Get-Content -LiteralPath (Join-Path $root "Directory.Build.props") -Raw -Encoding UTF8
if ($propsText -notmatch '<Version>([^<]+)</Version>') { throw "Version is missing from Directory.Build.props" }
$version = $Matches[1].Trim()

New-Item -ItemType Directory -Force -Path $dist | Out-Null

if (-not (Test-Path -LiteralPath $doorstopZip)) {
    Write-Host "Downloading Doorstop from the BepInEx 6.0.0-be.788 package"
    Invoke-WebRequest -Uri $zipUrl -OutFile $doorstopZip
}

if (-not (Test-Path -LiteralPath (Join-Path $doorstop "winhttp.dll"))) {
    if (Test-Path -LiteralPath $doorstop) {
        Remove-Item -LiteralPath $doorstop -Recurse -Force
    }
    Expand-Archive -LiteralPath $doorstopZip -DestinationPath $doorstop -Force
}

dotnet build (Join-Path $root "PtcglFastStartup\PtcglFastStartup.csproj") -c Release
if ($LASTEXITCODE -ne 0) { throw "plugin build failed" }
dotnet build (Join-Path $root "FastStartupBootstrap\FastStartupBootstrap.csproj") -c Release
if ($LASTEXITCODE -ne 0) { throw "bootstrap build failed" }

$name = "ptcgl-fast-startup-$version"
$stage = Join-Path $dist $name
if (Test-Path -LiteralPath $stage) {
    Remove-Item -LiteralPath $stage -Recurse -Force
}
$fast = Join-Path $stage "FastStartup"
New-Item -ItemType Directory -Force -Path $fast | Out-Null

Copy-Item -LiteralPath (Join-Path $doorstop "winhttp.dll") -Destination (Join-Path $stage "winhttp.dll")
Copy-Item -LiteralPath (Join-Path $doorstop ".doorstop_version") -Destination (Join-Path $stage ".doorstop_version")
Copy-Item -LiteralPath (Join-Path $root "pack\doorstop_config.ini") -Destination (Join-Path $stage "doorstop_config.ini")
Copy-Item -LiteralPath (Join-Path $root "PtcglFastStartup\bin\Release\netstandard2.1\PtcglFastStartup.dll") -Destination (Join-Path $fast "PtcglFastStartup.dll")
Copy-Item -LiteralPath (Join-Path $root "FastStartupBootstrap\bin\Release\netstandard2.1\FastStartupBootstrap.dll") -Destination (Join-Path $fast "FastStartupBootstrap.dll")
Set-Content -LiteralPath (Join-Path $fast "version.txt") -Value $version -Encoding ASCII -NoNewline

$core = Join-Path $doorstop "BepInEx\core"
foreach ($dll in @(
    "0Harmony.dll",
    "MonoMod.RuntimeDetour.dll",
    "MonoMod.Utils.dll",
    "Mono.Cecil.dll",
    "Mono.Cecil.Mdb.dll",
    "Mono.Cecil.Pdb.dll",
    "Mono.Cecil.Rocks.dll"
)) {
    Copy-Item -LiteralPath (Join-Path $core $dll) -Destination (Join-Path $fast $dll)
}

$readme = @"
PTCGL fast startup $version

Unzip this archive into the folder that contains the game executable.
These files should end up next to that executable:

  winhttp.dll
  doorstop_config.ini
  .doorstop_version
  FastStartup\

Launch the game. FastStartup.log is written next to the executable and records this version.

An empty FastStartup\optimize.off file skips the manifest, localization, and early shop-offerings patches. The loading-line text and setup timing logs stay on.
"@
Set-Content -LiteralPath (Join-Path $stage "README.txt") -Value $readme -Encoding UTF8

$archive = Join-Path $dist "$name.zip"
if (Test-Path -LiteralPath $archive) {
    Remove-Item -LiteralPath $archive -Force
}
Add-Type -AssemblyName System.IO.Compression.FileSystem
[System.IO.Compression.ZipFile]::CreateFromDirectory($stage, $archive)

Write-Host "Release $version"
Write-Host $archive
