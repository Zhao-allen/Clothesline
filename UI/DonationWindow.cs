using System;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Clothesline.Services;

// This project sets both UseWPF and UseWindowsForms, so several types exist
// in two namespaces. This is a WPF window; alias the WPF ones.
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using MouseButtonEventArgs = System.Windows.Input.MouseButtonEventArgs;
using Cursors = System.Windows.Input.Cursors;
using Image = System.Windows.Controls.Image;
using Brush = System.Windows.Media.Brush;
using DropShadowEffect = System.Windows.Media.Effects.DropShadowEffect;
using TextBlock = System.Windows.Controls.TextBlock;
using StackPanel = System.Windows.Controls.StackPanel;
using Grid = System.Windows.Controls.Grid;
using ColumnDefinition = System.Windows.Controls.ColumnDefinition;
using GridLength = System.Windows.GridLength;
using Border = System.Windows.Controls.Border;
using Key = System.Windows.Input.Key;
using HA = System.Windows.HorizontalAlignment;
using VA = System.Windows.VerticalAlignment;

namespace Clothesline.UI;

/// <summary>
/// "Buy me a coffee" window: the WeChat and Alipay collection codes side by
/// side, on a rounded glass card.
///
/// Follows ui-redesign/11-qr-light.html and 11-qr-dark.html: a 700-wide
/// card with the two codes on equal 260x300 white plates, a brand-coloured
/// label under each, and a thank-you line at the bottom. The theme follows
/// <see cref="AppTheme.IsDark"/>.
///
/// The plates stay near-white even in dark mode - a QR code needs contrast
/// to scan, so they deliberately do not follow the theme.
/// </summary>
public sealed class DonationWindow : Window
{
    /// <summary>At most one of these at a time, so repeat tray clicks
    /// raise the open window instead of stacking copies.</summary>
    private static DonationWindow? _open;

    private readonly DonationTheme _t;
    private bool _dragging;
    private Point _dragOrigin;
    private double _leftAtDragStart;
    private double _topAtDragStart;

    public static void ShowOrRaise()
    {
        if (_open is { } existing)
        {
            if (existing.WindowState == WindowState.Minimized) existing.WindowState = WindowState.Normal;
            existing.Activate();
            return;
        }

        var win = new DonationWindow();
        _open = win;
        win.Closed += (_, _) => _open = null;
        win.Show();
    }

    private DonationWindow()
    {
        _t = AppTheme.IsDark ? DonationTheme.Dark : DonationTheme.Light;

        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ResizeMode = ResizeMode.NoResize;
        SizeToContent = SizeToContent.WidthAndHeight;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Title = I18n.Get("DonationTitle");

        // 28 DIP of slack around the card: the far shadow is a 64px blur at
        // depth 12, and a tighter margin clips it against the window edge.
        var root = new Grid { Margin = new Thickness(28) };
        root.Children.Add(ShadowLayer(_t.CardShadowFar));
        root.Children.Add(ShadowLayer(_t.CardShadowNear));

        root.Children.Add(BuildCard());
        Content = root;

        MouseLeftButtonDown += OnDragStart;
        MouseMove += OnDragMove;
        MouseLeftButtonUp += OnDragEnd;
        PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape) SafeClose(); };
        // Clicking away dismisses it, which is what a pop-up is expected to do.
        //
        // Guarded twice over. A window that is still coming up can receive a
        // stray WM_ACTIVATE, and closing from inside that - or from a second
        // Deactivated before the first Close has finished - makes WPF throw
        // InvalidOperationException from VerifyNotClosing. That crash reached
        // the dispatcher unhandled and took the whole app down, because this
        // window is the only place the exception could surface.
        //
        // IsLoaded skips the pre-show flutter; _closing makes a repeat call a
        // no-op; the try/catch is the belt to that pair of braces.
        Deactivated += (_, _) => DismissOnFocusLoss();
    }

    private bool _closing;

    /// <summary>Closes, tolerating a window that is already on its way out.</summary>
    private void SafeClose()
    {
        if (_closing) return;
        _closing = true;
        try { Close(); }
        catch (InvalidOperationException) { _closing = false; }
    }

    /// <summary>
    /// Closes because the user clicked somewhere else. Safe to call more than
    /// once and at any point in the window's life.
    /// </summary>
    private void DismissOnFocusLoss()
    {
        // Not on screen yet: the activation flutter that follows Show() is not
        // the user going away, and closing here is what crashed.
        if (!IsLoaded) return;
        if (_closing) return;

        _closing = true;
        try
        {
            Close();
        }
        catch (InvalidOperationException)
        {
            // Already closing or closed. Nothing to do, and nothing worth
            // failing an app over.
            _closing = false;
        }
    }

    /// <summary>
    /// A solid copy of the card shape carrying one shadow. DropShadowEffect
    /// paints outside the element's bounds, so it needs a real surface behind
    /// it - a transparent one would render the shadow as a grey ring.
    /// </summary>
    private Border ShadowLayer(DropShadowEffect effect) => new()
    {
        Background = _t.CardBackground,
        CornerRadius = new CornerRadius(24),
        Effect = effect,
        IsHitTestVisible = false,
    };

    private Border BuildCard()
    {
        var card = new Border
        {
            Width = 700,
            CornerRadius = new CornerRadius(24),
            Background = _t.CardBackground,
            BorderBrush = _t.CardBorder,
            BorderThickness = new Thickness(1),
        };

        var layer = new Grid();

        // 135 degree sheen, then the 1px top highlight. Both are decorative
        // and must not swallow clicks meant for the card.
        layer.Children.Add(new Border
        {
            CornerRadius = new CornerRadius(24),
            Background = _t.CardSheen,
            IsHitTestVisible = false,
        });
        layer.Children.Add(new Border
        {
            CornerRadius = new CornerRadius(24),
            BorderBrush = _t.TopHighlight,
            BorderThickness = new Thickness(1, 1, 1, 0),
            IsHitTestVisible = false,
        });

        var body = new StackPanel { Margin = new Thickness(28, 26, 28, 22) };

        // ── Title ──
        var titleRow = new StackPanel
        {
            Orientation = System.Windows.Controls.Orientation.Horizontal,
            HorizontalAlignment = HA.Center,
        };
        titleRow.Children.Add(new TextBlock
        {
            Text = I18n.Get("DonationTitle"),
            FontSize = 19,
            FontWeight = FontWeights.ExtraBold,
            Foreground = _t.Title,
        });
        titleRow.Children.Add(new TextBlock
        {
            Text = " \u2615",      // ☕
            FontSize = 19,
            Foreground = _t.Title,
        });
        body.Children.Add(titleRow);

        // ── Subtitle ──
        body.Children.Add(new TextBlock
        {
            Text = I18n.Get("DonationSubtitle"),
            FontSize = 12.5,
            Foreground = _t.Subtitle,
            HorizontalAlignment = HA.Center,
            Margin = new Thickness(0, 6, 0, 0),
        });

        // ── The two codes ──
        body.Children.Add(BuildCodes());

        // ── Footer ──
        var footer = new StackPanel
        {
            Orientation = System.Windows.Controls.Orientation.Horizontal,
            HorizontalAlignment = HA.Center,
            Margin = new Thickness(0, 22, 0, 0),
        };
        footer.Children.Add(new TextBlock
        {
            Text = I18n.Get("DonationFooter"),
            FontSize = 12,
            Foreground = _t.FooterText,
        });
        footer.Children.Add(new TextBlock
        {
            Text = " \u2764",      // ❤
            FontSize = 12,
            Foreground = _t.Heart,
            Margin = new Thickness(6, 0, 0, 0),
        });
        body.Children.Add(footer);

        layer.Children.Add(body);

        // The close button is positioned against the card, not stacked -
        // inside a StackPanel it would be pushed into the flow instead.
        layer.Children.Add(BuildCloseButton());

        card.Child = layer;
        return card;
    }

    /// <summary>
    /// Two 280-wide columns with a fixed 32 gap, centred in the 644-wide
    /// content area. Grid rather than a wrapping panel because both the gap
    /// and the column widths are exact.
    /// </summary>
    private Grid BuildCodes()
    {
        var codes = new Grid
        {
            Margin = new Thickness(0, 22, 0, 0),
            HorizontalAlignment = HA.Center,
        };
        codes.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(280) });
        codes.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(32) });
        codes.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(280) });

        var wechat = BuildCode("donation_qr_wechat.png", I18n.Get("DonationWeChat"),
                               _t.WeChatLabel, DonationTheme.WeChatDot);
        Grid.SetColumn(wechat, 0);
        codes.Children.Add(wechat);

        var alipay = BuildCode("donation_qr_alipay.png", I18n.Get("DonationAlipay"),
                               _t.AlipayLabel, DonationTheme.AlipayDot);
        Grid.SetColumn(alipay, 2);
        codes.Children.Add(alipay);

        return codes;
    }

    private StackPanel BuildCode(string file, string label, Brush labelBrush, Brush dotBrush)
    {
        var stack = new StackPanel();

        var plate = new Border
        {
            Width = 260,
            Height = 300,
            CornerRadius = new CornerRadius(16),
            Background = _t.FrameBackground,
            BorderBrush = _t.FrameBorder,
            BorderThickness = new Thickness(1),
            Padding = new Thickness(16),
            HorizontalAlignment = HA.Center,
        };

        var plateLayer = new Grid();
        plateLayer.Children.Add(new Border
        {
            CornerRadius = new CornerRadius(16),
            Background = _t.FrameBackground,
            Effect = _t.FrameShadowFar,
            IsHitTestVisible = false,
        });
        plateLayer.Children.Add(new Border
        {
            CornerRadius = new CornerRadius(16),
            Background = _t.FrameBackground,
            Effect = _t.FrameShadowNear,
            IsHitTestVisible = false,
        });

        var code = LoadCode(file);
        if (code is not null)
        {
            var img = new Image
            {
                Source = code,
                Width = 228,
                Height = 228,
                Stretch = Stretch.Uniform,
                HorizontalAlignment = HA.Center,
                VerticalAlignment = VA.Center,
            };
            // 560 down to 228 is a 2.5x reduction; the default scaler would
            // soften the code's edges and cost scan reliability.
            RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.HighQuality);
            plateLayer.Children.Add(img);
        }
        else
        {
            plateLayer.Children.Add(new TextBlock
            {
                Text = label,
                FontSize = 13,
                Foreground = _t.Subtitle,
                HorizontalAlignment = HA.Center,
                VerticalAlignment = VA.Center,
            });
        }

        plate.Child = plateLayer;
        stack.Children.Add(plate);

        // Label: a brand dot, then the payment app's name.
        var caption = new StackPanel
        {
            Orientation = System.Windows.Controls.Orientation.Horizontal,
            HorizontalAlignment = HA.Center,
            Margin = new Thickness(0, 16, 0, 0),
        };
        caption.Children.Add(new Border
        {
            Width = 15,
            Height = 15,
            CornerRadius = new CornerRadius(7.5),
            Background = _t.DotRing,
            VerticalAlignment = VA.Center,
            Child = new Border
            {
                Width = 9,
                Height = 9,
                CornerRadius = new CornerRadius(4.5),
                Background = dotBrush,
            },
        });
        caption.Children.Add(new TextBlock
        {
            Text = label,
            FontSize = 13,
            FontWeight = FontWeights.Bold,
            Foreground = labelBrush,
            VerticalAlignment = VA.Center,
            Margin = new Thickness(7, 0, 0, 0),
        });
        stack.Children.Add(caption);

        return stack;
    }

    private Border BuildCloseButton()
    {
        var button = new Border
        {
            // Tagged so the drag handler can recognise clicks on this subtree
            // and leave them alone - it is a Border like the card is.
            Tag = "close",
            Width = 30,
            Height = 30,
            CornerRadius = new CornerRadius(15),
            Background = _t.CloseBackground,
            HorizontalAlignment = HA.Right,
            VerticalAlignment = VA.Top,
            Margin = new Thickness(0, 16, 16, 0),
            Cursor = Cursors.Hand,
            Child = new TextBlock
            {
                // U+00D7 multiplication sign, as in the mockup - not U+2715.
                Text = "\u00D7",
                FontSize = 18,
                Foreground = _t.CloseForeground,
                HorizontalAlignment = HA.Center,
                VerticalAlignment = VA.Center,
            },
        };

        button.MouseLeftButtonDown += (_, e) =>
        {
            e.Handled = true;
            Close();
        };
        button.MouseEnter += (_, _) => button.Background = _t.CloseHover;
        button.MouseLeave += (_, _) => button.Background = _t.CloseBackground;

        return button;
    }

    /// <summary>
    /// Loads one code, preferring the copy embedded in the assembly and
    /// falling back to a file beside the exe (which is what a developer
    /// running from the build output, or a user who swapped in their own
    /// code, will have).
    /// </summary>
    private static BitmapImage? LoadCode(string fileName)
    {
        try
        {
            var img = new BitmapImage();
            img.BeginInit();
            img.UriSource = new Uri($"pack://application:,,,/Resources/{fileName}");
            img.CacheOption = BitmapCacheOption.OnLoad;   // releases the file handle
            img.EndInit();
            img.Freeze();
            if (img.PixelWidth > 0) return img;
        }
        catch
        {
            // Fall through to the on-disk copy.
        }

        try
        {
            var local = Path.Combine(AppContext.BaseDirectory, "Resources", fileName);
            if (!File.Exists(local)) return null;

            var img = new BitmapImage();
            img.BeginInit();
            img.UriSource = new Uri(local, UriKind.Absolute);
            img.CacheOption = BitmapCacheOption.OnLoad;
            img.EndInit();
            img.Freeze();
            return img;
        }
        catch
        {
            return null;
        }
    }

    // MARK: dragging

    private void OnDragStart(object sender, MouseButtonEventArgs e)
    {
        // The close button is a Border too, so shape alone cannot tell it
        // apart from the card. Ask the button directly instead.
        if (e.OriginalSource is DependencyObject source && IsWithinCloseButton(source)) return;

        _dragOrigin = e.GetPosition(this);
        _leftAtDragStart = Left;
        _topAtDragStart = Top;
        _dragging = CaptureMouse();
    }

    private void OnDragMove(object sender, MouseEventArgs e)
    {
        if (!_dragging) return;
        var p = e.GetPosition(this);
        // Absolute, not incremental: adding a delta each frame accumulates
        // rounding error and the window drifts away from the cursor.
        Left = _leftAtDragStart + (p.X - _dragOrigin.X);
        Top = _topAtDragStart + (p.Y - _dragOrigin.Y);
    }

    private void OnDragEnd(object sender, MouseButtonEventArgs e)
    {
        if (!_dragging) return;
        _dragging = false;
        ReleaseMouseCapture();
    }

    private bool IsWithinCloseButton(DependencyObject node)
    {
        for (DependencyObject? n = node; n is not null; n = VisualTreeHelper.GetParent(n))
        {
            if (n is Border { Tag: "close" }) return true;
        }

        return false;
    }
}
