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
        ((ToolStripMenuItem)menu.Items[1]).Enabled = _line.LiveCount > 0;
        ((ToolStripMenuItem)menu.Items[3]).Checked = Inbox.IsEnabled;
        ((ToolStripMenuItem)menu.Items[4]).Checked = ClipboardWatcher.IsEnabled;
        // Items 5..9 may shift; refresh by tag where possible.
        foreach (var item in menu.Items.OfType<ToolStripMenuItem>())
        {
            if (item.Text == I18n.Get("Sounds")) item.Checked = Sound.SoundOn;
            if (item.Text == I18n.Get("OpenAtLogin")) item.Checked = StartupManager.IsEnabled;
        }
    }

    /// <summary>Draws a simple t-shirt icon (filled when clipboard watch is on).</summary>
    private static Icon MakeTShirtIcon(bool filled)
    {
        const int size = 64;
        var dv = new DrawingVisual();
        using (var dc = dv.RenderOpen())
        {
            var color = filled ? Colors.DarkSlateBlue : Colors.Gray;
            var brush = new SolidColorBrush(color);
            // Body
            dc.DrawRoundedRectangle(brush, null, new Rect(14, 24, 36, 32), 6, 6);
            // Left sleeve
            dc.DrawRoundedRectangle(brush, null, new Rect(6, 26, 12, 14), 4, 4);
            // Right sleeve
            dc.DrawRoundedRectangle(brush, null, new Rect(46, 26, 12, 14), 4, 4);
            // Collar
            dc.DrawEllipse(Brushes.Transparent, null, new Point(32, 24), 8, 6);
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
