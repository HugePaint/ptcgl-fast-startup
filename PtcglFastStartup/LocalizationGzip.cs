using System;
using System.Collections.Generic;
using System.Net;
using HarmonyLib;

namespace PtcglFastStartup;

/// <summary>
/// Localization startup downloads one gzip per manifest directory, two at a time.
/// This Mono HttpClient ignores <c>MaxConnectionsPerServer</c> and uses <see cref="ServicePointManager"/>,
/// whose default is also two connections per host.
/// </summary>
internal static class LocalizationGzip
{
    internal const int Concurrency = 16;

    private static readonly HashSet<string> RaisedHosts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    private static bool _reportedBadUrl;

    public static void Apply(Harmony harmony)
    {
        var queue = AccessTools.Field(AccessTools.TypeByName("_Rainier.Scripts.Localization.GZipLocalizationTableProvider"), "GZipQueueSize");
        if (queue == null)
        {
            FileLog.Error("Fast startup skipped GZipLocalizationTableProvider.GZipQueueSize: field not found");
        }
        else
        {
            queue.SetValue(null, Concurrency);
            FileLog.Info($"Patched GZipLocalizationTableProvider.GZipQueueSize {Concurrency}");
        }

        try
        {
            ServicePointManager.DefaultConnectionLimit = Concurrency;
            FileLog.Info($"Patched ServicePointManager.DefaultConnectionLimit {Concurrency}");
        }
        catch (Exception ex)
        {
            FileLog.Error("Fast startup skipped ServicePointManager.DefaultConnectionLimit: " + ex.Message);
        }

        var provider = AccessTools.TypeByName("_Rainier.Scripts.Network.LocalizationByteDataProvider");
        var method = provider == null ? null : AccessTools.Method(provider, "GetBytesAsyncWithIfModifiedHeader");
        if (method == null)
        {
            FileLog.Error("Fast startup skipped LocalizationByteDataProvider.GetBytesAsyncWithIfModifiedHeader: method not found");
            return;
        }

        harmony.Patch(method, prefix: new HarmonyMethod(typeof(LocalizationGzip), nameof(Prefix)));
        FileLog.Info("Patched LocalizationByteDataProvider.GetBytesAsyncWithIfModifiedHeader");
    }

    public static void Prefix(string url)
    {
        try
        {
            Raise(url);
        }
        catch (Exception ex)
        {
            FileLog.Warn("Localization connection limit skipped: " + ex.Message);
        }
    }

    private static void Raise(string url)
    {
        if (string.IsNullOrEmpty(url))
        {
            return;
        }

        if (!Uri.TryCreate(url.Replace('\\', '/'), UriKind.Absolute, out var uri))
        {
            if (!_reportedBadUrl)
            {
                _reportedBadUrl = true;
                FileLog.Warn("Localization connection limit skipped an unparseable url");
            }

            return;
        }

        var point = ServicePointManager.FindServicePoint(uri);
        if (point.ConnectionLimit < Concurrency)
        {
            point.ConnectionLimit = Concurrency;
        }

        var host = uri.GetLeftPart(UriPartial.Authority);
        lock (RaisedHosts)
        {
            if (!RaisedHosts.Add(host))
            {
                return;
            }
        }

        FileLog.Info($"Localization connection limit {point.ConnectionLimit} for {host}");
    }
}
