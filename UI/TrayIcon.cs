using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Forms;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Clothesline.Models;
using Clothesline.Services;

using GColor = System.Drawing.Color;
using GSize = System.Drawing.Size;
using GRect = System.Drawing.Rectangle;
using GFont = System.Drawing.Font;
using GFontStyle = System.Drawing.FontStyle;
using GImage = System.Drawing.Image;
using GBitmap = System.Drawing.Bitmap;
using GSolidBrush = System.Drawing.SolidBrush;
using GPen = System.Drawing.Pen;
using GPath = System.Drawing.Drawing2D.GraphicsPath;
using GAlign = System.Drawing.ContentAlignment;
using GUnit = System.Drawing.GraphicsUnit;
using WColor = System.Windows.Media.Color;
using WPoint = System.Windows.Point;
using WPen = System.Windows.Media.Pen;
using WSolidBrush = System.Windows.Media.SolidColorBrush;
using WScale = System.Windows.Media.ScaleTransform;
using WTranslate = System.Windows.Media.TranslateTransform;

namespace Clothesline.UI;

/// <summary>
/// The Win11 dark palette shared by the tray menu and its custom-painted
/// parts (header strip, thumbnail strip, toggle switches).
///
/// This is the app's single source of truth for the dark theme. WPF surfaces
/// do not restate these values: <see cref="CardMenuTheme"/> reads them and
/// adapts them to WPF brushes, so the tray menu and the card's right-click
/// menu cannot drift apart. Add a colour here, not in a UI file.
/// </summary>
internal static class TrayPalette
{
    public static readonly GColor BgCard      = GColor.FromArgb(43, 43, 43);    // #2B2B2B
    public static readonly GColor BgHover     = GColor.FromArgb(60, 60, 60);
    public static readonly GColor BgElevated  = GColor.FromArgb(52, 52, 52);
    public static readonly GColor FgPrimary   = GColor.FromArgb(255, 255, 255);
    public static readonly GColor FgSecondary = GColor.FromArgb(200, 200, 200);
    public static readonly GColor FgMuted     = GColor.FromArgb(138, 138, 138);
    public static readonly GColor Accent      = GColor.FromArgb(76, 194, 255);  // #4CC2FF
    public static readonly GColor AccentSoft  = GColor.FromArgb(40, 76, 194, 255);
    public static readonly GColor Stroke      = GColor.FromArgb(61, 61, 61);    // #3D3D3D
    public static readonly GColor Danger      = GColor.FromArgb(255, 153, 164);
    public static readonly GColor Coffee      = GColor.FromArgb(253, 230, 138);  // #FDE68A
    public static readonly GColor KnobDark    = GColor.FromArgb(11, 42, 61);
}

/// <summary>
/// System tray icon with the full Clothesline menu.
///
/// v0.2.0 redesign (see Clothesline-UI重设计稿.html):
///  * section headers localised instead of hardcoded English
///  * an icon on every row, so the menu can be scanned rather than read
///  * on/off rows use a painted toggle switch instead of a checkmark - the
///    checkmark column made the labels look ragged and was easily confused
///    with the "currently selected" mark in the sub-menus
///  * sub-menu rows show their current value on the right, so you do not
///    have to open them to know what is set
///  * a header strip with the live count plus a thumbnail row
///  * "Take a screenshot" is back, wired to the *correct* protocol
/// </summary>
internal sealed class TrayIcon : IDisposable
{
    private readonly NotifyIcon _icon;
    private readonly Line _line;
    private readonly LineWindow _window;
    private bool _disposed;

    /// <summary>
    /// Bitmaps handed to menu items / hosted controls. WinForms does not own
    /// item images, so they are tracked here and released on rebuild.
    /// </summary>
    private readonly List<GImage> _ownedImages = new();

    /// <summary>
    /// Re-evaluated every time the menu opens. Each dynamic row registers how
    /// to bring itself up to date, which is far less brittle than the old
    /// approach of finding items again by comparing their <c>Text</c>.
    /// </summary>
    private readonly List<Action> _refreshers = new();

    private MenuHeaderStrip? _header;
    private MenuThumbnailStrip? _thumbs;

    public TrayIcon(Line line, LineWindow window, bool showIcon = true)
    {
        _line = line;
        _window = window;
        _icon = new NotifyIcon
        {
            Visible = showIcon,
            Text = "Clothesline"
        };
        UpdateIcon();
        _icon.ContextMenuStrip = BuildMenu();
        _icon.MouseClick += (_, e) =>
        {
            if (e.Button != MouseButtons.Left) return;
            Diag.Write("tray", "tray icon left click -> toggle line");
            try { _window.Toggle(); }
            catch (Exception ex) { Diag.Error("tray", ex); }
        };

        I18n.Changed += Rebuild;
    }

    public void UpdateIcon()
    {
        // Assign first, then release the old icon. The setter pushes the new
        // handle to the shell synchronously, so by the time this returns
        // nothing is pointing at the old one and destroying it is safe.
        // Disposing first would leave _icon.Icon holding a dead HICON for the
        // duration of the call, and the shell reads that handle while the
        // notification is being sent.
        var next = MakeTShirtIcon(ClipboardWatcher.IsEnabled);
        var previous = _icon.Icon;
        _icon.Icon = next;
        previous?.Dispose();
    }

    private void Rebuild()
    {
        // Release the converted thumbnails before the drop-down that hosts
        // them goes away. ToolStripControlHost does not reliably dispose the
        // control it hosts, and even if it did, the GDI bitmaps in there are
        // owned by the strip rather than by the menu, so a rebuild (a language
        // change is enough to trigger one) would drop them on the floor.
        _thumbs?.Release();
        _icon.ContextMenuStrip?.Dispose();
        ReleaseImages();
        _icon.ContextMenuStrip = BuildMenu();
        UpdateIcon();
    }

    private void ReleaseImages()
    {
        foreach (var img in _ownedImages) img.Dispose();
        _ownedImages.Clear();
    }

    /// <summary>Shared with the header strip, which paints it as a subtitle.</summary>
    internal static string AppVersion =>
        Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.1.9";

    // MARK: icon geometry (authored in a 24x24 box, stroked, never filled)

    private const string IcoLine       = "M2 9c3.5 5 6.5 7 10 7s6.5-2 10-7M7 15v4.5h10V15";
    private const string IcoScreenshot = "M3.5 7.5V3.5h4M20.5 7.5v-4h-4M3.5 16.5v4h4M20.5 16.5v4h-4M8 8.5h8v7h-8z";
    private const string IcoClear      = "M4 6h16M9 6V3.5h6V6M6 6l1 13h10l1-13";
    private const string IcoCamera     = "M3 7.5A2.5 2.5 0 0 1 5.5 5h3L10 3h4l1.5 2h3A2.5 2.5 0 0 1 21 7.5v9A2.5 2.5 0 0 1 18.5 19h-13A2.5 2.5 0 0 1 3 16.5Z"
                                       + "M8.8 11.5A3.2 3.2 0 1 1 15.2 11.5A3.2 3.2 0 1 1 8.8 11.5Z";
    private const string IcoCopy       = "M9 3h10a1.5 1.5 0 0 1 1.5 1.5v11A1.5 1.5 0 0 1 19 17H9a1.5 1.5 0 0 1-1.5-1.5v-11A1.5 1.5 0 0 1 9 3Z"
                                       + "M16.5 20.5H5A1.5 1.5 0 0 1 3.5 19V7";
    private const string IcoBehavior   = "M12 3.5v6M6.5 15A5.5 5.5 0 1 1 17.5 15A5.5 5.5 0 1 1 6.5 15Z";
    private const string IcoFolder     = "M3 6.5A1.5 1.5 0 0 1 4.5 5h4l2 2.5h9A1.5 1.5 0 0 1 21 9v9a1.5 1.5 0 0 1-1.5 1.5h-15A1.5 1.5 0 0 1 3 18Z";
    private const string IcoList       = "M4 7h16M4 12h16M4 17h10";
    private const string IcoCardSize   = "M4.5 5.5h15v13h-15zM4.5 10.5h15";
    private const string IcoGlobe      = "M3.5 12A8.5 8.5 0 1 1 20.5 12A8.5 8.5 0 1 1 3.5 12Z"
                                       + "M3.5 12h17M12 3.5c2.5 3 2.5 14 0 17M12 3.5c-2.5 3-2.5 14 0 17";
    private const string IcoSpeaker    = "M11 5 6.5 9H3.5v6h3L11 19zM15.5 9.5a3.6 3.6 0 0 1 0 5";
    private const string IcoPower      = "M12 3.5v8M7.5 6.5a7 7 0 1 0 9 0";
    private const string IcoRefresh    = "M20 12a8 8 0 1 1-2.4-5.7M20 4v4h-4";
    private const string IcoWarning    = "M10.3 4.3 3.2 17a2 2 0 0 0 1.7 3h14.2a2 2 0 0 0 1.7-3L13.7 4.3a2 2 0 0 0-3.4 0Z"
                                       + "M12 9.5v4.5M12 17.5h.01";
    // A coffee cup: body, saucer, and two rising steam wisps.
    private const string IcoCoffee     = "M4.5 8h11v6a4 4 0 0 1-4 4h-3a4 4 0 0 1-4-4z"
                                       + "M15.5 9.5h1.8a2.2 2.2 0 0 1 0 4.4h-1.8"
                                       + "M7 5.5c-.7-1.2.7-1.9 0-3M10.5 5.5c-.7-1.2.7-1.9 0-3"
                                       + "M3 20.5h15";

    /// <summary>
    /// Rasterises an icon and takes ownership of the bitmap so it can be
    /// released on the next rebuild. Named LoadIcon rather than Icon so it
    /// cannot shadow <see cref="System.Drawing.Icon"/> inside this class.
    /// </summary>
    private GImage? LoadIcon(string data, GColor color)
    {
        var img = MakeIcon(data, color);
        if (img is not null) _ownedImages.Add(img);
        return img;
    }

    // MARK: menu construction

    /// <summary>
    /// Internal rather than private so the off-screen preview in
    /// <see cref="MenuPreview"/> can render exactly the menu the user sees.
    /// </summary>
    internal ContextMenuStrip BuildMenu()
    {
        var menu = new ContextMenuStrip
        {
            Font = new GFont("Segoe UI Variable Display", 10f, GUnit.Point),
            // No horizontal padding: rows run edge to edge (Win11 style) and
            // the header strip's divider can reach both sides. The vertical
            // inset does not live here either - ToolStripDropDownMenu
            // recomputes Padding from its image margin during layout, so a
            // Padding set on the strip is discarded. Row height comes from
            // StyleRow and the section/separator margins instead.
            //
            // ShowImageMargin MUST stay true: it is the column the per-row
            // icons live in now that the checkmarks are gone.
            ShowImageMargin = true,
            ImageScalingSize = new GSize(16, 16),
            Renderer = new ToolStripProfessionalRenderer(new TrayColorTable()),
            ForeColor = TrayPalette.FgPrimary,
            BackColor = TrayPalette.BgCard
        };
        _refreshers.Clear();

        // ── Header: name, version, watch state, live count ──
        _header = new MenuHeaderStrip { Width = 296, Height = 58 };
        var host = new ToolStripControlHost(_header)
        {
            AutoSize = false,
            Size = new GSize(296, 58),
            Margin = new Padding(0),
            Padding = new Padding(0)
        };
        menu.Items.Add(host);

        // ── Header thumbnail row ──
        _thumbs = new MenuThumbnailStrip { Width = 296, Height = 52 };
        menu.Items.Add(new ToolStripControlHost(_thumbs)
        {
            AutoSize = false,
            Size = new GSize(296, 52),
            Margin = new Padding(0),
            Padding = new Padding(0)
        });

        // ── LINE ──
        AddSection(menu, I18n.Get("SectionLine"));

        var toggle = MakeItem(_window.IsRevealed ? I18n.Get("HideLine") : I18n.Get("ShowLine"),
                              () => _window.Toggle(), IcoLine);
        toggle.ShortcutKeyDisplayString = "Ctrl+Alt+T";
        menu.Items.Add(toggle);
        _refreshers.Add(() =>
        {
            toggle.Text = _window.IsRevealed ? I18n.Get("HideLine") : I18n.Get("ShowLine");
        });

        // The capture trigger. Windows has no API that makes the OS write a
        // screenshot straight into a folder the way macOS does, so Clothesline
        // asks the shell to start a capture and picks the result up from the
        // clipboard. The URI must be ms-screenclip://capture/image?rectangle:
        // the older ms-screensketch: scheme only *opens* the Snipping Tool
        // window, which is why this entry was dropped in v0.1.9.
        var shot = MakeItem(I18n.Get("TakeScreenshot"), StartScreenshot, IcoScreenshot);
        shot.ShortcutKeyDisplayString = "Win+Shift+S";
        menu.Items.Add(shot);

        var clear = MakeItem(I18n.Get("TakeEverythingDown"), () => _line.Clear(), IcoClear);
        clear.ShortcutKeyDisplayString = I18n.Get("ClearLineHint");
        menu.Items.Add(clear);
        _refreshers.Add(() => clear.Enabled = _line.LiveCount > 0);

        AddSeparator(menu);

        // ── CAPTURE ──
        AddSection(menu, I18n.Get("SectionCapture"));

        var inbox = MakeToggle(I18n.Get("HandleScreenshots"), IcoCamera, () => Inbox.IsEnabled, v =>
        {
            Inbox.IsEnabled = v;
            App.Instance.ApplyInboxMode();
        });
        menu.Items.Add(inbox);

        var clipboard = MakeToggle(I18n.Get("HangCopiedImages"), IcoCopy,
                                   () => ClipboardWatcher.IsEnabled, v =>
        {
            ClipboardWatcher.IsEnabled = v;
            Settings.Default.SetBool("clipboardUserDisabled", !v);
            App.Instance.ApplyClipboardWatch();
            UpdateIcon();
        });
        menu.Items.Add(clipboard);
        _refreshers.Add(() => _header.Watching = ClipboardWatcher.IsEnabled);

        AddSeparator(menu);

        // ── BEHAVIOR ──
        AddSection(menu, I18n.Get("SectionBehavior"));

        var bringDown = MakeParent(I18n.Get("BringLineDown"), IcoBehavior);
        var bringSub = MakeToggle(I18n.Get("WhenPointerRestsAtTop"), null,
                                  () => App.OpensFromMenuBar, v => App.OpensFromMenuBar = v);
        var peekSub = MakeToggle(I18n.Get("WhenSomethingNewHangs"), null,
                                 () => App.PeeksAtNew, v => App.PeeksAtNew = v);
        var fsSub = MakeToggle(I18n.Get("OverFullScreenApps"), null,
                               () => FullScreen.ShowLineOver, v =>
        {
            FullScreen.ShowLineOver = v;
            App.Instance.Refresh();
        });
        bringDown.DropDownItems.Add(bringSub);
        bringDown.DropDownItems.Add(peekSub);
        bringDown.DropDownItems.Add(fsSub);
        menu.Items.Add(bringDown);
        _refreshers.Add(() =>
        {
            int on = (App.OpensFromMenuBar ? 1 : 0) + (App.PeeksAtNew ? 1 : 0)
                     + (FullScreen.ShowLineOver ? 1 : 0);
            SetValue(bringDown, I18n.Get("ItemsOn", on));
        });

        var folders = MakeParent(I18n.Get("AlsoWatchFolders"), IcoFolder);
        folders.DropDownItems.Add(MakeItem(I18n.Get("AddFolder"), App.Instance.ChooseExtraFolder, null));
        foreach (var f in App.ExtraFolders)
        {
            var name = Path.GetFileName(f);
            var entry = MakeItem(name, null, null);
            entry.Checked = true;
            folders.DropDownItems.Add(entry);
            folders.DropDownItems.Add(MakeItem(I18n.Get("StopWatching"), () =>
            {
                App.ExtraFolders = App.ExtraFolders.Where(x => x != f).ToList();
                App.Instance.RestartWatchers();
            }, null));
        }
        menu.Items.Add(folders);
        _refreshers.Add(() =>
        {
            var watched = App.ExtraFolders;
            // One folder is the common case, so name it; several would not fit.
            SetValue(folders, watched.Count switch
            {
                0 => I18n.Get("ValueNone"),
                1 => Path.GetFileName(watched[0].TrimEnd(Path.DirectorySeparatorChar)),
                _ => watched.Count.ToString(CultureInfo.CurrentCulture),
            });
        });

        var keep = MakeParent(I18n.Get("KeepOnLine"), IcoList);
        var fit = MakeRadio(I18n.Get("AsManyAsFit"), () => Line.KeepOnLine is null,
                            () => App.Instance.SetKeep(null));
        keep.DropDownItems.Add(fit);
        foreach (var n in Line.KeepChoices)
        {
            var captured = n;
            keep.DropDownItems.Add(MakeRadio(I18n.Get("PhotosSuffix", captured),
                                             () => Line.KeepOnLine == captured,
                                             () => App.Instance.SetKeep(captured)));
        }
        menu.Items.Add(keep);
        _refreshers.Add(() =>
        {
            var keepNow = Line.KeepOnLine;
            SetValue(keep, keepNow is null
                ? I18n.Get("AsManyAsFit")
                : I18n.Get("PhotosSuffix", keepNow.Value));
        });

        var size = MakeParent(I18n.Get("Size"), IcoCardSize);
        foreach (var s in new[] { Layout.SizeKind.Small, Layout.SizeKind.Medium, Layout.SizeKind.Large })
        {
            var captured = s;
            size.DropDownItems.Add(MakeRadio(Layout.SizeLabel(captured),
                                             () => Layout.Size == captured,
                                             () => App.Instance.SetSize(captured)));
        }
        menu.Items.Add(size);
        _refreshers.Add(() => SetValue(size, Layout.SizeLabel(Layout.Size)));

        var lang = MakeParent(I18n.Get("Language"), IcoGlobe);
        lang.DropDownItems.Add(MakeRadio(I18n.Get("SystemDefault"),
                                         () => string.IsNullOrWhiteSpace(I18n.OverrideName),
                                         () => I18n.OverrideName = ""));
        foreach (var ci in I18n.Supported)
        {
            var captured = ci;
            lang.DropDownItems.Add(MakeRadio(captured.NativeName,
                                             () => I18n.OverrideName == captured.Name,
                                             () => I18n.OverrideName = captured.Name));
        }
        menu.Items.Add(lang);
        _refreshers.Add(() =>
        {
            var current = string.IsNullOrWhiteSpace(I18n.OverrideName)
                ? I18n.Get("SystemDefault")
                : I18n.Supported.FirstOrDefault(c => c.Name == I18n.OverrideName)?.NativeName
                  ?? I18n.OverrideName;
            SetValue(lang, current);
        });

        AddSeparator(menu);

        // ── APP ──
        AddSection(menu, I18n.Get("SectionApp"));

        var openFolder = MakeItem(I18n.Get("OpenScreenshotsFolder"), () =>
        {
            var folder = Inbox.IsEnabled ? Inbox.Folder : Inbox.ScreenshotsFolder;
            try { System.Diagnostics.Process.Start("explorer.exe", folder); } catch { }
        }, IcoFolder);
        openFolder.ShortcutKeyDisplayString = "\u2197";   // ↗ external-link affordance
        menu.Items.Add(openFolder);

        var sound = MakeToggle(I18n.Get("Sounds"), IcoSpeaker, () => Sound.SoundOn, v => Sound.SoundOn = v);
        menu.Items.Add(sound);

        var startup = MakeToggle(I18n.Get("OpenAtLogin"), IcoPower,
                                 () => StartupManager.IsEnabled, _ => App.Instance.ToggleStartup());
        menu.Items.Add(startup);

        var update = MakeItem(I18n.Get("CheckForUpdates"), () =>
        {
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(
                    "https://github.com/Zhao-allen/Clothesline/releases") { UseShellExecute = true });
            }
            catch { }
        }, IcoRefresh);
        menu.Items.Add(update);

        AddSeparator(menu);

        // ── SUPPORT ──
        // Its own group in the menu, kept apart from the settings above it:
        // the one row in the menu that asks for something rather than
        // configuring the app.
        AddSection(menu, I18n.Get("SectionSupport"));

        // Opens the codes directly, with no sub-menu.
        var coffee = MakeItem(I18n.Get("BuyMeACoffee"), () => DonationWindow.ShowOrRaise(), IcoCoffee);
        coffee.ForeColor = TrayPalette.Coffee;
        menu.Items.Add(coffee);

        AddSeparator(menu);

        var quit = MakeItem(I18n.Get("Quit"), () => App.Instance.ShutdownApp(), IcoWarning);
        quit.ForeColor = TrayPalette.Danger;
        menu.Items.Add(quit);

        menu.Opening += (_, _) => PrepareMenu();
        return menu;
    }

    /// <summary>
    /// Brings every dynamic row up to date. Split out of the Opening handler
    /// so the off-screen preview can run it without showing the menu.
    /// </summary>
    internal void PrepareMenu()
    {
        foreach (var refresh in _refreshers) refresh();
        _thumbs?.SetItems(_line);
        _header?.SetCount(_line.LiveCount);
        _header?.Invalidate();
    }

    // MARK: row builders

    private static void AddSection(ContextMenuStrip menu, string text)
    {
        menu.Items.Add(new ToolStripLabel(text)
        {
            BackColor = TrayPalette.BgCard,
            ForeColor = TrayPalette.FgMuted,
            Font = new GFont("Segoe UI Variable Display", 8.5f, GFontStyle.Bold),
            Padding = new Padding(0, 8, 0, 3),
            Margin = new Padding(0)
        });
    }

    private static void AddSeparator(ContextMenuStrip menu)
    {
        menu.Items.Add(new ToolStripSeparator
        {
            BackColor = TrayPalette.Stroke,
            ForeColor = TrayPalette.Stroke,
            Margin = new Padding(8, 4, 8, 4)
        });
    }

    /// <summary>Plain row. A null action makes a non-interactive row (e.g. a watched folder).</summary>
    private ToolStripMenuItem MakeItem(string text, Action? action, string? icon)
    {
        var item = new ToolStripMenuItem(text);
        StyleRow(item);
        if (icon is not null) item.Image = LoadIcon(icon, TrayPalette.FgSecondary);
        if (action is not null)
            item.Click += (_, _) => Run("click", text, action);
        return item;
    }

    /// <summary>
    /// Every interactive row funnels through here, which is what makes
    /// "I clicked it and nothing happened" answerable: the log records the
    /// row that fired, and an exception inside the action is captured with
    /// its stack instead of taking the whole app down mid-test.
    /// </summary>
    private static void Run(string kind, string label, Action action)
    {
        Diag.Write("tray", $"{kind} \"{label}\"");
        try
        {
            action();
        }
        catch (Exception ex)
        {
            // Deliberately swallowed *for this diagnostic round*: one bad row
            // should not abort a session where the user is walking the whole
            // menu. The full exception is in diag.log either way.
            Diag.Fatal("tray", ex);
        }
    }

    /// <summary>
    /// Row that opens a sub-menu; the current value is painted on the right.
    /// ShortcutKeyDisplayString is not used here because WinForms reserves
    /// that column for the drop-down arrow and silently skips the text.
    /// </summary>
    private ValueRow MakeParent(string text, string? icon)
    {
        var item = new ValueRow(text);
        StyleRow(item);
        if (icon is not null) item.Image = LoadIcon(icon, TrayPalette.FgSecondary);
        if (item.DropDown is ToolStripDropDownMenu drop)
        {
            // Everything the sub-menu needs to match the first level is set
            // here. The two levels used to disagree on the left inset (the
            // sub-menu carried its own Padding), on the icon column width and
            // on the separator colour, so an open cascade read as two
            // different menus.
            //
            // Padding is deliberately NOT set: ToolStripDropDownMenu
            // recomputes it from the image margin during layout, so a value
            // assigned here is discarded. What matters is that ShowImageMargin
            // and ImageScalingSize match the root strip, which is what makes
            // the two levels line up.
            drop.ShowImageMargin = true;
            drop.ImageScalingSize = new GSize(16, 16);
            drop.Font = new GFont("Segoe UI Variable Display", 10f, GUnit.Point);
            drop.BackColor = TrayPalette.BgCard;
            drop.ForeColor = TrayPalette.FgPrimary;
            drop.Renderer = new ToolStripProfessionalRenderer(new TrayColorTable());
        }
        return item;
    }

    /// <summary>
    /// On/off row. Uses a painted switch rather than <c>Checked</c>: the
    /// checkmark column made labels look ragged and collided visually with
    /// the "selected" mark used inside the sub-menus.
    /// </summary>
    private ToggleRow MakeToggle(string text, string? icon, Func<bool> read, Action<bool> write)
    {
        var item = new ToggleRow(text, read, v => Run("toggle", $"{text} -> {v}", () => write(v)));
        StyleRow(item);
        if (icon is not null) item.Image = LoadIcon(icon, TrayPalette.FgSecondary);
        _refreshers.Add(item.Sync);
        return item;
    }

    /// <summary>
    /// Mutually exclusive row inside a sub-menu. Sub-menus do not raise the
    /// root menu's Opening event, so each one re-derives its own tick through
    /// the same refresher list.
    /// </summary>
    private ToolStripMenuItem MakeRadio(string text, Func<bool> isOn, Action choose)
    {
        var item = new ToolStripMenuItem(text);
        StyleRow(item);
        item.Click += (_, _) => Run("choose", text, choose);
        _refreshers.Add(() => item.Checked = isOn());
        return item;
    }

    private static void SetValue(ValueRow item, string value) => item.SetValue(value);

    /// <summary>
    /// Win11-style row metrics: 6px vertical padding, no horizontal padding.
    /// With the 10pt font that lands on a 34px row - tight enough to read as
    /// compact, and still above the 32px floor Win11 uses for a comfortable
    /// click target. The same helper runs for sub-menu rows, so both levels
    /// are the same height.
    ///
    /// The horizontal inset is deliberately left at zero. A drop-down sizes
    /// itself to the *content* width of its widest row and does not add the
    /// rows' own horizontal padding back on, so any left/right padding makes
    /// every row overflow the menu by that amount and clips the right edge.
    /// The left inset comes from the menu's own image margin instead, and the
    /// right inset is drawn per-row (toggle switch, or right-aligned value).
    /// </summary>
    private static void StyleRow(ToolStripMenuItem item)
    {
        // Set the font on every row: sub-menu rows live in their own
        // auto-created ContextMenuStrip whose font does not always inherit.
        item.Font = new GFont("Segoe UI Variable Display", 10f, GUnit.Point);
        item.Padding = new Padding(0, 6, 0, 6);
        item.Margin = new Padding(0);
        item.TextAlign = GAlign.MiddleLeft;
    }

    /// <summary>
    /// Ask the shell to start a rectangular capture. Windows will not write
    /// the file for us, so ClipboardWatcher picks the result up instead.
    /// </summary>
    private static void StartScreenshot()
    {
        const string uri = "ms-screenclip://capture/image?rectangle";
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(
                uri) { UseShellExecute = true });
            Diag.Write("tray", $"launched {uri}");
        }
        catch (Exception ex)
        {
            Diag.Error("tray", ex);
            Sound.Beep();
        }
    }

    // MARK: icon rendering

    /// <summary>
    /// Rasterises a stroked path authored in a 24x24 box into a square bitmap.
    /// Rendered oversized and left for WinForms to downscale, which keeps the
    /// 1.9px strokes from turning to mush at 16px.
    /// </summary>
    private static GBitmap? MakeIcon(string data, GColor color, int size = 32)
    {
        try
        {
            var geometry = Geometry.Parse(data);
            var visual = new DrawingVisual();
            using (var dc = visual.RenderOpen())
            {
                double s = (size - 4) / 24.0;
                dc.PushTransform(new WScale(s, s));
                dc.PushTransform(new WTranslate(2, 2));
                var brush = new WSolidBrush(WColor.FromArgb(color.A, color.R, color.G, color.B));
                // Thicker than the mockup's 1.9: these are drawn oversized and
                // handed to WinForms at 16px, where a 1.9px stroke thins out
                // to barely a pixel and the glyph goes grey and mushy.
                var pen = new WPen(brush, 2.35)
                {
                    StartLineCap = PenLineCap.Round,
                    EndLineCap = PenLineCap.Round,
                    LineJoin = PenLineJoin.Round
                };
                dc.DrawGeometry(null, pen, geometry);
                dc.Pop();
                dc.Pop();
            }

            var rtb = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
            rtb.Render(visual);

            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(rtb));
            using var ms = new MemoryStream();
            encoder.Save(ms);
            ms.Position = 0;
            // Bitmap(Stream) keeps the stream alive for its own lifetime, so
            // hand back a detached copy.
            using var decoded = new GBitmap(ms);
            return new GBitmap(decoded);
        }
        catch { return null; }
    }

    /// <summary>
    /// Draws a clothesline icon: a curved rope with a photo hanging from a
    /// clothespin. Filled (colored) when clipboard watch is on.
    /// </summary>
    internal static System.Drawing.Icon MakeTShirtIcon(bool filled)
    {
        const int size = 64;
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            var ropeColor = filled ? WColor.FromRgb(80, 80, 80) : WColor.FromRgb(140, 140, 140);
            var ropePen = new WPen(new WSolidBrush(ropeColor), 2.5);
            var ropeGeo = new StreamGeometry();
            using (var ctx = ropeGeo.Open())
            {
                ctx.BeginFigure(new WPoint(4, 18), false, false);
                ctx.QuadraticBezierTo(new WPoint(32, 30), new WPoint(60, 18), true, false);
            }
            dc.DrawGeometry(null, ropePen, ropeGeo);

            var pinColor = filled ? WColor.FromRgb(70, 70, 70) : WColor.FromRgb(150, 150, 150);
            dc.DrawRoundedRectangle(new WSolidBrush(pinColor), null,
                new System.Windows.Rect(29, 14, 6, 12), 1, 1);

            var cardColor = filled ? WColor.FromRgb(100, 80, 200) : WColor.FromRgb(180, 180, 180);
            dc.DrawRoundedRectangle(new WSolidBrush(cardColor),
                new WPen(System.Windows.Media.Brushes.White, 1.5),
                new System.Windows.Rect(20, 26, 24, 28), 4, 4);
            dc.DrawRectangle(new WSolidBrush(WColor.FromArgb(180, 255, 255, 255)), null,
                new System.Windows.Rect(24, 30, 16, 14));
            var mountain = new StreamGeometry();
            using (var mctx = mountain.Open())
            {
                mctx.BeginFigure(new WPoint(26, 40), true, true);
                mctx.LineTo(new WPoint(31, 34), true, false);
                mctx.LineTo(new WPoint(36, 40), true, false);
            }
            dc.DrawGeometry(System.Windows.Media.Brushes.White, null, mountain);
        }

        var rtb = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(visual);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(rtb));
        using var ms = new MemoryStream();
        encoder.Save(ms);
        ms.Position = 0;
        using var bmp = new GBitmap(ms);
        var hicon = bmp.GetHicon();
        try
        {
            // FromHandle does not own the handle, so the temporary wrapper is
            // disposed and the HICON is destroyed by hand; the clone is what
            // gets handed out, because it owns its own copy.
            using var wrapper = System.Drawing.Icon.FromHandle(hicon);
            return (System.Drawing.Icon)wrapper.Clone();
        }
        finally
        {
            NativeMethods.DestroyIcon(hicon);
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        I18n.Changed -= Rebuild;
        // Capture the icon before disposing the NotifyIcon: NotifyIcon does
        // not own its Icon, so the HICON behind it would leak otherwise.
        var icon = _icon.Icon;
        _thumbs?.Release();
        _icon.Visible = false;
        _icon.ContextMenuStrip?.Dispose();
        ReleaseImages();
        _icon.Dispose();
        icon?.Dispose();
    }
}

/// <summary>
/// A row with an on/off switch painted on the right. The switch is drawn
/// after <c>base.OnPaint</c> so the hover highlight and the label come from
/// WinForms and only the control itself is custom.
/// </summary>
internal sealed class ToggleRow : ToolStripMenuItem
{
    private readonly Func<bool> _read;
    private readonly Action<bool> _write;

    public ToggleRow(string text, Func<bool> read, Action<bool> write) : base(text)
    {
        _read = read;
        _write = write;
        Sync();
        Click += (_, _) =>
        {
            _write(!_read());
            Sync();
            Owner?.Invalidate();
        };
    }

    /// <summary>Re-reads the underlying setting. Called each time the menu opens.</summary>
    public void Sync()
    {
        ToggleOn = _read();
        Invalidate();
    }

    public bool ToggleOn { get; private set; }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);

        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;

        // Position against the paint clip, not Width. WinForms hands the item
        // a clip rectangle that is item-relative but narrower than Size once
        // the row carries a large right padding - using Width pushed the
        // switch straight off the edge of the menu and nothing was drawn.
        var area = e.ClipRectangle;

        const int w = 34, h = 19;
        int x = area.Width - w - 16;
        int y = area.Top + (area.Height - h) / 2;
        var track = new GRect(x, y, w, h);

        using (var path = RoundRect(track, h / 2f))
        {
            using var trackBrush = new GSolidBrush(ToggleOn ? TrayPalette.Accent : TrayPalette.BgElevated);
            g.FillPath(trackBrush, path);
            if (!ToggleOn)
            {
                using var border = new GPen(TrayPalette.Stroke);
                g.DrawPath(border, path);
            }
        }

        int knob = h - 6;
        int knobX = ToggleOn ? x + w - knob - 3 : x + 3;
        using var knobBrush = new GSolidBrush(ToggleOn ? TrayPalette.KnobDark : TrayPalette.FgMuted);
        g.FillEllipse(knobBrush, knobX, y + 3, knob, knob);
    }

    internal static GPath RoundRect(GRect r, float radius)
    {
        float d = radius * 2;
        var path = new GPath();
        path.AddArc(r.X, r.Y, d, d, 180, 90);
        path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }
}

/// <summary>
/// A row that opens a sub-menu and shows its current value on the right, so
/// the setting can be read without opening it.
///
/// The value is painted by hand: <c>ShortcutKeyDisplayString</c> is skipped
/// entirely for items that own a drop-down, because WinForms gives that
/// column to the arrow.
/// </summary>
internal sealed class ValueRow : ToolStripMenuItem
{
    private string _value = "";

    public ValueRow(string text) : base(text) { }

    public void SetValue(string value)
    {
        var next = value ?? "";
        if (next == _value) return;
        _value = next;
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        if (_value.Length == 0) return;

        // Reserve the right-hand 34px for the drop-down arrow.
        var area = e.ClipRectangle;
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;

        using var font = new GFont("Segoe UI Variable Display", 8.5f, GUnit.Point);
        using var brush = new GSolidBrush(TrayPalette.FgSecondary);
        using var fmt = new StringFormat
        {
            Alignment = StringAlignment.Far,
            LineAlignment = StringAlignment.Center,
            Trimming = StringTrimming.EllipsisCharacter,
            FormatFlags = StringFormatFlags.NoWrap
        };
        g.DrawString(_value, font, brush,
            new RectangleF(area.Left, area.Top, area.Width - 34, area.Height), fmt);
    }
}

/// <summary>
/// The strip at the top of the menu: app name, version, watch state and a
/// live count of what is currently hanging.
/// </summary>
internal sealed class MenuHeaderStrip : System.Windows.Forms.Control
{
    /// <summary>Painted state, not a designer property.</summary>
    [System.ComponentModel.Browsable(false)]
    [System.ComponentModel.DesignerSerializationVisibility(
        System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public bool Watching { get; set; } = true;

    private int _count;

    public MenuHeaderStrip()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
                 | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        BackColor = TrayPalette.BgCard;
    }

    public void SetCount(int count) => _count = count;

    // Header geometry, in one place: the logo tile, the text column that
    // follows it, the badge and the divider. Split out of OnPaint so the
    // off-screen preview can report the numbers for every translation instead
    // of only for the one the machine happens to be set to.
    private const int LogoLeft = 16, LogoTop = 14, LogoSize = 34;
    private const int TitleTop = 14, SubtitleTop = 34;
    private const int BadgeHeight = 20, BadgeRightInset = 14, BadgeGutter = 8;

    /// <summary>Where the title/subtitle may be painted, and where the badge sits.</summary>
    internal readonly record struct HeaderLayout(int TextX, int TextWidth, int BadgeLeft, int BadgeWidth);

    internal HeaderLayout MeasureHeader(Graphics g)
    {
        var badgeText = I18n.Get("OnLineCount", _count);
        using var badgeFont = new GFont("Segoe UI Variable Display", 8.5f, GFontStyle.Bold);
        int badgeWidth = (int)Math.Ceiling(g.MeasureString(badgeText, badgeFont).Width) + 18;
        int badgeLeft = Width - badgeWidth - BadgeRightInset;

        int textX = LogoLeft + LogoSize + 11 - 2;
        // Stops short of the badge with a gutter, so the two can never touch
        // whatever the translation or the count.
        int textWidth = Math.Max(0, badgeLeft - BadgeGutter - textX);
        return new HeaderLayout(textX, textWidth, badgeLeft, badgeWidth);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

        using (var bg = new GSolidBrush(TrayPalette.BgCard))
            g.FillRectangle(bg, ClientRectangle);

        // Logo tile
        var logo = new GRect(LogoLeft, LogoTop, LogoSize, LogoSize);
        using (var path = ToggleRow.RoundRect(logo, 8f))
        using (var grad = new System.Drawing.Drawing2D.LinearGradientBrush(
                   new System.Drawing.Point(logo.Left, logo.Top),
                   new System.Drawing.Point(logo.Right, logo.Bottom),
                   GColor.FromArgb(91, 75, 214), GColor.FromArgb(62, 143, 216)))
            g.FillPath(grad, path);

        using (var glyphPen = new GPen(GColor.White, 1.8f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
        {
            g.DrawBezier(glyphPen,
                new System.Drawing.Point(24, 29), new System.Drawing.Point(28, 36),
                new System.Drawing.Point(38, 36), new System.Drawing.Point(42, 29));
            g.DrawRectangle(glyphPen, 29, 32, 8, 7);
        }

        // Count badge, right aligned. Its width is measured BEFORE the title and
        // subtitle are painted, because it decides how much room they get.
        var layout = MeasureHeader(g);
        var badge = new GRect(layout.BadgeLeft, (Height - BadgeHeight) / 2,
                              layout.BadgeWidth, BadgeHeight);

        // Title + subtitle, bounded by the badge.
        //
        // The subtitle used to be drawn with no width limit and the badge was
        // then painted over it. The badge sits at y 19..39 and the subtitle at
        // 34..48, so they overlap vertically by 5px - draw order hid nothing,
        // it just clipped the tail of the text. The overlap was real for the
        // longer translations: 5.2px in English, 12.3px in Spanish, 42.8px in
        // French, where "presse-papiers" was eaten whole, and worse once the
        // count reaches two digits.
        //
        // The subtitle is now drawn into a rectangle that stops short of the
        // badge and ellipsised, which holds for any translation and any count
        // rather than for the strings that happen to fit today.
        using (var titleFont = new GFont("Segoe UI Variable Display", 10f, GFontStyle.Bold))
        using (var subFont = new GFont("Segoe UI Variable Display", 8f))
        using (var titleBrush = new GSolidBrush(TrayPalette.FgPrimary))
        using (var subBrush = new GSolidBrush(TrayPalette.FgMuted))
        using (var fmt = new StringFormat(StringFormatFlags.NoWrap)
        {
            Alignment = StringAlignment.Near,
            // Near, with the rectangle's top at the old y, keeps the text on
            // exactly the line it was on before the bound was added.
            LineAlignment = StringAlignment.Near,
            Trimming = StringTrimming.EllipsisCharacter
        })
        {
            g.DrawString("Clothesline", titleFont, titleBrush,
                new RectangleF(layout.TextX, TitleTop, layout.TextWidth, titleFont.Height), fmt);

            g.DrawString(Subtitle(), subFont, subBrush,
                new RectangleF(layout.TextX, SubtitleTop, layout.TextWidth, subFont.Height), fmt);
        }

        using (var path = ToggleRow.RoundRect(badge, BadgeHeight / 2f))
        {
            using (var fill = new GSolidBrush(TrayPalette.AccentSoft))
                g.FillPath(fill, path);
            using (var edge = new GPen(GColor.FromArgb(90, 76, 194, 255)))
                g.DrawPath(edge, path);
        }
        using (var badgeFont = new GFont("Segoe UI Variable Display", 8.5f, GFontStyle.Bold))
        using (var badgeBrush = new GSolidBrush(TrayPalette.Accent))
        using (var badgeFmt = new StringFormat
        {
            Alignment = StringAlignment.Center,
            LineAlignment = StringAlignment.Center
        })
        {
            g.DrawString(I18n.Get("OnLineCount", _count), badgeFont, badgeBrush,
                new RectangleF(badge.X, badge.Y, badge.Width, badge.Height), badgeFmt);
        }

        // Bottom divider
        using var line = new GPen(TrayPalette.Stroke);
        g.DrawLine(line, 0, Height - 1, Width, Height - 1);
    }

    /// <summary>Version line under the app name. Shared with the layout probe.</summary>
    internal string Subtitle() => "v" + TrayIcon.AppVersion + " · "
        + (Watching ? I18n.Get("WatchingClipboard") : I18n.Get("ClipboardOff"));
}

/// <summary>
/// A row of the first few thumbnails currently on the line, so the menu
/// answers "what is hanging right now?" without the user pulling the line
/// down first.
/// </summary>
internal sealed class MenuThumbnailStrip : System.Windows.Forms.Control
{
    private readonly List<GBitmap> _thumbs = new();
    private int _extra;

    public MenuThumbnailStrip()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
                 | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        BackColor = TrayPalette.BgCard;
    }

    /// <summary>Re-reads the line. Bitmaps are converted once per menu open.</summary>
    public void SetItems(Line line)
    {
        Release();

        var live = line.Items.Where(i => !i.Falling).ToList();
        foreach (var item in live.Take(3))
        {
            var bmp = ToGdi(item.Thumb);
            if (bmp is not null) _thumbs.Add(bmp);
        }
        _extra = Math.Max(0, live.Count - _thumbs.Count);
        Invalidate();
    }

    /// <summary>
    /// Releases the converted thumbnails.
    ///
    /// The strip is thrown away and rebuilt whenever the menu is rebuilt - a
    /// language change is enough - and those GDI bitmaps belong to the strip,
    /// not to the drop-down hosting it, so they are not released by disposing
    /// the menu. Without this they accumulate one rebuild at a time.
    /// </summary>
    public void Release()
    {
        foreach (var t in _thumbs) t.Dispose();
        _thumbs.Clear();
        _extra = 0;
    }

    private static GBitmap? ToGdi(ImageSource source)
    {
        if (source is not BitmapSource bs) return null;
        try
        {
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bs));
            using var ms = new MemoryStream();
            encoder.Save(ms);
            ms.Position = 0;
            using var decoded = new GBitmap(ms);
            return new GBitmap(decoded);
        }
        catch { return null; }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;

        using (var bg = new GSolidBrush(TrayPalette.BgCard))
            g.FillRectangle(bg, ClientRectangle);

        const int tw = 52, th = 34, gap = 7, left = 16, top = 11;

        if (_thumbs.Count == 0)
        {
            using var empty = new GSolidBrush(TrayPalette.FgMuted);
            using var font = new GFont("Segoe UI Variable Display", 8f);
            g.DrawString(I18n.Get("EmptyHint"), font, empty, left - 2, top + 10);
        }
        else
        {
            for (int i = 0; i < _thumbs.Count; i++)
            {
                var box = new GRect(left + i * (tw + gap), top, tw, th);
                using (var clip = ToggleRow.RoundRect(box, 4f))
                {
                    var saved = g.Save();
                    g.SetClip(clip);
                    DrawCover(g, _thumbs[i], box);
                    g.Restore(saved);
                    using var edge = new GPen(GColor.FromArgb(34, 255, 255, 255));
                    g.DrawPath(edge, clip);
                }
            }

            if (_extra > 0)
            {
                var box = new GRect(left + _thumbs.Count * (tw + gap), top, tw, th);
                using (var path = ToggleRow.RoundRect(box, 4f))
                {
                    using var fill = new GSolidBrush(TrayPalette.BgElevated);
                    g.FillPath(fill, path);
                }
                using var brush = new GSolidBrush(TrayPalette.FgSecondary);
                using var font = new GFont("Segoe UI Variable Display", 8f, GFontStyle.Bold);
                using var fmt = new StringFormat
                {
                    Alignment = StringAlignment.Center,
                    LineAlignment = StringAlignment.Center
                };
                g.DrawString("+" + _extra, font, brush,
                    new RectangleF(box.X, box.Y, box.Width, box.Height), fmt);
            }
        }

        using var line = new GPen(TrayPalette.Stroke);
        g.DrawLine(line, 0, Height - 1, Width, Height - 1);
    }

    /// <summary>Centre-crops the source into the box, the way a CSS cover fit would.</summary>
    private static void DrawCover(Graphics g, GBitmap src, GRect box)
    {
        float scale = Math.Max((float)box.Width / src.Width, (float)box.Height / src.Height);
        float w = src.Width * scale, h = src.Height * scale;
        float x = box.X + (box.Width - w) / 2f;
        float y = box.Y + (box.Height - h) / 2f;
        g.DrawImage(src, x, y, w, h);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) Release();
        base.Dispose(disposing);
    }
}

/// <summary>
/// Windows 11 dark theme palette. Hover is a subtle gray pill, the border is
/// the same 1px hairline the WPF card menu draws, and selection uses the app
/// accent (#4CC2FF).
/// </summary>
internal sealed class TrayColorTable : ProfessionalColorTable
{
    public override GColor MenuItemSelected => TrayPalette.BgHover;
    public override GColor MenuItemSelectedGradientBegin => TrayPalette.BgHover;
    public override GColor MenuItemSelectedGradientEnd => TrayPalette.BgHover;
    public override GColor MenuItemBorder => TrayPalette.Accent;
    // Was #141414, which is invisible against a dark desktop and left the
    // drop-down looking unbordered. The card menu's shell draws #3D3D3D, so
    // both menus now have the same visible edge.
    public override GColor MenuBorder => TrayPalette.Stroke;
    public override GColor ToolStripDropDownBackground => TrayPalette.BgCard;
    public override GColor ImageMarginGradientBegin => TrayPalette.BgCard;
    public override GColor ImageMarginGradientMiddle => TrayPalette.BgCard;
    public override GColor ImageMarginGradientEnd => TrayPalette.BgCard;
    public override GColor CheckBackground => TrayPalette.Accent;
    public override GColor CheckPressedBackground => TrayPalette.Accent;
    public override GColor CheckSelectedBackground => TrayPalette.Accent;
    public override GColor SeparatorDark => TrayPalette.Stroke;
    public override GColor SeparatorLight => TrayPalette.Stroke;
}
