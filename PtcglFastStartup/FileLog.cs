using System;
using System.IO;

namespace PtcglFastStartup;

internal static class FileLog
{
    private static readonly object Gate = new object();
    private static readonly string Path = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "FastStartup.log");

    public static void Info(string message) => Write("INFO", message);

    public static void Warn(string message) => Write("WARN", message);

    public static void Error(string message) => Write("ERROR", message);

    private static void Write(string level, string message)
    {
        var line = DateTime.Now.ToString("HH:mm:ss.fff") + " " + level + " " + message + Environment.NewLine;
        lock (Gate)
        {
            System.IO.File.AppendAllText(Path, line);
        }
    }
}
