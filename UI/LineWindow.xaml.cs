using System.Collections.Specialized;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Threading;
using System.Windows.Forms;
using Clothesline.Models;
using Clothesline.Services;

using Line = Clothesline.Models.Line;

namespace Clothesline.UI;

/// <summary>
/// The transparent strip along the top of the screen that floats over every
/// app, never takes focus, and lets clicks pass through everywhere except
/// over the photos. Mirrors the Swift <c>LinePanel</c>.
/// </summary>
internal partial class LineWindow : Window
{
    private readonly Line _line;
    private readonly Path _ropePath = new();
    private readonly Path _ropeShadow = new();
    private readonly Path _ropeHighlight = new();
    private readonly Border _emptyHint = new();
    private readonly Dictionary<Guid, PeggedCard> _cards = new();

    private DispatcherTimer? _mouseTimer;
    private DateTime? _hotZoneStart;
    private DateTime? _awaySince;

    public bool IsPresent { get; private set; }
    public bool IsRevealed { get; private set; }
    public bool Pinned { get; private set; }
    private DateTime _peekUntil = DateTime.MinValue;
    private bool _wanted;
    private bool _keepOpen;
    private bool _menuBarSuppressed;

    public event Action? RequestRefresh;

    public LineWindow(Line line)
    {
        _line = line;
        InitializeComponent();
        // Start tucked above the top edge. Must be set here (before Show)
        // so it never fights with the slide-in animation that Reveal starts.
        SlideTransform.Y = -Layout.PanelHeight - 12;
        SourceInitialized += (_, _) => ApplyWindowStyles();
        Loaded += (_, _) =>
        {
            BuildRope();
            PlaceOnScreen();
            UpdateCapacity();
            _line.Items.CollectionChanged += Items_CollectionChanged;
            _line.PropertyChanged += Line_PropertyChanged;
            foreach (var item in _line.Items) AddCard(item);
            LayoutItems();
        };
        MouseWheel += LineWindow_MouseWheel;
    }

    private void ApplyWindowStyles()
    {
        var hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
        int ex = NativeMethods.GetWindowLong(hwnd, NativeMethods.GWL_EXSTYLE);
        ex |= NativeMethods.WS_EX_TOOLWINDOW;     // hide from Alt-Tab
        ex |= NativeMethods.WS_EX_NOACTIVATE;      // don't take focus
        NativeMethods.SetWindowLong(hwnd, NativeMethods.GWL_EXSTYLE, ex);
    }

    private void BuildRope()
    {
        // Shadow: soft blurred shadow under the rope (mirrors the original's
        // .blur(radius: 1.2)).
        _ropeShadow.Stroke = new SolidColorBrush(Color.FromArgb(56, 0, 0, 0));
        _ropeShadow.StrokeThickness = 1.4;
        _ropeShadow.IsHitTestVisible = false;
        _ropeShadow.Effect = new System.Windows.Media.Effects.BlurEffect { Radius = 1.2 };

        // Core rope: mid-gray, 1.2px. Anti-aliased (no Aliased) so the curve
        // stays smooth on high-DPI screens.
        _ropePath.Stroke = new SolidColorBrush(Color.FromRgb(140, 140, 140));
        _ropePath.StrokeThickness = 1.2;
        _ropePath.IsHitTestVisible = false;

        // Highlight: faint white line above the core, 0.4px.
        _ropeHighlight.Stroke = new SolidColorBrush(Color.FromArgb(115, 255, 255, 255));
        _ropeHighlight.StrokeThickness = 0.4;
        _ropeHighlight.IsHitTestVisible = false;

        // Fade both ends so the rope seems to come from beyond the screen.
        var fade = new LinearGradientBrush
        {
            StartPoint = new Point(0, 0),
            EndPoint = new Point(1, 0)
        };
        fade.GradientStops.Add(new GradientStop(Colors.Transparent, 0.0));
        fade.GradientStops.Add(new GradientStop(Colors.Black, 0.08));
        fade.GradientStops.Add(new GradientStop(Colors.Black, 0.92));
        fade.GradientStops.Add(new GradientStop(Colors.Transparent, 1.0));
        _ropeShadow.OpacityMask = fade;
        _ropePath.OpacityMask = fade;
        _ropeHighlight.OpacityMask = fade;

        RootCanvas.Children.Add(_ropeShadow);
        RootCanvas.Children.Add(_ropePath);
        RootCanvas.Children.Add(_ropeHighlight);
        Panel.SetZIndex(_ropePath, 0);

        // Empty-state hint shown when the line has no screenshots.
        _emptyHint.Child = new TextBlock
        {
            Text = "Press Win+Alt+S to take a screenshot",
            Foreground = new SolidColorBrush(Color.FromArgb(180, 90, 90, 90)),
            FontSize = 12,
            Padding = new Thickness(12, 6, 12, 6)
        };
        _emptyHint.Background = new SolidColorBrush(Color.FromArgb(180, 245, 245, 245));
        _emptyHint.CornerRadius = new CornerRadius(14);
        _emptyHint.IsHitTestVisible = false;
        RootCanvas.Children.Add(_emptyHint);
        Panel.SetZIndex(_emptyHint, 0);
    }

    private void UpdateRope()
    {
        double w = ActualWidth > 0 ? ActualWidth : Width;
        if (w <= 0) return;
        var top = Layout.RopeTop;
        var sag = Layout.Sag(w);
        var geo = new StreamGeometry();
        using (var ctx = geo.Open())
        {
            ctx.BeginFigure(new Point(-20, top), false, false);
            ctx.QuadraticBezierTo(
                new Point(w / 2, top + 2 * sag),
                new Point(w + 20, top), true, false);
        }
        geo.Freeze();
        _ropeShadow.Data = geo;
        _ropePath.Data = geo;
        _ropeHighlight.Data = geo;
        _ropeShadow.RenderTransform = new TranslateTransform(0, 1.2);
        _ropeHighlight.RenderTransform = new TranslateTransform(0, -0.35);
    }

    public void PlaceOnScreen(Screen? screen = null)
    {
        screen ??= ScreenUnderPointer();
        if (screen is null) return;
        var bounds = screen.Bounds;

        // Screen.Bounds is in physical pixels; WPF uses DIPs. With Per-Monitor
        // V2 DPI awareness we must convert so the window covers the screen
        // crisply at any scaling.
        var dpi = GetDpiForScreen(screen);
        double scale = dpi / 96.0;
        Left = bounds.Left / scale;
        Top = bounds.Top / scale;
        Width = bounds.Width / scale;
        Height = Layout.PanelHeight;
        UpdateRope();
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

    public static Screen? ScreenUnderPointer()
    {
        if (NativeMethods.GetCursorPos(out var p))
            return Screen.AllScreens.FirstOrDefault(s => s.Bounds.Contains(p.X, p.Y))
                   ?? Screen.PrimaryScreen;
        return Screen.PrimaryScreen;
    }

    public void UpdateCapacity()
    {
        double usable = Width - 200;
        int fits = Math.Max(3, Math.Min(12, (int)(usable / Layout.Spacing)));
        _line.VisibleCount = fits;
        _line.MaxItems = Math.Max(fits, Line.KeepOnLine ?? fits);
    }

    private void Items_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.NewItems is not null)
            foreach (PeggedItem item in e.NewItems) AddCard(item);
        if (e.OldItems is not null)
            foreach (PeggedItem item in e.OldItems) RemoveCard(item.Id);
        LayoutItems();
    }

    private void Line_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(Line.Scroll) || e.PropertyName == nameof(Line.VisibleCount))
            LayoutItems();
        else if (e.PropertyName == nameof(Line.Gust))
            foreach (var c in _cards.Values) c.Breeze();
    }

    private void AddCard(PeggedItem item)
    {
        var card = new PeggedCard(item, _line);
        _cards[item.Id] = card;
        RootCanvas.Children.Add(card);
        Panel.SetZIndex(card, 1);
    }

    private void RemoveCard(Guid id)
    {
        if (_cards.Remove(id, out var card))
        {
            RootCanvas.Children.Remove(card);
            card.Dispose();
        }
    }

    public void LayoutItems()
    {
        double w = ActualWidth > 0 ? ActualWidth : Width;
        int count = _line.Items.Count;
        for (int i = 0; i < count; i++)
        {
            var item = _line.Items[i];
            if (!_cards.TryGetValue(item.Id, out var card)) continue;
            double x = Layout.X(i, count, w, _line.VisibleCount, _line.Scroll);
            double ropeY = Layout.RopeY(x, w);
            double cardHeight = Layout.PanelHeight - ropeY;
            Canvas.SetLeft(card, x - Layout.CardWidth / 2);
            Canvas.SetTop(card, ropeY - Layout.PinAbove);
            card.Width = Layout.CardWidth;
            card.Height = cardHeight;
            card.UpdateFade(x, w);
        }

        // Show the empty hint only when there are no screenshots.
        if (count == 0)
        {
            double ropeMidY = Layout.RopeY(w / 2, w);
            _emptyHint.Visibility = Visibility.Visible;
            _emptyHint.Measure(new System.Windows.Size(double.PositiveInfinity, double.PositiveInfinity));
            Canvas.SetLeft(_emptyHint, w / 2 - _emptyHint.DesiredSize.Width / 2);
            Canvas.SetTop(_emptyHint, ropeMidY + 34);
        }
        else
        {
            _emptyHint.Visibility = Visibility.Collapsed;
        }
    }

    public new void Show()
    {
        if (IsPresent) return;
        IsPresent = true;
        base.Show();
    }

    public void Present()
    {
        if (IsPresent) return;
        IsPresent = true;
        Opacity = 1;
        if (IsRevealed) Show();
        else base.Show();
    }

    public void Dismiss()
    {
        if (!IsPresent) return;
        IsPresent = false;
        SetRevealed(false);
        Task.Delay(400).ContinueWith(_ =>
        {
            Dispatcher.Invoke(() => { if (!IsPresent) Hide(); });
        });
    }

    public void Reveal(bool pinned = false, double peekSeconds = 0)
    {
        if (!IsPresent) return;
        if (pinned) Pinned = true;
        if (peekSeconds > 0) _peekUntil = DateTime.Now.AddSeconds(peekSeconds);
        _awaySince = null;
        SetRevealed(true);
    }

    public void SetRevealed(bool on)
    {
        if (on == IsRevealed) return;
        IsRevealed = on;
        _line.Revealed = on;
        if (on)
        {
            _line.Scroll = 0;
            Show();
            AnimateSlide(0, TimeSpan.FromMilliseconds(420));
        }
        else
        {
            AnimateSlide(-(Layout.PanelHeight + 12), TimeSpan.FromMilliseconds(220));
            Pinned = false;
            _peekUntil = DateTime.MinValue;
        }
    }

    private void AnimateSlide(double to, TimeSpan duration)
    {
        var anim = new DoubleAnimation(to, duration)
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut }
        };
        SlideTransform.BeginAnimation(TranslateTransform.YProperty, anim);
    }

    public void Toggle()
    {
        if (IsRevealed)
        {
            SetRevealed(false);
            if (_line.LiveCount == 0)
            {
                _keepOpen = false;
                _wanted = false;
                RequestRefresh?.Invoke();
            }
        }
        else
        {
            _keepOpen = true;
            _wanted = true;
            PlaceOnScreen();
            UpdateCapacity();
            RequestRefresh?.Invoke();
            Reveal(pinned: true);
        }
    }

    public void ComeDown(Screen? screen = null)
    {
        PlaceOnScreen(screen);
        UpdateCapacity();
        _wanted = true;
        RequestRefresh?.Invoke();
        if (App.PeeksAtNew) Reveal(peekSeconds: 2.5);
    }

    public void ItemsChanged()
    {
        if (_line.LiveCount != 0 || _keepOpen) return;
        Task.Delay(700).ContinueWith(_ => Dispatcher.Invoke(() =>
        {
            if (_line.LiveCount != 0 || _keepOpen) return;
            _wanted = false;
            RequestRefresh?.Invoke();
        }));
    }

    public bool Wanted => _wanted;
    public void SetWanted(bool value) { _wanted = value; }

    public void StartMouseTracking()
    {
        if (_mouseTimer is not null) return;
        _mouseTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1000.0 / 30.0) };
        _mouseTimer.Tick += (_, _) => Tick();
        _mouseTimer.Start();
    }

    public void StopMouseTracking()
    {
        _mouseTimer?.Stop();
        _mouseTimer = null;
    }

    private const double RevealDelay = 0.25;
    private const double RetractDelay = 0.5;

    private void Tick()
    {
        if (!NativeMethods.GetCursorPos(out var cursor)) return;
        var now = DateTime.Now;

        var screen = Screen.AllScreens.FirstOrDefault(s => s.Bounds.Contains(cursor.X, cursor.Y));
        bool inTopBand = screen is not null && cursor.Y <= screen.Bounds.Top + 2;

        if (!inTopBand) _menuBarSuppressed = false;

        if (!IsRevealed)
        {
            if (App.OpensFromMenuBar && screen is not null && inTopBand && !_menuBarSuppressed
                && !FullScreen.BlocksLineUnderPointer())
            {
                _hotZoneStart ??= now;
                if ((now - _hotZoneStart.Value).TotalSeconds >= RevealDelay)
                {
                    _hotZoneStart = null;
                    var currentScreen = ScreenUnderPointer();
                    PlaceOnScreen(currentScreen);
                    UpdateCapacity();
                    RequestRefresh?.Invoke();
                    Reveal();
                }
            }
            else _hotZoneStart = null;
            return;
        }

        // Convert cursor Y from physical pixels to DIPs so it matches the
        // window's Top/Height (which WPF keeps in DIPs with PerMonitorV2).
        double scale = screen is not null ? GetDpiForScreen(screen) / 96.0 : 1.0;
        double cursorY = cursor.Y / scale;
        var zoneTop = screen is not null ? screen.Bounds.Top / scale : 0;
        var zoneBottom = Top + Height;
        bool inside = cursorY >= zoneTop && cursorY <= zoneBottom + 20;
        if (inside && Pinned) Pinned = false;

        bool busy = Pinned || PeggedCard.IsDragging || _line.PressedId is not null || now < _peekUntil;
        if (inside || busy)
        {
            _awaySince = null;
        }
        else
        {
            _awaySince ??= now;
            if ((now - _awaySince.Value).TotalSeconds >= RetractDelay)
            {
                _awaySince = null;
                SetRevealed(false);
            }
        }
    }

    public void MenuBarClicked()
    {
        _menuBarSuppressed = true;
        _hotZoneStart = null;
        if (IsRevealed)
        {
            Pinned = false;
            SetRevealed(false);
        }
    }

    private void LineWindow_MouseWheel(object sender, MouseWheelEventArgs e)
    {
        _line.ScrollBy(-e.Delta * 0.5);
        e.Handled = true;
    }

    public Rect CardFrame(Guid id)
    {
        if (!_cards.TryGetValue(id, out var card)) return Rect.Empty;
        var index = _line.Items.FirstIndex(i => i.Id == id);
        if (index < 0) return Rect.Empty;
        double w = Width;
        double x = Layout.X(index, _line.Items.Count, w, _line.VisibleCount, _line.Scroll);
        return new Rect(Left + x - Layout.CardWidth / 2,
                        Top + card.ActualHeight,
                        Layout.CardWidth, card.ActualHeight);
    }
}
