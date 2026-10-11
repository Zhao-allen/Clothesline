namespace Tendedero.UI;

/// <summary>
/// Layout math for the line. Mirrors the Swift `Layout` enum.
/// </summary>
internal static class Layout
{
    public enum SizeKind { Small, Medium, Large }

    public static SizeKind Size
    {
        get
        {
            var s = Services.Settings.Default.GetString("lineSize");
            return Enum.TryParse<SizeKind>(s, true, out var v) ? v : SizeKind.Medium;
        }
        set => Services.Settings.Default.SetString("lineSize", value.ToString());
    }

    public static double SizeScale => Size switch
    {
        SizeKind.Small => 0.8,
        SizeKind.Medium => 1.0,
        SizeKind.Large => 1.35,
        _ => 1.0
    };

    public static double PhotoMaxHeight => Math.Round(104 * SizeScale);
    public static double CardWidth => Math.Round(150 * SizeScale);
    public static double Spacing => CardWidth + 24;
    public static double PanelHeight => PhotoMaxHeight + 106;
    public const double RopeTop = 10;
    public const double PinAbove = 9.5;

    public static string SizeLabel(SizeKind kind) => kind switch
    {
        SizeKind.Small   => Services.I18n.Get("Small"),
        SizeKind.Medium  => Services.I18n.Get("Medium"),
        SizeKind.Large   => Services.I18n.Get("Large"),
        _ => kind.ToString()
    };

    public static double Sag(double width) => Math.Min(30, width * 0.018);

    public static double RopeY(double x, double width)
    {
        if (width <= 0) return RopeTop;
        var f = x / width;
        return RopeTop + 4 * Sag(width) * f * (1 - f);
    }

    /// <summary>
    /// X position of the photo at <paramref name="index"/> among <paramref name="count"/>
    /// photos, across a panel of <paramref name="width"/>, given <paramref name="visible"/>
    /// fit and a current <paramref name="scroll"/>.
    /// </summary>
    public static double X(int index, int count, double width, int visible, double scroll)
    {
        if (Models.Line.KeepOnLine is not null && count > visible)
        {
            var newest = width / 2 + (visible - 1) * Spacing / 2.0;
            return newest - (count - 1 - index) * Spacing + scroll;
        }
        var gaps = Math.Max(count - 1, 0);
        var step = gaps > 0 ? Math.Min(Spacing, Math.Max(0, (width - 200) / gaps)) : Spacing;
        return width / 2 - gaps * step / 2.0 + index * step;
    }
}
