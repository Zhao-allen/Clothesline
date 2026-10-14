using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Ink;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Clothesline.Services;




using WpfOrientation = System.Windows.Controls.Orientation;
using WpfComboBox = System.Windows.Controls.ComboBox;
using WpfImage = System.Windows.Controls.Image;
using WpfRectangle = System.Windows.Shapes.Rectangle;
using WpfButton = System.Windows.Controls.Button;
using WpfCursors = System.Windows.Input.Cursors;
using WpfPen = System.Windows.Media.Pen;
using WpfMessageBox = System.Windows.MessageBox;
using WpfMouseEventArgs = System.Windows.Input.MouseEventArgs;
using WpfKeyEventArgs = System.Windows.Input.KeyEventArgs;
using WpfTextBox = System.Windows.Controls.TextBox;

namespace Clothesline.UI;

/// <summary>
/// Annotation window with freehand, rectangle, arrow, and text tools.
/// Saves over the original PNG and reloads the thumbnail.
/// </summary>
internal sealed class AnnotationWindow : Window
{
    private readonly string _path;
    private readonly BitmapSource _original;
    private readonly InkCanvas _ink;
    private readonly Canvas _shapeCanvas;
    private readonly WpfTextBox? _activeTextBox;

    private enum Tool { Pen, Select, Erase, Rectangle, Arrow, Text }
    private Tool _currentTool = Tool.Pen;
    private Point _shapeStart;
    private Shape? _previewShape;
    private Color _currentColor = Colors.Red;
    private double _strokeWidth = 4;

    public AnnotationWindow(string path)
    {
        _path = path;
        _original = LoadImage(path);

        Title = "标注 – Clothesline";
        WindowStyle = WindowStyle.ToolWindow;
        ResizeMode = ResizeMode.CanResizeWithGrip;
        Background = new SolidColorBrush(Color.FromRgb(30, 30, 30));
        Width = Math.Min(1200, _original.PixelWidth + 60);
        Height = Math.Min(900, _original.PixelHeight + 120);
        WindowStartupLocation = WindowStartupLocation.CenterScreen;

        var grid = new Grid();
        Content = grid;
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        // Toolbar
        var toolbar = new StackPanel
        {
            Orientation = WpfOrientation.Horizontal,
            Background = new SolidColorBrush(Color.FromRgb(48, 48, 48)),
            Height = 50
        };
        Grid.SetRow(toolbar, 0);
        grid.Children.Add(toolbar);

        AddToolButton(toolbar, "画笔", () => SetTool(Tool.Pen));
        AddToolButton(toolbar, "矩形", () => SetTool(Tool.Rectangle));
        AddToolButton(toolbar, "箭头", () => SetTool(Tool.Arrow));
        AddToolButton(toolbar, "文字", () => SetTool(Tool.Text));
        AddToolButton(toolbar, "选择", () => SetTool(Tool.Select));
        AddToolButton(toolbar, "橡皮", () => SetTool(Tool.Erase));

        // Separator
        toolbar.Children.Add(new Border { Width = 1, Background = Brushes.Gray, Margin = new Thickness(8, 8, 8, 8) });

        AddColorButton(toolbar, Colors.Red);
        AddColorButton(toolbar, Colors.Orange);
        AddColorButton(toolbar, Colors.Yellow);
        AddColorButton(toolbar, Colors.LimeGreen);
        AddColorButton(toolbar, Colors.DeepSkyBlue);
        AddColorButton(toolbar, Colors.White);
        AddColorButton(toolbar, Colors.Black);

        // Width selector
        var widthLabel = new TextBlock { Text = "粗细", Foreground = Brushes.White, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 4, 0) };
        toolbar.Children.Add(widthLabel);
        var widthBox = new WpfComboBox { Width = 50, SelectedIndex = 1, VerticalAlignment = VerticalAlignment.Center };
        widthBox.Items.Add(2);
        widthBox.Items.Add(4);
        widthBox.Items.Add(6);
        widthBox.Items.Add(8);
        widthBox.SelectionChanged += (_, _) =>
        {
            if (widthBox.SelectedItem is int w)
            {
                _strokeWidth = w;
                _ink.DefaultDrawingAttributes.Width = w;
                _ink.DefaultDrawingAttributes.Height = w;
            }
        };
        toolbar.Children.Add(widthBox);

        AddToolButton(toolbar, "保存", Save);
        AddToolButton(toolbar, "取消", Close);

        // Viewer
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

        var img = new WpfImage
        {
            Source = _original,
            Width = _original.PixelWidth,
            Height = _original.PixelHeight,
            Stretch = Stretch.None
        };
        container.Children.Add(img);

        _shapeCanvas = new Canvas
        {
            Width = _original.PixelWidth,
            Height = _original.PixelHeight,
            Background = Brushes.Transparent
        };
        container.Children.Add(_shapeCanvas);

        _ink = new InkCanvas
        {
            Width = _original.PixelWidth,
            Height = _original.PixelHeight,
            Background = Brushes.Transparent,
            EditingMode = InkCanvasEditingMode.Ink
        };
        _ink.DefaultDrawingAttributes = new DrawingAttributes
        {
            Color = _currentColor,
            Width = _strokeWidth,
            Height = _strokeWidth,
            FitToCurve = true
        };
        container.Children.Add(_ink);

        // Shape drawing events on the canvas
        _shapeCanvas.MouseLeftButtonDown += OnShapeDown;
        _shapeCanvas.MouseMove += OnShapeMove;
        _shapeCanvas.MouseLeftButtonUp += OnShapeUp;

        KeyDown += OnKeyDown;
    }

    private void SetTool(Tool tool)
    {
        _currentTool = tool;
        _ink.EditingMode = tool switch
        {
            Tool.Pen => InkCanvasEditingMode.Ink,
            Tool.Select => InkCanvasEditingMode.Select,
            Tool.Erase => InkCanvasEditingMode.EraseByPoint,
            _ => InkCanvasEditingMode.None
        };
        _shapeCanvas.IsHitTestVisible = tool is Tool.Rectangle or Tool.Arrow or Tool.Text;
        _ink.IsHitTestVisible = tool is not (Tool.Rectangle or Tool.Arrow or Tool.Text);
    }

    private void OnShapeDown(object sender, MouseButtonEventArgs e)
    {
        if (_currentTool is not (Tool.Rectangle or Tool.Arrow or Tool.Text)) return;
        _shapeStart = e.GetPosition(_shapeCanvas);
        _shapeCanvas.CaptureMouse();

        if (_currentTool == Tool.Text)
        {
            var tb = new WpfTextBox
            {
                Background = Brushes.Transparent,
                Foreground = new SolidColorBrush(_currentColor),
                BorderThickness = new Thickness(1),
                BorderBrush = new SolidColorBrush(_currentColor),
                FontSize = 18,
                Padding = new Thickness(2),
                AcceptsReturn = true
            };
            Canvas.SetLeft(tb, _shapeStart.X);
            Canvas.SetTop(tb, _shapeStart.Y);
            _shapeCanvas.Children.Add(tb);
            tb.Focus();
            tb.LostFocus += (_, _) => { if (string.IsNullOrWhiteSpace(tb.Text)) _shapeCanvas.Children.Remove(tb); };
            e.Handled = true;
            return;
        }

        _previewShape = _currentTool == Tool.Rectangle
            ? new WpfRectangle { Stroke = new SolidColorBrush(_currentColor), StrokeThickness = _strokeWidth, Fill = Brushes.Transparent }
            : new Line { Stroke = new SolidColorBrush(_currentColor), StrokeThickness = _strokeWidth, StrokeEndLineCap = PenLineCap.Triangle, StrokeStartLineCap = PenLineCap.Round };

        _shapeCanvas.Children.Add(_previewShape);
        e.Handled = true;
    }

    private void OnShapeMove(object sender, WpfMouseEventArgs e)
    {
        if (_previewShape == null || !_shapeCanvas.IsMouseCaptured) return;
        var pos = e.GetPosition(_shapeCanvas);

        if (_previewShape is WpfRectangle rect)
        {
            var x = Math.Min(_shapeStart.X, pos.X);
            var y = Math.Min(_shapeStart.Y, pos.Y);
            rect.Width = Math.Abs(pos.X - _shapeStart.X);
            rect.Height = Math.Abs(pos.Y - _shapeStart.Y);
            Canvas.SetLeft(rect, x);
            Canvas.SetTop(rect, y);
        }
        else if (_previewShape is Line line)
        {
            line.X1 = _shapeStart.X;
            line.Y1 = _shapeStart.Y;
            line.X2 = pos.X;
            line.Y2 = pos.Y;
        }
    }

    private void OnShapeUp(object sender, MouseButtonEventArgs e)
    {
        _shapeCanvas.ReleaseMouseCapture();
        _previewShape = null;
    }

    private void OnKeyDown(object sender, WpfKeyEventArgs e)
    {
        if (e.Key == Key.Escape) Close();
        if (e.Key == Key.S && (Keyboard.Modifiers & ModifierKeys.Control) != 0) Save();
    }

    private void AddToolButton(StackPanel parent, string text, Action action)
    {
        var btn = new WpfButton
        {
            Content = text,
            Margin = new Thickness(4, 8, 0, 8),
            Padding = new Thickness(12, 4, 12, 4),
            Background = new SolidColorBrush(Color.FromRgb(68, 68, 68)),
            Foreground = Brushes.White,
            BorderThickness = new Thickness(0),
            Cursor = WpfCursors.Hand
        };
        btn.Click += (_, _) => action();
        parent.Children.Add(btn);
    }

    private void AddColorButton(StackPanel parent, Color color)
    {
        var btn = new WpfButton
        {
            Width = 24,
            Height = 24,
            Margin = new Thickness(3, 13, 0, 13),
            Background = new SolidColorBrush(color),
            BorderThickness = new Thickness(1),
            BorderBrush = Brushes.White,
            Cursor = WpfCursors.Hand
        };
        btn.Click += (_, _) =>
        {
            _currentColor = color;
            _ink.DefaultDrawingAttributes.Color = color;
            SetTool(Tool.Pen);
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

                // Render shapes
                foreach (UIElement child in _shapeCanvas.Children)
                {
                    if (child is Shape shape)
                    {
                        var visual = new DrawingVisual();
                        using (var sdc = visual.RenderOpen())
                        {
                            if (shape is WpfRectangle r)
                            {
                                var left = Canvas.GetLeft(r);
                                var top = Canvas.GetTop(r);
                                sdc.DrawRectangle(null, new WpfPen(shape.Stroke, shape.StrokeThickness), new Rect(left, top, r.Width, r.Height));
                            }
                            else if (shape is Line l)
                            {
                                sdc.DrawLine(new WpfPen(shape.Stroke, shape.StrokeThickness) { EndLineCap = PenLineCap.Triangle, StartLineCap = PenLineCap.Round },
                                    new Point(l.X1, l.Y1), new Point(l.X2, l.Y2));
                            }
                        }
                        visual.Drawing.Freeze();
                        dc.DrawDrawing(visual.Drawing);
                    }
                    else if (child is WpfTextBox tb && !string.IsNullOrWhiteSpace(tb.Text))
                    {
                        var ft = new FormattedText(
                            tb.Text,
                            System.Globalization.CultureInfo.CurrentCulture,
                            System.Windows.FlowDirection.LeftToRight,
                            new Typeface(tb.FontFamily, tb.FontStyle, tb.FontWeight, tb.FontStretch),
                            tb.FontSize,
                            tb.Foreground,
                            VisualTreeHelper.GetDpi(this).PixelsPerDip);
                        dc.DrawText(ft, new Point(Canvas.GetLeft(tb), Canvas.GetTop(tb)));
                    }
                }

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
            WpfMessageBox.Show($"保存失败:\n{ex.Message}", "Clothesline", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }
}
