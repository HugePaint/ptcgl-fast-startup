using System;
using System.IO;
using System.Reflection;
using System.Threading;
using HarmonyLib;

namespace Doorstop;

public static class Entrypoint
{
    private static int _applied;

    public static void Start()
    {
        try
        {
            Log("doorstop start");
            AppDomain.CurrentDomain.AssemblyLoad += (_, args) =>
            {
                // GetName() walks the assembly codebase. Unity's Mono throws on the
                // non-ASCII game path, and that exception aborts Harmony's dynamic assembly load.
                string fullName;
                try
                {
                    fullName = args.LoadedAssembly.FullName;
                }
                catch
                {
                    return;
                }

                if (fullName != null && fullName.StartsWith("TPCI.RainierClient,", StringComparison.Ordinal))
                {
                    TryApply("assembly-load");
                }
            };
            new Thread(Poll) { IsBackground = true, Name = "FastStartup" }.Start();
        }
        catch (Exception ex)
        {
            Log("doorstop start failed: " + ex);
        }
    }

    private static void Poll()
    {
        for (var i = 0; i < 600 && _applied == 0; i++)
        {
            Thread.Sleep(100);
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                try
                {
                    var fullName = assembly.FullName;
                    if (fullName != null && fullName.StartsWith("TPCI.RainierClient,", StringComparison.Ordinal))
                    {
                        TryApply("poll");
                        return;
                    }
                }
                catch
                {
                    // Some dynamic assemblies cannot report a name from this install path.
                }
            }
        }
    }

    private static void TryApply(string reason)
    {
        if (Interlocked.Exchange(ref _applied, 1) != 0)
        {
            return;
        }

        try
        {
            var dir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            var pluginPath = Path.Combine(dir, "PtcglFastStartup.dll");
            Log("loading " + pluginPath + " via " + reason);
            var plugin = Assembly.LoadFrom(pluginPath);
            var patches = plugin.GetType("PtcglFastStartup.ManifestLoadPatches");
            var apply = patches.GetMethod("Apply", BindingFlags.Public | BindingFlags.Static);
            apply.Invoke(null, new object[] { new Harmony("ptcgl.faststartup") });
            Log("patches applied");
        }
        catch (Exception ex)
        {
            Interlocked.Exchange(ref _applied, 0);
            Log("apply failed: " + ex);
        }
    }

    private static void Log(string message)
    {
        try
        {
            var path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "FastStartup.log");
            File.AppendAllText(path, DateTime.Now.ToString("HH:mm:ss.fff") + " BOOT " + message + Environment.NewLine);
        }
        catch
        {
            // Logging must not take down startup.
        }
    }
}
