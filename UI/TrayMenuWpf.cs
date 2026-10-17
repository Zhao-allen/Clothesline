using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using Clothesline.Models;
using Clothesline.Services;

// This project sets both UseWPF and UseWindowsForms, so several types exist
// in two namespaces. Everything here is WPF.
using Image = System.Windows.Controls.Image;
using Brush = System.Windows.Media.Brush;
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

    // Mockup metrics (ui-redesign/02-main). All in DIP.
    private const double MenuWidth = 340;
    private const double CornerRadius = 20;
    private const double RowHeight = 30;
    private const double RowGap = 9;
    private const double RowRadius = 7;
    private const double IconSize = 14;
    private const double ThumbHeight = 48;
    private const double ThumbGap = 6;
    private const double ThumbRadius = 9;

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
            Focusable = false,
        };
    }

    /// <summary>
    /// Opens the menu near the cursor. The tray icon's own click is what
    /// calls this; Windows puts the cursor there already.
    /// </summary>
    public void Show()
    {
        if (_disposed) return;
        Rebuild();
        _popup.IsOpen = true;

        // A popup with no focusable content still needs to become active for
        // the keyboard to reach it, otherwise Esc does nothing and the rows
        // never highlight under the caret.
        _card?.MoveFocus(new TraversalRequest(FocusNavigationDirection.First));
    }

    public void Close() => _popup.IsOpen = false;

    public bool IsOpen => _popup.IsOpen;

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

        _card = BuildCard(t);
        _popup.Child = _card;

        // Swap after assigning the new child so the menu is never in a state
        // with no content, which would flash an empty popup on open.
        old?.BeginAnimation(UIElement.OpacityProperty, null);
    }

    private Border BuildCard(TrayTheme t)
    {
        var card = new Border
        {
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
            // bounds.
            Margin = new Thickness(28),
            Padding = new Thickness(0, 2, 0, 2),
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
        var row = new Grid { Margin = new Thickness(10, 2, 10, 10) };
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
        });
        text.Children.Add(new TextBlock
        {
            Text = $"v{TrayIcon.AppVersion} · " + (ClipboardWatcher.IsEnabled
                ? I18n.Get("WatchingClipboard")
                : I18n.Get("ClipboardOff")),
            FontSize = 10.5,
            Foreground = t.SubtitleBrush,
            LineHeight = 1.3,
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
            Margin = new Thickness(10, 0, 10, 10),
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

        strip.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        strip.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(ThumbGap) });
        strip.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        strip.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(ThumbGap) });
        strip.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

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
    /// </summary>
    private Border Row(TrayTheme t, MenuIcons.Entry icon, string label,
                       string? value = null, bool hasSubMenu = false,
                       bool isToggle = false, bool toggleOn = false,
                       Action? click = null, bool coffee = false)
    {
        var row = new Border
        {
            Height = RowHeight,
            Margin = new Thickness(4, 0, 4, RowGap),
            CornerRadius = new CornerRadius(RowRadius),
            Background = coffee ? t.CoffeeBrush : Brushes.Transparent,
            Cursor = click is null ? null : Cursors.Hand,
            Child = RowContent(t, icon, label, value, hasSubMenu, isToggle, toggleOn, coffee),
        };

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

    private Panel RowContent(TrayTheme t, MenuIcons.Entry icon, string label,
                             string? value, bool hasSubMenu, bool isToggle,
                             bool toggleOn, bool coffee)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });   // icon
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); // label
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });   // value / chevron

        var glyph = new System.Windows.Shapes.Path
        {
            Data = MenuIcons.Wpf(icon),
            Stroke = coffee ? t.CoffeeIconBrush : t.RowIconBrush,
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

        grid.Children.Add(new TextBlock
        {
            Text = label,
            FontSize = 12.5,
            Foreground = coffee ? t.CoffeeTextBrush : t.RowTextBrush,
            VerticalAlignment = VA.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
        });
        Grid.SetColumn(grid.Children[^1], 1);

        if (isToggle)
        {
            grid.Children.Add(BuildToggle(t, toggleOn));
            Grid.SetColumn(grid.Children[^1], 2);
        }
        else if (hasSubMenu)
        {
            grid.Children.Add(new System.Windows.Shapes.Path
            {
                Data = MenuIcons.Wpf(MenuIcons.Chevron),
                Stroke = t.RowIconBrush,
                StrokeThickness = 2.5,
                Width = 10, Height = 10,
                Stretch = Stretch.Uniform,
                VerticalAlignment = VA.Center,
                Margin = new Thickness(8, 0, 10, 0),
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round,
                StrokeLineJoin = PenLineJoin.Round,
            });
            Grid.SetColumn(grid.Children[^1], 2);
        }
        else if (value is not null)
        {
            grid.Children.Add(new TextBlock
            {
                Text = value,
                FontSize = 11.5,
                Foreground = coffee ? t.CoffeeTextBrush : t.RowValueBrush,
                VerticalAlignment = VA.Center,
                Margin = new Thickness(8, 0, 10, 0),
            });
            Grid.SetColumn(grid.Children[^1], 2);
        }

        return grid;
    }

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

    private FrameworkElement BuildLineSection(TrayTheme t)
    {
        var s = new StackPanel();
        s.Children.Add(SectionTitle(t, I18n.Get("SectionLine")));
        s.Children.Add(Row(t, MenuIcons.Line,
            _window.IsRevealed ? I18n.Get("HideLine") : I18n.Get("ShowLine"),
            value: "Ctrl+Alt+T", click: () => _window.Toggle()));
        s.Children.Add(Row(t, MenuIcons.Screenshot, I18n.Get("TakeScreenshot"),
                           value: "Win+Shift+S", click: StartScreenshot));
        s.Children.Add(Row(t, MenuIcons.Clear, I18n.Get("TakeEverythingDown"),
                           value: I18n.Get("ClearLineHint"), click: () => _line.Clear()));
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

        s.Children.Add(Row(t, MenuIcons.Behavior, I18n.Get("BringLineDown"),
                           value: I18n.Get("ItemsOn", CountOn(App.OpensFromMenuBar, App.PeeksAtNew, FullScreen.ShowLineOver)),
                           hasSubMenu: true));
        s.Children.Add(Row(t, MenuIcons.Folder, I18n.Get("AlsoWatchFolders"),
                           value: DescribeWatched(), hasSubMenu: true));
        s.Children.Add(Row(t, MenuIcons.List, I18n.Get("KeepOnLine"),
                           value: Line.KeepOnLine is null
                               ? I18n.Get("AsManyAsFit")
                               : I18n.Get("PhotosSuffix", Line.KeepOnLine.Value),
                           hasSubMenu: true));
        s.Children.Add(Row(t, MenuIcons.CardSize, I18n.Get("Size"),
                           value: Layout.SizeLabel(Layout.Size), hasSubMenu: true));
        s.Children.Add(Row(t, MenuIcons.Globe, I18n.Get("Language"),
                           value: CurrentLanguageName(), hasSubMenu: true));
        s.Children.Add(Row(t, MenuIcons.Palette, I18n.Get("Theme"),
                           value: ThemeLabel(), hasSubMenu: true));
        return s;
    }

    private FrameworkElement BuildAppSection(TrayTheme t)
    {
        var s = new StackPanel();
        s.Children.Add(SectionTitle(t, I18n.Get("SectionApp")));
        s.Children.Add(Row(t, MenuIcons.Folder, I18n.Get("OpenScreenshotsFolder"),
                           value: "\u2197", click: () =>
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
        s.Children.Add(Row(t, MenuIcons.Warning, I18n.Get("Quit"), click: () => App.Instance.ShutdownApp()));
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
        _popup.IsOpen = false;
        _popup.Child = null;
        _card = null;
    }
}
