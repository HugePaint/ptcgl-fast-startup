# PTCGL fast startup

[中文](README.zh-CN.md)

Harmony patches for the Windows Pokémon TCG Live client. The release zip's `doorstop_config.ini` loads `FastStartup\FastStartupBootstrap.dll`, which applies the patches once `TPCI.RainierClient` loads. `install.ps1` points Doorstop at `FastStartup\PtcglCodeRedeem.Doorstop.dll` when that DLL is already in the game folder, so FastStartup and the code-redeem mod both load.

The loading line keeps the game's text and lists managers still inside `Initialize`. `FastStartup.log` is written next to the executable.

Version `1.0.0` lives in `Directory.Build.props`. Both assemblies use it, and `FastStartup.log` records it at startup. Tested on client `1.42.2.1247115` (Unity `6000.3.5`).

## Optimizations

**Asset manifests.** `AssetBundleSetup` downloads dated manifest buckets one after another, and loads each locale source the same way. The patch starts every bucket at once, and runs the locale source and the fallback source together. A bucket named `manifest_*_10101_*`, or a dated bucket from before today (UTC), gets a stable cache hash from the bundle path and the `asset-bundle-manifest` revision. A later launch reuses Unity's cache for those files. Today's bucket is left without a hash, so it is downloaded again.

**Localization gzip.** `LocalizationConfigManager` downloads one gzip per manifest directory, two at a time. This Mono `HttpClient` ignores `MaxConnectionsPerServer` and uses `ServicePointManager`, which also allows two connections per host. The patch raises `GZipQueueSize` and that connection limit to 16.

**Shop offerings.** After the client connects, `NetworkManager` waits for commerce, inventory, season rank, and gifts, then requests shop offerings. Offerings only need commerce setup, so the patch starts `GetShopOfferingsAsync` when `Commerce.Setup` finishes. The original call waits on that same task.

## Install

Windows, the game, and the .NET SDK.

```powershell
.\install.ps1
```

The script builds `dist\ptcgl-fast-startup-<version>.zip`, then copies that layout into the game directory it resolves. It stops a running game process before copying. Both projects reference `0Harmony.dll` from the Doorstop download, so build through this script.

## Release

```powershell
.\pack.ps1
```

This writes `dist\ptcgl-fast-startup-<version>.zip`. Unzip it into the folder that contains the game executable. `winhttp.dll`, `doorstop_config.ini`, `.doorstop_version`, and `FastStartup\` land next to that executable. The zip already contains Doorstop and the Harmony dependencies, so the game can launch without the .NET SDK.

## Optimize off

An empty `FastStartup\optimize.off` next to the executable skips the manifest, localization, and early shop-offerings patches. The loading-line text and setup timing logs stay on.

## License

MIT. See [LICENSE](LICENSE).
