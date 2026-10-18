using System.ComponentModel;

namespace ClotheslineSetup;

/// <summary>
/// A flat button in the installer's palette. Owner-drawn because the stock
/// WinForms button is a light-themed 3D bevel that fights the dark window.
/// </summary>
internal sealed class FlatButton : Button
{
    private static readonly Color BgCard = Color.FromArgb(0x2B, 0x2B, 0x2B);
    private static readonly Color BgHover = Color.FromArgb(0x3A, 0x3A, 0x3A);
    private static readonly Color FgPrimary = Color.White;
    private static readonly Color FgSecondary = Color.FromArgb(0xB4, 0xB4, 0xB4);
    private static readonly Color Accent = Color.FromArgb(0x4C, 0xC2, 0xFF);
    private static readonly Color Stroke = Color.FromArgb(0x3D, 0x3D, 0x3D);

    private bool _hover;

    public FlatButton()
    {
        FlatStyle = FlatStyle.Flat;
        FlatAppearance.BorderSize = 0;
        BackColor = BgCard;
        ForeColor = FgPrimary;
        Font = new Font("Segoe UI Variable Display", 9.5f);
        Cursor = Cursors.Hand;
        SetStyle(ControlStyles.AllPaintingInWmPaint
               | ControlStyles.UserPaint
               | ControlStyles.OptimizedDoubleBuffer, true);
    }

    /// <summary>The accent-filled variant, used for the one action that matters.</summary>
    [DefaultValue(false)]
    public bool Primary { get; set; }

    /// <summary>Language rows mark the active choice; defaults to the first.</summary>
    [DefaultValue(false)]
    public bool Selected { get; set; }

    protected override void OnMouseEnter(EventArgs e)
    {
        _hover = true;
        Invalidate();
        base.OnMouseEnter(e);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        _hover = false;
        Invalidate();
        base.OnMouseLeave(e);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        var r = new Rectangle(0, 0, Width - 1, Height - 1);
        int radius = 6;

        using var path = Rounded(r, radius);

        Color fill;
        if (!Enabled) fill = Color.FromArgb(0x28, 0x28, 0x28);
        else if (Primary) fill = Accent;
        else if (Selected) fill = Color.FromArgb(0x2A, 0x3D, 0x4C);
        else if (_hover) fill = BgHover;
        else fill = BgCard;

        using (var brush = new SolidBrush(fill)) g.FillPath(brush, path);

        // A 1px hairline, in the stroke colour for normal rows and a darker
        // accent for the filled one so the edge does not wash out.
        using (var pen = new Pen(Primary && Enabled ? Color.FromArgb(0x2E, 0x8C, 0xC0) : Stroke))
            g.DrawPath(pen, path);

        var fg = !Enabled ? Color.FromArgb(0x66, 0x66, 0x66)
                : Primary ? Color.FromArgb(0x10, 0x22, 0x2C)
                : FgPrimary;

        using (var brush = new SolidBrush(fg))
        using (var fmt = new StringFormat
        {
            Alignment = StringAlignment.Center,
            LineAlignment = StringAlignment.Center,
            FormatFlags = StringFormatFlags.NoWrap,
            Trimming = StringTrimming.EllipsisCharacter,
        })
        {
            g.DrawString(Text, Font, brush, r, fmt);
        }
    }

    private static System.Drawing.Drawing2D.GraphicsPath Rounded(Rectangle r, int radius)
    {
        int d = radius * 2;
        var path = new System.Drawing.Drawing2D.GraphicsPath();
        path.AddArc(r.X, r.Y, d, d, 180, 90);
        path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }
}

/// <summary>
/// A 6px progress bar. The stock one is chunky and light-themed; this matches
/// the installer's hairline treatment.
/// </summary>
internal sealed class FlatProgressBar : Control
{
    private int _value;

    public FlatProgressBar()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint
               | ControlStyles.UserPaint
               | ControlStyles.OptimizedDoubleBuffer, true);
        Height = 6;
        BackColor = Color.FromArgb(0x2B, 0x2B, 0x2B);
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int Value
    {
        get => _value;
        set
        {
            var next = Math.Max(0, Math.Min(100, value));
            if (next == _value) return;
            _value = next;
            Invalidate();
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        using (var track = new SolidBrush(Color.FromArgb(0x2B, 0x2B, 0x2B)))
            g.FillRectangle(track, ClientRectangle);

        if (_value <= 0) return;

        int w = ClientSize.Width * _value / 100;
        if (w <= 0) return;
        using (var fill = new SolidBrush(Color.FromArgb(0x4C, 0xC2, 0xFF)))
            g.FillRectangle(fill, 0, 0, w, ClientSize.Height);
    }
}
