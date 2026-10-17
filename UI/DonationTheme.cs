using System.Windows.Media;
using System.Windows.Media.Effects;

// Both UseWPF and UseWindowsForms are on, so Brush exists in System.Drawing
// as well. This palette feeds WPF brushes only.
using Brush = System.Windows.Media.Brush;

namespace Clothesline.UI;

/// <summary>
/// The light and dark palettes for the donation window, lifted from
/// ui-redesign/11-qr-light.html and 11-qr-dark.html.
///
/// One entry per colour the window needs, two palettes side by side, so the
/// two themes cannot drift: a value exists for both or for neither.
///
/// Opacity note: the mockups specify the card at 0.66 (light) / 0.55 (dark)
/// because in a browser a backdrop-filter blurs whatever sits behind it. WPF
/// cannot blur the desktop behind an AllowsTransparency window, so a card
/// that transparent averages against the wallpaper - on a light desktop the
/// dark card turns mid-grey and its light text drops to roughly 2:1
/// contrast. The card colours here are therefore pushed up to 0.92 / 0.90:
/// still translucent, still legible on any background. Everything else is
/// the mockup value verbatim.
/// </summary>
internal sealed class DonationTheme
{
    public required Brush CardBackground { get; init; }
    public required Brush CardBorder { get; init; }
    public required Brush CardSheen { get; init; }
    public required Brush TopHighlight { get; init; }

    public required Brush Title { get; init; }
    public required Brush Subtitle { get; init; }

    public required Brush FrameBackground { get; init; }
    public required Brush FrameBorder { get; init; }

    public required Brush WeChatLabel { get; init; }
    public required Brush AlipayLabel { get; init; }
    public required Brush DotRing { get; init; }

    public required Brush FooterText { get; init; }
    public required Brush Heart { get; init; }

    public required Brush CloseBackground { get; init; }
    public required Brush CloseForeground { get; init; }
    public required Brush CloseHover { get; init; }

    public required DropShadowEffect CardShadowFar { get; init; }
    public required DropShadowEffect CardShadowNear { get; init; }
    public required DropShadowEffect FrameShadowFar { get; init; }
    public required DropShadowEffect FrameShadowNear { get; init; }

    public static DonationTheme Light { get; } = new()
    {
        // Mockup 0.66 raised to 0.92 - see the note on this class.
        CardBackground = B(0xEB, 0xFF, 0xFF, 0xFF),
        CardBorder = B(0xD9, 0xFF, 0xFF, 0xFF),
        CardSheen = Gradient(0x45, 0x00),
        TopHighlight = VerticalHighlight(0xE6),

        Title = B(0xFF, 0x0F, 0x17, 0x2A),
        Subtitle = B(0xFF, 0x64, 0x74, 0x8B),

        FrameBackground = B(0xFF, 0xFF, 0xFF, 0xFF),
        FrameBorder = B(0x0F, 0x0F, 0x17, 0x2A),

        WeChatLabel = B(0xFF, 0x07, 0xC1, 0x60),
        AlipayLabel = B(0xFF, 0x16, 0x77, 0xFF),
        DotRing = B(0x0A, 0x00, 0x00, 0x00),

        FooterText = B(0xFF, 0x94, 0xA3, 0xB8),
        Heart = B(0xFF, 0xF4, 0x3F, 0x5E),

        CloseBackground = B(0x0F, 0x0F, 0x17, 0x2A),
        CloseForeground = B(0xFF, 0x64, 0x74, 0x8B),
        CloseHover = B(0x1A, 0x0F, 0x17, 0x2A),

        CardShadowFar = Shadow(0x0F172A, 0.30, 64, 12),
        CardShadowNear = Shadow(0x0F172A, 0.08, 12, 4),
        FrameShadowFar = Shadow(0x0F172A, 0.20, 26, 8),
        FrameShadowNear = Shadow(0x0F172A, 0.08, 8, 3),
    };

    public static DonationTheme Dark { get; } = new()
    {
        // Mockup 0.55 raised to 0.90 - see the note on this class.
        CardBackground = B(0xE6, 0x14, 0x16, 0x26),
        CardBorder = B(0x1A, 0xFF, 0xFF, 0xFF),
        CardSheen = Gradient(0x14, 0x00),
        TopHighlight = VerticalHighlight(0x14),

        Title = B(0xFF, 0xF8, 0xFA, 0xFC),
        Subtitle = B(0xFF, 0x94, 0xA3, 0xB8),

        // Near-white on purpose: the codes have to stay scannable, so the
        // plates do not follow the theme.
        FrameBackground = B(0xFF, 0xF8, 0xFA, 0xFC),
        FrameBorder = B(0x1A, 0xFF, 0xFF, 0xFF),

        WeChatLabel = B(0xFF, 0x34, 0xD3, 0x99),
        AlipayLabel = B(0xFF, 0x60, 0xA5, 0xFA),
        DotRing = B(0x0D, 0xFF, 0xFF, 0xFF),

        FooterText = B(0xFF, 0x94, 0xA3, 0xB8),
        Heart = B(0xFF, 0xFB, 0x71, 0x85),

        CloseBackground = B(0x1A, 0xFF, 0xFF, 0xFF),
        CloseForeground = B(0xFF, 0xCB, 0xD5, 0xE1),
        CloseHover = B(0x26, 0xFF, 0xFF, 0xFF),

        CardShadowFar = Shadow(0x000000, 0.55, 64, 12),
        CardShadowNear = Shadow(0x000000, 0.40, 12, 4),
        FrameShadowFar = Shadow(0x000000, 0.60, 26, 8),
        FrameShadowNear = Shadow(0x000000, 0.40, 8, 3),
    };

    /// <summary>The dot colour is the brand colour in both themes.</summary>
    public static Brush WeChatDot => B(0xFF, 0x07, 0xC1, 0x60);

    /// <summary>The dot colour is the brand colour in both themes.</summary>
    public static Brush AlipayDot => B(0xFF, 0x16, 0x77, 0xFF);

    // MARK: factories

    /// <summary>A frozen solid brush. Frozen brushes are cheaper to render
    /// and safe to share across the window's lifetime.</summary>
    private static SolidColorBrush B(byte a, byte r, byte g, byte b)
    {
        var brush = new SolidColorBrush(Color.FromArgb(a, r, g, b));
        brush.Freeze();
        return brush;
    }

    /// <summary>
    /// The mockup's ::before sheen: a 135 degree white wash that fades out at
    /// 40% of the card's diagonal.
    /// </summary>
    private static LinearGradientBrush Gradient(byte startAlpha, byte endAlpha)
    {
        var brush = new LinearGradientBrush
        {
            StartPoint = new System.Windows.Point(0, 0),
            EndPoint = new System.Windows.Point(1, 1),
        };
        brush.GradientStops.Add(new GradientStop(Color.FromArgb(startAlpha, 0xFF, 0xFF, 0xFF), 0));
        brush.GradientStops.Add(new GradientStop(Color.FromArgb(endAlpha, 0xFF, 0xFF, 0xFF), 0.4));
        brush.Freeze();
        return brush;
    }

    /// <summary>The 1px inset highlight along the card's top edge.</summary>
    private static LinearGradientBrush VerticalHighlight(byte alpha)
    {
        var brush = new LinearGradientBrush
        {
            StartPoint = new System.Windows.Point(0, 0),
            EndPoint = new System.Windows.Point(0, 1),
        };
        brush.GradientStops.Add(new GradientStop(Color.FromArgb(alpha, 0xFF, 0xFF, 0xFF), 0));
        brush.GradientStops.Add(new GradientStop(Color.FromArgb(0x00, 0xFF, 0xFF, 0xFF), 0.28));
        brush.Freeze();
        return brush;
    }

    /// <summary>
    /// Downward (270 degree) shadow. WPF has no spread, so the mockup's
    /// negative spread is approximated with more blur at lower opacity.
    /// </summary>
    private static DropShadowEffect Shadow(int color, double opacity, double blur, double depth)
    {
        var effect = new DropShadowEffect
        {
            Color = Color.FromRgb((byte)(color >> 16), (byte)(color >> 8), (byte)color),
            Opacity = opacity,
            BlurRadius = blur,
            ShadowDepth = depth,
            Direction = 270,
        };
        effect.Freeze();
        return effect;
    }
}
