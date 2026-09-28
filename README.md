# PTCGL fast startup

[中文](README.zh-CN.md)

Harmony patches for the Windows Pokémon TCG Live client. Unity Doorstop loads `FastStartup\FastStartupBootstrap.dll`, which applies the patches once `TPCI.RainierClient` loads.

The loading line keeps the game's text and lists managers still inside `Initialize`. `FastStartup.log` is written next to the executable.

Tested on client `1.42.2.1247115` (Unity `6000.3.5`).

## Optimizations

**Asset manifests.** `AssetBundleSetup` downloads dated manifest buckets one after another, and loads each locale source the same way. The patch starts every bucket at once, and runs the locale source and the fallback source together. A bucket named `manifest_*_10101_*`, or a dated bucket from before today (UTC), gets a stable cache hash from the bundle path and the `asset-bundle-manifest` revision. A later launch reuses Unity's cache for those files. Today's bucket is left without a hash, so it is downloaded again.

**Localization gzip.** `LocalizationConfigManager` downloads one gzip per manifest directory, two at a time. This Mono `HttpClient` ignores `MaxConnectionsPerServer` and uses `ServicePointManager`, which also allows two connections per host. The patch raises `GZipQueueSize` and that connection limit to 16.

**Shop offerings.** After the client connects, `NetworkManager` waits for commerce, inventory, season rank, and gifts, then requests shop offerings. Offerings only need commerce setup, so the patch starts `GetShopOfferingsAsync` when `Commerce.Setup` finishes. The original call waits on that same task.

## Install

Windows, the game, and the .NET SDK.

```powershell
.\install.ps1
```

The script downloads Unity Doorstop from the BepInEx `6.0.0-be.788` package, builds both projects in Release, and copies `winhttp.dll`, `doorstop_config.ini`, and `FastStartup\` into the first `The Pok*\Pok*` directory under `C:\Users\yangyuhan`. It stops a running game process before copying. Both projects reference `0Harmony.dll` from that download, so build through this script.

## Optimize off

An empty `FastStartup\optimize.off` next to the executable skips the manifest, localization, and early shop-offerings patches. The loading-line text and setup timing logs stay on.

## License

MIT. See [LICENSE](LICENSE).
