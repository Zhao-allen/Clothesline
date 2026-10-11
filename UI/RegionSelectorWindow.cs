using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Forms;
using System.Drawing;
using Clothesline.Services;
using WpfPath = System.Windows.Shapes.Path;
using WpfRectangle = System.Windows.Shapes.Rectangle;

namespace Clothesline.UI;

/// <summary>
/// Full-screen overlay that lets the user drag-select a rectangular region for
/// a screenshot. The area outside the selection is dimmed; the selected area
/// shows the desktop untouched. Returns the chosen rectangle in physical
/// screen pixels, or null if the user cancels (Esc / click without dragging).
/// </summary>
internal sealed class RegionSelectorWindow : Window
{
    private readonly Screen _screen;
    private readonly double _scale;
    private Point? _startPoint;
    private Rectangle? _result;

    private readonly WpfPath _dimPath;
    private readonly WpfRectangle _selectionBorder;
    private readonly TextBlock _hint;

    public RegionSelectorWindow(Screen screen)
    {
        _screen = screen;
        _scale = GetDpi(screen) / 96.0;

        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        Topmost = true;
        ShowInTaskbar = false;
        ShowActivated = true;
        Cursor = Cursors.Cross;
        ResizeMode = NoResize;
        SnapsToDevicePixels = true;

        var bounds = screen.Bounds;
        Left = bounds.Left / _scale;
        Top = bounds.Top / _scale;
        Width = bounds.Width / _scale;
        Height = bounds.Height / _scale;

        var canvas = new Canvas { Background = Brushes.Transparent };
        Content = canvas;

        // Dim layer with a rectangular hole where the selection is.
        _dimPath = new WpfPath
        {
            Fill = new SolidColorBrush(Color.FromArgb(120, 0, 0, 0)),
            IsHitTestVisible = false
        };
        canvas.Children.Add(_dimPath);

        // 1px white border around the selection.
        _selectionBorder = new WpfRectangle
        {
            Stroke = Brushes.White,
            StrokeThickness = 1.5,
            IsHitTestVisible = false,
            Visibility = Visibility.Collapsed
        };
        canvas.Children.Add(_selectionBorder);

        // Hint text at top-center.
        _hint = new TextBlock
        {
            Text = I18n.Get("DragToSelect"),
            Foreground = Brushes.White,
            FontSize = 14,
            Background = new SolidColorBrush(Color.FromArgb(160, 0, 0, 0)),
            Padding = new Thickness(14, 6, 14, 6)
        };
        canvas.Children.Add(_hint);
        Canvas.SetTop(_hint, 16);

        MouseLeftButtonDown += OnDown;
        MouseMove += OnMove;
        MouseLeftButtonUp += OnUp;
        KeyDown += OnKey;
        Loaded += (_, _) => UpdateHintPosition();
    }

    private void UpdateHintPosition()
    {
        _hint.Measure(new System.Windows.Size(double.PositiveInfinity, double.PositiveInfinity));
        Canvas.SetLeft(_hint, (Width - _hint.DesiredSize.Width) / 2);
    }

    /// <summary>Show the overlay and block until the user picks a region or cancels.</summary>
    public Rectangle? Select()
    {
        ShowDialog();
        return _result;
    }

    private void OnDown(object sender, MouseButtonEventArgs e)
    {
        _startPoint = e.GetPosition(this);
        _selectionBorder.Visibility = Visibility.Visible;
        _hint.Visibility = Visibility.Collapsed;
        CaptureMouse();
    }

    private void OnMove(object sender, MouseEventArgs e)
    {
        if (_startPoint is null) return;
        var current = e.GetPosition(this);
        UpdateSelection(_startPoint.Value, current);
    }

    private void OnUp(object sender, MouseButtonEventArgs e)
    {
        if (_startPoint is null) return;
        var current = e.GetPosition(this);
        var rect = DipRect(_startPoint.Value, current);
        ReleaseMouseCapture();

        if (rect.Width < 5 || rect.Height < 5)
        {
            // Treat a near-click as "capture the whole screen".
            var b = _screen.Bounds;
            _result = new Rectangle(b.X, b.Y, b.Width, b.Height);
        }
        else
        {
            _result = ToPhysical(rect);
        }

        // Hide the overlay before BitBlt so it isn't captured.
        Hide();
        DialogResult = true;
    }

    private void OnKey(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            _result = null;
            DialogResult = false;
        }
    }

    private void UpdateSelection(Point a, Point b)
    {
        var r = DipRect(a, b);

        // GeometryGroup with EvenOdd fill: full-screen rect minus selection.
        var group = new GeometryGroup { FillRule = FillRule.EvenOdd };
        group.Children.Add(new RectangleGeometry(new Rect(0, 0, Width, Height)));
        group.Children.Add(new RectangleGeometry(r));
        _dimPath.Data = group;

        Canvas.SetLeft(_selectionBorder, r.X);
        Canvas.SetTop(_selectionBorder, r.Y);
        _selectionBorder.Width = r.Width;
        _selectionBorder.Height = r.Height;
    }

    private static Rect DipRect(Point a, Point b)
    {
        double x = Math.Min(a.X, b.X);
        double y = Math.Min(a.Y, b.Y);
        double w = Math.Abs(a.X - b.X);
        double h = Math.Abs(a.Y - b.Y);
        return new Rect(x, y, w, h);
    }

    private Rectangle ToPhysical(Rect r)
    {
        int x = (int)Math.Round(_screen.Bounds.Left + r.X * _scale);
        int y = (int)Math.Round(_screen.Bounds.Top + r.Y * _scale);
        int w = (int)Math.Round(r.Width * _scale);
        int h = (int)Math.Round(r.Height * _scale);
        return new Rectangle(x, y, w, h);
    }

    private static double GetDpi(Screen screen)
    {
        try
        {
            var pt = new NativeMethods.POINT
            {
                X = screen.Bounds.Left + 1,
                Y = screen.Bounds.Top + 1
            };
            var hmon = NativeMethods.MonitorFromPoint(pt, NativeMethods.MONITOR_DEFAULTTONEAREST);
            if (hmon != IntPtr.Zero)
            {
                NativeMethods.GetDpiForMonitor(hmon, NativeMethods.MDT_EFFECTIVE_DPI, out uint dx, out _);
                if (dx > 0) return dx;
            }
        }
        catch { }
        return 96.0;
    }
}
