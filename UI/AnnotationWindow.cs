using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Ink;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Clothesline.Services;

namespace Clothesline.UI;

/// <summary>
/// Simple annotation window. Freehand ink over the screenshot (1:1 pixels).
/// Save overwrites the original file and reloads the thumbnail.
/// </summary>
internal sealed class AnnotationWindow : Window
{
    private readonly string _path;
    private readonly BitmapSource _original;
    private readonly InkCanvas _ink;

    public AnnotationWindow(string path)
    {
        _path = path;
        _original = LoadImage(path);

        Title = "Annotate – Clothesline";
        WindowStyle = WindowStyle.ToolWindow;
        ResizeMode = ResizeMode.CanResizeWithGrip;
        Background = new SolidColorBrush(Color.FromRgb(30, 30, 30));
        Width = Math.Min(1200, _original.PixelWidth + 60);
        Height = Math.Min(900, _original.PixelHeight + 100);
        WindowStartupLocation = WindowStartupLocation.CenterScreen;

        var grid = new Grid();
        Content = grid;
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        // Toolbar
        var toolbar = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Background = new SolidColorBrush(Color.FromRgb(48, 48, 48)),
            Height = 46
        };
        Grid.SetRow(toolbar, 0);
        grid.Children.Add(toolbar);

        AddToolButton(toolbar, "Pen", () => _ink.EditingMode = InkCanvasEditingMode.Ink);
        AddToolButton(toolbar, "Select", () => _ink.EditingMode = InkCanvasEditingMode.Select);
        AddToolButton(toolbar, "Erase", () => _ink.EditingMode = InkCanvasEditingMode.EraseByPoint);
        AddColorButton(toolbar, Colors.Red);
        AddColorButton(toolbar, Colors.Orange);
        AddColorButton(toolbar, Colors.Yellow);
        AddColorButton(toolbar, Colors.LimeGreen);
        AddColorButton(toolbar, Colors.DeepSkyBlue);
        AddColorButton(toolbar, Colors.White);
        AddToolButton(toolbar, "Save", Save);
        AddToolButton(toolbar, "Cancel", Close);

        // Scrollable 1:1 viewer so ink coordinates match pixels
        var scroll = new ScrollViewer
        {
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Background = Brushes.Black
        };
        Grid.SetRow(scroll, 1);
        grid.Children.Add(scroll);

        var container = new Grid
        {
            Width = _original.PixelWidth,
            Height = _original.PixelHeight
        };
        scroll.Content = container;

        var img = new Image
        {
            Source = _original,
            Width = _original.PixelWidth,
            Height = _original.PixelHeight,
            Stretch = Stretch.None
        };
        container.Children.Add(img);

        _ink = new InkCanvas
        {
            Width = _original.PixelWidth,
            Height = _original.PixelHeight,
            Background = Brushes.Transparent,
            EditingMode = InkCanvasEditingMode.Ink
        };
        _ink.DefaultDrawingAttributes = new DrawingAttributes
        {
            Color = Colors.Red,
            Width = 4,
            Height = 4,
            FitToCurve = true
        };
        container.Children.Add(_ink);

        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) Close();
            if (e.Key == Key.S && (Keyboard.Modifiers & ModifierKeys.Control) != 0) Save();
        };
    }

    private void AddToolButton(StackPanel parent, string text, Action action)
    {
        var btn = new Button
        {
            Content = text,
            Margin = new Thickness(6, 6, 0, 6),
            Padding = new Thickness(14, 4, 14, 4),
            Background = new SolidColorBrush(Color.FromRgb(68, 68, 68)),
            Foreground = Brushes.White,
            BorderThickness = new Thickness(0),
            Cursor = Cursors.Hand
        };
        btn.Click += (_, _) => action();
        parent.Children.Add(btn);
    }

    private void AddColorButton(StackPanel parent, Color color)
    {
        var btn = new Button
        {
            Width = 26,
            Height = 26,
            Margin = new Thickness(4, 10, 0, 10),
            Background = new SolidColorBrush(color),
            BorderThickness = new Thickness(1),
            BorderBrush = Brushes.White,
            Cursor = Cursors.Hand
        };
        btn.Click += (_, _) =>
        {
            _ink.DefaultDrawingAttributes.Color = color;
            _ink.EditingMode = InkCanvasEditingMode.Ink;
        };
        parent.Children.Add(btn);
    }

    private static BitmapSource LoadImage(string path)
    {
        var bmp = new BitmapImage();
        bmp.BeginInit();
        bmp.CacheOption = BitmapCacheOption.OnLoad;
        bmp.UriSource = new Uri(path);
        bmp.EndInit();
        bmp.Freeze();
        return bmp;
    }

    private void Save()
    {
        try
        {
            int w = _original.PixelWidth;
            int h = _original.PixelHeight;
            var rtb = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);

            var dv = new DrawingVisual();
            using (var dc = dv.RenderOpen())
            {
                dc.DrawImage(_original, new Rect(0, 0, w, h));
                _ink.Strokes.Draw(dc);
            }
            rtb.Render(dv);

            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(rtb));
            using var fs = File.Create(_path);
            encoder.Save(fs);

            Close();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Save failed:\n{ex.Message}", "Clothesline", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }
}
