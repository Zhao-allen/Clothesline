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
/// Full-screen overlay for free-form region selection, similar to QQ screenshot.
/// Covers the entire monitor under the pointer so you can drag from anywhere.
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
    private readonly StackPanel _toolbar;

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

        // Cover the full monitor in DIP
        var bounds = screen.Bounds;
        Left = bounds.Left / _scale;
        Top = bounds.Top / _scale;
        Width = bounds.Width / _scale;
        Height = bounds.Height / _scale;

        var canvas = new Canvas { Background = Brushes.Transparent };
        Content = canvas;

        _dimPath = new WpfPath
        {
            Fill = new SolidColorBrush(Color.FromArgb(120, 0, 0, 0)),
            IsHitTestVisible = false
        };
        canvas.Children.Add(_dimPath);

        _selectionBorder = new WpfRectangle
        {
            Stroke = Brushes.White,
            StrokeThickness = 2,
            IsHitTestVisible = false,
            Visibility = Visibility.Collapsed
        };
        canvas.Children.Add(_selectionBorder);

        _hint = new TextBlock
        {
            Text = I18n.Culture.Name.StartsWith("zh") ? "拖拽选择区域 · ESC 取消" : "Drag to select · ESC to cancel",
            Foreground = Brushes.White,
            FontSize = 15,
            Background = new SolidColorBrush(Color.FromArgb(200, 30, 30, 30)),
            Padding = new Thickness(16, 8, 16, 8)
        };
        canvas.Children.Add(_hint);
        Canvas.SetTop(_hint, 30);

        _sizeLabel = new TextBlock
        {
            Foreground = Brushes.White,
            FontSize = 13,
            Background = new SolidColorBrush(Color.FromArgb(220, 0, 0, 0)),
            Padding = new Thickness(8, 4, 8, 4),
            Visibility = Visibility.Collapsed
        };
        canvas.Children.Add(_sizeLabel);

        // Floating toolbar (appears after selection)
        _toolbar = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Background = new SolidColorBrush(Color.FromArgb(230, 40, 40, 40)),
            Visibility = Visibility.Collapsed
        };
        var okBtn = new Button { Content = "确定", Margin = new Thickness(6, 4, 2, 4), Padding = new Thickness(12, 4, 12, 4), Background = new SolidColorBrush(Color.FromRgb(0, 120, 215)), Foreground = Brushes.White, BorderThickness = new Thickness(0) };
        var cancelBtn = new Button { Content = "取消", Margin = new Thickness(2, 4, 6, 4), Padding = new Thickness(12, 4, 12, 4), Background = new SolidColorBrush(Color.FromRgb(80, 80, 80)), Foreground = Brushes.White, BorderThickness = new Thickness(0) };
        okBtn.Click += (_, _) => Confirm();
        cancelBtn.Click += (_, _) => Cancel();
        _toolbar.Children.Add(okBtn);
        _toolbar.Children.Add(cancelBtn);
        canvas.Children.Add(_toolbar);

        MouseLeftButtonDown += OnDown;
        MouseMove += OnMove;
        MouseLeftButtonUp += OnUp;
        KeyDown += OnKey;
        Loaded += (_, _) =>
        {
            UpdateHintPosition();
            // Ensure window is exactly on the monitor
            Activate();
        };
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
        _toolbar.Visibility = Visibility.Collapsed;
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

        if (rect.Width < 10 || rect.Height < 10)
        {
            ResetSelection();
            return;
        }

        _result = ToScreenRect(rect);
        // Show toolbar near the selection for confirm/cancel
        PositionToolbar(rect);
        _toolbar.Visibility = Visibility.Visible;
    }

    private void PositionToolbar(Rect r)
    {
        _toolbar.Measure(new WpfSize(double.PositiveInfinity, double.PositiveInfinity));
        double left = r.X + r.Width - _toolbar.DesiredSize.Width;
        double top = r.Y + r.Height + 8;
        if (left < 0) left = r.X;
        if (top + _toolbar.DesiredSize.Height > Height) top = r.Y - _toolbar.DesiredSize.Height - 8;
        Canvas.SetLeft(_toolbar, Math.Max(0, left));
        Canvas.SetTop(_toolbar, Math.Max(0, top));
    }

    private void Confirm()
    {
        if (_result.HasValue)
        {
            Opacity = 0;
            Hide();
            DialogResult = true;
        }
    }

    private void Cancel()
    {
        _result = null;
        DialogResult = false;
    }

    private void ResetSelection()
    {
        _selectionBorder.Visibility = Visibility.Collapsed;
        _sizeLabel.Visibility = Visibility.Collapsed;
        _toolbar.Visibility = Visibility.Collapsed;
        _hint.Visibility = Visibility.Visible;
        _dimPath.Data = null;
    }

    private void OnKey(object sender, WpfKeyEventArgs e)
    {
        if (e.Key == Key.Escape) Cancel();
        if (e.Key == Key.Enter && _result.HasValue) Confirm();
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
        Canvas.SetLeft(_sizeLabel, r.X + r.Width - _sizeLabel.DesiredSize.Width - 6);
        Canvas.SetTop(_sizeLabel, r.Y + r.Height + 6);
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
        return System.Drawing.Rectangle.FromLTRB(left, top, Math.Max(left + 1, right), Math.Max(top + 1, bottom));
    }

    private static double GetDpi(Screen screen)
    {
        try
        {
            var pt = new NativeMethods.POINT { X = screen.Bounds.Left + screen.Bounds.Width / 2, Y = screen.Bounds.Top + screen.Bounds.Height / 2 };
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
