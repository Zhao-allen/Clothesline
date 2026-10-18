using System.Diagnostics;

namespace ClotheslineSetup;

/// <summary>
/// One window, two jobs: install and uninstall, picked by the command line.
/// The language picked here is also handed to the app on first run, so the
/// installer and the application never disagree about what language is in use.
///
///   Clothesline-Setup.exe                install, with UI
///   Clothesline-Setup.exe /silent        install with defaults, no UI
///   Clothesline-Setup.exe /uninstall     uninstall, with UI
///   Clothesline-Setup.exe /uninstall /silent
/// </summary>
internal static class Program
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

    [STAThread]
    private static int Main(string[] args)
    {
        ApplicationConfiguration.Initialize();

        bool uninstall = args.Contains("/uninstall", StringComparer.OrdinalIgnoreCase);
        bool silent = args.Contains("/silent", StringComparer.OrdinalIgnoreCase)
                   || args.Contains("/quiet", StringComparer.OrdinalIgnoreCase);

        // A previous install leaves the app in the tray, and it holds the very
        // files the uninstaller is about to delete.
        if (uninstall && Installer.IsRunning())
        {
            if (silent) { Log("app still running; refusing to uninstall"); return 2; }

            var s = Loc.For(Language);
            MessageBox.Show(s.RunningBody, s.Running, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return 2;
        }

        if (silent)
        {
            return uninstall ? RunSilentUninstall() : RunSilentInstall(args);
        }

        using var form = new MainForm(uninstall);
        Application.Run(form);
        return form.Result;
    }

    // MARK: silent

    /// <summary>
    /// Installs with the defaults and writes a log instead of showing a
    /// window. /dir= overrides the destination, so a machine can be provisioned
    /// without anybody sitting at it.
    /// </summary>
    private static int RunSilentInstall(string[] args)
    {
        var folder = Arg(args, "dir") ?? Installer.DefaultTarget();
        Language = Arg(args, "lang") ?? Loc.Default;
        TargetFolder = folder;
        MakeDesktopShortcut = !Has(args, "nodesktop");
        MakeStartMenuShortcut = !Has(args, "nostartmenu");

        Log($"silent install -> {folder} (lang {Language})");

        try
        {
            Installer.Install(folder, _ => { }, _ => { });
            Log($"silent install complete: {folder}");
            FlushLog();
            return 0;
        }
        catch (Exception ex)
        {
            Log($"silent install FAILED: {ex}");
            FlushLog();
            return 1;
        }
    }

    private static int RunSilentUninstall()
    {
        using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
            $@"Software\Microsoft\Windows\CurrentVersion\Uninstall\{Installer.AppName}");
        var folder = key?.GetValue("InstallLocation") as string ?? Installer.DefaultTarget();

        Log($"silent uninstall <- {folder}");
        try
        {
            Installer.Uninstall(folder, _ => { });
            Log("silent uninstall complete");
            FlushLog();
            return 0;
        }
        catch (Exception ex)
        {
            Log($"silent uninstall FAILED: {ex}");
            FlushLog();
            return 1;
        }
    }

    private static string? Arg(string[] args, string name)
    {
        foreach (var a in args)
        {
            if (a.StartsWith("/" + name + "=", StringComparison.OrdinalIgnoreCase))
                return a[("/" + name + "=").Length..];
        }
        return null;
    }

    private static bool Has(string[] args, string name) =>
        args.Any(a => string.Equals(a, "/" + name, StringComparison.OrdinalIgnoreCase));

    // MARK: log

    /// <summary>
    /// Written to %LocalAppData%\Clothesline so it sits next to the app's own
    /// log. Silent installs have no window to report through, so this is the
    /// only record that anything happened at all.
    /// </summary>
    internal static void FlushLog()
    {
        try
        {
            var dir = Path.Combine(Environment.GetFolderPath(
                Environment.SpecialFolder.LocalApplicationData), "Clothesline");
            Directory.CreateDirectory(dir);
            lock (LogLines) File.AppendAllLines(Path.Combine(dir, "install.log"), LogLines);
        }
        catch { /* a log we cannot write is not worth failing an install over */ }
    }
}
