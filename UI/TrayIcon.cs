using System.Drawing;
using System.Globalization;
using System.Windows;
using System.Windows.Forms;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Clothesline.Models;
using Clothesline.Services;

namespace Clothesline.UI;

/// <summary>
/// System tray icon with the full Clothesline menu. Mirrors the macOS status
/// item menu in <c>AppDelegate</c>.
/// </summary>
internal sealed class TrayIcon : IDisposable
{
    private readonly NotifyIcon _icon;
    private readonly Line _line;
    private readonly LineWindow _window;
    private bool _disposed;

    public TrayIcon(Line line, LineWindow window)
    {
        _line = line;
        _window = window;
        _icon = new NotifyIcon
        {
            Visible = true,
            Text = "Clothesline"
        };
        UpdateIcon();
        _icon.ContextMenuStrip = BuildMenu();
        _icon.MouseClick += (_, e) =>
        {
            if (e.Button == MouseButtons.Left) _window.Toggle();
        };

        I18n.Changed += Rebuild;
    }

    public void UpdateIcon()
    {
        _icon.Icon = MakeTShirtIcon(ClipboardWatcher.IsEnabled);
    }

    private void Rebuild()
    {
        _icon.ContextMenuStrip?.Dispose();
        _icon.ContextMenuStrip = BuildMenu();
        UpdateIcon();
    }

    private ContextMenuStrip BuildMenu()
    {
        var menu = new ContextMenuStrip();

        var toggle = new ToolStripMenuItem(_window.IsRevealed ? I18n.Get("HideLine") : I18n.Get("ShowLine"));
        toggle.Click += (_, _) => _window.Toggle();
        menu.Items.Add(toggle);

        var screenshot = new ToolStripMenuItem(I18n.Get("TakeScreenshot"))
        {
            ShortcutKeyDisplayString = "Win+Alt+S"
        };
        screenshot.Click += (_, _) => App.Instance.TakeScreenshot();
        menu.Items.Add(screenshot);

        // Screenshot mode: full screen or region selection.
        var shotMode = new ToolStripMenuItem(I18n.Get("ScreenshotMode"));
        var fullScreen = new ToolStripMenuItem(I18n.Get("ScreenshotModeFull"))
        {
            Checked = App.ScreenshotMode == App.ShotMode.FullScreen
        };
        fullScreen.Click += (_, _) => { App.ScreenshotMode = App.ShotMode.FullScreen; Rebuild(); };
        var region = new ToolStripMenuItem(I18n.Get("ScreenshotModeRegion"))
        {
            Checked = App.ScreenshotMode == App.ShotMode.Region
        };
        region.Click += (_, _) => { App.ScreenshotMode = App.ShotMode.Region; Rebuild(); };
        shotMode.DropDownItems.Add(fullScreen);
        shotMode.DropDownItems.Add(region);
        menu.Items.Add(shotMode);

        var clear = new ToolStripMenuItem(I18n.Get("TakeEverythingDown"));
        clear.Click += (_, _) => _line.Clear();
        menu.Items.Add(clear);

        menu.Items.Add(new ToolStripSeparator());

        var inbox = new ToolStripMenuItem(I18n.Get("HandleScreenshots")) { Checked = Inbox.IsEnabled };
        inbox.Click += (_, _) =>
        {
            Inbox.IsEnabled = !Inbox.IsEnabled;
            App.Instance.ApplyInboxMode();
        };
        menu.Items.Add(inbox);

        var clipboard = new ToolStripMenuItem(I18n.Get("HangCopiedImages")) { Checked = ClipboardWatcher.IsEnabled };
        clipboard.Click += (_, _) =>
        {
            ClipboardWatcher.IsEnabled = !ClipboardWatcher.IsEnabled;
            App.Instance.ApplyClipboardWatch();
            UpdateIcon();
        };
        menu.Items.Add(clipboard);

        var bringDown = new ToolStripMenuItem(I18n.Get("BringLineDown"));
        var bringSub = new ToolStripMenuItem(I18n.Get("WhenPointerRestsAtTop")) { Checked = App.OpensFromMenuBar };
        bringSub.Click += (_, _) => App.OpensFromMenuBar = !App.OpensFromMenuBar;
        var peekSub = new ToolStripMenuItem(I18n.Get("WhenSomethingNewHangs")) { Checked = App.PeeksAtNew };
        peekSub.Click += (_, _) => App.PeeksAtNew = !App.PeeksAtNew;
        var fsSub = new ToolStripMenuItem(I18n.Get("OverFullScreenApps")) { Checked = FullScreen.ShowLineOver };
        fsSub.Click += (_, _) => { FullScreen.ShowLineOver = !FullScreen.ShowLineOver; App.Instance.Refresh(); };
        bringDown.DropDownItems.Add(bringSub);
        bringDown.DropDownItems.Add(peekSub);
        bringDown.DropDownItems.Add(fsSub);
        menu.Items.Add(bringDown);

        var folderItem = new ToolStripMenuItem(I18n.Get("AlsoWatchFolders"));
        var addFolder = new ToolStripMenuItem(I18n.Get("AddFolder"));
        addFolder.Click += (_, _) => App.Instance.ChooseExtraFolder();
        folderItem.DropDownItems.Add(addFolder);
        foreach (var f in App.ExtraFolders)
        {
            var fi = new ToolStripMenuItem(System.IO.Path.GetFileName(f)) { Checked = true };
            var stop = new ToolStripMenuItem(I18n.Get("StopWatching"));
            stop.Click += (_, _) => { App.ExtraFolders = App.ExtraFolders.Where(x => x != f).ToList(); App.Instance.RestartWatchers(); };
            folderItem.DropDownItems.Add(fi);
            folderItem.DropDownItems.Add(stop);
        }
        menu.Items.Add(folderItem);

        var keepItem = new ToolStripMenuItem(I18n.Get("KeepOnLine"));
        var fit = new ToolStripMenuItem(I18n.Get("AsManyAsFit")) { Checked = Line.KeepOnLine is null };
        fit.Click += (_, _) => App.Instance.SetKeep(null);
        keepItem.DropDownItems.Add(fit);
        foreach (var n in Line.KeepChoices)
        {
            var k = new ToolStripMenuItem(I18n.Get("PhotosSuffix", n)) { Checked = Line.KeepOnLine == n };
            int captured = n;
            k.Click += (_, _) => App.Instance.SetKeep(captured);
            keepItem.DropDownItems.Add(k);
        }
        menu.Items.Add(keepItem);

        var sizeItem = new ToolStripMenuItem(I18n.Get("Size"));
        foreach (var s in new[] { Layout.SizeKind.Small, Layout.SizeKind.Medium, Layout.SizeKind.Large })
        {
            var si = new ToolStripMenuItem(Layout.SizeLabel(s)) { Checked = Layout.Size == s };
            var captured = s;
            si.Click += (_, _) => App.Instance.SetSize(captured);
            sizeItem.DropDownItems.Add(si);
        }
        menu.Items.Add(sizeItem);

        var langItem = new ToolStripMenuItem(I18n.Get("Language"));
        var sysItem = new ToolStripMenuItem(I18n.Get("SystemDefault")) { Checked = string.IsNullOrWhiteSpace(I18n.OverrideName) };
        sysItem.Click += (_, _) => { I18n.OverrideName = ""; };
        langItem.DropDownItems.Add(sysItem);
        foreach (var ci in I18n.Supported)
        {
            var li = new ToolStripMenuItem(ci.NativeName) { Checked = I18n.OverrideName == ci.Name };
            var captured = ci.Name;
            li.Click += (_, _) => { I18n.OverrideName = captured; };
            langItem.DropDownItems.Add(li);
        }
        menu.Items.Add(langItem);

        menu.Items.Add(new ToolStripSeparator());

        var openFolder = new ToolStripMenuItem(I18n.Get("OpenScreenshotsFolder"));
        openFolder.Click += (_, _) =>
        {
            var folder = Inbox.IsEnabled ? Inbox.Folder : Inbox.ScreenshotsFolder;
            try { System.Diagnostics.Process.Start("explorer.exe", folder); } catch { }
        };
        menu.Items.Add(openFolder);

        var sound = new ToolStripMenuItem(I18n.Get("Sounds")) { Checked = Sound.SoundOn };
        sound.Click += (_, _) => { Sound.SoundOn = !Sound.SoundOn; };
        menu.Items.Add(sound);

        var startup = new ToolStripMenuItem(I18n.Get("OpenAtLogin")) { Checked = StartupManager.IsEnabled };
        startup.Click += (_, _) => App.Instance.ToggleStartup();
        menu.Items.Add(startup);

        menu.Items.Add(new ToolStripSeparator());

        var quit = new ToolStripMenuItem(I18n.Get("Quit"));
        quit.Click += (_, _) => App.Instance.ShutdownApp();
        menu.Items.Add(quit);

        menu.Opening += (_, _) => RefreshMenu(menu);
        return menu;
    }

    private void RefreshMenu(ContextMenuStrip menu)
    {
        ((ToolStripMenuItem)menu.Items[0]).Text = _window.IsRevealed ? I18n.Get("HideLine") : I18n.Get("ShowLine");
        // 0 toggle, 1 screenshot, 2 mode, 3 clear, 4 separator, 5 inbox, 6 clipboard
        ((ToolStripMenuItem)menu.Items[3]).Enabled = _line.LiveCount > 0;
        ((ToolStripMenuItem)menu.Items[5]).Checked = Inbox.IsEnabled;
        ((ToolStripMenuItem)menu.Items[6]).Checked = ClipboardWatcher.IsEnabled;
        foreach (var item in menu.Items.OfType<ToolStripMenuItem>())
        {
            if (item.Text == I18n.Get("Sounds")) item.Checked = Sound.SoundOn;
            if (item.Text == I18n.Get("OpenAtLogin")) item.Checked = StartupManager.IsEnabled;
        }
    }

    /// <summary>
    /// Draws a clothesline icon: a curved rope with a photo hanging from a
    /// clothespin. Filled (colored) when clipboard watch is on.
    /// </summary>
    private static Icon MakeTShirtIcon(bool filled)
    {
        const int size = 64;
        var dv = new DrawingVisual();
        using (var dc = dv.RenderOpen())
        {
            var ropeColor = filled ? Color.FromRgb(80, 80, 80) : Color.FromRgb(140, 140, 140);
            var ropePen = new System.Windows.Media.Pen(new SolidColorBrush(ropeColor), 2.5);
            var ropeGeo = new StreamGeometry();
            using (var ctx = ropeGeo.Open())
            {
                ctx.BeginFigure(new Point(4, 18), false, false);
                ctx.QuadraticBezierTo(new Point(32, 30), new Point(60, 18), true, false);
            }
            dc.DrawGeometry(null, ropePen, ropeGeo);

            // Clothespin
            var pinColor = filled ? Color.FromRgb(70, 70, 70) : Color.FromRgb(150, 150, 150);
            dc.DrawRoundedRectangle(new SolidColorBrush(pinColor), null,
                new Rect(29, 14, 6, 12), 1, 1);

            // Photo card
            var cardColor = filled ? Color.FromRgb(100, 80, 200) : Color.FromRgb(180, 180, 180);
            dc.DrawRoundedRectangle(new SolidColorBrush(cardColor),
                new System.Windows.Media.Pen(Brushes.White, 1.5),
                new Rect(20, 26, 24, 28), 4, 4);
            // Photo inner
            dc.DrawRectangle(new SolidColorBrush(Color.FromArgb(180, 255, 255, 255)), null,
                new Rect(24, 30, 16, 14));
            // A little "mountain" in the photo
            var mountain = new StreamGeometry();
            using (var mctx = mountain.Open())
            {
                mctx.BeginFigure(new Point(26, 40), true, true);
                mctx.LineTo(new Point(31, 34), true, false);
                mctx.LineTo(new Point(36, 40), true, false);
            }
            dc.DrawGeometry(Brushes.White, null, mountain);
        }
        var rtb = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(dv);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(rtb));
        using var ms = new System.IO.MemoryStream();
        encoder.Save(ms);
        ms.Position = 0;
        using var bmp = new Bitmap(ms);
        var hicon = bmp.GetHicon();
        return Icon.FromHandle(hicon);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        I18n.Changed -= Rebuild;
        _icon.Visible = false;
        _icon.Dispose();
    }
}
