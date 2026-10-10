# PTCGL fast startup

`pack/doorstop_config.ini` targets `FastStartup\FastStartupBootstrap.dll`. When `FastStartup\PtcglCodeRedeem.Doorstop.dll` is already in the game folder, `install.ps1` rewrites `target_assembly` to that DLL and leaves `dll_search_path_override = "FastStartup"`. Keep that rewrite. This Unity 6 mono build has no `imageOpen`, so Doorstop cannot target `BepInEx.Preloader.dll`.

Patches stay on startup: asset manifests, localization gzip, and the early `GetShopOfferingsAsync`. Coupon verify and redeem stay in the code-redeem mod.

`install.ps1` resolves the game directory. Docs in this repo do not name an absolute game path.

Before editing a startup manager or the loading line, read [agent/startup-managers.md](agent/startup-managers.md). The header records the client that catalog was decompiled from. When the running client differs, re-decompile `TPCI.RainierClient.dll`, `TPCI.Shared.Singletons.dll`, and `TPCIAssetBundleSystem.dll` and update the catalog first. Done when the header names that client.

Before a timed launch or an `optimize.off` switch, read [agent/startup-compare.md](agent/startup-compare.md).
