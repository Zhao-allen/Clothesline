using System.Windows;
using System.Windows.Media;

namespace ClotheslineSetup;

internal static class Theme
{
    public static readonly ThemeColors Light = new(
        WindowBackground: B(168, 255, 255, 255),
        WindowBorder: B(217, 255, 255, 255),
        TitleBarForeground: B("#475569"),
        BrandTitle: B("#0f172a"),
        BrandTagline: B("#64748b"),
        Divider: G((0, 15, 23, 42), (30, 15, 23, 42), (0, 15, 23, 42), horizontal: true),
        SectionLabel: B("#64748b"),
        LangPillBackground: B(153, 255, 255, 255),
        LangPillBorder: B(20, 15, 23, 42),
        LangPillForeground: B("#475569"),
        LangPillActiveBackground: G("#3b82f6", "#6366f1"),
        LangPillActiveForeground: B("#FFFFFF"),
        Heading: B("#0f172a"),
        Body: B("#475569"),
        HintBackground: G((18, 59, 130, 246), (18, 99, 102, 241)),
        HintBorder: B(36, 99, 102, 241),
        HintForeground: B("#4338ca"),
        HintKbdBackground: B(31, 67, 56, 202),
        HintKbdForeground: B("#3730a3"),
        PathFieldBackground: B(179, 255, 255, 255),
        PathFieldBorder: B(26, 15, 23, 42),
        PathFieldForeground: B("#1f2937"),
        GhostButtonBackground: B(13, 15, 23, 42),
        GhostButtonForeground: B("#475569"),
        PrimaryButtonBackground: G("#3b82f6", "#6366f1"),
        PrimaryButtonForeground: B("#FFFFFF"),
        StatusForeground: B("#475569"),
        ProgressTrack: B("#E2E8F0"),
        ProgressFill: G("#3b82f6", "#6366f1"),
        OkBannerBackground: G((26, 16, 185, 129), (26, 20, 184, 166)),
        OkBannerBorder: B(56, 16, 185, 129),
        OkBadgeBackground: G("#10b981", "#14b8a6"),
        OkHeading: B("#065f46"),
        OkSubtext: B("#047857"),
        CheckRowBackground: B(10, 15, 23, 42),
        CheckRowBorder: B(15, 15, 23, 42),
        CheckBoxBackground: G("#3b82f6", "#6366f1"),
        CheckRowForeground: B("#1f2937"),
        HighlightOverlay: G((69, 255, 255, 255), (0, 255, 255, 255)),
        ChromeButtonHoverBackground: B(21, 15, 23, 42)
    );

    public static readonly ThemeColors Dark = new(
        WindowBackground: B(140, 20, 22, 38),
        WindowBorder: B(26, 255, 255, 255),
        TitleBarForeground: B("#cbd5e1"),
        BrandTitle: B("#f8fafc"),
        BrandTagline: B("#94a3b8"),
        Divider: G((0, 255, 255, 255), (36, 255, 255, 255), (0, 255, 255, 255), horizontal: true),
        SectionLabel: B("#94a3b8"),
        LangPillBackground: B(13, 255, 255, 255),
        LangPillBorder: B(26, 255, 255, 255),
        LangPillForeground: B("#cbd5e1"),
        LangPillActiveBackground: G("#60a5fa", "#818cf8"),
        LangPillActiveForeground: B("#FFFFFF"),
        Heading: B("#f8fafc"),
        Body: B("#cbd5e1"),
        HintBackground: G((26, 96, 165, 250), (26, 129, 140, 248)),
        HintBorder: B(56, 129, 140, 248),
        HintForeground: B("#bfdbfe"),
        HintKbdBackground: B(46, 96, 165, 250),
        HintKbdForeground: B("#dbeafe"),
        PathFieldBackground: B(10, 255, 255, 255),
        PathFieldBorder: B(26, 255, 255, 255),
        PathFieldForeground: B("#e2e8f0"),
        GhostButtonBackground: B(15, 255, 255, 255),
        GhostButtonForeground: B("#cbd5e1"),
        PrimaryButtonBackground: G("#60a5fa", "#818cf8"),
        PrimaryButtonForeground: B("#FFFFFF"),
        StatusForeground: B("#94a3b8"),
        ProgressTrack: B("#2B2B2B"),
        ProgressFill: G("#60a5fa", "#818cf8"),
        OkBannerBackground: G((41, 16, 185, 129), (41, 20, 184, 166)),
        OkBannerBorder: B(77, 16, 185, 129),
        OkBadgeBackground: G("#10b981", "#14b8a6"),
        OkHeading: B("#a7f3d0"),
        OkSubtext: B("#6ee7b7"),
        CheckRowBackground: B(10, 255, 255, 255),
        CheckRowBorder: B(20, 255, 255, 255),
        CheckBoxBackground: G("#60a5fa", "#818cf8"),
        CheckRowForeground: B("#e2e8f0"),
        HighlightOverlay: G((12, 255, 255, 255), (0, 255, 255, 255)),
        ChromeButtonHoverBackground: B(32, 255, 255, 255)
    );

    private static SolidColorBrush B(byte a, byte r, byte g, byte b)
    {
        var brush = new SolidColorBrush(Color.FromArgb(a, r, g, b));
        brush.Freeze();
        return brush;
    }

    private static SolidColorBrush B(string hex)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)!);
        brush.Freeze();
        return brush;
    }

    private static LinearGradientBrush G(string c1, string c2)
    {
        var brush = new LinearGradientBrush(
            (Color)ColorConverter.ConvertFromString(c1)!,
            (Color)ColorConverter.ConvertFromString(c2)!,
            new Point(0, 0), new Point(1, 1));
        brush.Freeze();
        return brush;
    }

    private static LinearGradientBrush G((byte a, byte r, byte g, byte b) c1, (byte a, byte r, byte g, byte b) c2)
    {
        var brush = new LinearGradientBrush(
            Color.FromArgb(c1.a, c1.r, c1.g, c1.b),
            Color.FromArgb(c2.a, c2.r, c2.g, c2.b),
            new Point(0, 0), new Point(1, 1));
        brush.Freeze();
        return brush;
    }

    private static LinearGradientBrush G((byte a, byte r, byte g, byte b) c1, (byte a, byte r, byte g, byte b) c2, (byte a, byte r, byte g, byte b) c3, bool horizontal)
    {
        var brush = new LinearGradientBrush { StartPoint = horizontal ? new Point(0, 0) : new Point(0, 0), EndPoint = horizontal ? new Point(1, 0) : new Point(1, 1) };
        brush.GradientStops.Add(new GradientStop(Color.FromArgb(c1.a, c1.r, c1.g, c1.b), 0));
        brush.GradientStops.Add(new GradientStop(Color.FromArgb(c2.a, c2.r, c2.g, c2.b), 0.5));
        brush.GradientStops.Add(new GradientStop(Color.FromArgb(c3.a, c3.r, c3.g, c3.b), 1));
        brush.Freeze();
        return brush;
    }
}

internal readonly record struct ThemeColors(
    Brush WindowBackground,
    Brush WindowBorder,
    Brush TitleBarForeground,
    Brush BrandTitle,
    Brush BrandTagline,
    Brush Divider,
    Brush SectionLabel,
    Brush LangPillBackground,
    Brush LangPillBorder,
    Brush LangPillForeground,
    Brush LangPillActiveBackground,
    Brush LangPillActiveForeground,
    Brush Heading,
    Brush Body,
    Brush HintBackground,
    Brush HintBorder,
    Brush HintForeground,
    Brush HintKbdBackground,
    Brush HintKbdForeground,
    Brush PathFieldBackground,
    Brush PathFieldBorder,
    Brush PathFieldForeground,
    Brush GhostButtonBackground,
    Brush GhostButtonForeground,
    Brush PrimaryButtonBackground,
    Brush PrimaryButtonForeground,
    Brush StatusForeground,
    Brush ProgressTrack,
    Brush ProgressFill,
    Brush OkBannerBackground,
    Brush OkBannerBorder,
    Brush OkBadgeBackground,
    Brush OkHeading,
    Brush OkSubtext,
    Brush CheckRowBackground,
    Brush CheckRowBorder,
    Brush CheckBoxBackground,
    Brush CheckRowForeground,
    Brush HighlightOverlay,
    Brush ChromeButtonHoverBackground
);
