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
        var menu = new ContextMenuStrip
        {
            Font = new Font("Segoe UI Variable Display", 10f, GraphicsUnit.Point),
            Padding = new Padding(6, 6, 6, 6),
            ShowImageMargin = false,
            Renderer = new ToolStripProfessionalRenderer(new TrayColorTable()),
            ForeColor = System.Drawing.Color.FromArgb(235, 235, 235),
            BackColor = System.Drawing.Color.FromArgb(36, 36, 40)
        };

        var toggle = MakeItem(_window.IsRevealed ? I18n.Get("HideLine") : I18n.Get("ShowLine"), () => _window.Toggle());
        menu.Items.Add(toggle);

        var clear = MakeItem(I18n.Get("TakeEverythingDown"), () => _line.Clear());
        menu.Items.Add(clear);

        menu.Items.Add(new ToolStripSeparator());

        // 'Handle screenshots' and 'Hang copied images' are toggle items:
        // unlike MakeItem (which doesn't touch Checked), we need the
        // checkmark to flip the moment the user clicks, before the menu
        // closes. The captured `inbox` / `clipboard` locals let us update
        // the live ToolStripMenuItem.Checked directly from the handler.
        var inbox = new ToolStripMenuItem(I18n.Get("HandleScreenshots"));
        StyleItem(inbox);
        inbox.Checked = Inbox.IsEnabled;
        inbox.Click += (_, _) =>
        {
            Inbox.IsEnabled = !Inbox.IsEnabled;
            App.Instance.ApplyInboxMode();
            inbox.Checked = Inbox.IsEnabled;
        };
        menu.Items.Add(inbox);

        var clipboard = new ToolStripMenuItem(I18n.Get("HangCopiedImages"));
        StyleItem(clipboard);
        clipboard.Checked = ClipboardWatcher.IsEnabled;
        clipboard.Click += (_, _) =>
        {
            ClipboardWatcher.IsEnabled = !ClipboardWatcher.IsEnabled;
            // Persist the user's explicit choice so App.OnStartup doesn't
            // override it on the next launch. `clipboardUserDisabled` is
            // the only thing that prevents the auto-enable-every-startup
            // logic from re-enabling.
            Settings.Default.SetBool("clipboardUserDisabled", !ClipboardWatcher.IsEnabled);
            App.Instance.ApplyClipboardWatch();
            UpdateIcon();
            clipboard.Checked = ClipboardWatcher.IsEnabled;
        };
        menu.Items.Add(clipboard);

        var bringDown = new ToolStripMenuItem(I18n.Get("BringLineDown"));
        StyleItem(bringDown);
        StyleDropDown(bringDown);
        var bringSub = MakeItem(I18n.Get("WhenPointerRestsAtTop"), () => App.OpensFromMenuBar = !App.OpensFromMenuBar);
        bringSub.Checked = App.OpensFromMenuBar;
        var peekSub = MakeItem(I18n.Get("WhenSomethingNewHangs"), () => App.PeeksAtNew = !App.PeeksAtNew);
        peekSub.Checked = App.PeeksAtNew;
        var fsSub = MakeItem(I18n.Get("OverFullScreenApps"), () => { FullScreen.ShowLineOver = !FullScreen.ShowLineOver; App.Instance.Refresh(); });
        fsSub.Checked = FullScreen.ShowLineOver;
        bringDown.DropDownItems.Add(bringSub);
        bringDown.DropDownItems.Add(peekSub);
        bringDown.DropDownItems.Add(fsSub);
        menu.Items.Add(bringDown);

        var folderItem = new ToolStripMenuItem(I18n.Get("AlsoWatchFolders"));
        StyleItem(folderItem);
        StyleDropDown(folderItem);
        var addFolder = MakeItem(I18n.Get("AddFolder"), () => App.Instance.ChooseExtraFolder());
        folderItem.DropDownItems.Add(addFolder);
        foreach (var f in App.ExtraFolders)
        {
            var fi = new ToolStripMenuItem(System.IO.Path.GetFileName(f)) { Checked = true };
            StyleItem(fi);
            var stop = MakeItem(I18n.Get("StopWatching"), () => { App.ExtraFolders = App.ExtraFolders.Where(x => x != f).ToList(); App.Instance.RestartWatchers(); });
            folderItem.DropDownItems.Add(fi);
            folderItem.DropDownItems.Add(stop);
        }
        menu.Items.Add(folderItem);

        var keepItem = new ToolStripMenuItem(I18n.Get("KeepOnLine"));
        StyleItem(keepItem);
        StyleDropDown(keepItem);
        var fit = MakeItem(I18n.Get("AsManyAsFit"), () => App.Instance.SetKeep(null));
        fit.Checked = Line.KeepOnLine is null;
        keepItem.DropDownItems.Add(fit);
        foreach (var n in Line.KeepChoices)
        {
            var k = MakeItem(I18n.Get("PhotosSuffix", n), () => App.Instance.SetKeep(n));
            k.Checked = Line.KeepOnLine == n;
            keepItem.DropDownItems.Add(k);
        }
        menu.Items.Add(keepItem);

        var sizeItem = new ToolStripMenuItem(I18n.Get("Size"));
        StyleItem(sizeItem);
        StyleDropDown(sizeItem);
        foreach (var s in new[] { Layout.SizeKind.Small, Layout.SizeKind.Medium, Layout.SizeKind.Large })
        {
            var si = MakeItem(Layout.SizeLabel(s), () => App.Instance.SetSize(s));
            si.Checked = Layout.Size == s;
            sizeItem.DropDownItems.Add(si);
        }
        menu.Items.Add(sizeItem);

        var langItem = new ToolStripMenuItem(I18n.Get("Language"));
        StyleItem(langItem);
        StyleDropDown(langItem);
        var sysItem = MakeItem(I18n.Get("SystemDefault"), () => I18n.OverrideName = "");
        sysItem.Checked = string.IsNullOrWhiteSpace(I18n.OverrideName);
        langItem.DropDownItems.Add(sysItem);
        foreach (var ci in I18n.Supported)
        {
            var li = MakeItem(ci.NativeName, () => I18n.OverrideName = ci.Name);
            li.Checked = I18n.OverrideName == ci.Name;
            langItem.DropDownItems.Add(li);
        }
        menu.Items.Add(langItem);

        menu.Items.Add(new ToolStripSeparator());

        var openFolder = MakeItem(I18n.Get("OpenScreenshotsFolder"), () =>
        {
            var folder = Inbox.IsEnabled ? Inbox.Folder : Inbox.ScreenshotsFolder;
            try { System.Diagnostics.Process.Start("explorer.exe", folder); } catch { }
        });
        menu.Items.Add(openFolder);

        var sound = MakeItem(I18n.Get("Sounds"), () => Sound.SoundOn = !Sound.SoundOn);
        sound.Checked = Sound.SoundOn;
        // The Sounds / OpenAtLogin items are toggles too, but their Checked
        // state is refreshed on every menu open by RefreshMenu (see below),
        // so we don't need to update them in their click handlers. The
        // user will see the new checkmark when they reopen the menu.
        menu.Items.Add(sound);

        var startup = MakeItem(I18n.Get("OpenAtLogin"), () => App.Instance.ToggleStartup());
        startup.Checked = StartupManager.IsEnabled;
        menu.Items.Add(startup);

        menu.Items.Add(new ToolStripSeparator());

        var update = MakeItem(I18n.Get("CheckForUpdates"), () =>
        {
            try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("https://github.com/Zhao-allen/Clothesline/releases") { UseShellExecute = true }); }
            catch { }
        });
        menu.Items.Add(update);

        var quit = MakeItem(I18n.Get("Quit"), () => App.Instance.ShutdownApp());
        menu.Items.Add(quit);

        menu.Opening += (_, _) => RefreshMenu(menu);
        return menu;
    }

    private static ToolStripMenuItem MakeItem(string text, Action action)
    {
        var item = new ToolStripMenuItem(text);
        StyleItem(item);
        item.Click += (_, _) => action();
        return item;
    }

    /// <summary>
    /// Apply the modern padding / sizing to a top-level or sub menu item so
    /// every entry has consistent visual weight instead of the cramped
    /// default 4-px WinForms look.
    /// </summary>
    private static void StyleItem(ToolStripMenuItem item)
    {
        item.Padding = new Padding(12, 6, 12, 6);
        item.Margin = new Padding(0);
        item.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
        // For checkable items, give the checkmark glyph enough room.
        item.CheckBoxLayout = ToolStripItemContentAlignment.MiddleLeft;
    }

    /// <summary>
    /// Apply the same dark background and padding to a parent item's
    /// drop-down panel as the root menu. Without this, submenus render with
    /// the cramped default WinForms look (tight padding, default border),
    /// which looks inconsistent with the modernized parent menu.
    /// </summary>
    private static void StyleDropDown(ToolStripMenuItem parent)
    {
        if (parent.DropDown is ToolStripDropDownMenu drop)
        {
            drop.Padding = new Padding(6, 6, 6, 6);
            drop.ShowImageMargin = false;
        }
    }

    private void RefreshMenu(ContextMenuStrip menu)
    {
        // Indices: 0 toggle, 1 clear, 2 sep, 3 inbox, 4 clipboard, 5 bringDown,
        // 6 folderItem, 7 keepItem, 8 sizeItem, 9 langItem, 10 sep, 11 openFolder,
        // 12 sound, 13 startup, 14 sep, 15 update, 16 quit
        if (menu.Items.Count < 5) return;
        ((ToolStripMenuItem)menu.Items[0]).Text = _window.IsRevealed ? I18n.Get("HideLine") : I18n.Get("ShowLine");
        ((ToolStripMenuItem)menu.Items[1]).Enabled = _line.LiveCount > 0;
        ((ToolStripMenuItem)menu.Items[3]).Checked = Inbox.IsEnabled;
        ((ToolStripMenuItem)menu.Items[4]).Checked = ClipboardWatcher.IsEnabled;
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
            dc.DrawRectangle(new SolidColorBrush(System.Windows.Media.Color.FromArgb(180, 255, 255, 255)), null,
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

/// <summary>
/// Modern dark tray theme: warm-cool neutral background, subtle hover,
/// Windows accent blue (#0078D4) for checkmarks and selection.
/// </summary>
internal sealed class TrayColorTable : ProfessionalColorTable
{
    public override System.Drawing.Color MenuItemSelected => System.Drawing.Color.FromArgb(58, 58, 64);
    public override System.Drawing.Color MenuItemBorder => System.Drawing.Color.FromArgb(0, 120, 212);
    public override System.Drawing.Color MenuBorder => System.Drawing.Color.FromArgb(20, 20, 22);
    public override System.Drawing.Color ToolStripDropDownBackground => System.Drawing.Color.FromArgb(36, 36, 40);
    public override System.Drawing.Color ImageMarginGradientBegin => System.Drawing.Color.FromArgb(36, 36, 40);
    public override System.Drawing.Color ImageMarginGradientMiddle => System.Drawing.Color.FromArgb(36, 36, 40);
    public override System.Drawing.Color ImageMarginGradientEnd => System.Drawing.Color.FromArgb(36, 36, 40);
    public override System.Drawing.Color CheckBackground => System.Drawing.Color.FromArgb(0, 120, 212);
    public override System.Drawing.Color CheckPressedBackground => System.Drawing.Color.FromArgb(0, 120, 212);
    public override System.Drawing.Color CheckSelectedBackground => System.Drawing.Color.FromArgb(0, 120, 212);
    public override System.Drawing.Color SeparatorDark => System.Drawing.Color.FromArgb(60, 60, 66);
    public override System.Drawing.Color SeparatorLight => System.Drawing.Color.FromArgb(60, 60, 66);
}
