# Startup compare

Run this once per mod iteration. Both arms load the mod. The baseline arm is an empty `FastStartup/optimize.off` next to the executable, which skips the three manifest patches and the localization gzip queue and connection-limit changes. `StartupStatusText` stays on and writes `manager start` / `manager finish` in both arms. `NullLogger.Log` stays patched in both arms and writes `setup` lines. The gap from `Begin ClientHandler.Setup` to `Begin InventoryQueries.Setup` is the connect prefix. The `completed` lines are the inventory steps. The second and third `GrantsNewAccountAsync` lines are tagged `[GetCompendiumAsync]` and `[cards, outfits, decks, boosters]`.

The arm is the log line `optimize patches on` or `optimize patches off`. The file on disk is only how that line gets chosen.

## Clocks

Managers overlap, so the loading time is the makespan. The sum of per-manager durations is a different number.

Read times from one `FastStartup.log` session (delete the file before each launch):

- Makespan: timestamp on the first `manager start` to timestamp on the last `manager finish`. Timestamps are the `HH:mm:ss.fff` prefix.
- Per manager: the `in Xs` value on `manager finish`. That value is `realtimeSinceStartup` and is the number to compare. Leave it separate from the timestamp delta.
- After the last manager: timestamp on the last `manager finish` to timestamp on the first `load final`. `load final` is the counter entering “Initializing scenes”. Scene load after that line is outside this protocol.

`RequiredNonBlockingInitialize` runs after `finishedInitialized` and is outside `in Xs`. AvatarManager keeps working after its finish line.

A shorter manager changes the makespan when later work was waiting on that finish. Read the makespan first. Then read `AssetBundleSetup`, which is the manager that holds “Loading assets 15/59”. When the makespan moves and `AssetBundleSetup` does not, read start/finish order in the log before treating the move as an optimization result.

## Scenario

Warm is the default for an iteration. Leave caches in place. Baseline still redownloads manifests, because those requests keep an empty hash. Optimized warm uses the hashed cache. That gap is the result to keep.

Cold is for an iteration that changes the download path. Delete the Unity AssetBundle cache this client writes before every launch, so both arms download. Find that directory on the first cold run: it is the one whose mtime updates while the log contains `Caching historical manifest`. Write the path into the cold note and reuse it. Report warm and cold as separate medians.

A baseline launch clears the hashed cache. In a warm session, finish every optimized launch before creating `optimize.off`.

## Steps

1. Write the client version and `git rev-parse --short HEAD` into the result note. Done when both are in the note.
2. Install the build under test with `install.ps1`. Done when that build’s dlls are in the game’s `FastStartup` directory.
3. Optimized block. Remove `optimize.off` if it exists. Delete `FastStartup.log`, launch, and quit at the main menu. That launch fills the cache; discard the log after it shows `load final`. Then three measured launches: delete the log, launch, quit at the main menu, copy the log aside. Done when three logs each have one `BOOT doorstop start`, `optimize patches on`, all three `Patched` lines for the manifest methods, `Patched GZipLocalizationTableProvider.GZipQueueSize`, `Patched ServicePointManager.DefaultConnectionLimit`, `Patched LocalizationByteDataProvider.GetBytesAsyncWithIfModifiedHeader`, one `load final`, a finish for every `manager start`, and none of `manager fail`, `apply failed`, `Fast startup skipped`, or `Parallel bundle sources failed`.
4. Baseline block. Create empty `FastStartup/optimize.off`. Three measured launches, same capture, with `optimize patches off` and none of those manifest or gzip `Patched` lines. `Patched` lines for `StartupStatusText` and `NullLogger.Log` stay. Done when three logs pass those checks.
5. Medians. For each arm, take the median makespan, the median gap from the last manager to `load final`, and the median `in Xs` for each manager name. Done when the note lists both arms and one row per manager, sorted by optimized median minus baseline median.
6. Read the iteration. When the two makespan ranges overlap, the wall clock stayed inside the run-to-run spread. When they do not, the makespan delta is the result and the largest manager deltas say which step moved. Remove `optimize.off` when the note is written.

Both arms in one iteration use the same set of manager names. Stop when the sets differ. A run that sits on the language or login screen is invalid; use an account that is already signed in. When a measured optimized `AssetBundleSetup` duration returns to the baseline range, that launch was cold; replace it after another cache-filling launch.

The result note records: date, git sha, client version, scenario (`warm` or `cold`), three-run min / median / max makespan per arm, median gap to `load final` per arm, and the manager table (name, baseline median, optimized median, delta).
