using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Text;
using HarmonyLib;
using UnityEngine;

namespace PtcglFastStartup;

/// <summary>
/// The loading line only advances when a manager finishes Initialize.
/// This keeps that line and adds the managers still inside Initialize.
/// </summary>
internal static class StartupStatusText
{
    private const float RefreshSeconds = 0.25f;
    private const int MaxLines = 8;

    private static readonly List<Entry> Running = new List<Entry>();
    private static object _text;
    private static string _headline = "";
    private static string _lastDone = "";
    private static string _applied = "";
    private static bool _prepared;
    private static bool _stop;
    private static bool _applying;
    private static WaitForSecondsRealtime _wait;

    public static void Apply(Harmony harmony)
    {
        Patch(harmony, "StartupScreenText.SetTotalSteps", "StartupScreenText", "SetTotalSteps", typeof(ScreenHooks), nameof(ScreenHooks.SetTotal), prefix: false);
        Patch(harmony, "StartupScreenText.UpdateLoadString", "StartupScreenText", "UpdateLoadString", typeof(ScreenHooks), nameof(ScreenHooks.LoadString), prefix: false);
        Patch(harmony, "ManagerBase.InitializeManager", "ManagerBase", "InitializeManager", typeof(LifeHooks), nameof(LifeHooks.Begin), prefix: true);
        PatchSetter(harmony, "ManagerBase.finishedInitialized", "ManagerBase", "finishedInitialized", nameof(LifeHooks.Finished));
        PatchSetter(harmony, "ManagerBase.hasError", "ManagerBase", "hasError", nameof(LifeHooks.Failed));
    }

    private static void Patch(Harmony harmony, string label, string typeName, string methodName, Type patchType, string hookName, bool prefix)
    {
        var type = AccessTools.TypeByName(typeName);
        var method = type == null ? null : AccessTools.Method(type, methodName);
        if (method == null)
        {
            FileLog.Error($"Startup status skipped {label}: method not found");
            return;
        }

        var hook = new HarmonyMethod(patchType, hookName);
        harmony.Patch(method, prefix: prefix ? hook : null, postfix: prefix ? null : hook);
        FileLog.Info($"Patched {label}");
    }

    private static void PatchSetter(Harmony harmony, string label, string typeName, string propertyName, string hookName)
    {
        var type = AccessTools.TypeByName(typeName);
        var setter = type?.GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetSetMethod(true);
        if (setter == null)
        {
            FileLog.Error($"Startup status skipped {label}: setter not found");
            return;
        }

        harmony.Patch(setter, postfix: new HarmonyMethod(typeof(LifeHooks), hookName));
        FileLog.Info($"Patched {label}");
    }

    private static class ScreenHooks
    {
        public static void SetTotal(object __instance)
        {
            Bind(__instance);
            Paint();
        }

        public static void LoadString(object __instance, bool final)
        {
            Bind(__instance);
            _headline = ReadText();
            Paint();
            if (final)
            {
                _stop = true;
                FileLog.Info("load final");
            }
        }
    }

    private static class LifeHooks
    {
        public static void Begin(object __instance)
        {
            if (__instance == null || Find(__instance) != null)
            {
                return;
            }

            var entry = new Entry(__instance, __instance.GetType().Name, Time.realtimeSinceStartup);
            Running.Add(entry);
            FileLog.Info("manager start " + entry.Name);
            Paint();
        }

        public static void Finished(object __instance, bool value)
        {
            if (!value)
            {
                return;
            }

            Remove(__instance, failed: false);
        }

        public static void Failed(object __instance, bool value)
        {
            if (!value)
            {
                return;
            }

            Remove(__instance, failed: true);
        }
    }

    private static bool MissingText()
    {
        if (_text == null)
        {
            return true;
        }

        return _text is UnityEngine.Object unityObject && unityObject == null;
    }

    private static void Bind(object screen)
    {
        if (screen == null)
        {
            return;
        }

        if (MissingText())
        {
            _text = null;
            _prepared = false;
            _applied = "";
            var field = AccessTools.Field(screen.GetType(), "loadingText");
            _text = field?.GetValue(screen);
            Prepare();
        }

        if (_wait == null)
        {
            _wait = new WaitForSecondsRealtime(RefreshSeconds);
        }

        StartupRunner.EnsureTick(Tick());
    }

    private static IEnumerator Tick()
    {
        while (!_stop)
        {
            yield return _wait;
            if (_stop)
            {
                yield break;
            }

            Paint();
        }
    }

    private static void Paint()
    {
        if (_applying || MissingText())
        {
            return;
        }

        var next = Compose();
        if (next == _applied)
        {
            return;
        }

        _applying = true;
        try
        {
            _text.GetType().GetProperty("text")?.SetValue(_text, next);
            _applied = next;
        }
        catch (Exception ex)
        {
            FileLog.Warn("Startup status text skipped: " + ex.Message);
        }
        finally
        {
            _applying = false;
        }
    }

    private static string Compose()
    {
        var now = Time.realtimeSinceStartup;
        Running.Sort((a, b) =>
        {
            var byTime = (now - b.Started).CompareTo(now - a.Started);
            return byTime != 0 ? byTime : string.CompareOrdinal(a.Name, b.Name);
        });
        var builder = new StringBuilder();
        var shown = Math.Min(MaxLines, Running.Count);
        for (var i = 0; i < shown; i++)
        {
            var entry = Running[i];
            var elapsed = Math.Max(0f, now - entry.Started);
            builder.Append(entry.Name);
            var detail = Describe(entry.Name);
            if (detail.Length > 0)
            {
                builder.Append("  ").Append(detail);
            }

            builder.Append("  ").Append(elapsed.ToString("0.0", CultureInfo.InvariantCulture)).Append("s\n");
        }

        var hidden = Running.Count - shown;
        if (hidden > 0)
        {
            builder.Append('+').Append(hidden.ToString(CultureInfo.InvariantCulture)).Append(" more\n");
        }

        if (Running.Count == 0 && _lastDone.Length > 0)
        {
            builder.Append(_lastDone).Append('\n');
        }

        if (_headline.Length > 0)
        {
            builder.Append(_headline);
        }

        return builder.ToString().TrimEnd();
    }

    private static string ReadText()
    {
        var value = _text?.GetType().GetProperty("text")?.GetValue(_text) as string;
        return value ?? "";
    }

    private static void Prepare()
    {
        if (_prepared || !(_text is Component component))
        {
            return;
        }

        _prepared = true;
        var type = _text.GetType();
        type.GetProperty("enableWordWrapping")?.SetValue(_text, true);
        var overflow = type.GetProperty("overflowMode");
        if (overflow != null)
        {
            overflow.SetValue(_text, Enum.ToObject(overflow.PropertyType, 0));
        }

        var alignment = type.GetProperty("alignment");
        if (alignment != null)
        {
            var current = Convert.ToInt32(alignment.GetValue(_text), CultureInfo.InvariantCulture);
            var horizontal = current & 0xFF;
            if (horizontal == 0)
            {
                horizontal = 2;
            }

            alignment.SetValue(_text, Enum.ToObject(alignment.PropertyType, 1024 | horizontal));
        }

        var rect = component.GetComponent<RectTransform>();
        if (rect == null || rect.sizeDelta.y >= 80f)
        {
            return;
        }

        var width = Mathf.Max(rect.sizeDelta.x, 1100f);
        var height = Mathf.Max(rect.sizeDelta.y, 280f);
        var extra = height - rect.sizeDelta.y;
        rect.sizeDelta = new Vector2(width, height);
        rect.anchoredPosition += new Vector2(0f, extra * rect.pivot.y);
    }

    private static void Remove(object manager, bool failed)
    {
        var entry = Find(manager);
        if (entry == null)
        {
            return;
        }

        Running.Remove(entry);
        var elapsed = Math.Max(0f, Time.realtimeSinceStartup - entry.Started);
        var seconds = elapsed.ToString("0.00", CultureInfo.InvariantCulture);
        if (failed)
        {
            _lastDone = "failed " + entry.Name;
            FileLog.Error($"manager fail {entry.Name} after {seconds}s");
        }
        else
        {
            _lastDone = "done " + entry.Name;
            FileLog.Info($"manager finish {entry.Name} in {seconds}s");
        }

        Paint();
    }

    private static Entry Find(object manager)
    {
        for (var i = 0; i < Running.Count; i++)
        {
            if (ReferenceEquals(Running[i].Manager, manager))
            {
                return Running[i];
            }
        }

        return null;
    }

    private static string Describe(string name)
    {
        switch (name)
        {
            case "AccountModeManager": return "account mode";
            case "AnalyticsManager": return "analytics channels";
            case "AssetBundleSetup": return "manifests, shaders, fonts";
            case "AvatarManager": return "avatar assets";
            case "BattleBoardManager": return "board presets";
            case "BnbDataManager": return "Build & Battle data";
            case "BuildAndBattlePopupManager": return "Build & Battle popups";
            case "CardDatabaseManager": return "card tables and actions";
            case "CardFormatManager": return "format rules";
            case "CardRarityManifest": return "rarity table";
            case "CommandLineOptionsProvider": return "command line";
            case "CommerceManager": return "store client";
            case "ConfigManager": return "remote config documents";
            case "ContentOverrideManager": return "content path and force update";
            case "DebugManager": return "log redirect";
            case "DeckAnalyticsManager": return "deck analytics hooks";
            case "DeckValidationManager": return "deck validation rules";
            case "DialogueManager": return "dialogue wiring";
            case "DustManager": return "dust exchange";
            case "ExpansionChallengeManager": return "battle pass challenges";
            case "FeatureFlagManagerSingleton": return "feature flags";
            case "FirebaseManager": return "Firebase consent";
            case "FrontEndPreloadManager": return "menu carousel images";
            case "FTUEManager": return "tutorial checkpoint and bundle";
            case "FXManager": return "effect pools";
            case "GameDriverManager": return "game driver";
            case "GameSettingsEndpoint": return "remote game settings";
            case "InboxEvents": return "inbox events";
            case "InboxManager": return "messages and invites";
            case "InteractionManager": return "input";
            case "ItemSetDatabaseManager": return "item set database";
            case "LocalizationBootstrapper": return "language bootstrap";
            case "LocalizationConfigManager": return "localization manifest";
            case "LocalizationDebugManager": return "localization debug options";
            case "LocalizedSpriteFontManager": return "sprite font";
            case "LoginManager": return "language and account login";
            case "MainMenuMusicProvider": return "menu music";
            case "NetworkManager": return "tokens and client connection";
            case "NewsDataManager": return "news";
            case "OperationSaver": return "saved operations";
            case "OverlayManager": return "overlays";
            case "PerformanceMonitor": return "memory telemetry";
            case "PermissionsManager": return "account permissions";
            case "PlayerConfigManager": return "preferred locale";
            case "PlayerInventoryManager": return "collection and owned cards";
            case "PlayerStatsManager": return "player stats";
            case "PopupManager": return "popups";
            case "ProfileServiceManager": return "profile service";
            case "QuestManager": return "active quests";
            case "RainierAudio": return "audio bundle and volumes";
            case "RainierManager": return "front-end root";
            case "ResearchMissionsDataManager": return "research missions";
            case "ResolutionManager": return "resolution";
            case "ResourceManager": return "icon and resource tables";
            case "SceneLoadManager": return "scene loading";
            case "ShopDataManager": return "shop catalog";
            case "SupportedDeviceManager": return "device requirements";
            case "TelemetryManagerWrapper": return "telemetry endpoints";
            case "TrainerTrialsRulesetScheduleManager": return "Trainer Trial schedule";
            case "TransitionManager": return "fade transition";
            default: return "";
        }
    }

    private sealed class Entry
    {
        public Entry(object manager, string name, float started)
        {
            Manager = manager;
            Name = name;
            Started = started;
        }

        public object Manager { get; }

        public string Name { get; }

        public float Started { get; }
    }
}
