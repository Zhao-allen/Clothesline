using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Clothesline.Models;
using Clothesline.Services;

namespace Clothesline.UI;

/// <summary>
/// Diagnostics: render Clothesline's two code-built surfaces to PNGs so their
/// layout can be reviewed without a human right-clicking a tray icon or
/// long-pressing a card. Neither surface has a XAML design view, so a layout
/// regression is otherwise invisible until someone notices it in the wild.
///
///   Clothesline.exe --export-menu         &lt;out.png&gt; [demo]
///   Clothesline.exe --export-annotation   &lt;out.png&gt; [demo]
///   Clothesline.exe --export-menu-cascade &lt;out.png&gt; [demo]
///   Clothesline.exe --export-cardmenu     &lt;out.png&gt; [inbox]
///
/// The first two are the original contract and are unchanged. The extra two
/// cover surfaces those two cannot reach: an *open* sub-menu, and the card's
/// WPF ContextMenu (which is neither a WinForms drop-down nor part of any
/// window's visual tree).
///
/// The annotation preview feeds the editor a synthetic in-memory image rather
/// than a file, so it does not depend on the host having working image
/// decoders - which is exactly what is broken in some build environments.
/// </summary>
internal static class UiPreview
{
    public static bool TryHandle(string[] args)
    {
        if (args.Length < 2) return false;

        var outputPath = args[1];
        bool demo = args.Length > 2
                    && string.Equals(args[2], "demo", StringComparison.OrdinalIgnoreCase);

        try
        {
            switch (args[0])
            {
                case "--export-menu":
                    RenderMenu(outputPath, demo);
                    return true;
                case "--export-traymenu-wpf":
                    RenderTrayMenuWpf(outputPath, demo);
                    return true;
                case "--export-annotation":
                    RenderAnnotation(outputPath);
                    return true;
                case "--export-menu-cascade":
                    RenderMenuCascade(outputPath, demo);
                    return true;
                case "--export-cardmenu":
                    // The third argument selects which branch of the menu to
                    // draw, not "demo": the menu's shape depends on whether the
                    // card lives in the inbox.
                    RenderCardMenu(outputPath, inbox: args.Length > 2
                        && string.Equals(args[2], "inbox", StringComparison.OrdinalIgnoreCase));
                    return true;
                case "--export-header":
                    RenderHeader(outputPath);
                    return true;
                case "--export-iconcheck":
                    RenderIconCheck(outputPath);
                    return true;
                default:
                    return false;
            }
        }
        catch (Exception ex)
        {
            // A WinExe has no console, so failures go to a sibling log file.
            try { File.WriteAllText(outputPath + ".log", ex.ToString()); } catch { }
            return true;
        }
    }

    // MARK: tray menu

    private static void RenderMenu(string outputPath, bool demo)
    {
        var line = new Line();
        if (demo)
        {
            for (int i = 0; i < 4; i++)
                line.Items.Add(new PeggedItem($"demo-{i}.png", DemoThumb(i)));
        }

        var window = new LineWindow(line);
        using var tray = new TrayIcon(line, window, showIcon: false);

        var menu = tray.BuildMenu();
        tray.PrepareMenu();

        // A ToolStripDropDown works out how much room it has from the screen it
        // is opened on, so it has to be opened on a real screen. The run is a
        // diagnostic, a brief flash is fine.
        var desired = menu.GetPreferredSize(System.Drawing.Size.Empty);
        menu.Show(new System.Drawing.Point(0, 0));
        System.Windows.Forms.Application.DoEvents();
        System.Windows.Forms.Application.DoEvents();

        if (Environment.GetEnvironmentVariable("CLOTHESLINE_TOGGLE_DEBUG") == "1")
        {
            var dump = new System.Text.StringBuilder();
            dump.AppendLine($"preferred={desired} shown={menu.Size} pad={menu.Padding}");
            foreach (System.Windows.Forms.ToolStripItem item in menu.Items)
            {
                var text = (item.Text ?? "").Replace('\n', ' ');
                dump.AppendLine($"  '{text}' size={item.Size} bounds={item.Bounds}");
            }
            File.WriteAllText(outputPath + ".items.log", dump.ToString());
        }

        var bounds = new Rectangle(0, 0, menu.Width, menu.Height);
        using var bmp = new Bitmap(bounds.Width, bounds.Height);
        menu.DrawToBitmap(bmp, bounds);
        bmp.Save(outputPath, ImageFormat.Png);

        menu.Close();
        menu.Dispose();
        Log(outputPath, $"{bounds.Width}x{bounds.Height}");
    }

    // MARK: tray menu, with a sub-menu expanded

    /// <summary>
    /// Renders the tray menu with one sub-menu open and composites the two
    /// drop-downs into a single PNG, so the first and second levels can be
    /// compared side by side. Row height, font, image-margin width, left inset
    /// and separator hairline are what this exists to check.
    ///
    /// Both drop-downs have to be opened on a real screen: a ToolStripDropDown
    /// works out how much room it has from the screen it opens on, and will
    /// clip its own rows if it is opened against the wrong one.
    /// </summary>
    private static void RenderMenuCascade(string outputPath, bool demo)
    {
        var line = new Line();
        if (demo)
        {
            for (int i = 0; i < 4; i++)
                line.Items.Add(new PeggedItem($"demo-{i}.png", DemoThumb(i)));
        }

        var window = new LineWindow(line);
        using var tray = new TrayIcon(line, window, showIcon: false);

        var menu = tray.BuildMenu();
        tray.PrepareMenu();

        menu.Show(new System.Drawing.Point(0, 0));
        System.Windows.Forms.Application.DoEvents();
        System.Windows.Forms.Application.DoEvents();

        // The first row that owns a sub-menu.
        var parent = menu.Items
            .OfType<System.Windows.Forms.ToolStripMenuItem>()
            .FirstOrDefault(i => i.DropDownItems.Count > 0);

        if (parent is null)
        {
            Log(outputPath, "no sub-menu to expand");
            menu.Close();
            menu.Dispose();
            return;
        }

        var drop = parent.DropDown;
        // Real screen coordinates, cascaded off the parent row the way the
        // shell would place it.
        drop.Show(new System.Drawing.Point(menu.Right - 4, menu.Top + parent.Bounds.Top));
        System.Windows.Forms.Application.DoEvents();
        System.Windows.Forms.Application.DoEvents();

        var rootBmp = new Bitmap(menu.Width, menu.Height);
        menu.DrawToBitmap(rootBmp, new Rectangle(0, 0, menu.Width, menu.Height));
        var subBmp = new Bitmap(drop.Width, drop.Height);
        drop.DrawToBitmap(subBmp, new Rectangle(0, 0, drop.Width, drop.Height));

        if (Environment.GetEnvironmentVariable("CLOTHESLINE_TOGGLE_DEBUG") == "1")
        {
            var dump = new System.Text.StringBuilder();
            DumpStrip(dump, "root", menu);
            dump.AppendLine($"root pad={menu.Padding} bounds={menu.Bounds}");
            dump.AppendLine($"sub  pad={drop.Padding} bounds={drop.Bounds} " +
                            $"offset=({drop.Bounds.Left - menu.Bounds.Left},{drop.Bounds.Top - menu.Bounds.Top})");
            DumpStrip(dump, "sub ", drop);

            // Measure the two levels' text insets off the rendered pixels. These
            // are the numbers "the levels line up" has to mean: same row height,
            // same x where the label starts.
            var rootRow = menu.Items.OfType<System.Windows.Forms.ToolStripMenuItem>()
                              .FirstOrDefault(i => i.Text == "显示晾衣绳");
            var subRow = drop.Items.OfType<System.Windows.Forms.ToolStripItem>()
                             .FirstOrDefault(i => i.Text == "当指针停在屏幕顶部");
            if (rootRow is not null) dump.AppendLine($"root {InkReport(rootBmp, rootRow)}");
            if (subRow is not null) dump.AppendLine($"sub  {InkReport(subBmp, subRow)}");

            File.WriteAllText(outputPath + ".items.log", dump.ToString());
        }

        // Offset by where the OS actually put the sub-menu, relative to the
        // root menu, so the composite is the real cascade and not a guess.
        int ox = drop.Bounds.Left - menu.Bounds.Left;
        int oy = drop.Bounds.Top - menu.Bounds.Top;
        int cw = Math.Max(menu.Width, ox + drop.Width);
        int ch = Math.Max(menu.Height, oy + drop.Height);

        using (var canvas = new Bitmap(cw, ch))
        {
            using (var g = Graphics.FromImage(canvas))
            {
                g.Clear(System.Drawing.Color.FromArgb(0x17, 0x17, 0x17));
                g.DrawImage(rootBmp, 0, 0);
                g.DrawImage(subBmp, ox, oy);
            }
            canvas.Save(outputPath, ImageFormat.Png);

            // A 3x nearest-neighbour blow-up of the left-hand column, where the
            // two levels' text inset can be compared by eye rather than by
            // measuring pixels.
            using (var zoom = new Bitmap(cw * 3, ch * 3))
            {
                using (var g = Graphics.FromImage(zoom))
                {
                    g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.NearestNeighbor;
                    g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.Half;
                    g.DrawImage(canvas, 0, 0, cw * 3, ch * 3);
                }
                zoom.Save(System.IO.Path.ChangeExtension(outputPath, null) + "-3x.png",
                          ImageFormat.Png);
            }
        }

        rootBmp.Dispose();
        subBmp.Dispose();
        drop.Close();
        menu.Close();
        menu.Dispose();
        Log(outputPath, $"{cw}x{ch} root={menu.Width}x{menu.Height} sub={drop.Width}x{drop.Height}");
    }

    /// <summary>
    /// Dumps a strip's rows for the layout probe: <c>Bounds</c> gives the row
    /// height and <c>ContentRectangle</c> gives where the text actually starts,
    /// which is the number the two menu levels have to agree on.
    /// </summary>
    private static void DumpStrip(System.Text.StringBuilder dump, string tag,
                                  System.Windows.Forms.ToolStrip strip)
    {
        foreach (System.Windows.Forms.ToolStripItem item in strip.Items)
        {
            var text = (item.Text ?? "").Replace('\n', ' ');
            dump.AppendLine($"  {tag} '{text}' size={item.Size} bounds={item.Bounds} " +
                            $"content={item.ContentRectangle} pad={item.Padding}");
        }
    }

    /// <summary>
    /// Reports where a row's label starts, measured off the rendered pixels.
    /// A row with an icon has its icon as the first ink run and the label as
    /// the run after it; a row without one starts with the label. Comparing
    /// <c>labelX</c> and <c>rowH</c> between a first-level row and a
    /// second-level row is the whole alignment claim.
    /// </summary>
    private static string InkReport(Bitmap bmp, System.Windows.Forms.ToolStripItem item)
    {
        var (first, second) = InkColumns(bmp, item.Bounds);
        bool hasIcon = item.Image is not null;
        int label = hasIcon ? (second < 0 ? first : second) : first;
        return $"row '{item.Text}' iconX={(hasIcon ? first : -1)} labelX={label} rowH={item.Height}";
    }

    /// <summary>
    /// Scans one row band of a rendered drop-down for the columns where ink
    /// starts. <c>first</c> is the first painted run, <c>text</c> the run after
    /// a gap - i.e. the label when the row carries an icon.
    ///
    /// Ink is judged against the known menu background rather than a sampled
    /// pixel, and a column only counts if more than one pixel in the band is
    /// off-background, so the shell's 1px border cannot masquerade as content.
    /// </summary>
    private static (int first, int text) InkColumns(Bitmap bmp, Rectangle row)
    {
        var bg = System.Drawing.Color.FromArgb(0x2B, 0x2B, 0x2B);

        bool Ink(int x)
        {
            int hits = 0;
            for (int y = row.Top + 5; y < row.Bottom - 5; y++)
            {
                var c = bmp.GetPixel(x, y);
                if (Math.Abs(c.R - bg.R) + Math.Abs(c.G - bg.G) + Math.Abs(c.B - bg.B) > 40)
                    hits++;
            }
            return hits >= 2;
        }

        int first = -1, text = -1, gap = 0;
        for (int x = Math.Max(2, row.Left); x < Math.Min(bmp.Width, row.Right); x++)
        {
            if (Ink(x))
            {
                if (first < 0) first = x;
                else if (text < 0 && gap >= 3) text = x;
                gap = 0;
            }
            else gap++;
        }
        return (first, text);
    }

    // MARK: card context menu (WPF)

    /// <summary>
    /// Renders the card's right-click menu. This is a WPF ContextMenu, which
    /// --export-menu cannot reach: it is not a WinForms drop-down, and it is
    /// not part of any window's visual tree either.
    ///
    /// A ContextMenu puts *itself* inside an internal Popup, so it only lays
    /// out once it is open. It is therefore opened for real on a throwaway
    /// host window, and the ContextMenu visual is then rendered straight to a
    /// RenderTargetBitmap. What the PNG shows is the real template: shell
    /// radius, hairline border, row height, hover geometry, disabled grey and
    /// danger ink. The one thing it cannot show is the OS popup's own drop
    /// shadow, because that belongs to the popup window, not to the control.
    ///
    /// Three cards are hung so the two "take down everything" rows show one
    /// disabled and one enabled state in the same image; the menu drawn is the
    /// leftmost card's.
    /// </summary>
    private static void RenderCardMenu(string outputPath, bool inbox)
    {
        var line = new Line();
        // Line() restores whatever the user last had hanging, which would make
        // the "take down everything to the left" row's enabled state depend on
        // the machine. Clearing the in-memory collection is safe: it raises
        // CollectionChanged, but nothing here is listening, and unlike
        // Line.Clear() it does not go through Discard/Trash, so no file is
        // touched.
        line.Items.Clear();

        var item = new PeggedItem(
            inbox
                ? System.IO.Path.Combine(Services.Inbox.Folder, "probe-demo.png")
                : System.IO.Path.Combine(System.IO.Path.GetTempPath(), "probe-demo.png"),
            DemoThumb(1));
        line.Items.Add(item);
        for (int i = 0; i < 2; i++)
            line.Items.Add(new PeggedItem($"demo-{i}.png", DemoThumb(i + 2)));

        var menu = PeggedCard.BuildContextMenu(item, line);

        var host = new Window
        {
            Width = 1,
            Height = 1,
            WindowStyle = WindowStyle.None,
            ShowInTaskbar = false,
            ShowActivated = true,
            AllowsTransparency = true,
            Background = System.Windows.Media.Brushes.Transparent,
            Left = -32000,
            Top = -32000
        };
        host.Show();
        host.UpdateLayout();

        menu.PlacementTarget = host;
        menu.Placement = System.Windows.Controls.Primitives.PlacementMode.AbsolutePoint;
        // Real screen coordinates: a popup measures itself against the screen
        // it opens on, so it must not be opened off-screen.
        menu.HorizontalOffset = 240;
        menu.VerticalOffset = 240;

        // Exercise the flag the rope's tracker reads, through the same call the
        // app uses. "Menu open keeps the rope down" is otherwise only testable
        // by a human right-clicking a card, so its three states are checked and
        // logged here instead.
        bool openBefore = PeggedCard.IsMenuOpen;
        PeggedCard.OpenTracked(menu);
        bool openDuring = PeggedCard.IsMenuOpen;

        host.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.Loaded);
        menu.UpdateLayout();

        // Put the hover state into the evidence shot. IsHighlighted is what the
        // template's highlight trigger reads, and keyboard focus is the one way
        // to raise it without a mouse, so this renders the real hover visual
        // rather than a copy of it. Preview only; nothing in the app does this.
        try
        {
            host.Activate();
            if (menu.Items.Count > 1 && menu.Items[1] is System.Windows.Controls.MenuItem hover)
                System.Windows.Input.Keyboard.Focus(hover);
            host.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.Loaded);
            menu.UpdateLayout();
        }
        catch { /* evidence-only; never fail the export for it */ }

        int w = (int)Math.Ceiling(menu.ActualWidth > 0 ? menu.ActualWidth : menu.DesiredSize.Width);
        int h = (int)Math.Ceiling(menu.ActualHeight > 0 ? menu.ActualHeight : menu.DesiredSize.Height);

        var rtb = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(menu);

        if (Environment.GetEnvironmentVariable("CLOTHESLINE_TOGGLE_DEBUG") == "1")
        {
            var dump = new System.Text.StringBuilder();
            dump.AppendLine($"menu {w}x{h} font={menu.FontFamily} size={menu.FontSize} " +
                            $"bg={(menu.Background as SolidColorBrush)?.Color}");
            foreach (var entry in menu.Items)
            {
                if (entry is System.Windows.Controls.MenuItem mi)
                    dump.AppendLine($"  row '{(mi.Header as string ?? "")}' h={mi.ActualHeight} " +
                                    $"enabled={mi.IsEnabled} localEnabled={mi.ReadLocalValue(UIElement.IsEnabledProperty)} " +
                                    $"highlighted={mi.IsHighlighted} " +
                                    $"fg={(mi.Foreground as SolidColorBrush)?.Color}");
                else if (entry is System.Windows.Controls.Separator sep)
                    dump.AppendLine($"  sep h={sep.ActualHeight}");
            }
            File.WriteAllText(outputPath + ".items.log", dump.ToString());
        }

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(rtb));
        using (var fs = new FileStream(outputPath, FileMode.Create, FileAccess.Write))
            encoder.Save(fs);

        menu.IsOpen = false;
        host.Close();
        bool openAfter = PeggedCard.IsMenuOpen;

        // Also prove the safety net: CloseOpenMenu() has to work even when it
        // is the only thing that closes the menu, or a stranded flag would keep
        // the rope from ever retracting.
        var stranded = PeggedCard.BuildContextMenu(item, line);
        PeggedCard.OpenTracked(stranded);
        bool openSecond = PeggedCard.IsMenuOpen;
        PeggedCard.CloseOpenMenu();
        bool openAfterForce = PeggedCard.IsMenuOpen;
        stranded.IsOpen = false;

        Log(outputPath, $"{w}x{h} inbox={inbox} " +
                        $"IsMenuOpen open={openBefore}->{openDuring} closed={openAfter} " +
                        $"forceClosed={openSecond}->{openAfterForce}");
    }

    // MARK: menu header, every translation

    /// <summary>
    /// Renders the menu's header strip once per supported language, with the
    /// clipboard watch on and off, plus the two-digit count case. The header is
    /// where the app name, the version line and the count badge compete for one
    /// 296px strip, and the version line used to be drawn unbounded and then
    /// covered by the badge - the longer the translation, the more of it was
    /// eaten, and the machine's own language hid the problem.
    ///
    /// For each row the log records the version line's measured width, the room
    /// it actually has, and the overhang - i.e. how much the old unbounded draw
    /// would have run under the badge - along with the y bands of the title,
    /// the version line and the divider, so "nothing collides" is a number
    /// rather than an impression.
    ///
    /// The culture is switched through <see cref="Services.I18n.Culture"/>, which is
    /// in-memory only (Services.I18n.OverrideName is the persisted one), and restored
    /// before returning.
    /// </summary>
    private static void RenderHeader(string outputPath)
    {
        const int stripW = 296, stripH = 58, gutter = 52, pad = 8, gap = 4;

        var rows = new List<(string Label, string Culture, bool Watching, int Count)>();
        foreach (var c in new[] { "en", "es", "zh", "ja", "fr" }) rows.Add(($"{c} watch", c, true, 5));
        foreach (var c in new[] { "en", "es", "zh", "ja", "fr" }) rows.Add(($"{c} off", c, false, 5));
        rows.Add(("fr 12", "fr", true, 12));
        rows.Add(("en 12", "en", true, 12));

        int canvasW = gutter + stripW + pad;
        int canvasH = pad + rows.Count * (stripH + gap) + pad;
        var log = new System.Text.StringBuilder();

        var original = Services.I18n.Culture;
        try
        {
            using var canvas = new Bitmap(canvasW, canvasH);
            using (var cg = Graphics.FromImage(canvas))
            {
                cg.Clear(System.Drawing.Color.FromArgb(0x17, 0x17, 0x17));
                using var labelFont = new System.Drawing.Font("Segoe UI", 8f);
                using var labelBrush = new System.Drawing.SolidBrush(System.Drawing.Color.FromArgb(0xB0, 0xB0, 0xB0));

                int y = pad;
                foreach (var (label, culture, watching, count) in rows)
                {
                    Services.I18n.Culture = System.Globalization.CultureInfo.GetCultureInfo(culture);

                    using var strip = new MenuHeaderStrip { Width = stripW, Height = stripH };
                    strip.Watching = watching;
                    strip.SetCount(count);
                    strip.CreateControl();

                    using (var bmp = new Bitmap(stripW, stripH))
                    {
                        strip.DrawToBitmap(bmp, new Rectangle(0, 0, stripW, stripH));
                        cg.DrawImage(bmp, gutter, y);

                        using var sg = strip.CreateGraphics();
                        var layout = strip.MeasureHeader(sg);
                        using var subFont = new System.Drawing.Font("Segoe UI Variable Display", 8f);
                        using var titleFont = new System.Drawing.Font("Segoe UI Variable Display", 10f, System.Drawing.FontStyle.Bold);
                        double subWidth = sg.MeasureString(strip.Subtitle(), subFont).Width;
                        double overhang = subWidth - layout.TextWidth;
                        int subBottom = 34 + subFont.Height;
                        int divider = stripH - 1;
                        var ink = InkRows(bmp, new Rectangle(layout.TextX, 30, layout.TextWidth, 22),
                                          System.Drawing.Color.FromArgb(0x2B, 0x2B, 0x2B));

                        log.AppendLine(
                            $"{label,-8} dpi={sg.DpiX:0} titleY=14..{14 + titleFont.Height} " +
                            $"subY=34..{subBottom} dividerY={divider} " +
                            $"textX={layout.TextX} room={layout.TextWidth} " +
                            $"subW={subWidth:0.0} overhang={overhang:0.0} " +
                            $"inkRows={ink.first}..{ink.last} badgeX={layout.BadgeLeft}");
                    }

                    cg.DrawString(label, labelFont, labelBrush, 4, y + stripH / 2 - 7);
                    y += stripH + gap;
                }
            }
            canvas.Save(outputPath, ImageFormat.Png);
        }
        finally
        {
            Services.I18n.Culture = original;
        }

        File.WriteAllText(outputPath + ".log",
            $"header strip {stripW}x{stripH}, {rows.Count} rows\n{log}\n{BoundMovedText()}");
    }

    /// <summary>
    /// Did giving the version line a bounded rectangle move it? Draws the same
    /// string that fits, both ways - the old unbounded point overload and the
    /// new bounded one - onto identical bitmaps and compares them pixel for
    /// pixel. Identical output means the fix changed only what happens when the
    /// text is too long, which is the point of the change.
    /// </summary>
    private static string BoundMovedText()
    {
        const int w = 220, h = 20;
        const string sample = "v0.1.9 · 正在监视剪贴板";

        using var font = new System.Drawing.Font("Segoe UI Variable Display", 8f);
        using var brush = new System.Drawing.SolidBrush(System.Drawing.Color.White);
        using var fmt = new StringFormat(StringFormatFlags.NoWrap)
        {
            Alignment = StringAlignment.Near,
            LineAlignment = StringAlignment.Near,
            Trimming = StringTrimming.EllipsisCharacter
        };

        using var oldWay = new Bitmap(w, h);
        using var newWay = new Bitmap(w, h);
        using (var g = Graphics.FromImage(oldWay))
            g.DrawString(sample, font, brush, 0, 0);
        using (var g = Graphics.FromImage(newWay))
            g.DrawString(sample, font, brush, new RectangleF(0, 0, w, font.Height), fmt);

        int differing = 0;
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                if (oldWay.GetPixel(x, y) != newWay.GetPixel(x, y)) differing++;

        return differing == 0
            ? $"bound-check: unbounded point overload and bounded rect render identically ({w}x{h} compared)"
            : $"bound-check: WARNING the bound moved the text, {differing} pixels differ";
    }

    /// <summary>Row range of the ink inside a band; -1 when the band is empty.</summary>
    private static (int first, int last) InkRows(Bitmap bmp, Rectangle band, System.Drawing.Color bg)
    {
        int first = -1, last = -1;
        for (int y = Math.Max(0, band.Top); y < Math.Min(bmp.Height, band.Bottom); y++)
        {
            bool ink = false;
            for (int x = Math.Max(0, band.Left); x < Math.Min(bmp.Width, band.Right) && !ink; x++)
            {
                var c = bmp.GetPixel(x, y);
                if (Math.Abs(c.R - bg.R) + Math.Abs(c.G - bg.G) + Math.Abs(c.B - bg.B) > 40)
                    ink = true;
            }
            if (ink) { if (first < 0) first = y; last = y; }
        }
        return (first, last);
    }

    // MARK: tray icon handle check

    /// <summary>
    /// Renders the tray icon from the real code path and, next to it, runs a
    /// control experiment on the HICON leak.
    ///
    /// <c>Bitmap.GetHicon()</c> returns an HICON the caller owns, and
    /// <c>Icon.FromHandle</c> does not take ownership. Nothing in the runtime
    /// frees such a handle: the Icon is not finalizable in a way that helps,
    /// because it never owned the handle to begin with. So each call that
    /// leaked one leaked it until the process exited. The experiment runs the
    /// same 200 iterations twice - once keeping the handle (the old code) and
    /// once destroying it (the new code) - and reports the GDI object count
    /// after everything has been disposed. The first must climb by ~200 and the
    /// second must come back to where it started.
    ///
    /// The same entry also proves the icon still renders, which is the thing a
    /// handle-handling change could plausibly break: the returned Icon comes
    /// from a clone that owns its own handle, after the original is destroyed.
    /// </summary>
    private static void RenderIconCheck(string outputPath)
    {
        const int iterations = 200;
        var log = new System.Text.StringBuilder();

        // (a) The real path, repeatedly, with each icon disposed.
        uint beforeReal = GdiObjects();
        for (int i = 0; i < iterations; i++)
            using (TrayIcon.MakeTShirtIcon(i % 2 == 0)) { }
        uint afterReal = GdiObjects();
        log.AppendLine($"real MakeTShirtIcon x{iterations}: GDI {beforeReal} -> {afterReal} " +
                       $"(delta {afterReal - beforeReal})");

        // (b) Control: keep the handle, as the old code did.
        uint beforeLeak = GdiObjects();
        {
            var held = new List<System.Drawing.Icon>();
            for (int i = 0; i < iterations; i++)
            {
                using var bmp = ProbeBitmap();
                held.Add(System.Drawing.Icon.FromHandle(bmp.GetHicon()));
            }
            foreach (var ico in held) ico.Dispose();   // does NOT free the handle
        }
        uint afterLeak = GdiObjects();
        log.AppendLine($"control, handle never destroyed x{iterations}: GDI {beforeLeak} -> {afterLeak} " +
                       $"(delta {afterLeak - beforeLeak})");

        // (c) Control: destroy it, as the new code does.
        uint beforeFixed = GdiObjects();
        {
            var held = new List<System.Drawing.Icon>();
            for (int i = 0; i < iterations; i++)
            {
                using var bmp = ProbeBitmap();
                var hicon = bmp.GetHicon();
                using var wrapper = System.Drawing.Icon.FromHandle(hicon);
                held.Add((System.Drawing.Icon)wrapper.Clone());
                NativeMethods.DestroyIcon(hicon);
            }
            foreach (var ico in held) ico.Dispose();
        }
        uint afterFixed = GdiObjects();
        log.AppendLine($"control, handle destroyed x{iterations}: GDI {beforeFixed} -> {afterFixed} " +
                       $"(delta {afterFixed - beforeFixed})");

        // The shipped icon, drawn. ToBitmap() only works on an Icon whose
        // handle is alive, so this is what catches a clone that lost its data.
        using var icon = TrayIcon.MakeTShirtIcon(true);
        using var rendered = icon.ToBitmap();
        log.AppendLine($"rendered icon: handle={icon.Handle != IntPtr.Zero} " +
                       $"{rendered.Width}x{rendered.Height}");

        const int scale = 4, pad = 16, label = 18;
        using (var canvas = new Bitmap(rendered.Width * scale + pad * 2,
                                       rendered.Height * scale + pad * 2 + label))
        {
            using (var g = Graphics.FromImage(canvas))
            {
                g.Clear(System.Drawing.Color.FromArgb(0x2B, 0x2B, 0x2B));
                g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.NearestNeighbor;
                g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.Half;
                g.DrawImage(rendered, pad, pad, rendered.Width * scale, rendered.Height * scale);
                using var font = new System.Drawing.Font("Segoe UI", 9f);
                using var brush = new System.Drawing.SolidBrush(System.Drawing.Color.White);
                g.DrawString($"tray icon via MakeTShirtIcon, {scale}x", font, brush, pad, pad + rendered.Height * scale + 2);
            }
            canvas.Save(outputPath, ImageFormat.Png);
        }

        File.WriteAllText(outputPath + ".log", log.ToString());
    }

    /// <summary>A throwaway 64px bitmap, to stand in for the icon source.</summary>
    private static Bitmap ProbeBitmap()
    {
        var bmp = new Bitmap(64, 64);
        using var g = Graphics.FromImage(bmp);
        g.Clear(System.Drawing.Color.FromArgb(0x5B, 0x4B, 0xD6));
        return bmp;
    }

    private static uint GdiObjects() =>
        NativeMethods.GetGuiResources(NativeMethods.GetCurrentProcess(), NativeMethods.GR_GDIOBJECTS);

    // MARK: annotation window

    private static void RenderAnnotation(string outputPath)
    {
        var image = DemoShot();

        var window = new AnnotationWindow("01.46.57.png");
        window.BuildForPreview(image);
        window.Show();
        window.UpdateLayout();
        System.Windows.Forms.Application.DoEvents();

        var root = (Visual)window.Content;
        var size = ((FrameworkElement)root).RenderSize;
        var rtb = new RenderTargetBitmap(
            (int)Math.Ceiling(size.Width), (int)Math.Ceiling(size.Height),
            96, 96, PixelFormats.Pbgra32);
        rtb.Render(root);

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(rtb));
        using (var fs = new FileStream(outputPath, FileMode.Create, FileAccess.Write))
            encoder.Save(fs);

        window.Close();
        Log(outputPath, $"{(int)size.Width}x{(int)size.Height}");
    }

    private static void Log(string outputPath, string detail)
    {
        try { File.WriteAllText(outputPath + ".log", $"rendered {detail} at {outputPath}"); }
        catch { }
    }

    // MARK: synthetic content

    /// <summary>
    /// A fake screenshot to annotate. Built with WPF's render target rather
    /// than decoded from disk so the preview works even where the image
    /// decoders are unavailable.
    /// </summary>
    private static BitmapSource DemoShot()
    {
        const int w = 1100, h = 620;
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(0xFA, 0xFA, 0xFB)), null,
                new Rect(0, 0, w, h));
            dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(0xED, 0xED, 0xF0)), null,
                new Rect(0, 0, w, 34));
            for (int i = 0; i < 3; i++)
                dc.DrawEllipse(new SolidColorBrush(Color.FromRgb(0xC9, 0xC9, 0xD1)), null,
                    new Point(20 + i * 18, 17), 5, 5);

            dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(0x2B, 0x2B, 0x33)), null,
                new Rect(52, 78, 420, 20));
            dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(0xD9, 0xD9, 0xE0)), null,
                new Rect(52, 118, 900, 11));
            dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(0xD9, 0xD9, 0xE0)), null,
                new Rect(52, 142, 760, 11));
            dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(0xD9, 0xD9, 0xE0)), null,
                new Rect(52, 166, 830, 11));
            dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(0xEF, 0xEF, 0xF4)), null,
                new Rect(52, 208, 900, 150));
            dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(0xE2, 0xE2, 0xE8)), null,
                new Rect(52, 208, 900, 1));
        }

        var rtb = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(visual);
        rtb.Freeze();
        return rtb;
    }

    private static BitmapSource DemoThumb(int seed)
    {
        var accents = new[]
        {
            Color.FromRgb(0x5B, 0x4B, 0xD6),
            Color.FromRgb(0x3E, 0x8F, 0xD8),
            Color.FromRgb(0xD8, 0x7A, 0x3E),
            Color.FromRgb(0x3E, 0xA8, 0x6B),
        };
        var accent = accents[seed % accents.Length];

        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(0xF7, 0xF7, 0xFA)), null,
                new Rect(0, 0, 320, 180));
            dc.DrawRectangle(new SolidColorBrush(accent), null, new Rect(0, 0, 320, 22));
            dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(0x2B, 0x2B, 0x33)), null,
                new Rect(20, 42, 130, 13));
            dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(0xD9, 0xD9, 0xE0)), null,
                new Rect(20, 68, 268, 9));
            dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(0xD9, 0xD9, 0xE0)), null,
                new Rect(20, 86, 210, 9));
            dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(0xEC, 0xEC, 0xF2)), null,
                new Rect(20, 110, 268, 50));
        }

        var rtb = new RenderTargetBitmap(320, 180, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(visual);
        rtb.Freeze();
        return rtb;
    }

    /// <summary>
    /// Renders the WPF tray menu to a PNG.
    ///
    /// The menu is a Popup, and a Popup has its own top-level window that only
    /// exists while it is open - so it cannot be handed to RenderTargetBitmap
    /// the way an in-tree visual can. Instead the card is built the same way
    /// TrayMenuWpf builds it, placed in a temporary window, measured and
    /// rendered, then thrown away.
    ///
    /// This exists because the menu is now the app's most-seen surface and it
    /// is built entirely in code: without this, a layout regression is
    /// invisible until someone right-clicks the tray.
    /// </summary>
    private static void RenderTrayMenuWpf(string outputPath, bool demo)
    {
        var line = new Line();
        if (demo)
        {
            for (int i = 0; i < 2; i++)
                line.Items.Add(new PeggedItem($"demo-{i}.png", DemoThumb(i)));
        }

        var window = new LineWindow(line);
        using var tray = new TrayIcon(line, window, showIcon: false);

        // Force the WPF menu to exist and build its card.
        var menu = new TrayMenuWpf(line, window);
        var card = menu.BuildCardForPreview();
        if (card is null) throw new InvalidOperationException("tray menu produced no card");

        // A throwaway host to measure and render against. Never shown.
        var host = new System.Windows.Window
        {
            Width = 1, Height = 1,
            WindowStyle = System.Windows.WindowStyle.None,
            ShowInTaskbar = false,
            ShowActivated = false,
            AllowsTransparency = true,
            Background = Brushes.Transparent,
            WindowState = System.Windows.WindowState.Minimized,
            Left = -4000, Top = -4000,
        };
        host.Content = card;

        card.Measure(new System.Windows.Size(double.PositiveInfinity, double.PositiveInfinity));
        card.Arrange(new System.Windows.Rect(0, 0, card.DesiredSize.Width, card.DesiredSize.Height));
        card.UpdateLayout();

        int w = (int)Math.Ceiling(card.ActualWidth);
        int h = (int)Math.Ceiling(card.ActualHeight);
        if (w <= 0 || h <= 0) throw new InvalidOperationException($"tray menu measured {w}x{h}");

        var rtb = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(card);

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(rtb));
        using var fs = File.Create(outputPath);
        encoder.Save(fs);
    }
}