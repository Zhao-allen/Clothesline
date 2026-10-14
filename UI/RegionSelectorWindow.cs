using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Drawing;
using Clothesline.Services;

using WpfCursors = System.Windows.Input.Cursors;
using WpfSize = System.Windows.Size;

using WpfPath = System.Windows.Shapes.Path;
using WpfRectangle = System.Windows.Shapes.Rectangle;
using WpfMouseEventArgs = System.Windows.Input.MouseEventArgs;
using WpfKeyEventArgs = System.Windows.Input.KeyEventArgs;

namespace Clothesline.UI;

/// <summary>
/// Full-screen overlay for drag-selecting a screenshot region.
/// The capture rectangle is derived from WPF's screen-coordinate transform,
/// rather than from an assumed monitor scale. This keeps it correct on mixed
/// DPI and multi-monitor desktops.
/// </summary>
internal sealed class RegionSelectorWindow : Window
{
    private readonly Screen _screen;
    private readonly double _scale;
    private Point? _startPoint;
    private System.Drawing.Rectangle? _result;

    private readonly WpfPath _dimPath;
    private readonly WpfRectangle _selectionBorder;
    private readonly TextBlock _hint;
    private readonly TextBlock _sizeLabel;

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
        Cursor = WpfCursors.Cross;
        ResizeMode = ResizeMode.NoResize;
        SnapsToDevicePixels = true;
        UseLayoutRounding = true;

        var bounds = screen.Bounds;
        Left = bounds.Left / _scale;
        Top = bounds.Top / _scale;
        Width = bounds.Width / _scale;
        Height = bounds.Height / _scale;

        var canvas = new Canvas { Background = Brushes.Transparent };
        Content = canvas;

        _dimPath = new WpfPath
        {
            Fill = new SolidColorBrush(Color.FromArgb(100, 0, 0, 0)),
            IsHitTestVisible = false
        };
        canvas.Children.Add(_dimPath);

        _selectionBorder = new WpfRectangle
        {
            Stroke = Brushes.White,
            StrokeThickness = 1.5,
            IsHitTestVisible = false,
            Visibility = Visibility.Collapsed
        };
        canvas.Children.Add(_selectionBorder);

        _hint = new TextBlock
        {
            Text = I18n.Culture.Name.StartsWith("zh") ? "拖拽选择区域" : I18n.Get("DragToSelect"),
            Foreground = Brushes.White,
            FontSize = 14,
            Background = new SolidColorBrush(Color.FromArgb(180, 0, 0, 0)),
            Padding = new Thickness(14, 6, 14, 6)
        };
        canvas.Children.Add(_hint);
        Canvas.SetTop(_hint, 20);

        _sizeLabel = new TextBlock
        {
            Foreground = Brushes.White,
            FontSize = 12,
            Background = new SolidColorBrush(Color.FromArgb(200, 0, 0, 0)),
            Padding = new Thickness(8, 3, 8, 3),
            Visibility = Visibility.Collapsed
        };
        canvas.Children.Add(_sizeLabel);

        MouseLeftButtonDown += OnDown;
        MouseMove += OnMove;
        MouseLeftButtonUp += OnUp;
        KeyDown += OnKey;
        Loaded += (_, _) => UpdateHintPosition();
    }

    private void UpdateHintPosition()
    {
        _hint.Measure(new WpfSize(double.PositiveInfinity, double.PositiveInfinity));
        Canvas.SetLeft(_hint, (Width - _hint.DesiredSize.Width) / 2);
    }

    public System.Drawing.Rectangle? Select()
    {
        ShowDialog();
        return _result;
    }

    private void OnDown(object sender, MouseButtonEventArgs e)
    {
        _startPoint = e.GetPosition(this);
        _selectionBorder.Visibility = Visibility.Visible;
        _hint.Visibility = Visibility.Collapsed;
        _sizeLabel.Visibility = Visibility.Visible;
        CaptureMouse();
    }

    private void OnMove(object sender, WpfMouseEventArgs e)
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

        if (rect.Width < 8 || rect.Height < 8)
        {
            // Near-click → full screen of this monitor
            var b = _screen.Bounds;
            _result = new System.Drawing.Rectangle(b.X, b.Y, b.Width, b.Height);
        }
        else
        {
            _result = ToPhysical(rect);
        }

        // Hide immediately so BitBlt does not capture the overlay
        Opacity = 0;
        Hide();
        DialogResult = true;
    }

    private void OnKey(object sender, WpfKeyEventArgs e)
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

        var group = new GeometryGroup { FillRule = FillRule.EvenOdd };
        group.Children.Add(new RectangleGeometry(new Rect(0, 0, Width, Height)));
        group.Children.Add(new RectangleGeometry(r));
        _dimPath.Data = group;

        Canvas.SetLeft(_selectionBorder, r.X);
        Canvas.SetTop(_selectionBorder, r.Y);
        _selectionBorder.Width = r.Width;
        _selectionBorder.Height = r.Height;

        // PointToScreen accounts for the DPI of the actual overlay window,
        // which is more reliable than multiplying by a monitor-wide scale.
        var screenRect = ToScreenRect(r);
        _sizeLabel.Text = $"{screenRect.Width} × {screenRect.Height}";
        _sizeLabel.Measure(new WpfSize(double.PositiveInfinity, double.PositiveInfinity));
        Canvas.SetLeft(_sizeLabel, r.X + r.Width - _sizeLabel.DesiredSize.Width - 4);
        Canvas.SetTop(_sizeLabel, r.Y + r.Height + 4);
    }

    private static Rect DipRect(Point a, Point b)
    {
        double x = Math.Min(a.X, b.X);
        double y = Math.Min(a.Y, b.Y);
        double w = Math.Abs(a.X - b.X);
        double h = Math.Abs(a.Y - b.Y);
        return new Rect(x, y, w, h);
    }

    private System.Drawing.Rectangle ToPhysical(Rect r) => ToScreenRect(r);

    private System.Drawing.Rectangle ToScreenRect(Rect r)
    {
        var topLeft = PointToScreen(new Point(r.Left, r.Top));
        var bottomRight = PointToScreen(new Point(r.Right, r.Bottom));
        int left = (int)Math.Floor(Math.Min(topLeft.X, bottomRight.X));
        int top = (int)Math.Floor(Math.Min(topLeft.Y, bottomRight.Y));
        int right = (int)Math.Ceiling(Math.Max(topLeft.X, bottomRight.X));
        int bottom = (int)Math.Ceiling(Math.Max(topLeft.Y, bottomRight.Y));
        return System.Drawing.Rectangle.FromLTRB(left, top,
            Math.Max(left + 1, right), Math.Max(top + 1, bottom));
    }

    private static double GetDpi(Screen screen)
    {
        try
        {
            var pt = new NativeMethods.POINT
            {
                X = screen.Bounds.Left + screen.Bounds.Width / 2,
                Y = screen.Bounds.Top + screen.Bounds.Height / 2
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
