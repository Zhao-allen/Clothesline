using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using Tendedero.Models;
using Tendedero.Services;

using Line = Tendedero.Models.Line;
using Image = System.Windows.Controls.Image;
using UserControl = System.Windows.Controls.UserControl;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using HA = System.Windows.HorizontalAlignment;
using VA = System.Windows.VerticalAlignment;

namespace Tendedero.UI;

/// <summary>
/// One photo with its clothespin. All the charm lives here: it drops onto
/// the line, swings, sways with the breeze and falls when you pull it off.
/// Mirrors the Swift <c>PeggedView</c> + <c>GrabView</c>.
/// </summary>
internal sealed class PeggedCard : UserControl, IDisposable
{
    public static bool IsDragging;

    private readonly PeggedItem _item;
    private readonly Line _line;

    private readonly RotateTransform _swing = new();
    private readonly TranslateTransform _arrival = new();
    private readonly ScaleTransform _scale = new(1, 1);
    private readonly TransformGroup _transforms = new();

    private readonly Border _card;
    private readonly Image _photo;
    private readonly Border _cross;
    private readonly Border _copiedBadge;
    private readonly Border _playBadge;

    private Point _downPoint;
    private bool _startedDrag;
    private bool _didLongPress;
    private DispatcherTimer? _holdTimer;
    private bool _hovering;
    private bool _disposed;

    private const double HoldDuration = 0.45;

    public PeggedCard(PeggedItem item, Line line)
    {
        _item = item;
        _line = line;

        _transforms.Children.Add(_scale);
        _transforms.Children.Add(_swing);
        _transforms.Children.Add(_arrival);
        RenderTransform = _transforms;
        RenderTransformOrigin = new Point(0.5, 0); // swing from the top

        var root = new Grid();
        // Clip
        var clip = BuildClothespin();
        Grid.SetRow(clip, 0);

        // Card
        _card = BuildCard(item, out _photo, out _cross, out _copiedBadge, out _playBadge);
        _cross.Tag = this;
        var cardPanel = new Grid();
        cardPanel.Children.Add(_card);
        Grid.SetRow(cardPanel, 1);

        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(26) });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.Children.Add(clip);
        root.Children.Add(cardPanel);

        Content = root;

        // Interactions
        MouseEnter += (_, _) => { _hovering = true; UpdateHover(); };
        MouseLeave += (_, _) => { _hovering = false; UpdateHover(); };
        MouseLeftButtonDown += OnMouseDown;
        MouseLeftButtonUp += OnMouseUp;
        MouseMove += OnMouseMove;
        MouseRightButtonDown += OnRightDown;
        PreviewMouseLeftButtonDown += (_, _) => _line.PressedId = _item.Id;
        PreviewMouseLeftButtonUp += (_, _) => _line.PressedId = null;

        Loaded += (_, _) => Arrive();

        _line.PropertyChanged += Line_PropertyChanged;
    }

    private void Line_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(Line.CopiedId))
        {
            bool copied = _line.CopiedId == _item.Id;
            _copiedBadge.Visibility = copied ? Visibility.Visible : Visibility.Collapsed;
            if (copied) Nudge(3);
        }
        else if (e.PropertyName == nameof(Line.DraggingId))
        {
            bool dragging = _line.DraggingId == _item.Id;
            _card.Opacity = dragging ? 0.45 : 1;
        }
    }

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
            HorizontalAlignment = HA.Center,
            VerticalAlignment = VA.Bottom,
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
        // Slot the line passes through.
        var slot = new Border
        {
            Width = 5, Height = 1.4,
            CornerRadius = new CornerRadius(0.7),
            Background = new SolidColorBrush(Color.FromArgb(82, 0, 0, 0)),
            HorizontalAlignment = HA.Center,
            VerticalAlignment = VA.Top,
            Margin = new Thickness(0, 8.5, 0, 0)
        };
        var grid = new Grid();
        grid.Children.Add(body);
        grid.Children.Add(slot);
        grid.IsHitTestVisible = false;
        var wrap = new Border { Child = grid, IsHitTestVisible = false };
        return wrap;
    }

    private static Border BuildCard(PeggedItem item, out Image photo, out Border cross, out Border copiedBadge, out Border playBadge)
    {
        var card = new Border
        {
            CornerRadius = new CornerRadius(16),
            Background = new SolidColorBrush(Color.FromArgb(184, 247, 247, 247)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(19, 0, 0, 0)),
            BorderThickness = new Thickness(0.5),
            Margin = new Thickness(4),
            Effect = new DropShadowEffect { Color = Colors.Black, Opacity = 0.18, BlurRadius = 10, ShadowDepth = 5, Direction = 270 }
        };

        photo = new Image
        {
            Source = item.Thumb,
            Stretch = Stretch.Uniform,
            HorizontalAlignment = HA.Center,
            VerticalAlignment = VA.Center,
            MaxHeight = Layout.PhotoMaxHeight,
            MaxWidth = Layout.CardWidth - 14,
            Clip = new RectangleGeometry(new Rect(0, 0, 1000, 1000), 12, 12),
        };

        var photoBorder = new Border
        {
            CornerRadius = new CornerRadius(12),
            BorderBrush = new SolidColorBrush(Color.FromArgb(46, 255, 255, 255)),
            BorderThickness = new Thickness(0.5),
            Child = photo
        };

        // Play badge for recordings.
        playBadge = new Border
        {
            Width = 28, Height = 28,
            CornerRadius = new CornerRadius(14),
            Background = new SolidColorBrush(Color.FromArgb(184, 255, 255, 255)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(46, 0, 0, 0)),
            BorderThickness = new Thickness(0.5),
            Visibility = item.IsRecording ? Visibility.Visible : Visibility.Collapsed,
            HorizontalAlignment = HA.Center,
            VerticalAlignment = VA.Center,
            IsHitTestVisible = false,
            Child = new TextBlock
            {
                Text = "▶",
                FontSize = 11,
                FontWeight = FontWeights.Bold,
                Foreground = Brushes.Black,
                HorizontalAlignment = HA.Center,
                VerticalAlignment = VA.Center,
                Margin = new Thickness(2, 0, 0, 0)
            }
        };

        // Corner cross: discard.
        cross = new Border
        {
            Width = 20, Height = 20,
            CornerRadius = new CornerRadius(10),
            Background = new SolidColorBrush(Color.FromArgb(184, 255, 255, 255)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(46, 0, 0, 0)),
            BorderThickness = new Thickness(0.5),
            Opacity = 0,
            HorizontalAlignment = HA.Left,
            VerticalAlignment = VA.Top,
            Margin = new Thickness(3),
            IsHitTestVisible = true,
            Child = new TextBlock
            {
                Text = "✕",
                FontSize = 8,
                FontWeight = FontWeights.Bold,
                Foreground = Brushes.Black,
                HorizontalAlignment = HA.Center,
                VerticalAlignment = VA.Center
            }
        };
        cross.MouseLeftButtonDown += (s, e) =>
        {
            e.Handled = true;
            if (((Border)s).Tag is PeggedCard pc)
                pc._line.Discard(pc._item.Id);
        };
        cross.Visibility = Visibility.Visible;

        // Copied badge.
        copiedBadge = new Border
        {
            CornerRadius = new CornerRadius(10),
            Background = new SolidColorBrush(Color.FromArgb(184, 255, 255, 255)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(46, 0, 0, 0)),
            BorderThickness = new Thickness(0.5),
            Padding = new Thickness(10, 5, 10, 5),
            Visibility = Visibility.Collapsed,
            HorizontalAlignment = HA.Center,
            VerticalAlignment = VA.Bottom,
            Margin = new Thickness(0, 0, 0, -8),
            IsHitTestVisible = false,
            Child = new TextBlock
            {
                Text = "✓ Copied",
                FontSize = 11,
                FontWeight = FontWeights.SemiBold,
                Foreground = Brushes.Black
            }
        };

        var inner = new Grid();
        inner.Children.Add(photoBorder);
        if (item.IsRecording) inner.Children.Add(playBadge);

        var host = new Grid();
        host.Children.Add(inner);
        host.Children.Add(cross);
        host.Children.Add(copiedBadge);
        card.Child = host;
        return card;
    }

    private void UpdateHover()
    {
        double targetScale = _line.PressedId == _item.Id ? 0.95 : (_hovering ? 1.035 : 1.0);
        var anim = new DoubleAnimation(targetScale, TimeSpan.FromMilliseconds(180))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut }
        };
        _scale.BeginAnimation(ScaleTransform.ScaleXProperty, anim);
        _scale.BeginAnimation(ScaleTransform.ScaleYProperty, anim);

        var shadow = (DropShadowEffect)_card.Effect;
        if (shadow is not null)
        {
            shadow.Opacity = _hovering ? 0.26 : 0.18;
            shadow.BlurRadius = _hovering ? 14 : 10;
            shadow.ShadowDepth = _hovering ? 8 : 5;
        }

        _cross.Opacity = _hovering && !IsDragging ? 1 : 0;
    }

    public void UpdateFade(double x, double width)
    {
        double fade = Math.Min(1, Math.Max(0, Math.Min(x, width - x) / 90.0));
        Opacity = fade;
    }

    private void Arrive()
    {
        if (_item.Flying) return;
        _arrival.Y = -46;
        _swing.Angle = 16;
        var drop = new DoubleAnimation(0, TimeSpan.FromMilliseconds(420))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        _arrival.BeginAnimation(TranslateTransform.YProperty, drop);
        SwingTo(0, TimeSpan.FromMilliseconds(600));
    }

    public void Land() => Nudge(2.2);

    public void Breeze()
    {
        var delay = TimeSpan.FromMilliseconds(Random.Shared.NextDouble() * 350);
        Task.Delay(delay).ContinueWith(_ => Dispatcher.Invoke(() =>
            Nudge(Random.Shared.NextDouble() * 1.8 + 1.6)));
    }

    private void Nudge(double degrees)
    {
        var outAnim = new DoubleAnimation(degrees, TimeSpan.FromMilliseconds(300))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        _swing.BeginAnimation(RotateTransform.AngleProperty, outAnim);
        Task.Delay(300).ContinueWith(_ => Dispatcher.Invoke(() => SwingTo(0, TimeSpan.FromMilliseconds(500))));
    }

    private void SwingTo(double degrees, TimeSpan duration)
    {
        var anim = new DoubleAnimation(degrees, duration)
        {
            EasingFunction = new ElasticEase { Springiness = 0.4, EasingMode = EasingMode.EaseOut, Oscillations = 3 }
        };
        _swing.BeginAnimation(RotateTransform.AngleProperty, anim);
    }

    // MARK: Interaction

    private void OnMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is Border b && b == _cross) return;
        if (e.ClickCount == 2)
        {
            _downPoint = new Point(double.NaN, double.NaN);
            _line.Open(_item.Id);
            return;
        }
        _downPoint = e.GetPosition(this);
        _startedDrag = false;
        _didLongPress = false;
        _line.PressedId = _item.Id;
        _holdTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(HoldDuration) };
        _holdTimer.Tick += (_, _) =>
        {
            _holdTimer?.Stop();
            if (!_startedDrag)
            {
                _didLongPress = true;
                _line.PressedId = null;
                _line.Markup(_item.Id);
            }
        };
        _holdTimer.Start();
        CaptureMouse();
    }

    private void OnMouseMove(object sender, MouseEventArgs e)
    {
        if (_downPoint.X is double.NaN) return;
        if (_startedDrag || _didLongPress) return;
        var p = e.GetPosition(this);
        if (Math.Sqrt((p.X - _downPoint.X) * (p.X - _downPoint.X) + (p.Y - _downPoint.Y) * (p.Y - _downPoint.Y)) > 4)
        {
            StartDrag();
        }
    }

    private void StartDrag()
    {
        _startedDrag = true;
        ReleaseMouseCapture();
        _holdTimer?.Stop();
        _line.PressedId = null;
        IsDragging = true;
        _line.DraggingId = _item.Id;

        var data = new DataObject();
        data.SetFileDropList(new System.Collections.Specialized.StringCollection { _item.Url });
        try
        {
            var result = DragDrop.DoDragDrop(this, data, DragDropEffects.Copy | DragDropEffects.Move);
            IsDragging = false;
            _line.DraggingId = null;
            if (result.HasFlag(DragDropEffects.Move))
            {
                // Saved into a folder; it leaves the line.
                Task.Delay(600).ContinueWith(_ => Dispatcher.Invoke(_line.Prune));
            }
            else if (result == DragDropEffects.None)
            {
                // dropped on nowhere accepting; prune in case file moved elsewhere.
                _line.Prune();
            }
        }
        catch
        {
            IsDragging = false;
            _line.DraggingId = null;
        }
    }

    private void OnMouseUp(object sender, MouseButtonEventArgs e)
    {
        ReleaseMouseCapture();
        _holdTimer?.Stop();
        _line.PressedId = null;
        if (!double.IsNaN(_downPoint.X) && !_startedDrag && !_didLongPress && e.ClickCount == 1)
        {
            _line.Copy(_item.Id);
        }
        _downPoint = new Point(double.NaN, double.NaN);
        _didLongPress = false;
    }

    private void OnRightDown(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        ShowContextMenu();
    }

    private void ShowContextMenu()
    {
        var menu = new ContextMenu();
        var inInbox = _line.IsInInbox(_item.Id);

        AddItem(menu, "Copy", () => _line.Copy(_item.Id));
        AddItem(menu, "Open", () => _line.Open(_item.Id));
        if (!_item.IsRecording)
            AddItem(menu, "Edit", () => _line.Markup(_item.Id));
        AddItem(menu, "Show in Explorer", () => _line.Reveal(_item.Id));
        if (inInbox)
            AddItem(menu, "Save to Desktop", () => _line.SaveToDesktop(_item.Id));

        menu.Items.Add(new Separator());
        if (inInbox)
            AddItem(menu, "Discard", () => _line.Discard(_item.Id));
        else
        {
            AddItem(menu, "Take down", () => _line.Discard(_item.Id));
            AddItem(menu, "Move to Trash", () => _line.Trash(_item.Id));
        }

        var left = AddItem(menu, "Take down everything to the left",
            () => _line.TakeDown(_item.Id, true));
        left.IsEnabled = _line.Neighbours(_item.Id, true).Count > 0;
        var right = AddItem(menu, "Take down everything to the right",
            () => _line.TakeDown(_item.Id, false));
        right.IsEnabled = _line.Neighbours(_item.Id, false).Count > 0;

        menu.Placement = System.Windows.Controls.Primitives.PlacementMode.MousePoint;
        menu.IsOpen = true;
    }

    private static MenuItem AddItem(ContextMenu menu, string header, Action action)
    {
        var item = new MenuItem { Header = header };
        item.Click += (_, _) => action();
        menu.Items.Add(item);
        return item;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _line.PropertyChanged -= Line_PropertyChanged;
        _holdTimer?.Stop();
    }
}
