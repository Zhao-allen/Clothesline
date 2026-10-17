using System;
using System.Collections.Generic;
using System.IO;

namespace ClotheslineSetup;

/// <summary>
/// Replaces the WinForms Program.cs static globals for the WPF installer.
/// Holds language choice, target folder, shortcut flags, and install logging.
/// </summary>
internal static class InstallContext
{
    public static string Language = Loc.Default;
    public static string? TargetFolder;
    public static bool MakeDesktopShortcut = true;
    public static bool MakeStartMenuShortcut = true;

    private static readonly List<string> LogLines = new();

    public static void Log(string line)
    {
        lock (LogLines)
        {
            LogLines.Add($"{DateTime.Now:HH:mm:ss} {line}");
        }
    }

    public static void FlushLog()
    {
        try
        {
            var dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Clothesline");
            Directory.CreateDirectory(dir);
            lock (LogLines) File.AppendAllLines(Path.Combine(dir, "install.log"), LogLines);
        }
        catch { /* a log we cannot write is not worth failing an install over */ }
    }
}
