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
/// Full-screen overlay for drag-selecting a screenshot region. The capture
/// rectangle is derived from the overlay's actual screen coordinates.
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
        UpdateSelection(_startPoint.Value, e.GetPosition(this));
    }

    private void OnUp(object sender, MouseButtonEventArgs e)
    {
        if (_startPoint is null) return;
        var rect = DipRect(_startPoint.Value, e.GetPosition(this));
        ReleaseMouseCapture();
        _startPoint = null;

        // This capture mode is always user-defined: a click does not fall back
        // to a full-screen screenshot.
        if (rect.Width < 8 || rect.Height < 8)
        {
            _selectionBorder.Visibility = Visibility.Collapsed;
            _sizeLabel.Visibility = Visibility.Collapsed;
            _hint.Visibility = Visibility.Visible;
            return;
        }

        _result = ToScreenRect(rect);
        Opacity = 0;
        Hide();
        DialogResult = true;
    }

    private void OnKey(object sender, WpfKeyEventArgs e)
    {
        if (e.Key != Key.Escape) return;
        _result = null;
        DialogResult = false;
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
        return new Rect(x, y, Math.Abs(a.X - b.X), Math.Abs(a.Y - b.Y));
    }

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
            var monitor = NativeMethods.MonitorFromPoint(pt, NativeMethods.MONITOR_DEFAULTTONEAREST);
            if (monitor != IntPtr.Zero)
            {
                NativeMethods.GetDpiForMonitor(monitor, NativeMethods.MDT_EFFECTIVE_DPI, out uint dpi, out _);
                if (dpi > 0) return dpi;
            }
        }
        catch { }
        return 96.0;
    }
}
