using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Threading;
using Clothesline.Models;
using Clothesline.Services;

// This project sets both UseWPF and UseWindowsForms, so several types exist
// in two namespaces. Everything here is WPF.
using Image = System.Windows.Controls.Image;
using Brush = System.Windows.Media.Brush;
using Run = System.Windows.Documents.Run;
using WColor = System.Windows.Media.Color;
using Orientation = System.Windows.Controls.Orientation;
using Popup = System.Windows.Controls.Primitives.Popup;
using PopupAnimation = System.Windows.Controls.Primitives.PopupAnimation;
using PlacementMode = System.Windows.Controls.Primitives.PlacementMode;

using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using MouseButtonEventArgs = System.Windows.Input.MouseButtonEventArgs;
using Cursors = System.Windows.Input.Cursors;
using HA = System.Windows.HorizontalAlignment;
using VA = System.Windows.VerticalAlignment;

namespace Clothesline.UI;

/// <summary>
/// The tray menu, in WPF.
///
/// The old menu was a WinForms ContextMenuStrip. That could not draw the
/// mockups: no backdrop blur, no rounded corners, no translucency, no inner
/// highlight - a WinForms drop-down is a plain rectangle. This is a
/// System.Windows.Controls.Primitives.Popup hosting a WPF card, which can.
///
/// It is deliberately a Popup rather than a Window: a Popup takes focus the
/// way a menu should, closes when the user clicks outside it (the shell
/// behaviour every menu is expected to have), and never appears in the task
/// bar. The popup itself is transparent and never activated; all the visible
/// chrome is the card inside it.
///
/// Rows are built once per open, not once per process: the toggles and the
/// values on the parent rows change between openings, and rebuilding is
/// cheaper than tracking and invalidating every one of them.
/// </summary>
internal sealed class TrayMenuWpf : IDisposable
{
    private readonly Line _line;
    private readonly LineWindow _window;
    private readonly Popup _popup;
    private Border? _card;
    private bool _disposed;

    /// <summary>
    /// Sub-menus currently open, so <see cref="Close"/> can take them down
    /// with the main menu. A Popup is its own top-level window: closing the
    /// parent does nothing to it, and an orphaned cascade would sit over the
    /// desktop with no way to dismiss it.
    /// </summary>
    private readonly List<Popup> _openSubs = new();

    // The same list, but kept across Close(): when the card is cached (the
    // theme has not changed since last open) the reused rows still own their
    // sub-popups, yet Close() emptied _openSubs. Holding the references here
    // lets Show() re-register them without rebuilding the whole card - which
    // is the slow path the cache is meant to avoid.
    private readonly List<Popup> _allSubs = new();

    // Auto-close: a popup with StaysOpen=false only closes on a click
    // outside, not on mouse-leave. A menu that stays up while the pointer
    // is nowhere near it feels broken, so a short timer checks whether the
    // cursor is still over the card or any of its cascades.
    private DispatcherTimer? _autoCloseTimer;

    // Cache guard: the theme (light/dark) is the only thing that changes
    // between openings and forces a rebuild. The toggles and values inside
    // the rows are refreshed by the lambdas at hover time, so a stale card
    // is only a colour problem - and rebuilding a 20-row card on every
    // right-click is what makes the menu feel sluggish.
    private bool _lastThemeDark;

    // True once the pointer has actually entered the card. The auto-close
    // timer must not fire until then: right after Show() the cursor is still
    // on the tray icon, not over the card, so IsMouseOverAnyMenu() is false
    // and an unconditional close would yank the menu away before the user
    // could use it - the menu would blink open and vanish.
    private bool _hovered;

    // Mockup metrics (ui-redesign/02-main). All in DIP.
    private const double MenuWidth = 340;
    private const double CornerRadius = 20;
    private const double RowHeight = 30;
    /// <summary>
    /// Rows sit flush in the mockup: 30px tall with nothing between them. The
    /// 9px gap this used to carry pushed a 17-row menu from ~510px to 663px.
    /// </summary>
    private const double RowGap = 0;
    private const double RowRadius = 7;
    private const double IconSize = 14;
    private const double ThumbHeight = 48;
    private const double ThumbGap = 6;
    private const double ThumbRadius = 9;

    /// <summary>
    /// Transparent room reserved around the card so its 50px drop shadow is
    /// not clipped by the popup's bounds.
    ///
    /// It must be added to <see cref="MenuWidth"/> rather than taken out of
    /// it: as a Margin inside a fixed Width it silently ate 56px of the card,
    /// which is what truncated the row labels.
    /// </summary>
    private const double ShadowMargin = 28;

    public TrayMenuWpf(Line line, LineWindow window)
    {
        _line = line;
        _window = window;

        _popup = new Popup
        {
            // The popup is a positioning host only: it must not steal focus,
            // must not show up in the task bar, and must not flicker as it
            // opens. AllowsTransparency is what lets the card have rounded
            // corners and a shadow without the window itself being clipped to
            // a rectangle.
            AllowsTransparency = true,
            PopupAnimation = PopupAnimation.Fade,
            StaysOpen = false,          // click outside -> close, like a menu
            Placement = PlacementMode.MousePoint,
            // The card carries a ShadowMargin of transparent padding, so the
            // popup's own origin sits that far outside the visible card. Pull
            // it back or the menu opens 28px right of and below the cursor.
            HorizontalOffset = -ShadowMargin,
            VerticalOffset = -ShadowMargin,
            Focusable = false,
        };
    }

    /// <summary>
    /// Opens the menu near the cursor. The tray icon's own click is what
    /// calls this; Windows puts the cursor there already.
    ///
    /// Rebuilding is skipped when the theme has not changed since the last
    /// open: the lambdas inside the rows read fresh values on hover, so the
    /// only stale thing a cached card can carry is the wrong palette.
    /// </summary>
    public void Show()
    {
        if (_disposed) return;

        bool dark = TrayTheme.Current.IsDarkSide;
        if (_card is null || dark != _lastThemeDark)
        {
            _lastThemeDark = dark;
            Rebuild();
        }
        else
        {
            // Cached card: Close() emptied _openSubs on the last dismiss, so
            // re-register the surviving cascades. The row closures still point
            // at the same popup objects, so re-opening them on hover works.
            _openSubs.Clear();
            _openSubs.AddRange(_allSubs);
        }
        _popup.IsOpen = true;

        // A popup with no focusable content still needs to become active for
        // the keyboard to reach it, otherwise Esc does nothing and the rows
        // never highlight under the caret.
        _card?.MoveFocus(new TraversalRequest(FocusNavigationDirection.First));

        // Start the auto-close watch. StaysOpen=false only closes on a click
        // outside; without this the menu stays up forever when the pointer
        // simply wanders away. _hovered is reset so the timer cannot fire
        // until the pointer first reaches the card.
        _hovered = false;
        StartAutoCloseTimer();
    }

    /// <summary>
    /// Closes the menu and every sub-menu it opened. Copying the list before
    /// iterating: each popup's Closed handler removes itself from
    /// <see cref="_openSubs"/>, which would otherwise mutate the collection
    /// mid-loop.
    /// </summary>
    public void Close()
    {
        _autoCloseTimer?.Stop();
        _autoCloseTimer = null;
        foreach (var sub in _openSubs.ToArray()) sub.IsOpen = false;
        _openSubs.Clear();
        _popup.IsOpen = false;
        _hovered = false;
    }

    public bool IsOpen => _popup.IsOpen;

    // MARK: auto-close

    /// <summary>
    /// Starts a 250ms timer that closes the menu when the cursor has left
    /// both the main card and every open sub-menu. The delay is long enough
    /// for the pointer to travel from a row to its cascade (they are adjacent
    /// but not overlapping) without killing the menu mid-journey.
    /// </summary>
    private void StartAutoCloseTimer()
    {
        _autoCloseTimer?.Stop();
        _autoCloseTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(250),
        };
        _autoCloseTimer.Tick += (_, _) =>
        {
            // _hovered guards against closing on open: until the pointer has
            // actually reached the card, IsMouseOverAnyMenu() is false (the
            // cursor is still on the tray icon) and a raw check would close the
            // menu before the user could interact with it.
            if (_hovered && !IsMouseOverAnyMenu())
            {
                _autoCloseTimer?.Stop();
                _autoCloseTimer = null;
                Close();
            }
        };
        _autoCloseTimer.Start();
    }

    /// <summary>
    /// True while the pointer is over the main menu card or any open
    /// sub-menu popup. Used by the auto-close timer to decide whether the
    /// menu has been abandoned.
    /// </summary>
    private bool IsMouseOverAnyMenu()
    {
        if (_card is not null && _card.IsMouseOver) return true;
        foreach (var sub in _openSubs)
        {
            if (sub.Child is FrameworkElement fe && fe.IsMouseOver) return true;
            // The popup itself does not have IsMouseOver; its child is what
            // the pointer is actually on.
        }
        return false;
    }

    /// <summary>
    /// Builds the card without opening the popup, for the off-screen preview.
    /// Returns null if the card cannot be built, so the preview can report
    /// that rather than write an empty PNG.
    /// </summary>
    internal Border? BuildCardForPreview()
    {
        try
        {
            return BuildCard(TrayTheme.Current);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Rebuilds the card from scratch. Cheap enough to do per open.</summary>
    private void Rebuild()
    {
        var t = TrayTheme.Current;
        var old = _card;

        // The old card's rows own the open sub-menus. Closing them first stops
        // a cascade from a stale menu outliving the menu that opened it.
        Close();

        _card = BuildCard(t);
        _popup.Child = _card;
        // Once the pointer actually reaches the card, arm auto-close: a later
        // "mouse left the menu" tick can then dismiss it. Set here (not in Show)
        // so repeated openings keep a single handler per rebuilt card.
        _card.MouseEnter += (_, _) => _hovered = true;
    }

    private Border BuildCard(TrayTheme t)
    {
        // A rebuild makes a fresh set of sub-popups; drop the stale ones so a
        // re-registered reference can never point at a menu that no longer
        // exists. Rebuild() calls Close() first, so _openSubs is already empty.
        _allSubs.Clear();

        var card = new Border
        {
            // Border.Width is the card itself; the shadow margin sits outside
            // it. (Adding the margin here instead made the card 396 wide.)
            Width = MenuWidth,
            CornerRadius = new CornerRadius(CornerRadius),
            // The mockup's translucent glass. Unlike the WinForms version this
            // really is translucent: a Popup with AllowsTransparency composites
            // against the desktop rather than erasing to a solid colour.
            Background = t.CardBrush,
            BorderBrush = t.CardBorderBrush,
            BorderThickness = new Thickness(1),
            // The layered shadow from the mockups: a wide soft one for lift
            // and a tight one for contact. Direction 270 points them down.
            Effect = new DropShadowEffect
            {
                Color = t.IsDarkShadow ? Colors.Black : Color.FromRgb(0x0F, 0x17, 0x2A),
                Opacity = t.IsDarkShadow ? 0.65 : 0.25,
                BlurRadius = 50,
                ShadowDepth = 12,
                Direction = 270,
            },
            // Room for the shadow to draw without being clipped by the popup's
            // bounds. The matching offsets on the Popup put the visible card
            // back under the cursor.
            Margin = new Thickness(ShadowMargin),
            // Mockup .menu padding: 12 top, 10 sides, 10 bottom.
            Padding = new Thickness(10, 2, 10, 2),
            SnapsToDevicePixels = true,
            UseLayoutRounding = true,
        };

        var layer = new Grid();

        // The 135-degree inner highlight (the mockup's ::before). Decorative,
        // so it must not eat clicks meant for the rows.
        layer.Children.Add(new Border
        {
            CornerRadius = new CornerRadius(CornerRadius),
            Background = new LinearGradientBrush
            {
                StartPoint = new Point(0, 0),
                EndPoint = new Point(1, 1),
                GradientStops =
                {
                    new GradientStop(t.SheenColor, 0),
                    new GradientStop(Colors.Transparent, 0.4),
                },
            },
            IsHitTestVisible = false,
        });

        var stack = new StackPanel { Margin = new Thickness(0, 10, 0, 10) };

        stack.Children.Add(BuildHeader(t));
        stack.Children.Add(BuildThumbs(t));
        stack.Children.Add(BuildLineSection(t));
        stack.Children.Add(BuildCaptureSection(t));
        stack.Children.Add(BuildBehaviorSection(t));
        stack.Children.Add(BuildAppSection(t));
        stack.Children.Add(BuildSupportSection(t));
        stack.Children.Add(BuildFooter(t));

        layer.Children.Add(stack);
        card.Child = layer;

        // Esc closes, which is the one keyboard affordance a menu owes.
        card.PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) Close();
        };

        return card;
    }

    // MARK: header

    private FrameworkElement BuildHeader(TrayTheme t)
    {
        // Mockup .header padding: 2px 8px 10px. The card supplies the 10px
        // outer padding, so this is the extra 8px inset only.
        var row = new Grid { Margin = new Thickness(8, 2, 8, 10) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        // Logo tile: the same gradient the WinForms header painted, now as a
        // real rounded square rather than a rasterised bitmap.
        var logo = new Border
        {
            Width = 34, Height = 34,
            CornerRadius = new CornerRadius(10),
            Background = new LinearGradientBrush
            {
                StartPoint = new Point(0, 0),
                EndPoint = new Point(1, 1),
                GradientStops =
                {
                    new GradientStop(t.LogoGradientStart, 0),
                    new GradientStop(t.LogoGradientEnd, 1),
                },
            },
            VerticalAlignment = VA.Center,
            Child = new System.Windows.Shapes.Path
            {
                Data = MenuIcons.Wpf(MenuIcons.Line),
                Stroke = Brushes.White,
                StrokeThickness = 2.4,
                Stretch = Stretch.Uniform,
                Width = 19, Height = 19,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round,
                StrokeLineJoin = PenLineJoin.Round,
            },
        };
        Grid.SetColumn(logo, 0);
        row.Children.Add(logo);

        var text = new StackPanel
        {
            Margin = new Thickness(10, 0, 0, 0),
            VerticalAlignment = VA.Center,
        };
        text.Children.Add(new TextBlock
        {
            Text = "Clothesline",
            FontSize = 14,
            FontWeight = FontWeights.Bold,
            Foreground = t.TitleBrush,
            LineHeight = 1.2,
            TextTrimming = TextTrimming.CharacterEllipsis,
        });
        text.Children.Add(new TextBlock
        {
            Text = $"v{TrayIcon.AppVersion} · " + (ClipboardWatcher.IsEnabled
                ? I18n.Get("WatchingClipboard")
                : I18n.Get("ClipboardOff")),
            FontSize = 10.5,
            Foreground = t.SubtitleBrush,
            LineHeight = 1.3,
            // The mockup gives this stack min-width:0 so the status badge keeps
            // its slot. Without trimming the subtitle's natural width pushed
            // the badge out past the card's padding.
            TextTrimming = TextTrimming.CharacterEllipsis,
        });
        Grid.SetColumn(text, 1);
        row.Children.Add(text);

        // Live count badge.
        var badge = new Border
        {
            Padding = new Thickness(9, 3, 9, 3),
            CornerRadius = new CornerRadius(999),
            Background = new LinearGradientBrush
            {
                StartPoint = new Point(0, 0),
                EndPoint = new Point(1, 1),
                GradientStops =
                {
                    new GradientStop(t.BadgeGradientStart, 0),
                    new GradientStop(t.BadgeGradientEnd, 1),
                },
            },
            VerticalAlignment = VA.Center,
            Child = new TextBlock
            {
                Text = I18n.Get("OnLineCount", _line.LiveCount),
                FontSize = 10.5,
                FontWeight = FontWeights.SemiBold,
                Foreground = t.BadgeTextBrush,
            },
        };
        Grid.SetColumn(badge, 2);
        row.Children.Add(badge);

        return row;
    }

    // MARK: thumbnails

    private FrameworkElement BuildThumbs(TrayTheme t)
    {
        var strip = new Grid
        {
            Height = ThumbHeight,
            // Mockup .thumbs padding: 0 6px 10px, on top of the card's 10px.
            Margin = new Thickness(6, 0, 6, 10),
        };

        var live = _line.Items.Where(i => !i.Falling).Take(3).ToList();
        if (live.Count == 0)
        {
            strip.Children.Add(new TextBlock
            {
                Text = I18n.Get("EmptyHint"),
                FontSize = 10,
                Foreground = t.SubtitleBrush,
                VerticalAlignment = VA.Center,
            });
            return strip;
        }

        // .thumbs is a flex row of flex:1 tiles, so the strip divides by how
        // many tiles there actually are. It used to be hard-wired to three
        // columns, which left a third of the strip empty at two captures.
        int slots = live.Count + (_line.LiveCount > live.Count ? 1 : 0);
        for (int i = 0; i < slots; i++)
        {
            if (i > 0) strip.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(ThumbGap) });
            strip.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        }

        for (int i = 0; i < live.Count; i++)
        {
            var tile = new Border
            {
                CornerRadius = new CornerRadius(ThumbRadius),
                Background = Brushes.Transparent,
                BorderBrush = t.ThumbBorderBrush,
                BorderThickness = new Thickness(1),
                ClipToBounds = true,
                Child = new Image
                {
                    Source = live[i].Thumb,
                    Stretch = Stretch.UniformToFill,
                },
            };
            Grid.SetColumn(tile, i * 2);
            strip.Children.Add(tile);
        }

        // Remaining count, in the last slot.
        int extra = _line.LiveCount - live.Count;
        if (extra > 0)
        {
            var more = new Border
            {
                CornerRadius = new CornerRadius(ThumbRadius),
                Background = t.RowHoverBrush,
                Child = new TextBlock
                {
                    Text = "+" + extra,
                    FontSize = 11,
                    FontWeight = FontWeights.Bold,
                    Foreground = t.SubtitleBrush,
                    HorizontalAlignment = HA.Center,
                    VerticalAlignment = VA.Center,
                },
            };
            Grid.SetColumn(more, live.Count * 2);
            strip.Children.Add(more);
        }

        return strip;
    }

    // MARK: rows

    /// <summary>
    /// One menu row: icon, label, optional trailing value, optional chevron.
    /// This is the single place a row's chrome is defined, so every row in
    /// every section is identical apart from its content.
    ///
    /// A row with <paramref name="items"/> opens a sub-menu: the chevron is
    /// drawn by the same <see cref="RowContent"/> and the click handler swaps
    /// the chevron for a nested popup beside this row. While that cascade is
    /// open the row wears the mockup's active gradient, which is why the row
    /// keeps a <see cref="RowVisuals"/> handle: the ink has to turn white.
    /// </summary>
    private Border Row(TrayTheme t, MenuIcons.Entry icon, string label,
                       string? value = null,
                       Func<IEnumerable<SubItem>>? items = null,
                       bool isToggle = false, bool toggleOn = false,
                       Action? click = null, bool coffee = false,
                       FrameworkElement? iconOverride = null,
                       bool danger = false,
                       bool dangerValue = false,
                       string? dimmedSuffix = null,
                       MenuIcons.Entry? trailingChevron = null,
                       SubKind subKind = SubKind.Choice)
    {
        var v = RowContent(t, icon, label, value, items is not null, isToggle,
                           toggleOn, coffee, iconOverride,
                           danger, dangerValue, dimmedSuffix, trailingChevron);

        var row = new Border
        {
            Height = RowHeight,
            Margin = new Thickness(4, 0, 4, RowGap),
            CornerRadius = new CornerRadius(RowRadius),
            Background = coffee ? t.CoffeeBrush : Brushes.Transparent,
            Cursor = click is not null || items is not null ? Cursors.Hand : null,
            Child = v.Root,
        };

        void Resting()
        {
            row.Background = coffee ? t.CoffeeBrush : Brushes.Transparent;
            PaintRow(t, v, active: false, coffee);
        }

        if (items is not null)
        {
            // The sub-menu is a popup of its own, positioned against this row
            // and closed with it. Keeping it a separate popup (rather than an
            // expanding panel) is what lets it overlap the menu edge the way
            // the mockups' cascades do.
            var sub = new Popup
            {
                AllowsTransparency = true,
                PopupAnimation = PopupAnimation.None,
                StaysOpen = false,
                Placement = PlacementMode.Right,
                Focusable = false,
                Child = SubCard(t, items, subKind),
            };

        // Every open sub-menu is registered so Close() can take them all
        // down together. Without this, dismissing the main menu left the
        // cascade floating over the desktop with nothing to close it.
        _openSubs.Add(sub);
        _allSubs.Add(sub);
            sub.Closed += (_, _) =>
            {
                _openSubs.Remove(sub);
                // The cascade is gone, so the row drops its active gradient.
                Resting();
            };

            row.MouseLeftButtonUp += (_, e) =>
            {
                e.Handled = true;
                sub.PlacementTarget = row;

                // Clicking the row again closes what it opened - the toggle
                // behaviour every cascading menu has.
                bool wasOpen = sub.IsOpen;
                foreach (var other in _openSubs)
                    if (!ReferenceEquals(other, sub)) other.IsOpen = false;

                sub.IsOpen = !wasOpen;
            };
            row.MouseEnter += (_, _) =>
            {
                // Mockup .row.active: while this row's cascade is up the row
                // is the gradient, not the hover wash.
                row.Background = t.ActiveRowBrush;
                PaintRow(t, v, active: true, coffee);

                // Opening on hover rather than on click is what the mockups
                // and every native menu do, and it means the pointer never has
                // to find a target smaller than the row it is already on.
                sub.PlacementTarget = row;
                sub.IsOpen = true;
                foreach (var other in _openSubs)
                    if (!ReferenceEquals(other, sub)) other.IsOpen = false;
            };
            row.MouseLeave += (_, _) =>
            {
                // The sub-menu sits to the right of this row, so the pointer
                // has to leave the row to reach it. Dropping the highlight here
                // made the menu unreadable the moment you moved toward it.
                // Closing is handled by StaysOpen=false (a click elsewhere)
                // and by Close() taking every registered sub down with it.
                if (sub.IsOpen) return;
                Resting();
            };
            return row;
        }

        if (click is not null)
        {
            row.MouseLeftButtonUp += (_, e) =>
            {
                e.Handled = true;
                Close();
                click();
            };
            row.MouseEnter += (_, _) => row.Background = coffee ? t.CoffeeBrush : t.RowHoverBrush;
            row.MouseLeave += (_, _) => row.Background = coffee ? t.CoffeeBrush : Brushes.Transparent;
        }

        return row;
    }

    /// <summary>
    /// Builds a sub-menu card without opening anything, for the off-screen
    /// preview. Sub-menus only exist while their parent row is hovered, so
    /// this is the only way to see one without a mouse.
    /// </summary>
    internal Border? BuildSubCardForPreview(Func<IEnumerable<SubItem>> items,
                                            SubKind kind = SubKind.Choice)
    {
        try
        {
            return SubCard(TrayTheme.Current, items, kind);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>One entry inside a sub-menu: a label, a tick, and what it does.</summary>
    /// <param name="Icon">Drawn before the label. The mockups give most
    /// sub-menu rows one; it is optional so a plain list still renders.</param>
    /// <param name="IsToggle">Render a switch instead of a tick. Used by the
    /// "bring the line down" cascade, whose entries are independent switches
    /// rather than one-of-N choices.</param>
    /// <param name="Accent">Indigo 600 instead of the row ink. 05-sub2 gives
    /// this to "add a folder" so the one action in a list of folders reads as
    /// an action.</param>
    internal readonly record struct SubItem(string Label, bool IsOn, Action Choose,
                                            bool Danger = false,
                                            MenuIcons.Entry? Icon = null,
                                            bool IsToggle = false,
                                            bool Accent = false);

    /// <summary>
    /// The six cascades are not one design repeated. The mockups give each its
    /// own row height, and the label grows with it:
    /// 04 (switches) 32px / 12.5, 05 (folders) 34px / 12.5, 06-09 36px / 13.5.
    /// </summary>
    internal enum SubKind { Toggle, Folder, Choice }

    private static (double Row, double Font) SubMetrics(SubKind kind) => kind switch
    {
        SubKind.Toggle => (32, 12.5),
        SubKind.Folder => (34, 12.5),
        _ => (36, 13.5),
    };

    /// <summary>
    /// The card a sub-menu lives in: the same glass as the main menu, 240 wide
    /// per the mockups (200-280 depending on the cascade), with one row per
    /// <see cref="SubItem"/>.
    /// </summary>
    private Border SubCard(TrayTheme t, Func<IEnumerable<SubItem>> items, SubKind kind)
    {
        var list = items().ToList();
        var (rowHeight, rowFont) = SubMetrics(kind);

        var card = new Border
        {
            MinWidth = 240,
            CornerRadius = new CornerRadius(16),
            Background = t.CardBrush,
            BorderBrush = t.CardBorderBrush,
            BorderThickness = new Thickness(1),
            Padding = new Thickness(8),
            Margin = new Thickness(8),
            Effect = new DropShadowEffect
            {
                Color = t.IsDarkShadow ? Colors.Black : WColor.FromRgb(0x0F, 0x17, 0x2A),
                Opacity = t.IsDarkShadow ? 0.55 : 0.22,
                BlurRadius = 36,
                ShadowDepth = 8,
                Direction = 270,
            },
        };

        var stack = new StackPanel();
        foreach (var item in list)
        {
            stack.Children.Add(SubRow(t, item, rowHeight, rowFont));
        }
        card.Child = stack;
        return card;
    }

    /// <summary>
    /// The mockup's gradient check tile: a 16px rounded square with a white
    /// tick and a soft blue drop. Shared by sub-menu rows and the "show the
    /// line" row, which wear the same control at different sizes.
    /// </summary>
    private static Border CheckSquare(TrayTheme t, double size)
    {
        var tick = new System.Windows.Shapes.Path
        {
            Data = MenuIcons.Wpf(MenuIcons.Check),
            Stroke = t.ActiveRowTextBrush,
            StrokeThickness = 3.2,
            Width = size * 0.68,
            Height = size * 0.68,
            Stretch = Stretch.Uniform,
            HorizontalAlignment = HA.Center,
            VerticalAlignment = VA.Center,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            StrokeLineJoin = PenLineJoin.Round,
        };

        return new Border
        {
            Width = size,
            Height = size,
            CornerRadius = new CornerRadius(4),
            Background = t.CheckBrush,
            VerticalAlignment = VA.Center,
            Effect = new DropShadowEffect
            {
                Color = Color.FromRgb(0x3B, 0x82, 0xF6),
                Opacity = 0.3,
                BlurRadius = 6,
                ShadowDepth = 2,
                Direction = 270,
            },
            Child = tick,
        };
    }

    private static System.Windows.Shapes.Path SubGlyph(TrayTheme t, MenuIcons.Entry icon, double size = 15)
        => new()
        {
            Data = MenuIcons.Wpf(icon),
            Stroke = t.RowIconBrush,
            StrokeThickness = 1.9,
            Width = size, Height = size,
            Stretch = Stretch.Uniform,
            VerticalAlignment = VA.Center,
            // .item .ico sits at 65-70% opacity in the mockups: present, but
            // never competing with the label.
            Opacity = 0.68,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            StrokeLineJoin = PenLineJoin.Round,
        };

    private Border SubRow(TrayTheme t, SubItem item, double height, double font)
    {
        var row = new Border
        {
            Height = height,
            CornerRadius = new CornerRadius(8),
            Background = Brushes.Transparent,
            Cursor = Cursors.Hand,
            Padding = new Thickness(12, 0, 12, 0),
        };

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });   // tick + icon
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); // label
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });   // switch

        // The leading cluster: the tick (single-choice rows only) then the
        // icon. A switch row carries no tick, because its state is the switch.
        var lead = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VA.Center,
        };

        if (item.IsOn && !item.IsToggle) lead.Children.Add(CheckSquare(t, 16));
        if (item.Icon is not null)
        {
            // 04/05 draw their icons at 14px, 09 at 15 - the taller choice
            // lists carry the larger glyph.
            lead.Children.Add(SubGlyph(t, item.Icon, height >= 36 ? 15 : 14));
        }

        if (lead.Children.Count > 0)
        {
            // 10px between the tick and the icon, and 10px before the label -
            // the mockup's gap.
            for (int i = 0; i < lead.Children.Count - 1; i++)
                lead.Children[i].SetValue(FrameworkElement.MarginProperty, new Thickness(0, 0, 10, 0));
            lead.Margin = new Thickness(0, 0, 10, 0);
            Grid.SetColumn(lead, 0);
            grid.Children.Add(lead);
        }

        var label = new TextBlock
        {
            Text = item.Label,
            FontSize = font,
            // .item.add is 600; .item.danger is 500 like the rest.
            FontWeight = item.Accent ? FontWeights.SemiBold : FontWeights.Medium,
            Foreground = item.Danger ? t.DangerBrush
                       : item.Accent ? t.AccentStrongBrush
                       : t.RowTextBrush,
            VerticalAlignment = VA.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        Grid.SetColumn(label, 1);
        grid.Children.Add(label);

        if (item.IsToggle)
        {
            grid.Children.Add(BuildToggle(t, item.IsOn));
            Grid.SetColumn(grid.Children[^1], 2);
        }

        row.Child = grid;
        row.MouseEnter += (_, _) => row.Background = t.RowHoverBrush;
        row.MouseLeave += (_, _) => row.Background = Brushes.Transparent;
        row.MouseLeftButtonUp += (_, e) =>
        {
            e.Handled = true;
            item.Choose();
            // A switch stays open so several can be flipped in one visit; a
            // single-choice entry closes the cascade, the way it always has.
            if (!item.IsToggle) Close();
        };

        return row;
    }

    /// <summary>
    /// The pieces of a row whose ink has to change when the row goes active.
    /// Null fields are simply absent from that row.
    ///
    /// The resting brushes are captured at build time rather than re-derived
    /// in <see cref="PaintRow"/>: a row's ink is not always the palette
    /// default. Danger rows are red, coffee rows amber, and a destructive
    /// value ("Empty the line") is a different red from a destructive label
    /// ("Quit"), so recomputing from the theme would quietly repaint them.
    /// </summary>
    private sealed class RowVisuals
    {
        public required Grid Root { get; init; }
        public required TextBlock Label { get; init; }
        public TextBlock? Value { get; init; }
        public System.Windows.Shapes.Path? Icon { get; init; }
        public System.Windows.Shapes.Path? Chevron { get; init; }

        public required Brush LabelInk { get; init; }
        public Brush? ValueInk { get; init; }
        public Brush? IconInk { get; init; }
        public Brush? ChevronInk { get; init; }

        /// <summary>
        /// The dimmed brand run on the quit row, and the brush that dims it.
        /// Run has no Opacity of its own, so the 55% is baked into an alpha
        /// brush - and because it is set per-run, PaintRow has to restore it
        /// by hand when the row goes active.
        /// </summary>
        public Run? DimSuffix { get; init; }
        public Brush? DimInk { get; init; }
    }

    /// <summary>
    /// Repaints a row's ink for the active / resting state. The active row is
    /// a saturated gradient, so every glyph on it has to be white; resting
    /// puts back exactly what the row was built with.
    /// </summary>
    private static void PaintRow(TrayTheme t, RowVisuals v, bool active, bool coffee)
    {
        if (active)
        {
            var ink = t.ActiveRowTextBrush;
            v.Label.Foreground = ink;
            if (v.Value is not null) v.Value.Foreground = ink;
            if (v.Icon is not null) v.Icon.Stroke = ink;
            if (v.Chevron is not null) v.Chevron.Stroke = ink;
            if (v.DimSuffix is not null) v.DimSuffix.Foreground = ink;
            return;
        }

        v.Label.Foreground = v.LabelInk;
        if (v.Value is not null) v.Value.Foreground = v.ValueInk ?? v.LabelInk;
        if (v.Icon is not null) v.Icon.Stroke = v.IconInk ?? t.RowIconBrush;
        if (v.Chevron is not null) v.Chevron.Stroke = v.ChevronInk ?? t.RowIconBrush;
        if (v.DimSuffix is not null) v.DimSuffix.Foreground = v.DimInk ?? v.LabelInk;
    }

    private RowVisuals RowContent(TrayTheme t, MenuIcons.Entry icon, string label,
                                  string? value, bool hasSubMenu, bool isToggle,
                                  bool toggleOn, bool coffee,
                                  FrameworkElement? iconOverride,
                                  bool danger, bool dangerValue, string? dimmedSuffix,
                                  MenuIcons.Entry? trailingChevron)
    {
        var iconInk = danger ? t.DangerBrush
                    : coffee ? t.CoffeeIconBrush
                    : t.RowIconBrush;
        var labelInk = danger ? t.DangerBrush
                     : coffee ? t.CoffeeTextBrush
                     : t.RowTextBrush;

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });   // icon
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); // label
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });   // value / chevron

        System.Windows.Shapes.Path? glyph = null;

        if (iconOverride is not null)
        {
            // A caller-supplied control in the icon slot - the gradient check
            // square "show the line" wears, which cannot be a stroked glyph.
            iconOverride.Margin = new Thickness(10, 0, 8, 0);
            iconOverride.VerticalAlignment = VA.Center;
            Grid.SetColumn(iconOverride, 0);
            grid.Children.Add(iconOverride);
        }
        else
        {
            glyph = new System.Windows.Shapes.Path
            {
                Data = MenuIcons.Wpf(icon),
                Stroke = iconInk,
                StrokeThickness = 1.9,
                Width = IconSize, Height = IconSize,
                Stretch = Stretch.Uniform,
                VerticalAlignment = VA.Center,
                Margin = new Thickness(10, 0, 8, 0),
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round,
                StrokeLineJoin = PenLineJoin.Round,
            };
            Grid.SetColumn(glyph, 0);
            grid.Children.Add(glyph);
        }

        var labelBlock = new TextBlock
        {
            FontSize = 12.5,
            // Mockup .foot is weight 600; the destructive rows are the only
            // ones that carry it.
            FontWeight = danger ? FontWeights.SemiBold : FontWeights.Normal,
            Foreground = labelInk,
            VerticalAlignment = VA.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };

        Run? dimSuffixRun = null;
        Brush? dimInk = null;

        if (dimmedSuffix is null)
        {
            labelBlock.Text = label;
        }
        else
        {
            // Two runs rather than two TextBlocks so the ellipsis and the
            // vertical centring still work as one line. Only the ink and the
            // weight differ - 55% of the row's own colour, per .foot span.
            dimInk = Dim(labelInk);
            dimSuffixRun = new Run(" " + dimmedSuffix)
            {
                Foreground = dimInk,
                FontWeight = FontWeights.Medium,
            };
            labelBlock.Inlines.Add(new Run(label));
            labelBlock.Inlines.Add(dimSuffixRun);
        }

        Grid.SetColumn(labelBlock, 1);
        grid.Children.Add(labelBlock);

        TextBlock? valueBlock = null;
        System.Windows.Shapes.Path? chevron = null;

        if (isToggle)
        {
            grid.Children.Add(BuildToggle(t, toggleOn));
            Grid.SetColumn(grid.Children[^1], 2);
        }
        else if (hasSubMenu)
        {
            chevron = TrailingChevron(t, MenuIcons.Chevron, iconInk);
            grid.Children.Add(chevron);
            Grid.SetColumn(chevron, 2);
        }
        else if (trailingChevron is not null)
        {
            // "Open the screenshots folder" wears the mockup's double chevron:
            // it leaves the menu rather than opening a cascade.
            chevron = TrailingChevron(t, trailingChevron, iconInk);
            grid.Children.Add(chevron);
            Grid.SetColumn(chevron, 2);
        }
        else if (value is not null)
        {
            valueBlock = new TextBlock
            {
                Text = value,
                FontSize = 11.5,
                // .row .v sits at 55% opacity over the row's own ink; the
                // destructive value overrides that with a solid red at 600.
                Foreground = dangerValue ? t.DangerValueBrush
                           : coffee ? t.CoffeeTextBrush
                           : t.RowValueBrush,
                FontWeight = dangerValue ? FontWeights.SemiBold : FontWeights.Normal,
                Opacity = dangerValue || coffee ? 1 : 0.55,
                VerticalAlignment = VA.Center,
                Margin = new Thickness(8, 0, 10, 0),
            };
            grid.Children.Add(valueBlock);
            Grid.SetColumn(valueBlock, 2);
        }

        return new RowVisuals
        {
            Root = grid,
            Label = labelBlock,
            Value = valueBlock,
            Icon = glyph,
            Chevron = chevron,
            LabelInk = labelInk,
            ValueInk = valueBlock?.Foreground,
            IconInk = glyph is null ? null : iconInk,
            ChevronInk = chevron is null ? null : iconInk,
            DimSuffix = dimSuffixRun,
            DimInk = dimInk,
        };
    }

    /// <summary>The same colour at 55% alpha, for the dimmed half of a label.</summary>
    private static Brush Dim(Brush ink)
    {
        if (ink is not SolidColorBrush solid) return ink;
        var dim = new SolidColorBrush(WColor.FromArgb(
            (byte)(solid.Color.A * 0.55), solid.Color.R, solid.Color.G, solid.Color.B));
        dim.Freeze();
        return dim;
    }

    /// <summary>The 10px chevron at the row's trailing edge, in the row's ink.</summary>
    private static System.Windows.Shapes.Path TrailingChevron(
        TrayTheme t, MenuIcons.Entry glyph, Brush ink) => new()
        {
            Data = MenuIcons.Wpf(glyph),
            Stroke = ink,
            StrokeThickness = 2.5,
            Width = 10, Height = 10,
            Stretch = Stretch.Uniform,
            VerticalAlignment = VA.Center,
            Margin = new Thickness(8, 0, 10, 0),
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            StrokeLineJoin = PenLineJoin.Round,
        };

    /// <summary>
    /// The mockup's toggle: a 30x18 track with a 14px knob that slides 12px.
    /// Drawn rather than styled because WPF's own ToggleButton chrome is far
    /// heavier than the mockup and would fight the row height.
    /// </summary>
    private FrameworkElement BuildToggle(TrayTheme t, bool on)
    {
        var track = new Border
        {
            Width = 30, Height = 18,
            CornerRadius = new CornerRadius(9),
            Margin = new Thickness(8, 0, 10, 0),
            VerticalAlignment = VA.Center,
            Background = on ? t.ToggleOnBrush : t.ToggleOffBrush,
        };

        var knob = new Border
        {
            Width = 14, Height = 14,
            CornerRadius = new CornerRadius(7),
            Background = on ? t.ToggleKnobOnBrush : t.ToggleKnobOffBrush,
            HorizontalAlignment = on ? HA.Right : HA.Left,
            Margin = new Thickness(2),
        };
        track.Child = knob;
        return track;
    }

    /// <summary>A group heading, per the mockup: small, uppercase, tracked out.</summary>
    private FrameworkElement SectionTitle(TrayTheme t, string text)
        => new TextBlock
        {
            Text = text.ToUpperInvariant(),
            FontSize = 10,
            FontWeight = FontWeights.Bold,
            Foreground = t.SubtitleBrush,
            Margin = new Thickness(14, 12, 14, 6),
        };

    // MARK: sections

    /// <summary>
    /// The "show the line" affordance: the mockup's 14px gradient check square
    /// when the line is on screen, an empty rounded box when it is not. A
    /// stroked line glyph could not show state; this can, at a glance.
    /// </summary>
    private static FrameworkElement LineCheck(TrayTheme t, bool on)
    {
        if (on) return CheckSquare(t, 14);

        return new Border
        {
            Width = 14,
            Height = 14,
            CornerRadius = new CornerRadius(4),
            BorderBrush = t.RowIconBrush,
            BorderThickness = new Thickness(1.4),
            Opacity = 0.55,
            VerticalAlignment = VA.Center,
        };
    }

    private FrameworkElement BuildLineSection(TrayTheme t)
    {
        var s = new StackPanel();
        s.Children.Add(SectionTitle(t, I18n.Get("SectionLine")));

        bool revealed = _window.IsRevealed;
        s.Children.Add(Row(t, MenuIcons.Line,
            revealed ? I18n.Get("HideLine") : I18n.Get("ShowLine"),
            value: "Ctrl+Alt+T", click: () => _window.Toggle(),
            iconOverride: LineCheck(t, revealed)));
        s.Children.Add(Row(t, MenuIcons.Screenshot, I18n.Get("TakeScreenshot"),
                           value: "Win+Shift+S", click: StartScreenshot));
        s.Children.Add(Row(t, MenuIcons.Clear, I18n.Get("TakeEverythingDown"),
                           value: I18n.Get("ClearLineHint"), dangerValue: true,
                           click: () => _line.Clear()));
        return s;
    }

    private FrameworkElement BuildCaptureSection(TrayTheme t)
    {
        var s = new StackPanel();
        s.Children.Add(SectionTitle(t, I18n.Get("SectionCapture")));
        s.Children.Add(Row(t, MenuIcons.Camera, I18n.Get("HandleScreenshots"),
                           isToggle: true, toggleOn: Inbox.IsEnabled,
                           click: () =>
                           {
                               Inbox.IsEnabled = !Inbox.IsEnabled;
                               App.Instance.ApplyInboxMode();
                           }));
        s.Children.Add(Row(t, MenuIcons.Copy, I18n.Get("HangCopiedImages"),
                           isToggle: true, toggleOn: ClipboardWatcher.IsEnabled,
                           click: () =>
                           {
                               ClipboardWatcher.IsEnabled = !ClipboardWatcher.IsEnabled;
                               Settings.Default.SetBool("clipboardUserDisabled", !ClipboardWatcher.IsEnabled);
                               App.Instance.ApplyClipboardWatch();
                           }));
        return s;
    }

    private FrameworkElement BuildBehaviorSection(TrayTheme t)
    {
        var s = new StackPanel();
        s.Children.Add(SectionTitle(t, I18n.Get("SectionBehavior")));

        // Every row below carries a sub-menu. Each is a lambda rather than a
        // built list because the tick marks and the values have to be read at
        // the moment the menu opens - a list built once would show whatever
        // was true when the app started.
        s.Children.Add(Row(t, MenuIcons.Behavior, I18n.Get("BringLineDown"),
                           value: I18n.Get("ItemsOn", CountOn(App.OpensFromMenuBar, App.PeeksAtNew, FullScreen.ShowLineOver)),
                           items: () => new[]
                           {
                               // Independent switches, not a one-of-three
                               // choice: mockup 04 draws each with its own
                               // icon and a 30x18 switch on the right.
                               new SubItem(I18n.Get("WhenPointerRestsAtTop"), App.OpensFromMenuBar,
                                           () => App.OpensFromMenuBar = !App.OpensFromMenuBar,
                                           Icon: MenuIcons.Line, IsToggle: true),
                               new SubItem(I18n.Get("WhenSomethingNewHangs"), App.PeeksAtNew,
                                           () => App.PeeksAtNew = !App.PeeksAtNew,
                                           Icon: MenuIcons.Camera, IsToggle: true),
                               new SubItem(I18n.Get("OverFullScreenApps"), FullScreen.ShowLineOver,
                                           () => { FullScreen.ShowLineOver = !FullScreen.ShowLineOver; App.Instance.Refresh(); },
                                           Icon: MenuIcons.Desktop, IsToggle: true),
                           },
                           subKind: SubKind.Toggle));
        s.Children.Add(Row(t, MenuIcons.Folder, I18n.Get("AlsoWatchFolders"),
                           value: DescribeWatched(),
                           subKind: SubKind.Folder,
                           items: () =>
                           {
                               var list = new List<SubItem>
                               {
                                   // 05-sub2's one action: indigo 600 with a plus.
                                   new(I18n.Get("AddFolder"), false,
                                       () => App.Instance.ChooseExtraFolder(),
                                       Icon: MenuIcons.Plus, Accent: true),
                               };
                               foreach (var f in App.ExtraFolders)
                               {
                                   var captured = f;
                                   list.Add(new SubItem(Path_GetFileName(captured), true, () => { }));
                                   list.Add(new SubItem(I18n.Get("StopWatching"), false, () =>
                                   {
                                       App.ExtraFolders = App.ExtraFolders.Where(x => x != captured).ToList();
                                       App.Instance.RestartWatchers();
                                   }, Danger: true, Icon: MenuIcons.StopWatching));
                               }
                               return list;
                           }));
        s.Children.Add(Row(t, MenuIcons.List, I18n.Get("KeepOnLine"),
                           value: Line.KeepOnLine is null
                               ? I18n.Get("AsManyAsFit")
                               : I18n.Get("PhotosSuffix", Line.KeepOnLine.Value),
                           items: () =>
                           {
                               var list = new List<SubItem>
                               {
                                   new(I18n.Get("AsManyAsFit"), Line.KeepOnLine is null,
                                       () => App.Instance.SetKeep(null)),
                               };
                               foreach (var n in Line.KeepChoices)
                               {
                                   var captured = n;
                                   list.Add(new SubItem(I18n.Get("PhotosSuffix", captured), Line.KeepOnLine == captured,
                                                       () => App.Instance.SetKeep(captured)));
                               }
                               return list;
                           }));
        s.Children.Add(Row(t, MenuIcons.CardSize, I18n.Get("Size"),
                           value: Layout.SizeLabel(Layout.Size),
                           items: () => Layout.AllSizes.Select(s =>
                               new SubItem(Layout.SizeLabel(s), Layout.Size == s,
                                           () => App.Instance.SetSize(s))).ToArray()));
        s.Children.Add(Row(t, MenuIcons.Globe, I18n.Get("Language"),
                           value: CurrentLanguageName(),
                           items: () =>
                           {
                               var list = new List<SubItem>
                               {
                                   new(I18n.Get("SystemDefault"), string.IsNullOrWhiteSpace(I18n.OverrideName),
                                       () => I18n.OverrideName = ""),
                               };
                               foreach (var ci in I18n.Supported)
                               {
                                   var captured = ci;
                                   list.Add(new SubItem(captured.NativeName, I18n.OverrideName == captured.Name,
                                                       () => I18n.OverrideName = captured.Name));
                               }
                               return list;
                           }));
        return s;
    }

    private FrameworkElement BuildAppSection(TrayTheme t)
    {
        var s = new StackPanel();
        s.Children.Add(SectionTitle(t, I18n.Get("SectionApp")));

        // Mockup 02-main puts the theme picker first in this section, not at
        // the end of 行为.
        s.Children.Add(Row(t, MenuIcons.Palette, I18n.Get("Theme"),
                           value: ThemeLabel(),
                           items: () => new[]
                           {
                               // 09-sub6 gives the two explicit themes their
                               // own glyphs and leaves "follow the system" to
                               // the tick alone.
                               new SubItem(I18n.Get("ThemeLight"), AppTheme.Mode == ThemeChoice.Light,
                                           () => AppTheme.Mode = ThemeChoice.Light,
                                           Icon: MenuIcons.Sun),
                               new SubItem(I18n.Get("ThemeDark"), AppTheme.Mode == ThemeChoice.Dark,
                                           () => AppTheme.Mode = ThemeChoice.Dark,
                                           Icon: MenuIcons.Moon),
                               new SubItem(I18n.Get("ThemeSystem"), AppTheme.Mode == ThemeChoice.System,
                                           () => AppTheme.Mode = ThemeChoice.System),
                           }));

        // 02-main gives this row no value text, only the double chevron: it
        // opens Explorer rather than a cascade, and the two chevrons say
        // "leaves the menu" where one says "opens a sub-menu".
        s.Children.Add(Row(t, MenuIcons.Folder, I18n.Get("OpenScreenshotsFolder"),
                           trailingChevron: MenuIcons.DoubleChevron, click: () =>
                           {
                               var folder = Inbox.IsEnabled ? Inbox.Folder : Inbox.ScreenshotsFolder;
                               try { System.Diagnostics.Process.Start("explorer.exe", folder); } catch { }
                           }));
        s.Children.Add(Row(t, MenuIcons.Speaker, I18n.Get("Sounds"),
                           isToggle: true, toggleOn: Sound.SoundOn,
                           click: () => Sound.SoundOn = !Sound.SoundOn));
        s.Children.Add(Row(t, MenuIcons.Power, I18n.Get("OpenAtLogin"),
                           isToggle: true, toggleOn: StartupManager.IsEnabled,
                           click: () => App.Instance.ToggleStartup()));
        s.Children.Add(Row(t, MenuIcons.Refresh, I18n.Get("CheckForUpdates"), click: () =>
        {
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(
                    "https://github.com/Zhao-allen/Clothesline/releases") { UseShellExecute = true });
            }
            catch { }
        }));
        return s;
    }

    private FrameworkElement BuildSupportSection(TrayTheme t)
    {
        var s = new StackPanel();
        s.Children.Add(SectionTitle(t, I18n.Get("SectionSupport")));
        s.Children.Add(Row(t, MenuIcons.Coffee, I18n.Get("BuyMeACoffee"),
                           value: "\u2615", coffee: true,
                           click: () => DonationWindow.ShowOrRaise()));
        return s;
    }

    private FrameworkElement BuildFooter(TrayTheme t)
    {
        var s = new StackPanel();
        s.Children.Add(new Border
        {
            Height = 1,
            Margin = new Thickness(14, 6, 14, 9),
            Background = t.DividerBrush,
        });

        // Mockup .foot: the verb in #dc2626 at 600, the brand at 55% opacity
        // and 500. Split at the brand name rather than adding a second
        // resource, because every translation already ends with it - except
        // Japanese, which puts it first; there the whole line stays red.
        var quit = I18n.Get("Quit");
        const string brand = "Clothesline";
        string label = quit;
        string? suffix = null;
        if (quit.EndsWith(brand, StringComparison.Ordinal) && quit.Length > brand.Length)
        {
            label = quit[..^brand.Length].TrimEnd();
            suffix = brand;
        }

        s.Children.Add(Row(t, MenuIcons.Warning, label, dimmedSuffix: suffix, danger: true,
                           click: () => App.Instance.ShutdownApp()));
        return s;
    }

    // MARK: helpers

    private static int CountOn(params bool[] flags)
    {
        int n = 0;
        foreach (var f in flags) if (f) n++;
        return n;
    }

    private static string DescribeWatched()
    {
        var watched = App.ExtraFolders;
        return watched.Count switch
        {
            0 => I18n.Get("ValueNone"),
            1 => Path_GetFileName(watched[0]),
            _ => watched.Count.ToString(CultureInfo.CurrentCulture),
        };
    }

    private static string Path_GetFileName(string p)
    {
        try { return System.IO.Path.GetFileName(p.TrimEnd('\\', '/')); }
        catch { return p; }
    }

    private static string CurrentLanguageName()
    {
        if (string.IsNullOrWhiteSpace(I18n.OverrideName)) return I18n.Get("SystemDefault");
        return I18n.Supported.FirstOrDefault(c => c.Name == I18n.OverrideName)?.NativeName
               ?? I18n.OverrideName;
    }

    private static string ThemeLabel() => AppTheme.Mode switch
    {
        ThemeChoice.Light => I18n.Get("ThemeLight"),
        ThemeChoice.Dark => I18n.Get("ThemeDark"),
        _ => I18n.Get("ThemeSystem"),
    };

    private static void StartScreenshot()
    {
        const string uri = "ms-screenclip://capture/image?rectangle";
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(uri) { UseShellExecute = true });
            Diag.Write("tray", $"launched {uri}");
        }
        catch (Exception ex)
        {
            Diag.Error("tray", ex);
            Sound.Beep();
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _autoCloseTimer?.Stop();
        _autoCloseTimer = null;
        Close();
        _popup.Child = null;
        _card = null;
    }
}
