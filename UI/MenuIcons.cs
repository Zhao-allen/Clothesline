using System.Windows.Media;
using System.Windows.Media.Imaging;

// Both UseWPF and UseWindowsForms are on, so Brush exists in System.Drawing
// too. Every glyph here is stroked by WPF.
using Brush = System.Windows.Media.Brush;

namespace Clothesline.UI;

/// <summary>
/// The menu's stroked icon geometry, in one place.
///
/// Every glyph is authored in a 24x24 box as a WPF path string, and each
/// entry is wrapped so it can be handed to either surface:
/// <see cref="Wpf"/> gives a WPF Geometry for the WPF tray menu, and
/// <see cref="Gdi"/> converts the same coordinates to a GDI+ path for the
/// WinForms preview tool, so a glyph cannot drift between the two.
///
/// To add an icon, add one line here and nothing else. Do not restate the
/// path data in another file - the tray menu, the preview and any future
/// surface all read from here.
/// </summary>
internal static class MenuIcons
{
    public sealed record Entry(string Data, string Key);

    public static readonly Entry Line = new(
        "M2 9c3.5 5 6.5 7 10 7s6.5-2 10-7M7 15v4.5h10V15", "line");

    public static readonly Entry Screenshot = new(
        "M3.5 7.5V3.5h4M20.5 7.5v-4h-4M3.5 16.5v4h4M20.5 16.5v4h-4M8 8.5h8v7h-8z", "screenshot");

    public static readonly Entry Clear = new(
        "M4 6h16M9 6V3.5h6V6M6 6l1 13h10l1-13", "clear");

    public static readonly Entry Camera = new(
        "M3 7.5A2.5 2.5 0 0 1 5.5 5h3L10 3h4l1.5 2h3A2.5 2.5 0 0 1 21 7.5v9A2.5 2.5 0 0 1 18.5 19h-13A2.5 2.5 0 0 1 3 16.5Z"
        + "M8.8 11.5A3.2 3.2 0 1 1 15.2 11.5A3.2 3.2 0 1 1 8.8 11.5Z", "camera");

    public static readonly Entry Copy = new(
        "M9 3h10a1.5 1.5 0 0 1 1.5 1.5v11A1.5 1.5 0 0 1 19 17H9a1.5 1.5 0 0 1-1.5-1.5v-11A1.5 1.5 0 0 1 9 3Z"
        + "M16.5 20.5H5A1.5 1.5 0 0 1 3.5 19V7", "copy");

    public static readonly Entry Behavior = new(
        "M12 3.5v6M6.5 15A5.5 5.5 0 1 1 17.5 15A5.5 5.5 0 1 1 6.5 15Z", "behavior");

    public static readonly Entry Folder = new(
        "M3 6.5A1.5 1.5 0 0 1 4.5 5h4l2 2.5h9A1.5 1.5 0 0 1 21 9v9a1.5 1.5 0 0 1-1.5 1.5h-15A1.5 1.5 0 0 1 3 18Z", "folder");

    public static readonly Entry List = new(
        "M4 7h16M4 12h16M4 17h10", "list");

    public static readonly Entry CardSize = new(
        "M4.5 5.5h15v13h-15zM4.5 10.5h15", "cardsize");

    public static readonly Entry Globe = new(
        "M3.5 12A8.5 8.5 0 1 1 20.5 12A8.5 8.5 0 1 1 3.5 12Z"
        + "M3.5 12h17M12 3.5c2.5 3 2.5 14 0 17M12 3.5c-2.5 3-2.5 14 0 17", "globe");

    public static readonly Entry Speaker = new(
        "M11 5 6.5 9H3.5v6h3L11 19zM15.5 9.5a3.6 3.6 0 0 1 0 5", "speaker");

    public static readonly Entry Power = new(
        "M12 3.5v8M7.5 6.5a7 7 0 1 0 9 0", "power");

    public static readonly Entry Refresh = new(
        "M20 12a8 8 0 1 1-2.4-5.7M20 4v4h-4", "refresh");

    public static readonly Entry Warning = new(
        "M10.3 4.3 3.2 17a2 2 0 0 0 1.7 3h14.2a2 2 0 0 0 1.7-3L13.7 4.3a2 2 0 0 0-3.4 0Z"
        + "M12 9.5v4.5M12 17.5h.01", "warning");

    public static readonly Entry Coffee = new(
        "M4.5 8h11v6a4 4 0 0 1-4 4h-3a4 4 0 0 1-4-4z"
        + "M15.5 9.5h1.8a2.2 2.2 0 0 1 0 4.4h-1.8"
        + "M7 5.5c-.7-1.2.7-1.9 0-3M10.5 5.5c-.7-1.2.7-1.9 0-3"
        + "M3 20.5h15", "coffee");

    public static readonly Entry Palette = new(
        "M12 3.5a8.5 8.5 0 1 0 0 17c1.4 0 2-1 2-2s-.6-2-2-2h-1a2 2 0 0 1 0-4h5.5A5 5 0 0 0 12 3.5Z"
        + "M8.5 8.5h.01M6.5 12.5h.01M9.5 15.5h.01M13.5 7.5h.01", "palette");

    public static readonly Entry Chevron = new(
        "M9 6l6 6-6 6", "chevron");

    public static readonly Entry Check = new(
        "M20 6 9 17 4 12", "check");

    public static readonly Entry Pen = new(
        "M11 4H4a2 2 0 0 0-2 2v14a2 2 0 0 0 2 2h14a2 2 0 0 0 2-2v-7"
        + "M18.5 2.5a2.121 2.121 0 0 1 3 3L12 15l-4 1 1-4 9.5-9.5z", "pen");

    public static readonly Entry Search = new(
        "M3 3h7v7H3zM14 3h7v7h-7zM14 14h7v7h-7zM3 14h7v7H3z", "search");

    public static readonly Entry Desktop = new(
        "M2 3h20v14H2zM8 21h8M12 17v4", "desktop");

    public static readonly Entry ChevronLeft = new(
        "M15 18 9 12 15 6", "chevron-left");

    public static readonly Entry ChevronRight = new(
        "M9 18 15 12 9 6", "chevron-right");

    public static readonly Entry ExternalLink = new(
        "M7 17L17 7M9 7h8v8", "external");

    /// <summary>The geometry for WPF surfaces, or null if the data is bad.</summary>
    public static Geometry? Wpf(Entry entry)
    {
        try
        {
            return Geometry.Parse(entry.Data);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// The glyph as an <see cref="ImageSource"/>, for surfaces that want a
    /// bitmap rather than a geometry - a MenuItem.Icon, which takes an
    /// ImageSource and nothing else.
    ///
    /// Rendered at 2x and handed over at that size: WPF scales an ImageSource
    /// down cleanly, but it cannot recover detail that was never drawn, and a
    /// 1.9px stroke rasterised at 16px comes out as grey mush.
    /// </summary>
    public static ImageSource? Render(Entry entry, Brush stroke, int size = 16)
    {
        try
        {
            const int supersample = 2;
            int canvas = size * supersample;

            var visual = new DrawingVisual();
            using (var dc = visual.RenderOpen())
            {
                double s = (canvas - 2 * supersample) / 24.0;
                dc.PushTransform(new ScaleTransform(s, s));
                dc.PushTransform(new TranslateTransform(supersample, supersample));
                var pen = new System.Windows.Media.Pen(stroke, 1.9 * supersample)
                {
                    StartLineCap = PenLineCap.Round,
                    EndLineCap = PenLineCap.Round,
                    LineJoin = PenLineJoin.Round,
                };
                dc.DrawGeometry(null, pen, Wpf(entry)!);
                dc.Pop();
                dc.Pop();
            }

            var rtb = new RenderTargetBitmap(canvas, canvas, 96, 96, PixelFormats.Pbgra32);
            rtb.Render(visual);
            rtb.Freeze();   // shared across every row that uses this glyph
            return rtb;
        }
        catch
        {
            return null;
        }
    }
}
