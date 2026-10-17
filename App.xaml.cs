using System.IO;
using System.Windows;
using System.Windows.Forms;
using System.Windows.Media.Imaging;
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
    private System.Windows.Threading.DispatcherTimer? _cursorSampler;
    private (int x, int y, DateTime t) _cursorSample;
    private int _flightsInProgress;

    // v0.1.9: removed _screenshotHotKey and ScreenshotShortcut. The
    // screenshot flow is back to the user pressing Win+Shift+S themselves.
    // No global hotkey, no tray 'Take Screenshot' menu item, no ms-screensketch:
    // URI launch. Just ClipboardWatcher listening for clipboard changes.

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

        // Catch-all logging, installed before anything else can throw. A crash
        // on a background thread used to leave no trace at all - the window
        // simply disappeared and the only evidence was a stack in an event
        // viewer nobody reads. We still let the exception kill the app (we do
        // NOT set args.Handled), we just record the path first.
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            if (args.ExceptionObject is Exception ex) Diag.Fatal("crash", ex);
            else Diag.Write("crash", $"non-CLR exception: {args.ExceptionObject}");
        };
        DispatcherUnhandledException += (_, args) => Diag.Fatal("crash", args.Exception);
        TaskScheduler.UnobservedTaskException += (_, args) => Diag.Fatal("task", args.Exception);

        Diag.Session($"v{TrayIcon.AppVersion} args=[{string.Join(" ", e.Args)}]");

        // Diagnostic entry point: render a code-built surface to a PNG and
        // exit. See UI/UiPreview.cs.
        if (UiPreview.TryHandle(e.Args))
        {
            Shutdown();
            return;
        }

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

        // Only one global hotkey remains: Ctrl+Alt+T to show/hide the line.
        // The screenshot shortcut is the OS's own Win+Shift+S: Clothesline
        // never takes screenshots itself, it just consumes what Windows
        // produces (mirrors the original Tendedero design on macOS).
        _hotKey = HotKey.RegisterDefault(() => _window.Toggle());

        // So "follow the system" actually follows: without this the resolved
        // theme is only read once at launch and a Windows light/dark flip
        // would not be picked up until the next start.
        AppTheme.WatchSystemChanges();

        _tray = new TrayIcon(_line, _window);

        // Cursor sampler for capture-flight origin (Tier 2 positioning).
        _cursorSampler = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
        _cursorSampler.Tick += (_, _) =>
        {
            if (NativeMethods.GetCursorPos(out var p))
                _cursorSample = (p.X, p.Y, DateTime.Now);
        };
        _cursorSampler.Start();

        Diag.Write("app", $"ready - live={_line.LiveCount} clipboard={ClipboardWatcher.IsEnabled} " +
                          $"inbox={Inbox.IsEnabled} hotkey={(_hotKey is null ? "NONE" : _hotKey.DisplayString)}");

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
            _clipboardWatcher ??= new ClipboardWatcher(url =>
            {
                // The capture was just written into a watched folder. Tell
                // every watcher that owns it so the folder scan does not
                // announce the same file a second time - Hang() dedups the
                // card, but the second pass still re-runs ComeDown and
                // resets the peek timer on every clipboard capture. Adopt
                // self-filters to its own folder, so calling it on all of
                // them is safe.
                _watcher?.Adopt(url);
                _safetyWatcher?.Adopt(url);
                foreach (var w in _folderWatchers) w.Adopt(url);
                HangCapture(url);
            });
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

        // Safety: also watch the Desktop in case captures land there. The
        // desktop holds mixed content, so this one is name-gated: only
        // files that call themselves screenshots hang, everything else
        // (a photo saved from an editor, a download dropped there) is
        // left alone. The Inbox and the system Screenshots folder are
        // dedicated capture locations and stay ungated.
        if (Inbox.IsEnabled && !SamePath(primary, Inbox.DesktopFolder))
        {
            _safetyWatcher = new ScreenshotWatcher(Inbox.DesktopFolder, HangCapture, _line.Prune,
                                                   requireScreenshotName: true);
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
        Diag.Write("app", $"capture arrived -> {Path.GetFileName(path)}");
        Dispatcher.Invoke(() =>
        {
            var screen = LineWindow.ScreenUnderPointer();
            _window.ComeDown(screen);

            bool fly = _flightsInProgress < 2;
            var id = _line.Hang(path, flying: fly);

            if (fly && id is not null)
            {
                _flightsInProgress++;
                StartFlight(id.Value, path, screen);
            }
        });
    }

    private void StartFlight(Guid id, string path, Screen screen)
    {
        int count = _line.Items.Count;
        int index = _line.Items.ToList().FindIndex(i => i.Id == id);
        if (index < 0) { _flightsInProgress--; return; }

        double w = _window.Width;
        double x = Layout.X(index, count, w, _line.VisibleCount, _line.Scroll);
        double ropeY = Layout.RopeY(x, w);
        double cardH = Layout.PanelHeight - ropeY;
        var to = new Rect(x - Layout.CardWidth / 2, ropeY - Layout.PinAbove, Layout.CardWidth, cardH);

        var photo = Thumbnail.Make(path, int.MaxValue);
        if (photo is null) { _flightsInProgress--; return; }

        var from = ComputeFromRect(photo, screen, to);
        var item = _line.Items.First(i => i.Id == id);

        FlightOverlay.Ensure(screen).StartFlight(item, from, to, photo, () =>
        {
            Dispatcher.Invoke(() =>
            {
                _line.Land(id);
                _flightsInProgress--;
            });
        });
    }

    private Rect ComputeFromRect(BitmapSource photo, Screen screen, Rect to)
    {
        var bounds = screen.Bounds;
        double scale = LineWindow.GetDpiForScreen(screen) / 96.0;
        double screenW = bounds.Width / scale;
        double screenH = bounds.Height / scale;
        double imgW = photo.PixelWidth / scale;
        double imgH = photo.PixelHeight / scale;

        // Tier 1 — full-screen match
        if (Math.Abs(imgW - screenW) / screenW < 0.01 && Math.Abs(imgH - screenH) / screenH < 0.01)
            return new Rect(0, 0, screenW, screenH);

        // Tier 2 — cursor anchor (bottom-right pinned to cursor)
        var (cx, cy, t) = _cursorSample;
        if ((DateTime.Now - t).TotalMilliseconds < 700)
        {
            double left = Math.Max(0, cx / scale - imgW);
            double top = Math.Max(0, cy / scale - imgH);
            return new Rect(left, top, imgW, imgH);
        }

        // Tier 3 — screen centre
        return new Rect(screenW / 2 - imgW / 2, screenH / 2 - imgH / 2, imgW, imgH);
    }

    public void Refresh()
    {
        bool blocked = FullScreen.BlocksLineUnderPointer();
        // A full-screen foreground window must not swallow a capture that just
        // landed. The Snipping Tool's own capture overlay covers the whole
        // screen for the moment after Win+Shift+S, which would otherwise
        // Dismiss() the line and turn the peek in ComeDown() into a no-op on a
        // hidden window - the capture hung, but the line never came down.
        bool showingNewCapture = _window.IsPeeking;
        if (_window.Wanted && (!blocked || showingNewCapture)) _window.Present();
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
        Diag.Write("app", "shutdown requested from tray");
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
