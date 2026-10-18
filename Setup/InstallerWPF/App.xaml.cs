using System.IO;
using System.Windows;
using Microsoft.Win32;

namespace ClotheslineSetup;

public partial class App : Application
{
    // 全局崩溃捕获 —— 否则进程在 Application_Startup 之前崩的话，
    // 连 InstallContext.Log 都走不到，CI 只能看到 exit code 为空。
    public App()
    {
        DispatcherUnhandledException += (_, args) =>
            DumpCrash("DispatcherUnhandledException", args.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            DumpCrash("AppDomainUnhandledException", args.ExceptionObject as Exception);
    }

    private static void DumpCrash(string kind, Exception? ex)
    {
        try
        {
            var dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Clothesline");
            Directory.CreateDirectory(dir);
            var log = Path.Combine(dir, "install.log");
            var line = $"[{DateTime.Now:HH:mm:ss}] CRASH ({kind}): {ex}\n\n{ex?.StackTrace}\n";
            File.AppendAllText(log, line);
        }
        catch { /* best-effort */ }
    }

    private void Application_Startup(object sender, StartupEventArgs e)
    {
        bool uninstall = e.Args.Contains("/uninstall", StringComparer.OrdinalIgnoreCase);
        bool silent = e.Args.Contains("/silent", StringComparer.OrdinalIgnoreCase)
                   || e.Args.Contains("/quiet", StringComparer.OrdinalIgnoreCase);

        if (uninstall && Installer.IsRunning())
        {
            if (silent)
            {
                InstallContext.Log("app still running; refusing to uninstall");
                InstallContext.FlushLog();
                Shutdown(2);
                return;
            }

            var s = Loc.For(InstallContext.Language);
            MessageBox.Show(s.RunningBody, s.Running, MessageBoxButton.OK, MessageBoxImage.Warning);
            Shutdown(2);
            return;
        }

        if (silent)
        {
            int code = uninstall ? RunSilentUninstall() : RunSilentInstall(e.Args);
            Shutdown(code);
            return;
        }

        var window = new MainWindow(uninstall);
        window.Show();
    }

    private static int RunSilentInstall(string[] args)
    {
        var folder = Arg(args, "dir") ?? Installer.DefaultTarget();
        InstallContext.Language = Arg(args, "lang") ?? Loc.Default;
        InstallContext.TargetFolder = folder;
        InstallContext.MakeDesktopShortcut = !Has(args, "nodesktop");
        InstallContext.MakeStartMenuShortcut = !Has(args, "nostartmenu");

        InstallContext.Log($"silent install -> {folder} (lang {InstallContext.Language})");

        try
        {
            Installer.Install(folder, _ => { }, _ => { });
            InstallContext.Log($"silent install complete: {folder}");
            InstallContext.FlushLog();
            return 0;
        }
        catch (Exception ex)
        {
            InstallContext.Log($"silent install FAILED: {ex}");
            InstallContext.FlushLog();
            return 1;
        }
    }

    private static int RunSilentUninstall()
    {
        using var key = Registry.CurrentUser.OpenSubKey(
            $@"Software\Microsoft\Windows\CurrentVersion\Uninstall\{Installer.AppName}");
        var folder = key?.GetValue("InstallLocation") as string ?? Installer.DefaultTarget();

        InstallContext.Log($"silent uninstall <- {folder}");
        try
        {
            Installer.Uninstall(folder, _ => { });
            InstallContext.Log("silent uninstall complete");
            InstallContext.FlushLog();
            return 0;
        }
        catch (Exception ex)
        {
            InstallContext.Log($"silent uninstall FAILED: {ex}");
            InstallContext.FlushLog();
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
}
