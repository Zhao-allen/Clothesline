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
    private bool _isTakingScreenshot;

    /// <summary>
    /// Human-readable shortcut for the screenshot, or empty if no
    /// global hotkey could be registered at startup. Surfaced to the
    /// tray menu so it shows "Win+Alt+S" or "Ctrl+Alt+S" depending on
    /// which fallback actually took effect - never a stale hard-coded
    /// string.
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
        ApplyClipboardWatch();

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

        var title = new TextBlock
        {
            Text = I18n.Get("InboxOfferTitle"),
            FontSize = 16,
            FontWeight = FontWeights.SemiBold,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(24, 24, 24, 8)
        };
        var body = new TextBlock
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
    /// Trigger the OS built-in region screenshot shortcut (Win+Shift+S).
    /// Windows captures the region to the clipboard; we then wait for the
    /// clipboard image to land, save it as a PNG, and hang it on the line.
    /// This mirrors the original Tendedero design on macOS: the app never
    /// takes screenshots itself, it only consumes what the OS produces.
    /// </summary>
    public void TakeScreenshot()
    {
        // Re-entrancy guard: the hotkey can fire again before the first
        // synthetic keystroke finishes.
        if (_isTakingScreenshot) return;
        _isTakingScreenshot = true;

        if (_window.IsRevealed) _window.SetRevealed(false);

        Dispatcher.InvokeAsync(async () =>
        {
            await Task.Delay(120); // let the line slide out of view

            // Snapshot the clipboard sequence BEFORE pressing Win+Shift+S so
            // we can detect the NEW image that will land on the clipboard
            // after the user finishes the region pick.
            uint beforeSeq = ClipboardWatcherNative.GetSequenceNumber();

            // Make the line fully transparent so it never appears in the shot.
            double prevOpacity = _window.Opacity;
            _window.Opacity = 0;
            string? path = null;
            try
            {
                TriggerWindowsScreenshot();
                path = await WaitForClipboardImage(beforeSeq, TimeSpan.FromSeconds(60));
            }
            finally
            {
                _window.Opacity = prevOpacity;
                _isTakingScreenshot = false;
            }

            if (path is not null)
            {
                Sound.PlayShot();
                HangCapture(path);
                _window.Reveal(peekSeconds: 3);
            }
        });
    }

    /// <summary>
    /// Poll the clipboard for an image that lands AFTER the given baseline
    /// sequence number. Returns the saved PNG path, or null on timeout.
    /// Bypasses the user's "Hang copied images" setting because the user
    /// explicitly invoked the screenshot shortcut.
    /// </summary>
    private static async Task<string?> WaitForClipboardImage(uint beforeSeq, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            await Task.Delay(300);

            uint nowSeq;
            IDataObject? data;
            try
            {
                nowSeq = ClipboardWatcherNative.GetSequenceNumber();
                if (nowSeq == beforeSeq) continue;
                data = Clipboard.GetDataObject();
            }
            catch { continue; }

            var png = ClipboardWatcher.TryExtractImage(data);
            if (png is null || png.Length == 0) continue;

            var path = ClipboardWatcher.SavePng(png);
            if (path is not null) return path;
        }
        return null;
    }

    /// <summary>
    /// Press Win+Shift+S via SendInput. The OS handles the region picker,
    /// capture, and clipboard copy.
    /// </summary>
    private static void TriggerWindowsScreenshot()
    {
        // Order: Win down, Shift down, S down, S up, Shift up, Win up.
        // Use VK_LWIN / VK_LSHIFT so the modifiers are not ambiguous, which
        // can happen with the generic VK_MENU / VK_SHIFT virtual codes.
        var inputs = new NativeMethods.INPUT[6];
        for (int i = 0; i < inputs.Length; i++)
        {
            inputs[i].type = NativeMethods.INPUT_KEYBOARD;
        }
        inputs[0].u.ki.wVk = NativeMethods.VK_LWIN;
        inputs[1].u.ki.wVk = NativeMethods.VK_LSHIFT;
        inputs[2].u.ki.wVk = NativeMethods.VK_S;
        inputs[3].u.ki.wVk = NativeMethods.VK_S;
        inputs[3].u.ki.dwFlags = NativeMethods.KEYEVENTF_KEYUP;
        inputs[4].u.ki.wVk = NativeMethods.VK_LSHIFT;
        inputs[4].u.ki.dwFlags = NativeMethods.KEYEVENTF_KEYUP;
        inputs[5].u.ki.wVk = NativeMethods.VK_LWIN;
        inputs[5].u.ki.dwFlags = NativeMethods.KEYEVENTF_KEYUP;

        NativeMethods.SendInput((uint)inputs.Length, inputs,
            System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.INPUT>());
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
