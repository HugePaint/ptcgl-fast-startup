using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq.Expressions;
using System.Reflection;
using System.Text.RegularExpressions;
using HarmonyLib;
using UnityEngine;

namespace PtcglFastStartup;

/// <summary>
/// Startup walks every date bucket in asset-bundle-manifest one manifest bundle at a time.
/// Those requests are built with an empty hash, so Unity clears the cache and downloads them again.
/// This patch starts the bucket downloads together, and gives buckets older than today a stable cache hash.
/// </summary>
internal sealed class StartupHost : MonoBehaviour
{
}

internal static class StartupRunner
{
    private static StartupHost _host;

    public static StartupHost Host()
    {
        if (_host != null)
        {
            return _host;
        }

        var go = new GameObject("PtcglFastStartup");
        UnityEngine.Object.DontDestroyOnLoad(go);
        _host = go.AddComponent<StartupHost>();
        return _host;
    }

    public static IEnumerator Finish(IEnumerator inner, Action done)
    {
        try
        {
            yield return inner;
        }
        finally
        {
            done();
        }
    }
}

internal sealed class ManifestTally
{
    public int Ok;
    public int Fail;

    public void RecordOk(object value)
    {
        Ok++;
    }

    public void RecordFail(object value)
    {
        Fail++;
        FileLog.Error("Manifest request failed: " + value);
    }
}

internal static class ManifestLoadPatches
{
    public static void Apply(Harmony harmony)
    {
        if (File.Exists(OptimizeOffFlag()))
        {
            FileLog.Info("optimize patches off");
        }
        else
        {
            FileLog.Info("optimize patches on");
            TryPatch(harmony, "RemoteAssetBundleSource.GetAvailableAssetBundles", FindGetAvailableAssetBundles(), typeof(ParallelBuckets), nameof(ParallelBuckets.Prefix));
            TryPatch(harmony, "AssetBundleManager.InitializeAvailableBundles", FindInitializeAvailableBundles(), typeof(ParallelSources), nameof(ParallelSources.Prefix));
            TryPatch(harmony, "AssetBundleRestWorker.CreateWebRequest", FindCreateWebRequest(), typeof(HistoricalManifestCache), nameof(HistoricalManifestCache.Prefix));
            LocalizationGzip.Apply(harmony);
        }

        StartupStatusText.Apply(harmony);
        SetupTimingLog.Apply(harmony);
    }

    private static string OptimizeOffFlag()
    {
        return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "FastStartup", "optimize.off");
    }

    private static void TryPatch(Harmony harmony, string label, MethodInfo method, Type patchType, string prefixName)
    {
        if (method == null)
        {
            FileLog.Error($"Fast startup skipped {label}: method not found");
            return;
        }

        harmony.Patch(method, prefix: new HarmonyMethod(patchType, prefixName));
        FileLog.Info($"Patched {label}");
    }

    private static MethodInfo FindGetAvailableAssetBundles()
    {
        var type = AccessTools.TypeByName("RemoteAssetBundleSource");
        return type == null ? null : AccessTools.Method(type, "GetAvailableAssetBundles");
    }

    private static MethodInfo FindInitializeAvailableBundles()
    {
        var type = AccessTools.TypeByName("TPCI.AssetBundleSystem.AssetBundleManager");
        return type == null ? null : AccessTools.Method(type, "InitializeAvailableBundles");
    }

    private static MethodInfo FindCreateWebRequest()
    {
        var type = AccessTools.TypeByName("TPCI.AssetBundleSystem.AssetBundleRestWorker");
        return type == null ? null : AccessTools.Method(type, "CreateWebRequest");
    }

    private static class ParallelBuckets
    {
        public static bool Prefix(object __instance, object[] __args, ref IEnumerator __result)
        {
            try
            {
                var type = __instance.GetType();
                var datedManifests = AccessTools.Method(type, "GetDatedAssetManifests");
                var loadManifest = AccessTools.Method(type, "LoadManifest");
                if (datedManifests == null || loadManifest == null || AccessTools.Field(type, "_manifestLocale") == null)
                {
                    FileLog.Error("Parallel manifest load could not find the original methods, using the original loader");
                    return true;
                }

                __result = Run(__instance, datedManifests, loadManifest, __args[0] as Delegate, __args[1] as Delegate);
                return false;
            }
            catch (Exception ex)
            {
                FileLog.Error($"Parallel manifest load failed, using the original loader: {ex}");
                return true;
            }
        }

        private static IEnumerator Run(object source, MethodInfo datedManifests, MethodInfo loadManifest, Delegate onComplete, Delegate onError)
        {
            var type = source.GetType();
            PrepareHeaders(source);
            var endpoint = ReadEndpoint(source);
            var locale = (string)AccessTools.Field(type, "_manifestLocale").GetValue(source);
            var task = (System.Threading.Tasks.Task)datedManifests.Invoke(source, new object[] { null });
            while (!task.IsCompleted)
            {
                yield return null;
            }

            if (task.IsFaulted || task.IsCanceled)
            {
                throw new InvalidOperationException("Failed to get dated manifests.\nCannot load asset bundles.", task.Exception);
            }

            var names = (string[])task.GetType().GetProperty("Result").GetValue(task);
            if (names == null || names.Length == 0)
            {
                throw new InvalidOperationException("Failed to get dated manifests.\nCannot load asset bundles.");
            }

            FileLog.Info($"Manifest endpoint {endpoint} locale {locale} buckets {names.Length}");
            var watch = Stopwatch.StartNew();
            var tally = new ManifestTally();
            var complete = Observe(loadManifest.GetParameters()[3].ParameterType, onComplete, tally.RecordOk);
            var error = Observe(loadManifest.GetParameters()[4].ParameterType, onError, tally.RecordFail);
            var pending = names.Length;
            var started = 0;
            var host = StartupRunner.Host();
            foreach (var datedManifest in names)
            {
                IEnumerator enumerator;
                try
                {
                    enumerator = (IEnumerator)loadManifest.Invoke(source, new object[] { endpoint, locale, datedManifest, complete, error });
                }
                catch (Exception ex)
                {
                    FileLog.Error($"Could not start manifest {datedManifest}: {ex.InnerException ?? ex}");
                    pending--;
                    continue;
                }

                if (enumerator == null)
                {
                    pending--;
                    continue;
                }

                // StartCoroutine runs the first step immediately, so each worker reaches the queue
                // before the next one is created. RestHandler only starts the worker at the front.
                host.StartCoroutine(StartupRunner.Finish(enumerator, () => pending--));
                started++;
            }

            FileLog.Info($"Started {started}/{names.Length} manifest downloads for locale {locale}");
            while (pending > 0)
            {
                yield return null;
            }

            watch.Stop();
            FileLog.Info($"Finished {names.Length} manifest buckets for locale {locale} in {watch.Elapsed.TotalSeconds:0.00}s (ok {tally.Ok}, fail {tally.Fail})");
        }

        private static Delegate Observe(Type delegateType, Delegate original, Action<object> probe)
        {
            var parameters = delegateType.GetMethod("Invoke").GetParameters();
            var args = new ParameterExpression[parameters.Length];
            for (var i = 0; i < parameters.Length; i++)
            {
                args[i] = Expression.Parameter(parameters[i].ParameterType, "a" + i);
            }

            Expression probeArg = parameters.Length == 0
                ? Expression.Constant(null, typeof(object))
                : Expression.Convert(args[0], typeof(object));
            Expression body = Expression.Call(Expression.Constant(probe), typeof(Action<object>).GetMethod("Invoke"), probeArg);
            if (original != null)
            {
                body = Expression.Block(body, Expression.Invoke(Expression.Constant(original), args));
            }

            return Expression.Lambda(delegateType, body, args).Compile();
        }

        private static void PrepareHeaders(object source)
        {
            var factory = AccessTools.TypeByName("_Rainier.Scripts.Network.HttpHeaderProviderFactory");
            var provider = factory.GetMethod("GetProvider").Invoke(null, new object[] { null });
            var headers = provider.GetType().GetMethod("GetHeaders").Invoke(provider, null);
            AccessTools.Field(source.GetType(), "_headers").SetValue(source, headers);
        }

        private static string ReadEndpoint(object source)
        {
            var controller = AccessTools.Field(source.GetType(), "_gameSettingsEndpointController").GetValue(source);
            if (controller == null)
            {
                throw new InvalidOperationException("Game settings endpoint controller is not ready");
            }

            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            MethodInfo getValue = null;
            foreach (var type in EnumerateTypes(controller.GetType()))
            {
                foreach (var method in type.GetMethods(flags))
                {
                    var parameters = method.GetParameters();
                    if (method.Name.EndsWith("GetValue", StringComparison.Ordinal) && parameters.Length >= 1 && parameters[0].ParameterType == typeof(string))
                    {
                        getValue = method;
                        break;
                    }
                }

                if (getValue != null)
                {
                    break;
                }
            }

            if (getValue == null)
            {
                throw new MissingMethodException(controller.GetType().FullName, "GetValue");
            }

            var invokeArgs = new object[getValue.GetParameters().Length];
            invokeArgs[0] = "contentpath";
            for (var i = 1; i < invokeArgs.Length; i++)
            {
                var parameter = getValue.GetParameters()[i];
                invokeArgs[i] = parameter.HasDefaultValue ? parameter.DefaultValue : null;
            }

            return (string)getValue.Invoke(controller, invokeArgs);
        }

        private static IEnumerable<Type> EnumerateTypes(Type type)
        {
            while (type != null)
            {
                yield return type;
                foreach (var iface in type.GetInterfaces())
                {
                    yield return iface;
                }

                type = type.BaseType;
            }
        }
    }

    private static class ParallelSources
    {
        public static bool Prefix(object __instance, ref IEnumerator __result)
        {
            try
            {
                __result = Run(__instance);
                return false;
            }
            catch (Exception ex)
            {
                FileLog.Error($"Parallel bundle sources failed, using the original loader: {ex}");
                return true;
            }
        }

        private static IEnumerator Run(object manager)
        {
            var sources = (IEnumerable)AccessTools.Field(manager.GetType(), "_assetBundleSources").GetValue(manager);
            var onRetrieved = AccessTools.Method(manager.GetType(), "OnAvailableAssetBundlesRetrieved");
            var onError = AccessTools.Method(manager.GetType(), "OnErrorRetrievingAvailableAssetBundles");
            var running = new List<IEnumerator>();
            var count = 0;
            foreach (var source in sources)
            {
                count++;
                var load = source.GetType().GetMethod("GetAvailableAssetBundles");
                var parameters = load.GetParameters();
                var retrieved = Delegate.CreateDelegate(parameters[0].ParameterType, manager, onRetrieved);
                var error = Delegate.CreateDelegate(parameters[1].ParameterType, manager, onError);
                var enumerator = (IEnumerator)load.Invoke(source, new object[] { retrieved, error });
                if (enumerator != null && enumerator.MoveNext())
                {
                    running.Add(enumerator);
                }
            }

            FileLog.Info($"Loading {count} asset bundle sources in parallel");
            while (running.Count > 0)
            {
                for (var i = running.Count - 1; i >= 0; i--)
                {
                    if (!running[i].MoveNext())
                    {
                        running.RemoveAt(i);
                    }
                }

                if (running.Count > 0)
                {
                    yield return null;
                }
            }
        }
    }

    private static class HistoricalManifestCache
    {
        private static readonly Regex BucketDate = new Regex(@"_(\d{8})_\d{4}$", RegexOptions.CultureInvariant | RegexOptions.Compiled);

        public static void Prefix(object __instance)
        {
            try
            {
                AssignCacheHash(__instance);
            }
            catch (Exception ex)
            {
                FileLog.Warn($"Manifest cache hash skipped: {ex.Message}");
            }
        }

        private static void AssignCacheHash(object __instance)
        {
            var info = AccessTools.Field(__instance.GetType(), "bundleInfo").GetValue(__instance);
            if (info == null)
            {
                return;
            }

            var hashField = AccessTools.Field(info.GetType(), "bundleHash");
            var hash = (Hash128)hashField.GetValue(info);
            if (hash.isValid)
            {
                return;
            }

            var bundleName = AccessTools.Field(info.GetType(), "bundleName").GetValue(info) as string;
            if (!ShouldCache(bundleName))
            {
                return;
            }

            var revision = ReadManifestRevision();
            if (string.IsNullOrEmpty(revision))
            {
                return;
            }

            var bundlePath = AccessTools.Field(info.GetType(), "bundlePath").GetValue(info) as string;
            hashField.SetValue(info, Hash128.Compute((bundlePath ?? bundleName) + "\n" + revision));
            FileLog.Info($"Caching historical manifest {bundleName}");
        }

        private static bool ShouldCache(string bundleName)
        {
            if (string.IsNullOrEmpty(bundleName) || !bundleName.StartsWith("manifest_", StringComparison.Ordinal))
            {
                return false;
            }

            if (bundleName.IndexOf("_10101_", StringComparison.Ordinal) >= 0)
            {
                return true;
            }

            var match = BucketDate.Match(bundleName);
            if (!match.Success)
            {
                return false;
            }

            if (!DateTime.TryParseExact(match.Groups[1].Value, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var bucketDay))
            {
                return false;
            }

            return bucketDay.Date < DateTime.UtcNow.Date;
        }

        private static string ReadManifestRevision()
        {
            try
            {
                var path = Path.Combine(Application.persistentDataPath, "config-cache", "asset-bundle-manifest_0.0.json");
                if (!File.Exists(path))
                {
                    return null;
                }

                var match = Regex.Match(File.ReadAllText(path), "\"revision\"\\s*:\\s*\"([^\"]+)\"");
                return match.Success ? match.Groups[1].Value : null;
            }
            catch (Exception ex)
            {
                FileLog.Warn($"Could not read asset-bundle-manifest revision: {ex.Message}");
                return null;
            }
        }
    }
}
