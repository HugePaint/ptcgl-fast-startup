using System;
using HarmonyLib;

namespace PtcglFastStartup;

/// <summary>
/// <c>Client.Setup</c> and <c>InventoryQueries.Setup</c> already time their steps and log them with <c>LogFlag.Performance</c>.
/// The logger passed in is a <c>NullLogger</c>, so those lines never reach <c>Player.log</c>.
/// Inventory logs the compendium step and the following parallel item queries under the same <c>GrantsNewAccountAsync</c> label.
/// </summary>
internal static class SetupTimingLog
{
    private const string GrantPrefix = "completed GrantsNewAccountAsync in ";

    private static readonly object Gate = new object();
    private static bool _insideInventory;
    private static int _grantLogs;

    public static void Apply(Harmony harmony)
    {
        var type = AccessTools.TypeByName("SharedLogicUtils.source.Logging.Implementations.NullLogger");
        var method = type == null ? null : AccessTools.Method(type, "Log");
        if (method == null)
        {
            FileLog.Error("Fast startup skipped NullLogger.Log: method not found");
            return;
        }

        harmony.Patch(method, postfix: new HarmonyMethod(typeof(SetupTimingLog), nameof(Postfix)));
        FileLog.Info("Patched NullLogger.Log");
    }

    public static void Postfix(string message)
    {
        if (string.IsNullOrEmpty(message))
        {
            return;
        }

        string line;
        lock (Gate)
        {
            if (message.IndexOf("Begin InventoryQueries.Setup", StringComparison.Ordinal) >= 0)
            {
                _insideInventory = true;
                _grantLogs = 0;
            }

            line = message;
            if (_insideInventory && message.StartsWith(GrantPrefix, StringComparison.Ordinal))
            {
                _grantLogs++;
                if (_grantLogs == 2)
                {
                    line = message + " [GetCompendiumAsync]";
                }
                else if (_grantLogs == 3)
                {
                    line = message + " [cards, outfits, decks, boosters]";
                }
            }

            var keep = line.IndexOf("ClientHandler", StringComparison.Ordinal) >= 0
                || line.IndexOf("InventoryQueries", StringComparison.Ordinal) >= 0
                || (_insideInventory && line.StartsWith("completed ", StringComparison.Ordinal));
            if (message.IndexOf("End InventoryQueries.setup", StringComparison.Ordinal) >= 0)
            {
                _insideInventory = false;
            }

            if (!keep)
            {
                return;
            }
        }

        FileLog.Info("setup " + line);
    }
}
