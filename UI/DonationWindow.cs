using System;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using Clothesline.Services;

// This project sets both UseWPF and UseWindowsForms, so several types exist
// in two namespaces. This is a WPF window; alias the WPF ones.
using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using MouseButtonEventArgs = System.Windows.Input.MouseButtonEventArgs;
using Cursors = System.Windows.Input.Cursors;
using Image = System.Windows.Controls.Image;
using TextBlock = System.Windows.Controls.TextBlock;
using StackPanel = System.Windows.Controls.StackPanel;
using Grid = System.Windows.Controls.Grid;
using Border = System.Windows.Controls.Border;
using TextAlignment = System.Windows.TextAlignment;
using TextWrapping = System.Windows.TextWrapping;
using FontWeights = System.Windows.FontWeights;
using HA = System.Windows.HorizontalAlignment;
using VA = System.Windows.VerticalAlignment;

namespace Clothesline.UI;

/// <summary>
/// A borderless glass-morphism window that shows a QR code for donations.
/// Matches the installer window style: dark translucent card, blurred
/// backdrop, rounded corners.
/// </summary>
public sealed class DonationWindow : Window
{
    private Point _dragStart;
    private bool _dragging;

    public DonationWindow()
    {
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ResizeMode = ResizeMode.NoResize;
        SizeToContent = SizeToContent.WidthAndHeight;
        ShowInTaskbar = false;
        Topmost = true;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Title = I18n.Get("DonationTitle");

        // Root visual
        var root = new Border
        {
            Width = 340,
            CornerRadius = new CornerRadius(18),
            Background = new SolidColorBrush(Color.FromArgb(210, 20, 22, 38)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(60, 255, 255, 255)),
            BorderThickness = new Thickness(1),
            Margin = new Thickness(20),
            Effect = new DropShadowEffect
            {
                Color = Colors.Black,
                Opacity = 0.55,
                BlurRadius = 30,
                ShadowDepth = 8,
                Direction = 270
            }
        };

        // Inner glow overlay
        var glow = new Border
        {
            CornerRadius = new CornerRadius(18),
            Background = new LinearGradientBrush(
                new GradientStopCollection
                {
                    new GradientStop(Color.FromArgb(30, 255, 255, 255), 0),
                    new GradientStop(Color.FromArgb(0, 255, 255, 255), 0.4)
                },
                new Point(0, 0), new Point(1, 1)),
            IsHitTestVisible = false
        };

        var content = new Grid();
        content.Children.Add(glow);

        var stack = new StackPanel { Margin = new Thickness(24, 20, 24, 20) };

        // ── Header row ──
        var header = new Grid();
        var title = new TextBlock
        {
            Text = I18n.Get("DonationTitle"),
            FontSize = 17,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Color.FromRgb(248, 250, 252)),
            HorizontalAlignment = HA.Left,
            VerticalAlignment = VA.Center
        };
        var close = new Border
        {
            Width = 28, Height = 28,
            CornerRadius = new CornerRadius(14),
            Background = Brushes.Transparent,
            HorizontalAlignment = HA.Right,
            VerticalAlignment = VA.Center,
            Cursor = Cursors.Hand,
            Child = new TextBlock
            {
                Text = "\u2715",
                FontSize = 13,
                Foreground = new SolidColorBrush(Color.FromRgb(148, 163, 184)),
                HorizontalAlignment = HA.Center,
                VerticalAlignment = VA.Center
            }
        };
        close.MouseLeftButtonDown += (_, _) => Close();
        close.MouseEnter += (_, _) => close.Background = new SolidColorBrush(Color.FromArgb(40, 255, 255, 255));
        close.MouseLeave += (_, _) => close.Background = Brushes.Transparent;
        header.Children.Add(title);
        header.Children.Add(close);
        stack.Children.Add(header);

        // ── Divider ──
        stack.Children.Add(new Border
        {
            Height = 1,
            Margin = new Thickness(0, 12, 0, 16),
            Background = new LinearGradientBrush(
                new GradientStopCollection
                {
                    new GradientStop(Colors.Transparent, 0),
                    new GradientStop(Color.FromArgb(60, 255, 255, 255), 0.5),
                    new GradientStop(Colors.Transparent, 1)
                },
                new Point(0, 0), new Point(1, 0))
        });

        // ── Body text ──
        stack.Children.Add(new TextBlock
        {
            Text = I18n.Get("DonationBody"),
            FontSize = 13,
            LineHeight = 20,
            TextWrapping = TextWrapping.Wrap,
            Foreground = new SolidColorBrush(Color.FromRgb(203, 213, 225)),
            Margin = new Thickness(0, 0, 0, 16)
        });

        // ── QR code ──
        var qr = LoadQrImage();
        var qrHost = new Border
        {
            Width = 200, Height = 200,
            CornerRadius = new CornerRadius(12),
            Background = new SolidColorBrush(Color.FromRgb(248, 250, 252)),
            Padding = new Thickness(12),
            HorizontalAlignment = HA.Center,
            Margin = new Thickness(0, 0, 0, 12)
        };
        if (qr is not null)
        {
            qrHost.Child = new Image
            {
                Source = qr,
                Stretch = Stretch.Uniform,
                Width = 176,
                Height = 176
            };
        }
        else
        {
            qrHost.Child = new TextBlock
            {
                Text = I18n.Get("DonationReplaceHint"),
                FontSize = 11,
                TextWrapping = TextWrapping.Wrap,
                TextAlignment = TextAlignment.Center,
                Foreground = new SolidColorBrush(Color.FromRgb(100, 116, 139)),
                VerticalAlignment = VA.Center
            };
        }
        stack.Children.Add(qrHost);

        // ── Replace hint (small) ──
        stack.Children.Add(new TextBlock
        {
            Text = "\u2615  " + I18n.Get("DonationReplaceHint"),
            FontSize = 11,
            TextWrapping = TextWrapping.Wrap,
            TextAlignment = TextAlignment.Center,
            Foreground = new SolidColorBrush(Color.FromRgb(100, 116, 139)),
            Margin = new Thickness(8, 0, 8, 0)
        });

        content.Children.Add(stack);
        root.Child = content;
        Content = root;

        // Drag to move
        MouseLeftButtonDown += OnMouseLeftButtonDown;
        MouseMove += OnMouseMove;
        MouseLeftButtonUp += OnMouseLeftButtonUp;
    }

    private static BitmapImage? LoadQrImage()
    {
        try
        {
            // Try as embedded resource first (pack URI)
            var uri = new Uri("pack://application:,,,/Resources/donation_qr.png");
            var img = new BitmapImage();
            img.BeginInit();
            img.UriSource = uri;
            img.CacheOption = BitmapCacheOption.OnLoad;
            img.EndInit();
            if (img.PixelWidth > 0) return img;
        }
        catch { /* fallback */ }

        // Fallback: file next to executable
        try
        {
            var local = Path.Combine(AppContext.BaseDirectory, "Resources", "donation_qr.png");
            if (File.Exists(local))
            {
                var img = new BitmapImage();
                img.BeginInit();
                img.UriSource = new Uri(local, UriKind.Absolute);
                img.CacheOption = BitmapCacheOption.OnLoad;
                img.EndInit();
                return img;
            }
        }
        catch { }

        return null;
    }

    private void OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        // Only drag from non-interactive areas
        if (e.OriginalSource is TextBlock or Border or Image or StackPanel or Grid)
        {
            _dragStart = e.GetPosition(this);
            _dragging = true;
            CaptureMouse();
        }
    }

    private void OnMouseMove(object sender, MouseEventArgs e)
    {
        if (!_dragging) return;
        var p = e.GetPosition(this);
        Left += p.X - _dragStart.X;
        Top += p.Y - _dragStart.Y;
    }

    private void OnMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        _dragging = false;
        ReleaseMouseCapture();
    }
}
