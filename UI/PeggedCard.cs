using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using Clothesline.Models;
using Clothesline.Services;

using Line = Clothesline.Models.Line;
using Image = System.Windows.Controls.Image;
using Brush = System.Windows.Media.Brush;
using SolidColorBrush = System.Windows.Media.SolidColorBrush;
using UserControl = System.Windows.Controls.UserControl;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using HA = System.Windows.HorizontalAlignment;
using VA = System.Windows.VerticalAlignment;

namespace Clothesline.UI;

/// <summary>
/// One photo with its clothespin. All the charm lives here: it drops onto
/// the line, swings, sways with the breeze and falls when you pull it off.
/// Mirrors the Swift <c>PeggedView</c> + <c>GrabView</c>.
/// </summary>
internal sealed class PeggedCard : UserControl, IDisposable
{
    public static bool IsDragging;

    /// <summary>
    /// The card menu currently on screen, if any. A ContextMenu is its own
    /// popup window, so exactly one can be open at a time and it has to be
    /// reachable from outside the card (see <see cref="IsMenuOpen"/> and
    /// <see cref="CloseOpenMenu"/>).
    /// </summary>
    private static ContextMenu? _openMenu;

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
    private DispatcherTimer? _menuAutoClose;
    private bool _hovering;
    private bool _disposed;

    /// <summary>This card's menu while it is open, so <see cref="Dispose"/> can close it.</summary>
    private ContextMenu? _menu;

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
                Text = "✓ " + I18n.Get("Copied"),
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

    /// <summary>
    /// Re-read the item's thumbnail. Called after the annotation window rewrote
    /// the file in place: Image.Source is assigned once in BuildCard and
    /// PeggedItem raises no change notification, so nothing would update.
    /// </summary>
    public void RefreshPhoto() => _photo.Source = _item.Thumb;

    private void Arrive()
    {
        if (_item.Flying) { Opacity = 0; return; }
        _arrival.Y = -46;
        _swing.Angle = 16 + _item.Tilt;
        var drop = new DoubleAnimation(0, TimeSpan.FromMilliseconds(420))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        _arrival.BeginAnimation(TranslateTransform.YProperty, drop);
        SwingTo(_item.Tilt, TimeSpan.FromMilliseconds(600));
    }

    public void Land()
    {
        Opacity = 0;
        var fade = new DoubleAnimation(1, TimeSpan.FromMilliseconds(160))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
            FillBehavior = FillBehavior.Stop
        };
        BeginAnimation(OpacityProperty, fade);
        Nudge(2.2);
    }

    /// <summary>
    /// The card lets go of the line: it swings, accelerates downward and
    /// fades out before being removed from the canvas. Mirrors the original
    /// Tendedero "fall" animation.
    /// </summary>
    public void Fall()
    {
        double dropTo = Layout.PanelHeight + 140;
        var duration = TimeSpan.FromMilliseconds(560);

        var dropAnim = new DoubleAnimation(dropTo, duration)
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
        };
        _arrival.BeginAnimation(TranslateTransform.YProperty, dropAnim);

        var swingAnim = new DoubleAnimation(_item.Tilt * 8 + 20, duration)
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
        };
        _swing.BeginAnimation(RotateTransform.AngleProperty, swingAnim);

        var fadeAnim = new DoubleAnimation(0, TimeSpan.FromMilliseconds(380))
        {
            BeginTime = TimeSpan.FromMilliseconds(180)
        };
        BeginAnimation(OpacityProperty, fadeAnim);
    }

    public void Breeze()
    {
        var delay = TimeSpan.FromMilliseconds(Random.Shared.NextDouble() * 350);
        Task.Delay(delay).ContinueWith(_ => Dispatcher.Invoke(() =>
            Nudge(Random.Shared.NextDouble() * 1.8 + 1.6)));
    }

    private void Nudge(double degrees)
    {
        var outAnim = new DoubleAnimation(_item.Tilt + degrees, TimeSpan.FromMilliseconds(300))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        _swing.BeginAnimation(RotateTransform.AngleProperty, outAnim);
        Task.Delay(300).ContinueWith(_ => Dispatcher.Invoke(() => SwingTo(_item.Tilt, TimeSpan.FromMilliseconds(500))));
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
                Services.Diag.Write("card", $"hold fired -> markup {System.IO.Path.GetFileName(_item.Url)}");
                _line.Markup(_item.Id);
            }
        };
        _holdTimer.Start();
        CaptureMouse();
        Services.Diag.Write("card", $"press down at {_downPoint.X:F0},{_downPoint.Y:F0}");
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

    // MARK: context menu

    /// <summary>
    /// True while a card's right-click menu is open. The rope's mouse tracker
    /// consults this.
    ///
    /// Why it has to exist: the menu is a separate popup window, so the moment
    /// the cursor moves off the card and onto the menu it leaves the rope's
    /// top band. Without this the tracker saw "not inside, not busy" and
    /// retracted the line after <c>RetractDelay</c>, which took the menu with
    /// it - the "mouse off the picture and the menu disappears" bug.
    ///
    /// It is derived from <see cref="ContextMenu.IsOpen"/> rather than stored
    /// as a boolean, so a missed event cannot strand the line open.
    /// </summary>
    public static bool IsMenuOpen => _openMenu?.IsOpen == true;

    /// <summary>
    /// Closes whatever card menu is open. Called when the line is hidden by
    /// the hot key or the menu-bar toggle: the popup does not travel with the
    /// rope, so it would otherwise be left floating over the desktop with no
    /// line under it.
    /// </summary>
    public static void CloseOpenMenu()
    {
        var menu = _openMenu;
        _openMenu = null;
        if (menu is { IsOpen: true }) menu.IsOpen = false;
    }

    private void ShowContextMenu()
    {
        var menu = BuildContextMenu(_item, _line);
        _menu = menu;

        // Closed fires for every way a ContextMenu can go away - Esc, a click
        // outside, picking a row, the window closing, or IsOpen set to false -
        // so clearing the reference here is what stops IsMenuOpen latching on
        // and leaving a rope that can never retract.
        menu.Closed += (_, _) =>
        {
            if (ReferenceEquals(_openMenu, menu)) _openMenu = null;
            if (ReferenceEquals(_menu, menu)) _menu = null;
            _menuAutoClose?.Stop();
            _menuAutoClose = null;
        };

        menu.Placement = System.Windows.Controls.Primitives.PlacementMode.MousePoint;
        OpenTracked(menu);
        StartMenuAutoClose(menu);
    }

    /// <summary>
    /// Closes the card menu once the pointer has left the picture and is not
    /// on the menu either.
    ///
    /// A ContextMenu is a separate popup window, so the card's own
    /// MouseLeave never fires while one is open - the popup takes the mouse
    /// for itself. Watching for the event would therefore never see the
    /// departure, which is why this polls instead.
    ///
    /// The two places that count as "still here" are the card and the menu:
    /// travelling from the picture onto a row must not close anything, or the
    /// menu would be unusable. Anything else - the desktop, another window -
    /// means the pointer has gone, and a menu nobody is looking at should not
    /// sit there waiting for an outside click.
    /// </summary>
    private void StartMenuAutoClose(ContextMenu menu)
    {
        _menuAutoClose?.Stop();
        _menuAutoClose = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(150)
        };
        _menuAutoClose.Tick += (_, _) =>
        {
            if (menu.IsOpen != true)
            {
                _menuAutoClose?.Stop();
                _menuAutoClose = null;
                return;
            }

            // IsMouseOver on a popup reports the popup itself, so moving onto
            // a row reads true even though the pointer has physically left the
            // card.
            if (IsMouseOver || menu.IsMouseOver) return;

            menu.IsOpen = false;
            _menuAutoClose?.Stop();
            _menuAutoClose = null;
        };
        _menuAutoClose.Start();
    }

    /// <summary>
    /// Opens a card menu and records it as the one that is open, which is what
    /// <see cref="IsMenuOpen"/> reports.
    ///
    /// Static and internal so the off-screen preview can exercise this exact
    /// state machine - false before, true while open, false again once closed -
    /// without a GUI. The caller sets <c>Placement</c>; the app uses the
    /// pointer, the preview uses a fixed screen point.
    /// </summary>
    internal static void OpenTracked(ContextMenu menu)
    {
        // Open first, record second. Opening this menu closes any other card's
        // menu, and that menu's Closed handler runs during this assignment; if
        // the field were set first the handler would clear it right back out.
        menu.IsOpen = true;
        _openMenu = menu;
    }

    /// <summary>
    /// Builds the card's right-click menu. Static and internal so the
    /// off-screen preview in <see cref="UiPreview"/> renders the menu the user
    /// actually gets, labels and enabled states included.
    ///
    /// The rows, their order and their enabled rules are unchanged from the
    /// original menu. Only the chrome is new: every row is dressed from
    /// <see cref="CardMenuTheme"/>, so the card menu reads as part of the same
    /// dark system as the tray menu instead of as a stock white WPF menu.
    /// </summary>
    internal static ContextMenu BuildContextMenu(PeggedItem item, Line line)
    {
        var menu = new ContextMenu { Style = CardMenuTheme.MenuStyle };
        var inInbox = line.IsInInbox(item.Id);

        // Icons on every row, per 01-ctx: a menu that has to be read is slower
        // than one that can be scanned. The glyphs are the tray menu's, read
        // from MenuIcons so the two surfaces cannot drift.
        var copy = AddItem(menu, I18n.Get("Copy"), () => line.Copy(item.Id), MenuIcons.Copy);
        copy.InputGestureText = "Ctrl+C";
        AddItem(menu, I18n.Get("Open"), () => line.Open(item.Id), MenuIcons.Folder);
        if (!item.IsRecording)
            AddItem(menu, I18n.Get("Edit"), () => line.Markup(item.Id), MenuIcons.Pen);
        AddItem(menu, I18n.Get("ShowInExplorer"), () => line.Reveal(item.Id), MenuIcons.Search);
        if (inInbox)
            AddItem(menu, I18n.Get("SaveToDesktop"), () => line.SaveToDesktop(item.Id), MenuIcons.Desktop);

        AddSeparator(menu);
        if (inInbox)
        {
            // In the inbox, Discard recycles the file, so it is the
            // destructive row here.
            AddItem(menu, I18n.Get("Discard"), () => line.Discard(item.Id), MenuIcons.Clear, danger: true);
        }
        else
        {
            AddItem(menu, I18n.Get("TakeDown"), () => line.Discard(item.Id), MenuIcons.Line);
            AddItem(menu, I18n.Get("MoveToTrash"), () => line.Trash(item.Id), MenuIcons.Clear, danger: true);
        }

        var left = AddItem(menu, I18n.Get("TakeDownLeft"), () => line.TakeDown(item.Id, true), MenuIcons.ChevronLeft);
        left.IsEnabled = line.Neighbours(item.Id, true).Count > 0;
        var right = AddItem(menu, I18n.Get("TakeDownRight"), () => line.TakeDown(item.Id, false), MenuIcons.ChevronRight);
        right.IsEnabled = line.Neighbours(item.Id, false).Count > 0;

        return menu;
    }

    private static MenuItem AddItem(ContextMenu menu, string header, Action action,
                                    MenuIcons.Entry icon, bool danger = false)
    {
        var item = new MenuItem
        {
            Header = header,
            Style = danger ? CardMenuTheme.DangerItemStyle : CardMenuTheme.MenuItemStyle,
            // The icon is a stroked path in a 24x24 box, drawn at 16px to match
            // the mockup. Icon takes a ImageSource, so the geometry is rendered
            // to a bitmap first - the same conversion MenuIcons serves for the
            // tray menu's WinForms rows.
            Icon = new Image
            {
                Source = MenuIcons.Render(icon, TrayTheme.Current.RowIconBrush, 16),
                Width = 16,
                Height = 16,
                Stretch = Stretch.Uniform,
                VerticalAlignment = VA.Center,
            },
        };
        item.Click += (_, _) => action();
        menu.Items.Add(item);
        return item;
    }

    private static void AddSeparator(ContextMenu menu)
        => menu.Items.Add(new Separator { Style = CardMenuTheme.MenuSeparatorStyle });

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _line.PropertyChanged -= Line_PropertyChanged;
        _holdTimer?.Stop();
        // A card can be pruned from the line while its menu is open. The popup
        // is not parented to the card, so it would survive the removal and sit
        // over the desktop; close it with the card.
        if (_menu is { IsOpen: true }) _menu.IsOpen = false;
        _menu = null;

        // The auto-close poller outlives the menu it belongs to, so stop it
        // here too - a card that goes away with a menu open would otherwise
        // leave the timer ticking against a menu nobody owns.
        _menuAutoClose?.Stop();
        _menuAutoClose = null;
    }
}

/// <summary>
/// The dark chrome for WPF menus, and the one place the card's right-click
/// menu gets its look from.
///
/// Colours are read from <see cref="TrayPalette"/> and never restated here.
/// The tray menu (WinForms) and the card menu (WPF) are one design system;
/// the tray table is the copy the running tray icon is built from, so it is
/// the source of truth and this class is only a type adapter on top of it.
/// Nothing can drift, because there is only one set of values.
///
/// (AnnotationWindow.cs still carries its own private copy of the same
/// values. It is outside this change's file scope and should be repointed at
/// TrayPalette in a follow-up - that copy is the last duplicate.)
///
/// The two menu stacks cannot share an implementation: a WPF ContextMenu is
/// templated XAML, a WinForms ToolStripDropDown is owner-drawn with
/// GDI+. Only the values are shared.
///
/// Metrics, taken from the tray menu so a row reads the same height and
/// weight wherever it appears:
///   34px row   10pt (13.33px) Segoe UI Variable Display, 7px above/below
///   12px text inset from the menu edge (4px highlight inset + 8px padding)
///   4px radius on the hover highlight, 8px on the menu shell
///   1px #3D3D3D hairline for both the shell border and separators
///   #8A8A8A for a disabled row, #FF99A4 for a destructive one
/// </summary>
/// <summary>
/// The card's right-click menu, to ui-redesign/01-ctx.
///
/// 260px wide, 20px shell radius, 40px rows at 10px radius, a 16px icon on
/// every row, the shortcut right-aligned and dimmed, and a red danger row.
///
/// The values used to be hardcoded here - a second copy of the same numbers
/// that lived in TrayIcon, which drifted the moment either side moved. They
/// are now generated from <see cref="TrayTheme"/> each time the theme
/// changes, so the card menu, the tray menu and the mockups cannot disagree.
/// The styles are built once per theme and cached: XamlReader is far too slow
/// to run on every right-click.
/// </summary>
public static class CardMenuTheme
{
    private static bool _dark = true;
    private static Style? _menu;
    private static Style? _row;
    private static Style? _dangerRow;
    private static Style? _separator;

    /// <summary>
    /// Builds (or rebuilds) the styles for the theme in force. Called when the
    /// app starts and again whenever <see cref="AppTheme.Changed"/> fires, so
    /// a theme switch repaints the card menu too.
    /// </summary>
    public static void Refresh()
    {
        if (AppTheme.IsDark == _dark && _menu is not null) return;
        _dark = AppTheme.IsDark;
        var t = TrayTheme.Current;

        // Hex, because XAML is a string. The palette hands back frozen WPF
        // brushes; the palette builds them all as solid colours, so a cast is
        // safe - and it keeps this file free of a second colour type.
        string Hex(Brush b) => b is SolidColorBrush solid
            ? $"#{solid.Color.A:X2}{solid.Color.R:X2}{solid.Color.G:X2}{solid.Color.B:X2}"
            : "#00000000";

        var card = Hex(t.CardBrush);
        var border = Hex(t.CardBorderBrush);
        var text = Hex(t.RowTextBrush);
        var muted = Hex(t.RowValueBrush);
        var hover = Hex(t.RowHoverBrush);
        var divider = Hex(t.DividerBrush);
        var danger = Hex(t.DangerBrush);

        // Opacity is the only place the mockup's translucency has to be
        // fudged. A ContextMenu is a real popup window, so it cannot blur what
        // is behind it the way backdrop-filter does; at the mockup's .62 the
        // light card averages against the desktop and its text loses contrast.
        // The shell is therefore painted at the palette's card colour, which
        // is already raised for exactly this reason.
        var xaml = $@"
<ResourceDictionary
    xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'
    xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml'>

  <!-- Shell: 260 wide, radius 20, hairline border, layered shadow. The 28px
       margin is what keeps the 50px blur from being clipped by the popup's
       own bounds - a ContextMenu clips its content to the window. -->
  <Style x:Key='CardMenu' TargetType='ContextMenu'>
    <Setter Property='Width' Value='260'/>
    <Setter Property='Background' Value='Transparent'/>
    <Setter Property='Foreground' Value='{text}'/>
    <Setter Property='FontFamily' Value='Segoe UI Variable Display, Segoe UI'/>
    <Setter Property='FontSize' Value='13.5'/>
    <Setter Property='SnapsToDevicePixels' Value='True'/>
    <Setter Property='UseLayoutRounding' Value='True'/>
    <Setter Property='HasDropShadow' Value='False'/>
    <Setter Property='Template'>
      <Setter.Value>
        <ControlTemplate TargetType='ContextMenu'>
          <Border Margin='28' SnapsToDevicePixels='True'>
            <Border CornerRadius='20' Background='{card}'
                    BorderBrush='{border}' BorderThickness='1'
                    Padding='10' SnapsToDevicePixels='True'>
              <Border.Effect>
                <DropShadowEffect Color='#0F172A' Opacity='{(_dark ? "0.55" : "0.25")}'
                                  BlurRadius='50' ShadowDepth='12' Direction='270'/>
              </Border.Effect>
              <ItemsPresenter/>
            </Border>
          </Border>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>

  <!-- Row: 40 tall, 10 radius, icon gutter on the left, shortcut on the
       right. The 12px gap is the mockup's, so an icon and its label read as
       one unit rather than two columns. -->
  <Style x:Key='CardRow' TargetType='MenuItem'>
    <Setter Property='Foreground' Value='{text}'/>
    <Setter Property='FontFamily' Value='Segoe UI Variable Display, Segoe UI'/>
    <Setter Property='FontSize' Value='13.5'/>
    <Setter Property='FontWeight' Value='Medium'/>
    <Setter Property='SnapsToDevicePixels' Value='True'/>
    <Setter Property='Template'>
      <Setter.Value>
        <ControlTemplate TargetType='MenuItem'>
          <Border x:Name='Bd' Height='40' Margin='0,1,0,1' Padding='14,0,14,0'
                  CornerRadius='10' Background='Transparent'
                  SnapsToDevicePixels='True'>
            <Grid>
              <Grid.ColumnDefinitions>
                <ColumnDefinition Width='Auto'/>
                <ColumnDefinition Width='*'/>
                <ColumnDefinition Width='Auto'/>
              </Grid.ColumnDefinitions>
              <ContentPresenter Grid.Column='0' ContentSource='Icon'
                                Margin='0,0,12,0'
                                VerticalAlignment='Center'
                                RecognizesAccessKey='False'/>
              <ContentPresenter Grid.Column='1' ContentSource='Header'
                                VerticalAlignment='Center'
                                RecognizesAccessKey='False'
                                TextElement.Foreground='{{TemplateBinding Foreground}}'/>
              <TextBlock Grid.Column='2' Margin='12,0,0,0' FontSize='11.5'
                         Opacity='0.5' VerticalAlignment='Center'
                         Text='{{Binding InputGestureText, RelativeSource={{RelativeSource AncestorType=MenuItem}}}}'
                         TextElement.Foreground='{muted}'/>
            </Grid>
          </Border>
          <ControlTemplate.Triggers>
            <Trigger Property='IsHighlighted' Value='True'>
              <Setter TargetName='Bd' Property='Background' Value='{hover}'/>
            </Trigger>
            <Trigger Property='IsEnabled' Value='False'>
              <Setter Property='Foreground' Value='{muted}'/>
            </Trigger>
          </ControlTemplate.Triggers>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>

  <!-- Destructive row: same metrics, danger ink, and a red wash on hover so
       the row reads as destructive before it is clicked. -->
  <Style x:Key='CardRowDanger' TargetType='MenuItem' BasedOn='{{StaticResource CardRow}}'>
    <Setter Property='Foreground' Value='{danger}'/>
    <Setter Property='Template'>
      <Setter.Value>
        <ControlTemplate TargetType='MenuItem'>
          <Border x:Name='Bd' Height='40' Margin='0,1,0,1' Padding='14,0,14,0'
                  CornerRadius='10' Background='Transparent'
                  SnapsToDevicePixels='True'>
            <Grid>
              <Grid.ColumnDefinitions>
                <ColumnDefinition Width='Auto'/>
                <ColumnDefinition Width='*'/>
                <ColumnDefinition Width='Auto'/>
              </Grid.ColumnDefinitions>
              <ContentPresenter Grid.Column='0' Margin='0,0,12,0'
                                VerticalAlignment='Center' RecognizesAccessKey='False'/>
              <ContentPresenter Grid.Column='1' ContentSource='Header'
                                VerticalAlignment='Center' RecognizesAccessKey='False'
                                TextElement.Foreground='{{TemplateBinding Foreground}}'/>
              <TextBlock Grid.Column='2' Margin='12,0,0,0' FontSize='11.5'
                         Opacity='0.5' VerticalAlignment='Center'
                         Text='{{Binding InputGestureText, RelativeSource={{RelativeSource AncestorType=MenuItem}}}}'
                         TextElement.Foreground='{muted}'/>
            </Grid>
          </Border>
          <ControlTemplate.Triggers>
            <Trigger Property='IsHighlighted' Value='True'>
              <Setter TargetName='Bd' Property='Background' Value='#14DC2626'/>
            </Trigger>
            <Trigger Property='IsEnabled' Value='False'>
              <Setter Property='Foreground' Value='{muted}'/>
            </Trigger>
          </ControlTemplate.Triggers>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>

  <!-- Separator: 1px, 6 above and below, 8 inset from each side. -->
  <Style x:Key='CardSeparator' TargetType='Separator'>
    <Setter Property='Template'>
      <Setter.Value>
        <ControlTemplate TargetType='Separator'>
          <Border Height='1' Margin='8,6,8,6'
                  Background='{divider}' SnapsToDevicePixels='True'/>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>
</ResourceDictionary>";

        var dict = (ResourceDictionary)XamlReader.Parse(xaml);
        _menu = (Style)dict["CardMenu"];
        _row = (Style)dict["CardRow"];
        _dangerRow = (Style)dict["CardRowDanger"];
        _separator = (Style)dict["CardSeparator"];
    }

    /// <summary>Shell style for the <see cref="ContextMenu"/> itself.</summary>
    public static Style MenuStyle
    {
        get { Refresh(); return _menu!; }
    }

    /// <summary>Row style for an ordinary entry.</summary>
    public static Style MenuItemStyle
    {
        get { Refresh(); return _row!; }
    }

    /// <summary>Row style for an entry that destroys or recycles the file.</summary>
    public static Style DangerItemStyle
    {
        get { Refresh(); return _dangerRow!; }
    }

    /// <summary>Style for a <see cref="Separator"/> row.</summary>
    public static Style MenuSeparatorStyle
    {
        get { Refresh(); return _separator!; }
    }
}
