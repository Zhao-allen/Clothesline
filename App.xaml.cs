using System.IO;
using System.Windows;
using System.Windows.Forms;
using Clothesline.Models;
using Clothesline.Services;
using Clothesline.UI;

namespace Clothesline;

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
    private HotKey? _screenshotHotKey;

    /// <summary>
    /// Human-readable shortcut for opening Windows Snipping Tool, or
    /// empty if no global hotkey could be registered at startup.
    /// </summary>
    public static string ScreenshotShortcut { get; private set; } = "";

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
        // Fall animation is handled by LineWindow (OnFall).

        _window.ComeDown();
        if (_line.LiveCount > 0)
            _window.Reveal(peekSeconds: 2.5);

        ApplyInboxMode();

        // Always make sure clipboard watching is on, unless the user has
        // explicitly turned it off via the tray menu. The previous logic
        // (gated on a one-shot `clipboardChosen` flag) silently left
        // ClipboardWatcher disabled when the user had toggled it off in a
        // prior version, which made Win+Shift+S produce no hang on the
        // line. Now we read the explicit `clipboardUserDisabled` flag that
        // is only ever set by the tray menu's "Hang copied images" click
        // handler.
        if (!Settings.Default.GetBool("clipboardUserDisabled"))
        {
            ClipboardWatcher.IsEnabled = true;
        }
        ApplyClipboardWatch();

        // Only one global hotkey is left for line visibility: Ctrl+Alt+T.
        // For screenshots, we register a Ctrl+Alt+S that opens the Windows
        // Snipping Tool via the ms-screensketch: URI - this bypasses the
        // keyboard entirely so it works even on systems where Win+Shift+S
        // has been disabled or intercepted by another app.
        _hotKey = HotKey.RegisterDefault(() => _window.Toggle());
        _screenshotHotKey = HotKey.RegisterScreenshot(TakeScreenshot);
        ScreenshotShortcut = _screenshotHotKey?.DisplayString ?? "";

        _tray = new TrayIcon(_line, _window);

        // First-run welcome: reveal the line briefly so the user finds it.
        if (!Settings.Default.GetBool("welcomed"))
        {
            Settings.Default.SetBool("welcomed", true);
            _window.SetWanted(true);
            Refresh();
            _window.Reveal(peekSeconds: 6);
        }

        // First-run Inbox offer: ask once whether Clothesline should
        // watch the Screenshots folder / clipboard for new captures.
        // Mirrors the original Tendedero (macOS) 'offerInbox' flow.
        if (!Inbox.WasOffered)
        {
            Dispatcher.InvokeAsync(async () =>
            {
                await Task.Delay(1200); // let the welcome slide settle
                OfferInbox();
            });
        }
    }

    /// <summary>
    /// Modal dialog asking once whether the user wants Clothesline to
    /// auto-hang screenshots. 'Turn on' enables Inbox + clipboard watch
    /// and saves the flag so we never ask again. 'Not now' just saves
    /// the flag; the user can toggle from the tray menu later.
    /// </summary>
    private void OfferInbox()
    {
        Inbox.WasOffered = true;

        // Build a WPF MessageBox-style dialog manually so we control the
        // button text via I18n (WPF's MessageBox.Show doesn't localize
        // the Yes/No buttons).
        var owner = _window;
        var dialog = new Window
        {
            Title = "Clothesline",
            Owner = owner,
            WindowStyle = WindowStyle.ToolWindow,
            ResizeMode = ResizeMode.NoResize,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Width = 460,
            SizeToContent = SizeToContent.Height,
            ShowInTaskbar = false,
            Background = System.Windows.Media.Brushes.White
        };

        var title = new System.Windows.Controls.TextBlock
        {
            Text = I18n.Get("InboxOfferTitle"),
            FontSize = 16,
            FontWeight = FontWeights.SemiBold,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(24, 24, 24, 8)
        };
        var body = new System.Windows.Controls.TextBlock
        {
            Text = I18n.Get("InboxOfferBody"),
            FontSize = 13,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(24, 0, 24, 20),
            Foreground = System.Windows.Media.Brushes.DarkGray
        };

        var turnOn = new System.Windows.Controls.Button
        {
            Content = I18n.Get("InboxOfferTurnOn"),
            Padding = new Thickness(20, 6, 20, 6),
            Margin = new Thickness(0, 0, 8, 24),
            Background = System.Windows.Media.Brushes.White,
            BorderBrush = System.Windows.Media.Brushes.DarkGray
        };
        var notNow = new System.Windows.Controls.Button
        {
            Content = I18n.Get("InboxOfferNotNow"),
            Padding = new Thickness(20, 6, 20, 6),
            Margin = new Thickness(0, 0, 24, 24),
            Background = System.Windows.Media.Brushes.White,
            BorderBrush = System.Windows.Media.Brushes.DarkGray
        };

        var buttons = new System.Windows.Controls.StackPanel
        {
            Orientation = System.Windows.Controls.Orientation.Horizontal,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Right
        };
        buttons.Children.Add(notNow);
        buttons.Children.Add(turnOn);

        var stack = new System.Windows.Controls.StackPanel();
        stack.Children.Add(title);
        stack.Children.Add(body);
        stack.Children.Add(buttons);
        dialog.Content = stack;

        turnOn.Click += (_, _) =>
        {
            Inbox.IsEnabled = true;
            ClipboardWatcher.IsEnabled = true;
            ApplyInboxMode();
            ApplyClipboardWatch();
            dialog.DialogResult = true;
            dialog.Close();
        };
        notNow.Click += (_, _) =>
        {
            dialog.DialogResult = false;
            dialog.Close();
        };

        dialog.ShowDialog();
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

        // Primary watcher: Clothesline's own folder (inbox) or the system
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

    /// <summary>
    /// Open the Windows Snipping Tool by launching the ms-screensketch:
    /// URI via ShellExecute. This bypasses the keyboard entirely, so it
    /// works on systems where Win+Shift+S has been disabled or is being
    /// intercepted by another app (OneNote screen clipping, Xbox Game
    /// Bar, system policy, ...). The user still drags to pick a region
    /// in the OS's own snipping overlay; the result lands on the
    /// clipboard, where ClipboardWatcher picks it up and hangs it.
    /// </summary>
    public void TakeScreenshot()
    {
        if (_window.IsRevealed) _window.SetRevealed(false);

        // Try the modern URI first; fall back to the older ms-screenclip:
        // alias (Win10 1803-era), then to the legacy SnippingTool.exe.
        // The ?? chain is wrapped in a discard assignment because C#
        // disallows a bare ?? expression as a statement.
        _ = TryLaunch("ms-screensketch:")
            ?? TryLaunch("ms-screenclip:")
            ?? TryLaunchLegacySnippingTool();
    }

    private static string? TryLaunch(string uri)
    {
        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo(uri)
            {
                UseShellExecute = true,
                Verb = "open"
            };
            System.Diagnostics.Process.Start(psi);
            return uri;
        }
        catch
        {
            return null;
        }
    }

    private static string? TryLaunchLegacySnippingTool()
    {
        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo("snippingtool.exe", "/clip")
            {
                UseShellExecute = true
            };
            System.Diagnostics.Process.Start(psi);
            return "snippingtool.exe";
        }
        catch
        {
            return null;
        }
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
        _screenshotHotKey?.Dispose();
        _watcher?.Dispose();
        _safetyWatcher?.Dispose();
        foreach (var w in _folderWatchers) w.Dispose();
        _clipboardWatcher?.Dispose();
        _tray.Dispose();
        _window.Close();
        Shutdown();
    }
}
