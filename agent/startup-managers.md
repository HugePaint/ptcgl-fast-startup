# Startup managers

Facts below were taken from the local PTCGL install on 2026-09-28 (client `1.42.2.1247115`, content path `StandaloneWindows64/1.42.0`). Assemblies: `TPCI.RainierClient.dll`, `TPCI.Shared.Singletons.dll`, `TPCIAssetBundleSystem.dll`. Re-decompile those assemblies when the game version changes; the prefab list and dependency edges are data, not something this repo compiles.

Splash labels live in `StartupStatusText.Describe`. Keep a label a short form of the job in the catalog below.

## Counter

`GameManager.InitializeManagers` walks the serialized `managersList`. A manager is instantiated and `InitializeManager` starts only after every type in its `Dependencies` has `finishedInitialized`. Several managers run at once.

The loading line is `StartupScreenText.UpdateLoadString(initializedManagersList.Count)`. The numerator is how many managers have finished `Initialize`, not an index into the prefab and not a file count. `SetTotalSteps` sets the denominator to `managersList.Managers.Count + 1`. A denominator of 59 means 58 prefab entries. The extra step is `LoadGame` calling `UpdateLoadString(-1, final: true)` after every manager succeeds, which switches the line to “Initializing scenes” and then loads the main menu.

`GameManager` is the host. It is not one of the 58. This catalog has 60 other `ManagerBase` types; two of them are absent from that prefab. Which two was not read out of the scene asset.

`DynamicStartupRoutine` is a `ManagerDependentRoutine`, not a manager. It runs `DefaultDynamicStartupController.PrepareAssets` for `dynamic-startup_0.0` and does not move the counter.

`ManagerBase.InitializeManager` logs begin/end through `Debug.Log`. `RainierLogHandler` drops those lines during startup, so they are absent from `Player.log`. This mod writes `manager start` / `manager finish` to `FastStartup.log` beside the game executable.

`finishedInitialized` is set only when `Initialize` returns without `hasError`. `RequiredNonBlockingInitialize` runs after that flag and does not hold the counter. A manager with an empty `Initialize` still consumes one step if it is on the prefab.

## Bands

`StartupScreenText.GetLoadingText` picks the localized sentence from the finished count. The words are a band, not a description of the manager that just finished.

| Finished count | Key | English |
|---|---|---|
| 0–6 | `loading_screen_3` | Collecting local assets {0}/{1} |
| 7–14 | `loading_screen_2` | Loading data from server {0}/{1} |
| 15–18 | `loading_screen_1` | Loading assets {0}/{1} |
| 19–21 | `loading_screen_7` | Waking up the Professor {0}/{1} |
| 22–23 | `loading_screen_8` | Restocking the shop {0}/{1} |
| 24–31 | `loading_screen_6` | Printing cards {0}/{1} |
| 32 and above | `loading_screen_5` | Refilling bottles of ketchup {0}/{1} |
| final | `loading_screen_4` | Initializing scenes {0}/{1} |

While the line sits on a number, every in-flight `Initialize` is still running. The long hold on “Loading assets 15/59” is the gap after 15 managers have finished and before the next one does. `AssetBundleSetup` is the manager whose `Initialize` matches that band.

## AssetBundleSetup

Direct dependencies: `LocalizationConfigManager`, `GameSettingsEndpoint`, `LoginManager`, `ConfigManager`, `NetworkManager`, `ResolutionManager`.

`AssetBundleSetup.Initialize` then:

1. Creates `TPCI.AssetBundleSystem.AssetBundleManager` if it is missing.
2. Adds a `RemoteAssetBundleSource` for the `asset_manifest` language, plus a second source when `asset_manifest_fallback` differs.
3. Calls `InitializeAvailableBundles`, which asks each source for its dated manifest buckets. On 2026-09-28 the `en` locale had 17 buckets (`manifest_en_10101_0000` and `manifest_en_YYYYMMDD_HHMM`). `ManifestLoadPatches` starts those bucket downloads together and gives buckets older than today a stable cache hash.
4. Loads and keeps `shadersbundle`, then hands it to `ShaderManager`.
5. On a landscape orientation, loads scenes through `AssetBundleSceneManager`.
6. Loads fonts for the `fonts` language.

Managers that list `AssetBundleSetup` directly: `AvatarManager`, `BattleBoardManager`, `BnbDataManager`, `FrontEndPreloadManager`, `FTUEManager`, `FXManager`, `LocalizationDebugManager`, `RainierAudio`, `ResearchMissionsDataManager`, `ResourceManager`.

## Catalog

Dependencies are the `ManagerBase` types passed to the `Dependencies` list in the constructor or `get_Dependencies`. The job is what that type’s `Initialize` calls.

| Manager | Depends on | Job |
|---|---|---|
| ResolutionManager | | Resolution setup |
| LocalizationBootstrapper | | Language display flags from PlayerPrefs |
| CommandLineOptionsProvider | | Command-line arguments |
| DebugManager | ResolutionManager | Console redirect |
| TransitionManager | LocalizationConfigManager | Instantiate the fade transition |
| SceneLoadManager | | Empty `Initialize` |
| InteractionManager | | Empty `Initialize` |
| OverlayManager | | Empty `Initialize` |
| GameDriverManager | | Empty `Initialize` |
| InboxEvents | | Empty `Initialize` |
| BuildAndBattlePopupManager | | Empty `Initialize` |
| TelemetryManagerWrapper | DebugManager, GameSettingsEndpoint | Telemetry endpoints and verbosity |
| AnalyticsManager | DebugManager | Analytics channels and the startup event |
| GameSettingsEndpoint | LocalizationBootstrapper, DebugManager | Remote game settings |
| LoginManager | LocalizationBootstrapper, TelemetryManagerWrapper | Language select, then PTCS login |
| AccountModeManager | LoginManager | Account mode |
| NetworkManager | DebugManager, AnalyticsManager, LoginManager | Access token, client SDK, server connection |
| SupportedDeviceManager | LoginManager | Minimum-spec check; sets `hasError` when the device fails |
| FirebaseManager | ConfigManager, LoginManager, FeatureFlagManagerSingleton, AccountModeManager | Firebase consent and init when the feature is on |
| ContentOverrideManager | GameSettingsEndpoint, NetworkManager | Per-platform content path, then the force-update message |
| ConfigManager | DebugManager, NetworkManager, AccountModeManager | Register the content loader and download config documents |
| LocalizationConfigManager | ConfigManager, LocalizationBootstrapper, NetworkManager | Load `localization-bundle-manifest_0.0` |
| FeatureFlagManagerSingleton | ConfigManager, NetworkManager | Feature-flag service |
| PermissionsManager | FeatureFlagManagerSingleton, AccountModeManager | Account permissions |
| PlayerConfigManager | LocalizationConfigManager, NetworkManager | Preferred locale |
| CardRarityManifest | ConfigManager | Rarity table from config |
| CardFormatManager | ConfigManager | Format flags |
| CardDatabaseManager | LocalizationConfigManager, DebugManager, ConfigManager, CardRarityManifest, CardFormatManager | Card data tables and the card action table |
| ItemSetDatabaseManager | ConfigManager, DebugManager, PermissionsManager | Item-set database request |
| PlayerInventoryManager | DebugManager, CommandLineOptionsProvider, NetworkManager, CardDatabaseManager | Collection, avatar customizations, owned-card counts |
| PlayerStatsManager | NetworkManager | Player stats |
| QuestManager | NetworkManager, DebugManager | Active quests |
| NewsDataManager | ConfigManager, DebugManager, LocalizationBootstrapper, PermissionsManager | News config and read state |
| CommerceManager | NetworkManager, ItemSetDatabaseManager | Store client |
| ShopDataManager | ConfigManager, DebugManager, CommerceManager | Shop catalog |
| DustManager | NetworkManager, ConfigManager, PlayerInventoryManager, DebugManager | Dust exchange |
| ExpansionChallengeManager | NetworkManager, DebugManager, ConfigManager | Battle-pass challenge data |
| InboxManager | DebugManager, NetworkManager, AccountModeManager, ConfigManager, LocalizationBootstrapper | Messages, friend requests, direct-match invites |
| BattleBoardManager | ConfigManager, AssetBundleSetup, DebugManager | Board presets from config |
| DeckValidationManager | NetworkManager, ConfigManager, PlayerInventoryManager, DebugManager | Deck-validation rules for the current mode |
| DeckAnalyticsManager | NetworkManager, CardDatabaseManager, DeckValidationManager, PlayerInventoryManager, AnalyticsManager | Deck collection analytics hooks |
| TrainerTrialsRulesetScheduleManager | DeckValidationManager, LocalizationConfigManager | Trainer Trial ruleset schedule |
| PopupManager | DebugManager, NetworkManager | Popup base init |
| PerformanceMonitor | AnalyticsManager | Low-memory analytics |
| LocalizedSpriteFontManager | FeatureFlagManagerSingleton, LocalizationBootstrapper | TMP sprite font for the current language |
| RainierManager | LocalizationConfigManager | Empty `Initialize` |
| OperationSaver | DebugManager, RainierManager | Empty `Initialize` |
| ProfileServiceManager | NetworkManager | Empty `Initialize` |
| AssetBundleSetup | LocalizationConfigManager, GameSettingsEndpoint, LoginManager, ConfigManager, NetworkManager, ResolutionManager | Manifest buckets, `shadersbundle`, fonts |
| ResourceManager | AssetBundleSetup | Icon and resource lookup tables |
| FXManager | AssetBundleSetup | Effect pools |
| RainierAudio | AssetBundleSetup, AnalyticsManager | Audio bundle, volumes, startup audio event |
| AvatarManager | NetworkManager, ConfigManager, PlayerInventoryManager, AssetBundleSetup, ResolutionManager | Avatar assets; further work in `RequiredNonBlockingInitialize` |
| FrontEndPreloadManager | ConfigManager, AssetBundleSetup | Casual-carousel images from `front-end-customizations-casual-carousel_0.0` |
| MainMenuMusicProvider | ConfigManager | Main-menu music selection |
| BnbDataManager | NetworkManager, AssetBundleSetup | Build & Battle data |
| FTUEManager | DialogueManager, DebugManager, NetworkManager, LocalizationConfigManager, RainierManager, PlayerInventoryManager, AssetBundleSetup, FeatureFlagManagerSingleton | Tutorial checkpoint and the `ftuecontrollers` bundle |
| DialogueManager | LocalizationConfigManager, RainierManager, AvatarManager, RainierAudio | Dialogue parts |
| LocalizationDebugManager | LocalizationBootstrapper, ConfigManager, DebugManager, AssetBundleSetup | Localization debug options |
| ResearchMissionsDataManager | NetworkManager, AssetBundleSetup, PlayerInventoryManager, FeatureFlagManagerSingleton, LocalizationBootstrapper | Research missions when the feature is on |

## Mod hooks

`StartupStatusText` patches `StartupScreenText.SetTotalSteps`, `StartupScreenText.UpdateLoadString`, `ManagerBase.InitializeManager`, and the `finishedInitialized` / `hasError` setters. The splash keeps the game’s localized line and lists in-flight managers above it.

`ManifestLoadPatches` replaces `RemoteAssetBundleSource.GetAvailableAssetBundles`, `AssetBundleManager.InitializeAvailableBundles`, and `AssetBundleRestWorker.CreateWebRequest`. Those run inside `AssetBundleSetup` step 3. On the same switch it sets `GZipLocalizationTableProvider.GZipQueueSize` and `ServicePointManager.DefaultConnectionLimit` to 16, and raises the `ServicePoint` limit for each localization gzip host. This Mono build throws from `HttpClientHandler.MaxConnectionsPerServer`, and `HttpClient` sends those gzip requests through `ServicePoint`. An empty file `FastStartup/optimize.off` next to the executable skips the manifest patches and the gzip changes. `StartupStatusText` still applies. `NullLogger.Log` is patched in both arms and writes `setup` lines for the `ClientHandler` and `InventoryQueries` timings. `UpdateLoadString` with `final: true` writes `load final`.
