using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using Clothesline.Models;
using Clothesline.Services;

namespace Clothesline.UI;

/// <summary>
/// A full-screen transparent overlay that plays capture-flight and fall
/// animations above the line window. Shared between flight and fall; lazy-
/// created and closed once the last animation finishes.
///
/// Mirrors the original NSPanel used for CaptureFlight.
/// </summary>
internal sealed class FlightOverlay : Window
{
    private readonly Canvas _canvas = new();
    private FlightDriver? _driver;
    private readonly List<Flight> _flights = new();
    private readonly List<FallAnim> _falls = new();

    public static FlightOverlay? Instance { get; private set; }

    public static FlightOverlay Ensure(Screen screen)
    {
        if (Instance is null || PresentationSource.FromVisual(Instance) is null)
        {
            Instance = new FlightOverlay();
            Instance.PlaceOnScreen(screen);
            Instance.Show();
        }
        return Instance;
    }

    private FlightOverlay()
    {
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        Topmost = true;
        ShowInTaskbar = false;
        ShowActivated = false;
        ResizeMode = ResizeMode.NoResize;
        Content = _canvas;
        SourceInitialized += (_, _) => ApplyExStyles();
    }

    private void ApplyExStyles()
    {
        var hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
        int ex = NativeMethods.GetWindowLong(hwnd, NativeMethods.GWL_EXSTYLE);
        ex |= NativeMethods.WS_EX_TRANSPARENT;
        ex |= NativeMethods.WS_EX_NOACTIVATE;
        ex |= NativeMethods.WS_EX_TOOLWINDOW;
        NativeMethods.SetWindowLong(hwnd, NativeMethods.GWL_EXSTYLE, ex);
        NativeMethods.SetWindowPos(hwnd, NativeMethods.HWND_TOPMOST, 0, 0, 0, 0,
            NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE);
    }

    public void PlaceOnScreen(Screen screen)
    {
        var bounds = screen.Bounds;
        double dpi = GetDpiForScreen(screen);
        double scale = dpi / 96.0;
        Left = bounds.Left / scale;
        Top = bounds.Top / scale;
        Width = bounds.Width / scale;
        Height = bounds.Height / scale;
    }

    private static double GetDpiForScreen(Screen screen)
    {
        try
        {
            var pt = new NativeMethods.POINT { X = screen.Bounds.Left + 1, Y = screen.Bounds.Top + 1 };
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

    public bool IsAnimating => _flights.Count > 0 || _falls.Count > 0;

    public void StartFlight(PeggedItem item, Rect from, Rect to, BitmapSource photo, Action onLanded)
    {
        var flight = new Flight(item, from, to, photo, onLanded);
        _flights.Add(flight);
        _canvas.Children.Add(flight.Visual);
        EnsureDriver();
    }

    public void StartFall(PeggedItem item, Rect from, BitmapSource photo, Action onDone)
    {
        var fall = new FallAnim(item, from, photo, onDone);
        _falls.Add(fall);
        _canvas.Children.Add(fall.Visual);
        EnsureDriver();
    }

    private void EnsureDriver()
    {
        if (_driver is not null) return;
        _driver = new FlightDriver();
        _driver.Tick += OnDriverTick;
        _driver.Start();
    }

    private void OnDriverTick(double raw)
    {
        foreach (var f in _flights.ToList())
        {
            if (f.Tick(raw)) { f.Complete(); _flights.Remove(f); _canvas.Children.Remove(f.Visual); }
        }
        foreach (var f in _falls.ToList())
        {
            if (f.Tick(raw)) { f.Complete(); _falls.Remove(f); _canvas.Children.Remove(f.Visual); }
        }
        if (!IsAnimating)
        {
            _driver?.Stop();
            _driver = null;
            Instance = null;
            Close();
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        _driver?.Stop();
        _driver = null;
        Instance = null;
        base.OnClosed(e);
    }
}

/// <summary>
/// Drives all overlay animations at display refresh rate using a single
/// shared stopwatch. One driver per overlay; stops when no animations remain.
/// </summary>
internal sealed class FlightDriver
{
    private readonly Stopwatch _sw = Stopwatch.StartNew();
    private bool _stopped;

    public event Action<double>? Tick;

    public void Start()
    {
        CompositionTarget.Rendering += OnRendering;
        OnRendering(null, EventArgs.Empty);
    }

    private void OnRendering(object? sender, EventArgs e)
    {
        if (_stopped) return;
        double raw = _sw.Elapsed.TotalMilliseconds / 650.0;
        Tick?.Invoke(raw);
    }

    public void Stop()
    {
        if (_stopped) return;
        _stopped = true;
        CompositionTarget.Rendering -= OnRendering;
    }

    public void SnapToEnd()
    {
        Stop();
        Tick?.Invoke(1.0);
    }
}

/// <summary>
/// One capture-flight animation: a screenshot flies from its capture origin
/// to its slot on the line, shrinking and arcing along the way.
/// </summary>
internal sealed class Flight
{
    private readonly PeggedItem _item;
    private readonly Rect _from;
    private readonly Rect _to;
    private readonly BitmapSource _photo;
    private readonly Action _onLanded;
    public Border Visual { get; }

    private readonly Border _card;
    private readonly System.Windows.Controls.Image _img;
    private readonly Border _clip;

    public Flight(PeggedItem item, Rect from, Rect to, BitmapSource photo, Action onLanded)
    {
        _item = item;
        _from = from;
        _to = to;
        _photo = photo;
        _onLanded = onLanded;

        _card = new Border
        {
            CornerRadius = new CornerRadius(0),
            Background = new SolidColorBrush(Color.FromArgb(184, 247, 247, 247)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(19, 0, 0, 0)),
            BorderThickness = new Thickness(0.5),
            Effect = new DropShadowEffect { Color = Colors.Black, Opacity = 0.18, BlurRadius = 10, ShadowDepth = 5, Direction = 270 }
        };

        _img = new System.Windows.Controls.Image
        {
            Source = photo,
            Stretch = Stretch.Uniform,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
            VerticalAlignment = System.Windows.VerticalAlignment.Center
        };

        var photoBorder = new Border
        {
            CornerRadius = new CornerRadius(0),
            BorderBrush = new SolidColorBrush(Color.FromArgb(46, 255, 255, 255)),
            BorderThickness = new Thickness(0.5),
            Child = _img
        };

        _clip = BuildClothespin();
        _clip.Opacity = 0;

        var inner = new Grid();
        inner.Children.Add(photoBorder);
        var host = new Grid();
        host.Children.Add(inner);
        host.Children.Add(_clip);
        _card.Child = host;

        Visual = _card;
    }

    /// <returns>true when finished.</returns>
    public bool Tick(double raw)
    {
        if (raw >= 1.0)
        {
            _onLanded();
            return true;
        }

        double k = raw < 0.5 ? 4 * raw * raw * raw : 1 - Math.Pow(-2 * raw + 2, 3) / 2;

        double w = Lerp(_from.Width, _to.Width, k);
        double h = Lerp(_from.Height, _to.Height, k);
        double x = Lerp(_from.X + _from.Width / 2, _to.X + _to.Width / 2, k) - w / 2;
        double yBase = Lerp(_from.Y, _to.Y, k);
        double y = yBase - Math.Sin(Math.PI * k) * 30;

        double chrome = ChromeEase(k);
        double radius = Lerp(0, 16, k);
        double inset = 4 * k;
        double rot = Lerp(0, _item.Tilt, k);

        _card.CornerRadius = new CornerRadius(radius);
        _card.Width = w;
        _card.Height = h;
        _card.Opacity = chrome;
        _clip.Opacity = chrome;

        Canvas.SetLeft(Visual, x);
        Canvas.SetTop(Visual, y);

        // Photo scaling: converge to resting card size at k=1 for a clean cross-fade
        double boxW = Math.Max(1, w - 2 * inset);
        double boxH = Math.Max(1, h - 2 * inset);
        double rest = Math.Min((Layout.CardWidth - 14.0) / _photo.PixelWidth, Layout.PhotoMaxHeight / _photo.PixelHeight);
        double cap = Lerp(1.0, rest, k);
        double s = Math.Min(Math.Min(boxW / _photo.PixelWidth, boxH / _photo.PixelHeight), cap);
        _img.Width = _photo.PixelWidth * s;
        _img.Height = _photo.PixelHeight * s;

        var tg = new TransformGroup();
        tg.Children.Add(new RotateTransform(rot, w / 2, 0));
        Visual.RenderTransform = tg;

        return false;
    }

    public void Complete() => _onLanded();

    private static Border BuildClothespin()
    {
        var metal = new LinearGradientBrush
        {
            StartPoint = new Point(0, 0), EndPoint = new Point(1, 0),
            GradientStops =
            {
                new GradientStop(Color.FromRgb(178, 178, 178), 0),
                new GradientStop(Color.FromRgb(237, 237, 237), 0.35),
                new GradientStop(Color.FromRgb(209, 209, 209), 0.65),
                new GradientStop(Color.FromRgb(158, 158, 158), 1),
            }
        };
        var body = new Border
        {
            Width = 9, Height = 26,
            CornerRadius = new CornerRadius(3.5),
            Background = metal,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
            VerticalAlignment = System.Windows.VerticalAlignment.Bottom,
            BorderBrush = new LinearGradientBrush
            {
                StartPoint = new Point(0, 0), EndPoint = new Point(0, 1),
                GradientStops =
                {
                    new GradientStop(Color.FromArgb(230, 255, 255, 255), 0),
                    new GradientStop(Color.FromArgb(46, 0, 0, 0), 1),
                }
            },
            BorderThickness = new Thickness(0.6),
            Effect = new DropShadowEffect { Color = Colors.Black, Opacity = 0.30, BlurRadius = 2, ShadowDepth = 1.5, Direction = 270 }
        };
        var slot = new Border
        {
            Width = 5, Height = 1.4,
            CornerRadius = new CornerRadius(0.7),
            Background = new SolidColorBrush(Color.FromArgb(82, 0, 0, 0)),
            HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
            VerticalAlignment = System.Windows.VerticalAlignment.Top,
            Margin = new Thickness(0, 8.5, 0, 0)
        };
        var grid = new Grid();
        grid.Children.Add(body);
        grid.Children.Add(slot);
        grid.IsHitTestVisible = false;
        return new Border { Child = grid, IsHitTestVisible = false };
    }

    private static double Lerp(double a, double b, double t) => a + (b - a) * t;
    private static double ChromeEase(double k)
    {
        double t = Math.Clamp((k - 0.35) / 0.65, 0, 1);
        return t * t * (3 - 2 * t);
    }
}

/// <summary>
/// One fall animation: the card drops 520 DIP while rotating and fading,
/// played on the full-screen overlay so it is never clipped by the line
/// window's 210px height.
/// </summary>
internal sealed class FallAnim
{
    private readonly PeggedItem _item;
    private readonly Rect _from;
    private readonly BitmapSource _photo;
    private readonly Action _onDone;
    public Border Visual { get; }

    private readonly Border _card;
    private readonly System.Windows.Controls.Image _img;
    private readonly TranslateTransform _translate = new();
    private readonly RotateTransform _rotate = new();

    public FallAnim(PeggedItem item, Rect from, BitmapSource photo, Action onDone)
    {
        _item = item;
        _from = from;
        _photo = photo;
        _onDone = onDone;

        _card = new Border
        {
            Width = from.Width,
            Height = from.Height,
            CornerRadius = new CornerRadius(16),
            Background = new SolidColorBrush(Color.FromArgb(184, 247, 247, 247)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(19, 0, 0, 0)),
            BorderThickness = new Thickness(0.5),
            Effect = new DropShadowEffect { Color = Colors.Black, Opacity = 0.18, BlurRadius = 10, ShadowDepth = 5, Direction = 270 }
        };

        _img = new System.Windows.Controls.Image
        {
            Source = photo,
            Stretch = Stretch.Uniform,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
            VerticalAlignment = System.Windows.VerticalAlignment.Center,
            MaxWidth = Layout.CardWidth - 14,
            MaxHeight = Layout.PhotoMaxHeight
        };

        var photoBorder = new Border
        {
            CornerRadius = new CornerRadius(12),
            BorderBrush = new SolidColorBrush(Color.FromArgb(46, 255, 255, 255)),
            BorderThickness = new Thickness(0.5),
            Child = _img,
            Margin = new Thickness(4)
        };

        var clip = BuildClothespin();
        var inner = new Grid();
        inner.Children.Add(photoBorder);
        var host = new Grid();
        host.Children.Add(inner);
        host.Children.Add(clip);
        _card.Child = host;

        var tg = new TransformGroup();
        tg.Children.Add(_translate);
        tg.Children.Add(_rotate);
        _card.RenderTransform = tg;
        _card.RenderTransformOrigin = new Point(0.5, 0);

        Visual = _card;
        Canvas.SetLeft(Visual, from.X);
        Canvas.SetTop(Visual, from.Y);
    }

    /// <returns>true when finished.</returns>
    public bool Tick(double raw)
    {
        // Fall uses a 550ms duration mapped to the same 650ms driver clock,
        // so raw is scaled: fallRaw = raw * (650/550) ≈ raw * 1.182
        double fallRaw = raw * 650.0 / 550.0;
        if (fallRaw >= 1.0)
        {
            _onDone();
            return true;
        }

        // PowerEase Power=3 EaseIn = raw^3
        double e = fallRaw * fallRaw * fallRaw;

        _translate.Y = e * 520;
        _rotate.Angle = _item.Tilt + (_item.Tilt * 7 + 20) * e;
        Visual.Opacity = 1 - e;

        return false;
    }

    public void Complete() => _onDone();

    private static Border BuildClothespin()
    {
        var metal = new LinearGradientBrush
        {
            StartPoint = new Point(0, 0), EndPoint = new Point(1, 0),
            GradientStops =
            {
                new GradientStop(Color.FromRgb(178, 178, 178), 0),
                new GradientStop(Color.FromRgb(237, 237, 237), 0.35),
                new GradientStop(Color.FromRgb(209, 209, 209), 0.65),
                new GradientStop(Color.FromRgb(158, 158, 158), 1),
            }
        };
        var body = new Border
        {
            Width = 9, Height = 26,
            CornerRadius = new CornerRadius(3.5),
            Background = metal,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
            VerticalAlignment = System.Windows.VerticalAlignment.Bottom,
            BorderBrush = new LinearGradientBrush
            {
                StartPoint = new Point(0, 0), EndPoint = new Point(0, 1),
                GradientStops =
                {
                    new GradientStop(Color.FromArgb(230, 255, 255, 255), 0),
                    new GradientStop(Color.FromArgb(46, 0, 0, 0), 1),
                }
            },
            BorderThickness = new Thickness(0.6),
            Effect = new DropShadowEffect { Color = Colors.Black, Opacity = 0.30, BlurRadius = 2, ShadowDepth = 1.5, Direction = 270 }
        };
        var slot = new Border
        {
            Width = 5, Height = 1.4,
            CornerRadius = new CornerRadius(0.7),
            Background = new SolidColorBrush(Color.FromArgb(82, 0, 0, 0)),
            HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
            VerticalAlignment = System.Windows.VerticalAlignment.Top,
            Margin = new Thickness(0, 8.5, 0, 0)
        };
        var grid = new Grid();
        grid.Children.Add(body);
        grid.Children.Add(slot);
        grid.IsHitTestVisible = false;
        return new Border { Child = grid, IsHitTestVisible = false };
    }
}
