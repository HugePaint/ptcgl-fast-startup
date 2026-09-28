using System;
using System.Diagnostics;
using System.Linq.Expressions;
using System.Reflection;
using System.Threading.Tasks;
using HarmonyLib;

namespace PtcglFastStartup;

/// <summary>
/// Client setup waits for inventory, commerce, season rank, and gifts, then requests shop offerings.
/// Offerings only need commerce setup, so that request starts when commerce setup finishes and the later call joins it.
/// The step timings are written in both arms.
/// </summary>
internal static class NetworkSetup
{
    private static readonly object Gate = new object();

    private static bool _overlap;
    private static bool _inShopCall;
    private static Task _shop;
    private static MethodInfo _shopMethod;
    private static Delegate _noopError;

    public static void ApplyTimings(Harmony harmony)
    {
        Time(harmony, "SharedLogicUtils.source.FeatureFlags.ConfigFeatureFlagService", "DownloadFeatureFlagsAsync", nameof(TimeTask), "feature flags");
        Time(harmony, "SharedLogicUtils.source.Permissions.ConfigPermissionsService", "DownloadPermissionsAsync", nameof(TimePermissions), "permissions");
        Time(harmony, "RainierClientSDK.source.Player.Implementations.PlatformPlayerQuery", "SetPreferredLocale", nameof(TimeString), "preferred locale");
        Time(harmony, "RainierClientSDK.source.ClientAnalytics.ClientAnalytics", "SetClientAnalyticsAsync", nameof(TimeBool), "client analytics");

        var commerce = AccessTools.TypeByName("RainierClientSDK.source.Commerce.Commerce");
        var setup = commerce == null ? null : AccessTools.Method(commerce, "Setup");
        if (setup == null)
        {
            FileLog.Error("Fast startup skipped Commerce.Setup: method not found");
        }
        else
        {
            harmony.Patch(setup, postfix: new HarmonyMethod(typeof(NetworkSetup), nameof(CommerceSetup)));
            FileLog.Info("Patched Commerce.Setup");
        }

        _shopMethod = commerce == null ? null : AccessTools.Method(commerce, "GetShopOfferingsAsync");
        if (_shopMethod == null)
        {
            FileLog.Error("Fast startup skipped Commerce.GetShopOfferingsAsync: method not found");
            return;
        }

        _noopError = EmptyDelegate(_shopMethod.GetParameters()[0].ParameterType);
        var resultType = _shopMethod.ReturnType.GetGenericArguments()[0];
        var watch = typeof(NetworkSetup).GetMethod(nameof(ShopWatch), BindingFlags.NonPublic | BindingFlags.Static);
        harmony.Patch(_shopMethod, postfix: new HarmonyMethod(watch.MakeGenericMethod(resultType)));
        FileLog.Info("Patched Commerce.GetShopOfferingsAsync");
    }

    public static void ApplyOverlap(Harmony harmony)
    {
        if (_shopMethod == null)
        {
            return;
        }

        _overlap = true;
        var errorType = _shopMethod.GetParameters()[0].ParameterType.GetGenericArguments()[0];
        var resultType = _shopMethod.ReturnType.GetGenericArguments()[0];
        var prefix = typeof(NetworkSetup).GetMethod(nameof(ShopPrefix), BindingFlags.NonPublic | BindingFlags.Static);
        harmony.Patch(_shopMethod, prefix: new HarmonyMethod(prefix.MakeGenericMethod(errorType, resultType)));
        FileLog.Info("Patched Commerce.GetShopOfferingsAsync early");
        ResetOnSetup(harmony, "RainierClientSDK.PlatformRainierClient");
        ResetOnSetup(harmony, "RainierClientSDK.LocalRainierClient");
    }

    public static void TimeTask(Task __result)
    {
        Watch(__result, "feature flags");
    }

    public static void TimePermissions(Task __result)
    {
        Watch(__result, "permissions");
    }

    public static void TimeString(Task<string> __result)
    {
        Watch(__result, "preferred locale");
    }

    public static void TimeBool(Task<bool> __result)
    {
        Watch(__result, "client analytics");
    }

    public static void CommerceSetup(Task<bool> __result)
    {
        Watch(__result, "Commerce.Setup");
        if (!_overlap || __result == null)
        {
            return;
        }

        Task setup = __result;
        setup.ContinueWith(
            _ =>
            {
                try
                {
                    StartShop(_noopError, early: true);
                }
                catch (Exception ex)
                {
                    FileLog.Error("setup GetShopOfferingsAsync early failed: " + ex);
                }
            },
            System.Threading.CancellationToken.None,
            TaskContinuationOptions.OnlyOnRanToCompletion,
            TaskScheduler.Default);
    }

    public static void ResetShop()
    {
        lock (Gate)
        {
            _shop = null;
        }
    }

    public static bool InShopCall => _inShopCall;

    public static object StartShop(object onError, bool early)
    {
        lock (Gate)
        {
            if (_shop != null)
            {
                FileLog.Info("setup GetShopOfferingsAsync joined");
                return _shop;
            }

            _inShopCall = true;
            try
            {
                _shop = (Task)_shopMethod.Invoke(null, new[] { onError ?? _noopError });
            }
            finally
            {
                _inShopCall = false;
            }

            FileLog.Info(early
                ? "setup GetShopOfferingsAsync started after Commerce.Setup"
                : "setup GetShopOfferingsAsync started at the original call");
            return _shop;
        }
    }

    public static void Watch(object result, string label)
    {
        if (result is not Task task)
        {
            return;
        }

        var watch = Stopwatch.StartNew();
        FileLog.Info($"setup {label} started");
        task.ContinueWith(
            finished =>
            {
                var state = finished.IsCanceled ? "canceled" : finished.IsFaulted ? "faulted" : "ok";
                FileLog.Info($"setup {label} {state} in {watch.Elapsed.TotalSeconds:0.00}s");
            },
            TaskScheduler.Default);
    }

    private static void Time(Harmony harmony, string typeName, string methodName, string postfix, string label)
    {
        var type = AccessTools.TypeByName(typeName);
        var method = type == null ? null : AccessTools.Method(type, methodName);
        if (method == null)
        {
            FileLog.Error($"Fast startup skipped {typeName}.{methodName}: method not found");
            return;
        }

        harmony.Patch(method, postfix: new HarmonyMethod(typeof(NetworkSetup), postfix));
        FileLog.Info($"Patched {typeName}.{methodName}");
        _ = label;
    }

    private static void ResetOnSetup(Harmony harmony, string typeName)
    {
        var type = AccessTools.TypeByName(typeName);
        var method = type == null ? null : AccessTools.Method(type, "Setup");
        if (method == null)
        {
            return;
        }

        harmony.Patch(method, prefix: new HarmonyMethod(typeof(NetworkSetup), nameof(ResetShop)));
    }

    private static void ShopWatch<TResult>(Task<TResult> __result)
    {
        Watch(__result, "shop offerings");
    }

    private static bool ShopPrefix<TArg, TResult>(Action<TArg> onError, ref Task<TResult> __result)
    {
        if (InShopCall)
        {
            return true;
        }

        try
        {
            if (StartShop(onError, early: false) is Task<TResult> task)
            {
                __result = task;
                return false;
            }
        }
        catch (Exception ex)
        {
            FileLog.Error("setup GetShopOfferingsAsync prefix failed: " + ex);
        }

        return true;
    }

    private static Delegate EmptyDelegate(Type delegateType)
    {
        var argument = delegateType.GetGenericArguments()[0];
        var parameter = Expression.Parameter(argument, "error");
        return Expression.Lambda(delegateType, Expression.Empty(), parameter).Compile();
    }
}
