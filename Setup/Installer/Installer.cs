using System.Diagnostics;
using System.IO.Compression;
using System.Reflection;
using Microsoft.Win32;

namespace ClotheslineSetup;

/// <summary>
/// Everything the installer does to the machine, kept out of the form so the
/// two can be reasoned about separately.
/// </summary>
internal static class Installer
{
    public const string AppName = "Clothesline";
    public const string ExeName = "Clothesline.exe";
    public const string Version = "0.2.0";

    /// <summary>
    /// Per-user by default: no UAC prompt, works for every account on the
    /// machine, and uninstalls cleanly from Settings. %ProgramFiles% would
    /// need elevation and would break for a standard user.
    /// </summary>
    public static string DefaultTarget() =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                     "Programs", AppName);

    public static string TargetExe(string folder) => Path.Combine(folder, ExeName);

    // MARK: install

    public static void Install(string folder, Action<string> report, Action<int> progress)
    {
        report(Loc.For(Program.Language).Extracting);
        ExtractPayload(folder, progress);

        report(Loc.For(Program.Language).Shortcuts);
        CreateShortcuts(folder);

        report(Loc.For(Program.Language).Registry);
        WriteUninstallEntry(folder);

        // The app starts itself from the tray; nothing else to do here.
        progress(100);
    }

    private static void ExtractPayload(string folder, Action<int> progress)
    {
        Directory.CreateDirectory(folder);

        var asm = Assembly.GetExecutingAssembly();
        using var stream = asm.GetManifestResourceStream("payload.zip")
            ?? throw new InvalidOperationException("payload.zip is missing from the installer");

        // Count first so the bar can actually move: a zip entry has no
        // reliable size until it is read, and a bar that jumps to 100% at the
        // end tells the user nothing.
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
        int total = archive.Entries.Count;
        if (total == 0) throw new InvalidOperationException("payload.zip is empty");

        int done = 0;
        foreach (var entry in archive.Entries)
        {
            // A directory entry ends in '/', and writing it as a file would
            // fail on the trailing separator.
            var dest = Path.Combine(folder, entry.FullName.Replace('/', Path.DirectorySeparatorChar));
            if (entry.FullName.EndsWith('/'))
            {
                Directory.CreateDirectory(dest);
            }
            else
            {
                Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                entry.ExtractToFile(dest, overwrite: true);
            }

            done++;
            // Report in whole percent, and only when it changes, so the UI
            // thread is not flooded with thousands of repaints.
            int pct = done * 100 / total;
            if (pct != (done - 1) * 100 / total) progress(pct);
        }
    }

    private static void CreateShortcuts(string folder)
    {
        if (Program.MakeDesktopShortcut)
            WriteShortcut(DesktopPath(), folder, "desktop");

        if (Program.MakeStartMenuShortcut)
            WriteShortcut(StartMenuPath(), folder, "startmenu");
    }

    private static string DesktopPath() =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
                     $"{AppName}.lnk");

    private static string StartMenuPath()
    {
        var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.StartMenu),
                               "Programs", AppName);
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, $"{AppName}.lnk");
    }

    /// <summary>
    /// A .lnk is an OLE compound file, so it is written through WScript.Shell
    /// rather than by hand. The working directory matters: the app writes its
    /// log and screenshots relative to %APPDATA%, but a missing working
    /// directory makes some WPF builds look for resources next to the exe.
    /// </summary>
    private static void WriteShortcut(string path, string targetFolder, string kind)
    {
        try
        {
            dynamic shell = Activator.CreateInstance(
                Type.GetTypeFromProgID("WScript.Shell")!)!;
            dynamic shortcut = shell.CreateShortcut(path);
            shortcut.TargetPath = TargetExe(targetFolder);
            shortcut.WorkingDirectory = targetFolder;
            shortcut.Description = AppName;
            shortcut.IconLocation = TargetExe(targetFolder) + ",0";
            shortcut.Save();
            Program.Log($"shortcut ({kind}): {path}");
        }
        catch (Exception ex)
        {
            // Full type and HRESULT, not just the message: the localized text
            // "unable to save shortcut" is the same whether the parent folder
            // is missing, the file is locked, or the COM call was rejected,
            // and guessing between them wastes a build cycle.
            var h = ex is System.Runtime.InteropServices.COMException ce
                ? $" hresult=0x{ce.HResult:X8}" : "";
            Program.Log($"shortcut ({kind}) FAILED {ex.GetType().FullName}: {ex.Message}{h}");
        }
    }

    // MARK: uninstall registration

    private static void WriteUninstallEntry(string folder)
    {
        // HKCU, matching the per-user install above.
        using var key = Registry.CurrentUser.CreateSubKey(
            $@"Software\Microsoft\Windows\CurrentVersion\Uninstall\{AppName}");
        if (key is null) return;

        // NOT Assembly.Location: in a single-file app that returns an empty
        // string, which would write an UninstallString of `"" /uninstall` and
        // leave the user with an entry in Settings that does nothing. The
        // process path is the only thing that names the real executable.
        var exe = Environment.ProcessPath;
        if (string.IsNullOrEmpty(exe))
        {
            Program.Log("no process path; uninstall entry written without UninstallString");
            return;
        }
        key.SetValue("DisplayName", AppName, RegistryValueKind.String);
        key.SetValue("DisplayVersion", Version, RegistryValueKind.String);
        key.SetValue("Publisher", "Clothesline", RegistryValueKind.String);
        key.SetValue("DisplayIcon", TargetExe(folder), RegistryValueKind.String);
        key.SetValue("InstallLocation", folder, RegistryValueKind.String);
        key.SetValue("UninstallString", $"\"{exe}\" /uninstall", RegistryValueKind.String);
        key.SetValue("QuietUninstallString", $"\"{exe}\" /uninstall /quiet", RegistryValueKind.String);
        key.SetValue("NoModify", 1, RegistryValueKind.DWord);
        key.SetValue("NoRepair", 1, RegistryValueKind.DWord);

        long bytes = 0;
        try
        {
            foreach (var f in Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories))
                bytes += new FileInfo(f).Length;
        }
        catch { /* a size estimate is not worth failing over */ }

        key.SetValue("EstimatedSize", (int)(bytes / 1024), RegistryValueKind.DWord);
        Program.Log($"registered uninstall entry -> {folder} ({(bytes / 1024 / 1024)} MB)");
    }

    // MARK: uninstall

    public static void Uninstall(string folder, Action<string> report)
    {
        report(Loc.For(Program.Language).Uninstalling);

        // Shortcuts first: if a later step fails, at least the user is not
        // left with a .lnk pointing at a folder that no longer exists.
        foreach (var path in new[] { DesktopPath(), StartMenuPath() })
        {
            try { if (File.Exists(path)) File.Delete(path); }
            catch (Exception ex) { Program.Log($"shortcut delete failed {path}: {ex.Message}"); }
        }

        // Remove the program folder but never %APPDATA%: that holds the user's
        // screenshots and settings, and they are not ours to delete.
        try
        {
            if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true);
        }
        catch (Exception ex)
        {
            Program.Log($"folder delete failed {folder}: {ex.Message}");
            throw;
        }

        try
        {
            Registry.CurrentUser.DeleteSubKeyTree(
                $@"Software\Microsoft\Windows\CurrentVersion\Uninstall\{AppName}", throwOnMissingSubKey: false);
        }
        catch (Exception ex)
        {
            Program.Log($"registry delete failed: {ex.Message}");
        }

        Program.Log($"uninstalled {folder}");
    }

    public static bool IsRunning()
    {
        var me = Environment.ProcessId;
        return Process.GetProcessesByName("Clothesline")
                      .Any(p => p.Id != me);
    }

    public static void Launch(string folder)
    {
        var exe = TargetExe(folder);
        if (!File.Exists(exe)) return;
        Process.Start(new ProcessStartInfo(exe) { WorkingDirectory = folder });
    }
}
