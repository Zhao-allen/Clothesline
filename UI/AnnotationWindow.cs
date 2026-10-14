using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Ink;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Clothesline.Services;


using WpfMouseEventArgs = System.Windows.Input.MouseEventArgs;
using WpfKeyEventArgs = System.Windows.Input.KeyEventArgs;
using WpfTextBox = System.Windows.Controls.TextBox;

namespace Clothesline.UI;

/// <summary>
/// Annotation window with freehand, rectangle, arrow, and text tools.
/// Shapes and text are managed as data models for reliable saving.
/// </summary>
internal sealed class AnnotationWindow : Window
{
    private readonly string _path;
    private readonly BitmapSource _original;
    private readonly InkCanvas _ink;
    private readonly Canvas _shapeCanvas;

    private enum Tool { Pen, Select, Erase, Rectangle, Arrow, Text }
    private Tool _currentTool = Tool.Pen;
    private Point _shapeStart;
    private Shape? _previewShape;
    private Color _currentColor = Colors.Red;
    private double _strokeWidth = 4;

    // Data models for stable rendering
    private readonly List<AnnotationShape> _shapes = new();
    private readonly List<AnnotationText> _texts = new();

    private sealed class AnnotationShape
    {
        public bool IsArrow { get; init; }
        public Point Start { get; init; }
        public Point End { get; init; }
        public Color Color { get; init; }
        public double Thickness { get; init; }
    }

    private sealed class AnnotationText
    {
        public string Text { get; init; } = "";
        public Point Position { get; init; }
        public Color Color { get; init; }
        public double FontSize { get; init; } = 18;
    }

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
            Orientation = Orientation.Horizontal,
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

        toolbar.Children.Add(new Border { Width = 1, Background = Brushes.Gray, Margin = new Thickness(8, 8, 8, 8) });

        AddColorButton(toolbar, Colors.Red);
        AddColorButton(toolbar, Colors.Orange);
        AddColorButton(toolbar, Colors.Yellow);
        AddColorButton(toolbar, Colors.LimeGreen);
        AddColorButton(toolbar, Colors.DeepSkyBlue);
        AddColorButton(toolbar, Colors.White);
        AddColorButton(toolbar, Colors.Black);

        var widthLabel = new TextBlock { Text = "粗细", Foreground = Brushes.White, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 4, 0) };
        toolbar.Children.Add(widthLabel);
        var widthBox = new ComboBox { Width = 50, SelectedIndex = 1, VerticalAlignment = VerticalAlignment.Center };
        widthBox.Items.Add(2); widthBox.Items.Add(4); widthBox.Items.Add(6); widthBox.Items.Add(8);
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

        AddToolButton(toolbar, "清除", ClearAll);
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

        var img = new Image
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
                AcceptsReturn = true,
                MinWidth = 60
            };
            Canvas.SetLeft(tb, _shapeStart.X);
            Canvas.SetTop(tb, _shapeStart.Y);
            _shapeCanvas.Children.Add(tb);
            tb.Focus();
            tb.LostFocus += (_, _) => CommitWpfTextBox(tb);
            e.Handled = true;
            return;
        }

        _previewShape = _currentTool == Tool.Rectangle
            ? new Rectangle { Stroke = new SolidColorBrush(_currentColor), StrokeThickness = _strokeWidth, Fill = Brushes.Transparent }
            : new Line { Stroke = new SolidColorBrush(_currentColor), StrokeThickness = _strokeWidth, StrokeEndLineCap = PenLineCap.Triangle };

        _shapeCanvas.Children.Add(_previewShape);
        e.Handled = true;
    }

    private void OnShapeMove(object sender, WpfMouseEventArgs e)
    {
        if (_previewShape == null || !_shapeCanvas.IsMouseCaptured) return;
        var pos = e.GetPosition(_shapeCanvas);

        if (_previewShape is Rectangle rect)
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
        if (_previewShape == null) return;
        _shapeCanvas.ReleaseMouseCapture();

        var pos = e.GetPosition(_shapeCanvas);
        bool isArrow = _previewShape is Line;

        if (isArrow)
        {
            if (Math.Abs(pos.X - _shapeStart.X) > 4 || Math.Abs(pos.Y - _shapeStart.Y) > 4)
            {
                _shapes.Add(new AnnotationShape
                {
                    IsArrow = true,
                    Start = _shapeStart,
                    End = pos,
                    Color = _currentColor,
                    Thickness = _strokeWidth
                });
            }
        }
        else if (_previewShape is Rectangle)
        {
            var w = Math.Abs(pos.X - _shapeStart.X);
            var h = Math.Abs(pos.Y - _shapeStart.Y);
            if (w > 4 && h > 4)
            {
                _shapes.Add(new AnnotationShape
                {
                    IsArrow = false,
                    Start = new Point(Math.Min(_shapeStart.X, pos.X), Math.Min(_shapeStart.Y, pos.Y)),
                    End = new Point(Math.Max(_shapeStart.X, pos.X), Math.Max(_shapeStart.Y, pos.Y)),
                    Color = _currentColor,
                    Thickness = _strokeWidth
                });
            }
        }

        _shapeCanvas.Children.Remove(_previewShape);
        _previewShape = null;
        RedrawShapes();
    }

    private void CommitWpfTextBox(WpfTextBox tb)
    {
        var text = tb.Text?.Trim();
        if (!string.IsNullOrEmpty(text))
        {
            _texts.Add(new AnnotationText
            {
                Text = text,
                Position = new Point(Canvas.GetLeft(tb), Canvas.GetTop(tb)),
                Color = _currentColor,
                FontSize = tb.FontSize
            });
        }
        _shapeCanvas.Children.Remove(tb);
        RedrawShapes();
    }

    private void RedrawShapes()
    {
        // Remove old shape visuals (keep textboxes that are still editing)
        var toRemove = _shapeCanvas.Children.OfType<Shape>().ToList();
        foreach (var s in toRemove) _shapeCanvas.Children.Remove(s);

        foreach (var shape in _shapes)
        {
            if (shape.IsArrow)
            {
                var line = new Line
                {
                    X1 = shape.Start.X, Y1 = shape.Start.Y,
                    X2 = shape.End.X, Y2 = shape.End.Y,
                    Stroke = new SolidColorBrush(shape.Color),
                    StrokeThickness = shape.Thickness,
                    StrokeEndLineCap = PenLineCap.Triangle,
                    IsHitTestVisible = false
                };
                _shapeCanvas.Children.Add(line);
            }
            else
            {
                var rect = new Rectangle
                {
                    Width = shape.End.X - shape.Start.X,
                    Height = shape.End.Y - shape.Start.Y,
                    Stroke = new SolidColorBrush(shape.Color),
                    StrokeThickness = shape.Thickness,
                    Fill = Brushes.Transparent,
                    IsHitTestVisible = false
                };
                Canvas.SetLeft(rect, shape.Start.X);
                Canvas.SetTop(rect, shape.Start.Y);
                _shapeCanvas.Children.Add(rect);
            }
        }

        // Redraw committed texts as TextBlocks
        var oldTexts = _shapeCanvas.Children.OfType<TextBlock>().ToList();
        foreach (var t in oldTexts) _shapeCanvas.Children.Remove(t);

        foreach (var txt in _texts)
        {
            var tb = new TextBlock
            {
                Text = txt.Text,
                Foreground = new SolidColorBrush(txt.Color),
                FontSize = txt.FontSize,
                IsHitTestVisible = false
            };
            Canvas.SetLeft(tb, txt.Position.X);
            Canvas.SetTop(tb, txt.Position.Y);
            _shapeCanvas.Children.Add(tb);
        }
    }

    private void ClearAll()
    {
        _shapes.Clear();
        _texts.Clear();
        _ink.Strokes.Clear();
        _shapeCanvas.Children.Clear();
    }

    private void OnKeyDown(object sender, WpfKeyEventArgs e)
    {
        if (e.Key == Key.Escape) Close();
        if (e.Key == Key.S && (Keyboard.Modifiers & ModifierKeys.Control) != 0) Save();
    }

    private void AddToolButton(StackPanel parent, string text, Action action)
    {
        var btn = new Button
        {
            Content = text,
            Margin = new Thickness(4, 8, 0, 8),
            Padding = new Thickness(12, 4, 12, 4),
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
            Width = 24,
            Height = 24,
            Margin = new Thickness(3, 13, 0, 13),
            Background = new SolidColorBrush(color),
            BorderThickness = new Thickness(1),
            BorderBrush = Brushes.White,
            Cursor = Cursors.Hand
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

                // Draw shapes from model
                foreach (var shape in _shapes)
                {
                    var pen = new Pen(new SolidColorBrush(shape.Color), shape.Thickness);
                    if (shape.IsArrow)
                    {
                        pen.EndLineCap = PenLineCap.Triangle;
                        pen.StartLineCap = PenLineCap.Round;
                        dc.DrawLine(pen, shape.Start, shape.End);
                    }
                    else
                    {
                        dc.DrawRectangle(null, pen, new Rect(shape.Start, shape.End));
                    }
                }

                // Draw texts from model
                foreach (var txt in _texts)
                {
                    var ft = new FormattedText(
                        txt.Text,
                        System.Globalization.CultureInfo.CurrentCulture,
                        FlowDirection.LeftToRight,
                        new Typeface("Segoe UI"),
                        txt.FontSize,
                        new SolidColorBrush(txt.Color),
                        VisualTreeHelper.GetDpi(this).PixelsPerDip);
                    dc.DrawText(ft, txt.Position);
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
            MessageBox.Show($"保存失败:\n{ex.Message}", "Clothesline", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }
}
