using System.IO;
using System.Windows;
using System.Windows.Forms;
using Tendedero.Models;
using Tendedero.Services;
using Tendedero.UI;

namespace Tendedero;

internal partial class App : Application
{
    public static App Instance { get; private set; } = null!;

    private Line _line = null!;
    private LineWindow _window = null!;
    private TrayIcon _tray = null!;
    private ScreenshotWatcher _watcher = null!;
    private ScreenshotWatcher? _safetyWatcher;
    private List<ScreenshotWatcher> _folderWatchers = new();
    private ClipboardWatcher? _clipboardWatcher;
    private HotKey? _hotKey;

    public static bool OpensFromMenuBar
    {
        get => !Settings.Default.GetBool("menuBarRevealOff");
        set => Settings.Default.SetBool("menuBarRevealOff", !value);
    }

    public static bool PeeksAtNew
    {
        get => !Settings.Default.GetBool("quietHang");
        set => Settings.Default.SetBool("quietHang", !value);
    }

    public static List<string> ExtraFolders
    {
        get => Settings.Default.GetStringList("extraFolders")
            .Where(Directory.Exists).ToList();
        set => Settings.Default.SetStringList("extraFolders", value);
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        Instance = this;
        ShutdownMode = ShutdownMode.OnExplicitShutdown;
        I18n.LoadOverride();

        _line = new Line();
        _window = new LineWindow(_line);
        _window.RequestRefresh += Refresh;
        _line.ItemsChanged += () => _window.ItemsChanged();
        _line.Fall += _ => { /* fall animation handled by card removal */ };

        _window.ComeDown();
        if (_line.LiveCount > 0)
            _window.Reveal(peekSeconds: 2.5);

        ApplyInboxMode();
        ApplyClipboardWatch();

        _hotKey = HotKey.RegisterDefault(() => _window.Toggle());

        _tray = new TrayIcon(_line, _window);

        // First-run welcome: reveal the line briefly so the user finds it.
        if (!Settings.Default.GetBool("welcomed"))
        {
            Settings.Default.SetBool("welcomed", true);
            _window.SetWanted(true);
            Refresh();
            _window.Reveal(peekSeconds: 6);
        }
    }

    public void ApplyInboxMode()
    {
        RestartWatchers();
    }

    public void ApplyClipboardWatch()
    {
        if (ClipboardWatcher.IsEnabled)
        {
            _clipboardWatcher ??= new ClipboardWatcher(HangCapture);
            _clipboardWatcher.Start();
        }
        else
        {
            _clipboardWatcher?.Stop();
        }
        _tray?.UpdateIcon();
    }

    internal void RestartWatchers()
    {
        _watcher?.Dispose();
        _safetyWatcher?.Dispose();
        _safetyWatcher = null;
        foreach (var w in _folderWatchers) w.Dispose();
        _folderWatchers.Clear();

        // Primary watcher: Tendedero's own folder (inbox) or the system
        // Screenshots folder.
        string primary = Inbox.IsEnabled ? Inbox.Folder : Inbox.ScreenshotsFolder;
        Inbox.EnsureFolder();
        _watcher = new ScreenshotWatcher(primary, HangCapture, _line.Prune);
        _watcher.Start();

        // Safety: also watch the Desktop in case captures land there.
        if (Inbox.IsEnabled && !SamePath(primary, Inbox.DesktopFolder))
        {
            _safetyWatcher = new ScreenshotWatcher(Inbox.DesktopFolder, HangCapture, _line.Prune);
            _safetyWatcher.Start();
        }

        // Extra user folders.
        foreach (var f in ExtraFolders)
        {
            if (SamePath(f, primary)) continue;
            var w = new ScreenshotWatcher(f, HangCapture, _line.Prune);
            w.Start();
            _folderWatchers.Add(w);
        }
    }

    private static bool SamePath(string a, string b) =>
        string.Equals(Path.GetFullPath(a).TrimEnd(Path.DirectorySeparatorChar),
                      Path.GetFullPath(b).TrimEnd(Path.DirectorySeparatorChar),
                      StringComparison.OrdinalIgnoreCase);

    private void HangCapture(string path)
    {
        Dispatcher.Invoke(() =>
        {
            var screen = LineWindow.ScreenUnderPointer();
            _window.ComeDown(screen);
            _line.Hang(path);
        });
    }

    public void Refresh()
    {
        bool blocked = FullScreen.BlocksLineUnderPointer();
        if (_window.Wanted && !blocked) _window.Present();
        else _window.Dismiss();
        if (_window.Wanted) _window.StartMouseTracking();
        else _window.StopMouseTracking();
    }

    public void ChooseExtraFolder()
    {
        using var dlg = new FolderBrowserDialog { Description = I18n.Get("FolderDialogDescription") };
        if (dlg.ShowDialog() != System.Windows.Forms.DialogResult.OK) return;
        var folders = ExtraFolders;
        if (folders.Count >= 3) return;
        if (folders.Any(f => SamePath(f, dlg.SelectedPath))) return;
        folders.Add(dlg.SelectedPath);
        ExtraFolders = folders;
        RestartWatchers();
    }

    public void SetKeep(int? n)
    {
        Line.KeepOnLine = n;
        _window.UpdateCapacity();
        _line.Trim();
    }

    public void SetSize(Layout.SizeKind size)
    {
        Layout.Size = size;
        _window.PlaceOnScreen();
        _window.UpdateCapacity();
        foreach (var item in _line.Items.ToList()) _line.ReloadThumbnail(item.Url);
    }

    public void ToggleStartup()
    {
        StartupManager.SetEnabled(!StartupManager.IsEnabled);
    }

    public void ShutdownApp()
    {
        _hotKey?.Dispose();
        _watcher?.Dispose();
        _safetyWatcher?.Dispose();
        foreach (var w in _folderWatchers) w.Dispose();
        _clipboardWatcher?.Dispose();
        _tray.Dispose();
        _window.Close();
        Shutdown();
    }
}
