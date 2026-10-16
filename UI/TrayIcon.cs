using System.Drawing;
using System.Globalization;
using System.Reflection;
using System.Windows;
using System.Windows.Forms;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Clothesline.Models;
using Clothesline.Services;

namespace Clothesline.UI;

/// <summary>
/// System tray icon with the full Clothesline menu.
/// v0.1.8 redesign: Windows 11 dark theme, section labels, accent-blue
/// checkmarks, app header strip, 9x14 padding, single #4CC2FF accent.
/// </summary>
internal sealed class TrayIcon : IDisposable
{
    private readonly NotifyIcon _icon;
    private readonly Line _line;
    private readonly LineWindow _window;
    private bool _disposed;

    // ── Win11 dark palette (matches the design mockup) ──
    private static readonly System.Drawing.Color BgCard      = System.Drawing.Color.FromArgb(43, 43, 43);     // #2B2B2B
    private static readonly System.Drawing.Color BgHover     = System.Drawing.Color.FromArgb(60, 60, 60);     // hover pill
    private static readonly System.Drawing.Color BgActive    = System.Drawing.Color.FromArgb(50, 0, 120, 212);  // 20% blue tint
    private static readonly System.Drawing.Color FgPrimary   = System.Drawing.Color.FromArgb(255, 255, 255);
    private static readonly System.Drawing.Color FgSecondary = System.Drawing.Color.FromArgb(200, 200, 200);
    private static readonly System.Drawing.Color FgMuted     = System.Drawing.Color.FromArgb(138, 138, 138);
    private static readonly System.Drawing.Color Accent      = System.Drawing.Color.FromArgb(76, 194, 255);   // #4CC2FF
    private static readonly System.Drawing.Color BgSep       = System.Drawing.Color.FromArgb(60, 60, 60);

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

    private static string AppVersion =>
        Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.1.8";

    private ContextMenuStrip BuildMenu()
    {
        var menu = new ContextMenuStrip
        {
            Font = new Font("Segoe UI Variable Display", 10f, GraphicsUnit.Point),
            Padding = new Padding(6, 6, 6, 6),
            ShowImageMargin = false,
            Renderer = new ToolStripProfessionalRenderer(new TrayColorTable()),
            ForeColor = FgPrimary,
            BackColor = BgCard
        };

        // ── App header strip ──
        var header = new ToolStripLabel($"  Clothesline   v{AppVersion}")
        {
            BackColor = BgCard,
            ForeColor = FgPrimary,
            Font = new Font("Segoe UI Variable Display", 10f, System.Drawing.FontStyle.Bold),
            Padding = new Padding(8, 12, 8, 10),
            Margin = new Padding(0, 0, 0, 4),
            Enabled = false
        };
        menu.Items.Add(header);

        // Thin separator after header
        AddSeparator(menu, 1);

        // ── Section: LINE ──
        AddSectionLabel(menu, "LINE");
        var toggle = MakeItem(_window.IsRevealed ? I18n.Get("HideLine") : I18n.Get("ShowLine"), () => _window.Toggle());
        toggle.ShortcutKeyDisplayString = "Ctrl+Alt+T";
        menu.Items.Add(toggle);

        var screenshot = MakeItem(I18n.Get("TakeScreenshot"), () => App.Instance.TakeScreenshot());
        screenshot.ShortcutKeyDisplayString = App.ScreenshotShortcut;
        screenshot.Enabled = !string.IsNullOrEmpty(App.ScreenshotShortcut);
        menu.Items.Add(screenshot);

        var clear = MakeItem(I18n.Get("TakeEverythingDown"), () => _line.Clear());
        menu.Items.Add(clear);

        AddSeparator(menu);
        // ── Section: CAPTURE ──
        AddSectionLabel(menu, "CAPTURE");
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
            Settings.Default.SetBool("clipboardUserDisabled", !ClipboardWatcher.IsEnabled);
            App.Instance.ApplyClipboardWatch();
            UpdateIcon();
            clipboard.Checked = ClipboardWatcher.IsEnabled;
        };
        menu.Items.Add(clipboard);

        AddSeparator(menu);
        // ── Section: BEHAVIOR ──
        AddSectionLabel(menu, "BEHAVIOR");

        var bringDown = MakeParentItem(I18n.Get("BringLineDown"));
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

        var folderItem = MakeParentItem(I18n.Get("AlsoWatchFolders"));
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

        var keepItem = MakeParentItem(I18n.Get("KeepOnLine"));
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

        var sizeItem = MakeParentItem(I18n.Get("Size"));
        foreach (var s in new[] { Layout.SizeKind.Small, Layout.SizeKind.Medium, Layout.SizeKind.Large })
        {
            var si = MakeItem(Layout.SizeLabel(s), () => App.Instance.SetSize(s));
            si.Checked = Layout.Size == s;
            sizeItem.DropDownItems.Add(si);
        }
        menu.Items.Add(sizeItem);

        var langItem = MakeParentItem(I18n.Get("Language"));
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

        AddSeparator(menu);
        // ── Section: APP ──
        AddSectionLabel(menu, "APP");
        var openFolder = MakeItem(I18n.Get("OpenScreenshotsFolder"), () =>
        {
            var folder = Inbox.IsEnabled ? Inbox.Folder : Inbox.ScreenshotsFolder;
            try { System.Diagnostics.Process.Start("explorer.exe", folder); } catch { }
        });
        menu.Items.Add(openFolder);

        var sound = MakeItem(I18n.Get("Sounds"), () => Sound.SoundOn = !Sound.SoundOn);
        sound.Checked = Sound.SoundOn;
        menu.Items.Add(sound);

        var startup = MakeItem(I18n.Get("OpenAtLogin"), () => App.Instance.ToggleStartup());
        startup.Checked = StartupManager.IsEnabled;
        menu.Items.Add(startup);

        AddSeparator(menu);
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

    private static void AddSeparator(ContextMenuStrip menu, int? _ = null)
    {
        var sep = new ToolStripSeparator
        {
            BackColor = BgSep,
            ForeColor = BgSep,
            Margin = new Padding(8, 4, 8, 4)
        };
        menu.Items.Add(sep);
    }

    private static void AddSectionLabel(ContextMenuStrip menu, string text)
    {
        var label = new ToolStripLabel(text)
        {
            BackColor = BgCard,
            ForeColor = FgMuted,
            Font = new Font("Segoe UI Variable Display", 8.5f, System.Drawing.FontStyle.Bold),
            Padding = new Padding(14, 8, 14, 4),
            Margin = new Padding(0)
        };
        menu.Items.Add(label);
    }

    private static ToolStripMenuItem MakeItem(string text, Action action)
    {
        var item = new ToolStripMenuItem(text);
        StyleItem(item);
        item.Click += (_, _) => action();
        return item;
    }

    private static ToolStripMenuItem MakeParentItem(string text)
    {
        var item = new ToolStripMenuItem(text);
        StyleItem(item);
        StyleDropDown(item);
        return item;
    }

    /// <summary>
    /// Modern Win11-style padding per item: 9px vertical, 14px horizontal.
    /// Right-aligned shortcut column is achieved by WinForms default
    /// ShortcutKeyDisplayString rendering (text-align: right).
    /// </summary>
    private static void StyleItem(ToolStripMenuItem item)
    {
        item.Padding = new Padding(14, 9, 14, 9);
        item.Margin = new Padding(0);
        item.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
    }

    /// <summary>
    /// Apply the same dark background and padding to a parent item's
    /// drop-down panel as the root menu.
    /// </summary>
    private static void StyleDropDown(ToolStripMenuItem parent)
    {
        if (parent.DropDown is ToolStripDropDownMenu drop)
        {
            drop.Padding = new Padding(6, 6, 6, 6);
            drop.ShowImageMargin = false;
        }
    }

    /// <summary>
    /// Walk the menu items by Text content instead of hardcoded indices.
    /// This is robust against layout changes (adding/removing section
    /// labels or separators). The ToolStripMenuItem.Text comparisons
    /// work because I18n.Get returns the same string each call within
    /// a culture, and we compare against the same key right after.
    /// </summary>
    private void RefreshMenu(ContextMenuStrip menu)
    {
        string hideLine = I18n.Get("HideLine");
        string showLine = I18n.Get("ShowLine");
        string takeEverything = I18n.Get("TakeEverythingDown");
        string handleScreenshots = I18n.Get("HandleScreenshots");
        string hangCopied = I18n.Get("HangCopiedImages");
        string sounds = I18n.Get("Sounds");
        string openAtLogin = I18n.Get("OpenAtLogin");

        foreach (var item in menu.Items.OfType<ToolStripMenuItem>())
        {
            if (item.Text == hideLine || item.Text == showLine)
            {
                item.Text = _window.IsRevealed ? hideLine : showLine;
            }
            else if (item.Text == takeEverything)
            {
                item.Enabled = _line.LiveCount > 0;
            }
            else if (item.Text == handleScreenshots)
            {
                item.Checked = Inbox.IsEnabled;
            }
            else if (item.Text == hangCopied)
            {
                item.Checked = ClipboardWatcher.IsEnabled;
            }
            else if (item.Text == sounds)
            {
                item.Checked = Sound.SoundOn;
            }
            else if (item.Text == openAtLogin)
            {
                item.Checked = StartupManager.IsEnabled;
            }
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

            var pinColor = filled ? Color.FromRgb(70, 70, 70) : Color.FromRgb(150, 150, 150);
            dc.DrawRoundedRectangle(new SolidColorBrush(pinColor), null,
                new Rect(29, 14, 6, 12), 1, 1);

            var cardColor = filled ? Color.FromRgb(100, 80, 200) : Color.FromRgb(180, 180, 180);
            dc.DrawRoundedRectangle(new SolidColorBrush(cardColor),
                new System.Windows.Media.Pen(Brushes.White, 1.5),
                new Rect(20, 26, 24, 28), 4, 4);
            dc.DrawRectangle(new SolidColorBrush(System.Windows.Media.Color.FromArgb(180, 255, 255, 255)), null,
                new Rect(24, 30, 16, 14));
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
/// Windows 11 dark theme palette. Hover is a subtle gray pill,
/// selection uses a blue tint with a 1px accent ring. Checkmarks
/// in Win11 system accent blue (#4CC2FF) for high contrast.
/// </summary>
internal sealed class TrayColorTable : ProfessionalColorTable
{
    public override System.Drawing.Color MenuItemSelected => System.Drawing.Color.FromArgb(60, 60, 60);
    public override System.Drawing.Color MenuItemSelectedGradientBegin => System.Drawing.Color.FromArgb(60, 60, 60);
    public override System.Drawing.Color MenuItemSelectedGradientEnd => System.Drawing.Color.FromArgb(60, 60, 60);
    public override System.Drawing.Color MenuItemBorder => System.Drawing.Color.FromArgb(76, 194, 255);
    public override System.Drawing.Color MenuBorder => System.Drawing.Color.FromArgb(20, 20, 20);
    public override System.Drawing.Color ToolStripDropDownBackground => System.Drawing.Color.FromArgb(43, 43, 43);
    public override System.Drawing.Color ImageMarginGradientBegin => System.Drawing.Color.FromArgb(43, 43, 43);
    public override System.Drawing.Color ImageMarginGradientMiddle => System.Drawing.Color.FromArgb(43, 43, 43);
    public override System.Drawing.Color ImageMarginGradientEnd => System.Drawing.Color.FromArgb(43, 43, 43);
    public override System.Drawing.Color CheckBackground => System.Drawing.Color.FromArgb(76, 194, 255);
    public override System.Drawing.Color CheckPressedBackground => System.Drawing.Color.FromArgb(76, 194, 255);
    public override System.Drawing.Color CheckSelectedBackground => System.Drawing.Color.FromArgb(76, 194, 255);
    public override System.Drawing.Color SeparatorDark => System.Drawing.Color.FromArgb(60, 60, 60);
    public override System.Drawing.Color SeparatorLight => System.Drawing.Color.FromArgb(60, 60, 60);
}
