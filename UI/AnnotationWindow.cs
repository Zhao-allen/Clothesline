using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Ink;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Shell;
using System.Windows.Threading;
using Clothesline.Services;

// GlobalUsings.cs already aliases Color, Colors, Brushes, Point and Panel to
// their WPF types. Brush and Path are not covered, and the WindowsDesktop SDK
// pulls System.Drawing / System.IO into scope, so they need settling here.
using Brush = System.Windows.Media.Brush;
using Path = System.IO.Path;
using HAlign = System.Windows.HorizontalAlignment;
using VAlign = System.Windows.VerticalAlignment;

using WpfOrientation = System.Windows.Controls.Orientation;
using WpfPath = System.Windows.Shapes.Path;
using WpfImage = System.Windows.Controls.Image;
using WpfRectangle = System.Windows.Shapes.Rectangle;
using WpfButton = System.Windows.Controls.Button;
using WpfCursors = System.Windows.Input.Cursors;
using WpfMouseEventArgs = System.Windows.Input.MouseEventArgs;
using WpfKeyEventArgs = System.Windows.Input.KeyEventArgs;
using WpfTextBox = System.Windows.Controls.TextBox;
using WpfPen = System.Windows.Media.Pen;
using WpfMessageBox = System.Windows.MessageBox;
using WpfLine = System.Windows.Shapes.Line;
using WpfEllipse = System.Windows.Shapes.Ellipse;
using WpfScrollViewer = System.Windows.Controls.ScrollViewer;

namespace Clothesline.UI;

/// <summary>
/// Markup window with freehand, rectangle, arrow, and text tools.
/// Shapes and text are managed as data models for reliable saving.
/// Triggered only by pressing and holding a hung card (see Line.cs);
/// the screenshot capture path no longer routes through here since
/// the BitBlt pipeline was replaced with the OS Win+Shift+S trigger.
///
/// v0.2.0 redesign (see Clothesline-UI重设计稿.html): custom title bar,
/// icon tool buttons with a real active state, grouped toolbar, colour
/// swatches with a selection ring, three stroke-width presets instead of a
/// system combo box, undo/redo, zoom, and a status bar. The previous toolbar
/// was six unlabelled words with no way to tell which tool was live.
/// </summary>
internal sealed class AnnotationWindow : Window
{
    // ── Win11 dark palette, matching the tray menu ──
    private static readonly Brush BgWindow   = Frozen(Color.FromRgb(0x20, 0x20, 0x20));
    private static readonly Brush BgCard     = Frozen(Color.FromRgb(0x2B, 0x2B, 0x2B));
    private static readonly Brush BgCanvas   = Frozen(Color.FromRgb(0x14, 0x14, 0x14));
    private static readonly Brush BgHover    = Frozen(Color.FromRgb(0x3C, 0x3C, 0x3C));
    private static readonly Brush BgPressed  = Frozen(Color.FromRgb(0x45, 0x45, 0x45));
    private static readonly Brush BgElevated = Frozen(Color.FromRgb(0x34, 0x34, 0x34));
    private static readonly Brush StrokeLine = Frozen(Color.FromRgb(0x3D, 0x3D, 0x3D));
    private static readonly Brush StrokeLine2 = Frozen(Color.FromRgb(0x4A, 0x4A, 0x4A));
    private static readonly Brush FgPrimary  = Frozen(Colors.White);
    private static readonly Brush FgSecond   = Frozen(Color.FromRgb(0xC8, 0xC8, 0xC8));
    private static readonly Brush FgMuted    = Frozen(Color.FromRgb(0x8A, 0x8A, 0x8A));
    private static readonly Brush Accent     = Frozen(Color.FromRgb(0x4C, 0xC2, 0xFF));
    private static readonly Brush AccentSoft = Frozen(Color.FromArgb(0x30, 0x4C, 0xC2, 0xFF));
    private static readonly Brush AccentRing = Frozen(Color.FromArgb(0x73, 0x4C, 0xC2, 0xFF));
    private static readonly Brush AccentInk  = Frozen(Color.FromRgb(0x0B, 0x2A, 0x3D));

    private static Brush Frozen(Color c)
    {
        var b = new SolidColorBrush(c);
        b.Freeze();
        return b;
    }

    private const string UiFont = "Segoe UI Variable Display, Segoe UI";

    /// <summary>
    /// Where the last successful save landed. Equal to the original path when
    /// the edit was written straight over the file hanging on the line; a
    /// sibling "-annotated" path only when the caller chose to keep a copy.
    /// Null means the window was closed without saving.
    /// </summary>
    public string? SavedPath { get; private set; }

    private readonly string _path;
    private BitmapSource _original = null!;

    private InkCanvas _ink = new();
    private Canvas _shapeCanvas = new();
    private Grid _container = new();
    private WpfScrollViewer _scroll = new();
    private TextBlock _statusInfo = new();
    private TextBlock _zoomLabel = new();

    private enum Tool { Pen, Select, Erase, Rectangle, Arrow, Text }
    private Tool _currentTool = Tool.Pen;
    private Point _shapeStart;
    private Shape? _previewShape;
    private WpfPath? _previewHead;
    private Color _currentColor = Color.FromRgb(0xFF, 0x4D, 0x4D);
    private double _strokeWidth = 4;
    private double _zoom = 1.0;

    // The two halves of the toolbar row, kept so the window can be sized to
    // actually hold them. See BuildUi.
    private StackPanel? _toolbarLeft;
    private StackPanel? _toolbarRight;

    private readonly List<AnnotationShape> _shapes = new();
    private readonly List<AnnotationText> _texts = new();

    // ── redesign scaffolding ──
    private readonly List<ToolChip> _toolChips = new();
    private readonly List<ColorSwatch> _swatches = new();
    private readonly List<WidthDot> _widthDots = new();
    private Border? _undoButton;
    private Border? _redoButton;
    private bool _spaceDown;
    private bool _panning;
    private Point _panStart;
    private double _panH, _panV;

    // ── undo / redo ──
    private readonly List<Snapshot> _undo = new();
    private readonly List<Snapshot> _redo = new();
    private const int UndoDepth = 60;

    private sealed record Snapshot(
        List<AnnotationShape> Shapes,
        List<AnnotationText> Texts,
        StrokeCollection Strokes);

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

    /// <summary>A toolbar button that can show whether it is the live tool.</summary>
    private sealed class ToolChip
    {
        public required Border Root { get; init; }
        public required WpfPath Icon { get; init; }
        public required Func<bool> IsActive { get; init; }
        public bool Hover;
    }

    private sealed class ColorSwatch
    {
        public required Border Root { get; init; }
        public required Color Color { get; init; }
    }

    private sealed class WidthDot
    {
        public required Border Root { get; init; }
        public required Ellipse Dot { get; init; }
        public required double Width { get; init; }
    }

    // MARK: lifecycle

    public AnnotationWindow(string path)
    {
        _path = path;
        // _original is loaded async in Loaded so the window opens
        // instantly instead of blocking the UI thread on image decode
        // (4K screenshots can take 200-500ms to decode synchronously,
        // which made long-pressing a hung card feel like the app froze).

        Title = I18n.Get("AnnotateTitle") + " – Clothesline";
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.CanResize;
        Background = BgWindow;
        Width = 800;
        Height = 600;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        ShowInTaskbar = false;
        // The line window is Topmost, and a plain ShowDialog() window has no
        // owner, so the editor used to open *behind* the rope (and anything
        // else that was on top). Modal alone does not win a z-order fight
        // against a topmost window. Topmost makes the editor the highest
        // priority window on screen, which is what an editor you just asked
        // for should be.
        Topmost = true;
        UseLayoutRounding = true;
        TextOptions.SetTextFormattingMode(this, TextFormattingMode.Display);

        // Custom chrome: the default ToolWindow title bar is a light grey
        // strip that fights the dark canvas.
        WindowChrome.SetWindowChrome(this, new WindowChrome
        {
            CaptionHeight = 42,
            ResizeBorderThickness = new Thickness(6),
            GlassFrameThickness = new Thickness(0),
            CornerRadius = new CornerRadius(0),
            UseAeroCaptionButtons = false
        });

        // Loading placeholder so the user sees something immediately.
        Content = new Grid { Background = BgCanvas };

        Loaded += OnLoaded;
    }

    /// <summary>
    /// Diagnostic hook used by <see cref="UiPreview"/>: build the editor
    /// around an in-memory image, skipping the file read and the image
    /// decoders entirely. The window is then shown normally; OnLoaded sees
    /// _original is already set and does nothing.
    /// </summary>
    internal void BuildForPreview(BitmapSource image)
    {
        _original = image;
        BuildUi();
    }

    private async void OnLoaded(object? sender, RoutedEventArgs e)
    {
        if (_original is not null) return; // already built

        // Decode off the UI thread so the window stays responsive during the
        // (potentially slow) image load.
        var loaded = await Task.Run(() =>
        {
            try { return LoadImage(_path); }
            catch { return null; }
        });

        if (loaded is null)
        {
            WpfMessageBox.Show(I18n.Get("LoadFailedBody", _path), "Clothesline",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            Close();
            return;
        }

        _original = loaded;
        BuildUi();
    }

    // MARK: layout

    private void BuildUi()
    {
        // Chrome is 42px title + 58px toolbar + 34px status = 134. The old
        // +120 was 14 short, so the image always overflowed the viewport and
        // the editor opened with a scrollbar and its bottom edge cropped.
        Width = Math.Min(1200, _original.PixelWidth + 60);
        Height = Math.Min(900, _original.PixelHeight + 134);

        var root = new Grid { Background = BgWindow };
        Content = root;
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });   // title bar
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });   // toolbar
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });   // status bar

        var titleBar = BuildTitleBar();
        Grid.SetRow(titleBar, 0);
        root.Children.Add(titleBar);

        var toolbar = BuildToolbar();
        Grid.SetRow(toolbar, 1);
        root.Children.Add(toolbar);

        var viewer = BuildViewer();
        Grid.SetRow(viewer, 2);
        root.Children.Add(viewer);

        var status = BuildStatusBar();
        Grid.SetRow(status, 3);
        root.Children.Add(status);

        PreviewKeyDown += OnPreviewKeyDown;
        PreviewKeyUp += OnPreviewKeyUp;

        RefreshChips();
        RefreshSwatches();
        RefreshWidthDots();
        RefreshHistoryButtons();
        UpdateZoomLabel();

        // Two things the window cannot work out before it has been measured:
        //
        // 1. How wide it has to be to hold the toolbar. The bar is a
        //    left-packed StackPanel that never wraps, so a window sized to a
        //    small image squeezes every control on it into each other. The
        //    floor is measured from the two halves rather than hardcoded,
        //    because the Cancel/Save labels change width with the language.
        //
        // 2. What zoom actually shows the whole picture. With the chrome
        //    arithmetic above the image usually lands a few pixels past the
        //    viewport, so opening at 100% leaves a scrollbar and a cropped
        //    bottom. Fitting sidesteps the arithmetic entirely and keeps the
        //    aspect ratio, since it is Math.Min(vw/w, vh/h).
        Dispatcher.BeginInvoke(new Action(() =>
        {
            if (_toolbarLeft is null || _toolbarRight is null) return;
            double need = _toolbarLeft.DesiredSize.Width
                        + _toolbarRight.DesiredSize.Width + 20;
            if (need <= 20) return;

            MinWidth = need;
            if (Width < MinWidth) Width = MinWidth;

            // Only shrink to fit. A picture that already fits the viewport
            // stays at 100% - scaling a small screenshot up to fill the
            // window makes it blurry and is not what anyone opening an editor
            // expects. A picture that does not fit is zoomed down to the
            // largest size that does, which is the whole point of the button
            // in the status bar, just applied on open.
            //
            // The 1px slack matters: the viewport is measured in fractional
            // device pixels and rounds down, so an image sized to fit exactly
            // would otherwise read as 199 vs 200 and drop to 99%.
            bool fitsAtNative = _original.PixelWidth <= _scroll.ViewportWidth + 1
                             && _original.PixelHeight <= _scroll.ViewportHeight + 1;
            if (!fitsAtNative && _scroll.ViewportWidth > 0 && _scroll.ViewportHeight > 0)
                FitToWindow();
        }), DispatcherPriority.Loaded);
    }

    private UIElement BuildTitleBar()
    {
        var bar = new Grid
        {
            Height = 42,
            Background = BgCard
        };
        bar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        bar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        bar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        bar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        // Accent dot + file name + dimensions
        var dot = new Border
        {
            Width = 9,
            Height = 9,
            CornerRadius = new CornerRadius(2),
            Background = Accent,
            Margin = new Thickness(14, 0, 10, 0),
            VerticalAlignment = VAlign.Center
        };
        Grid.SetColumn(dot, 0);
        bar.Children.Add(dot);

        var name = new TextBlock
        {
            Text = Path.GetFileName(_path),
            Foreground = FgPrimary,
            FontSize = 12.5,
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VAlign.Center
        };
        Grid.SetColumn(name, 1);
        bar.Children.Add(name);

        var dims = new TextBlock
        {
            Text = $"{_original.PixelWidth} × {_original.PixelHeight}",
            Foreground = FgMuted,
            FontSize = 11.5,
            Margin = new Thickness(9, 0, 0, 0),
            VerticalAlignment = VAlign.Center
        };
        Grid.SetColumn(dims, 2);
        bar.Children.Add(dims);

        // Window buttons
        var buttons = new StackPanel
        {
            Orientation = WpfOrientation.Horizontal,
            HorizontalAlignment = HAlign.Right
        };
        buttons.Children.Add(ChromeButton("\uE921", () => WindowState = WindowState.Minimized));
        buttons.Children.Add(ChromeButton("\uE922", ToggleMaximize));
        buttons.Children.Add(ChromeButton("\uE8BB", Close, isClose: true));
        Grid.SetColumn(buttons, 4);
        bar.Children.Add(buttons);

        bar.MouseLeftButtonDown += (_, e) =>
        {
            if (e.ClickCount == 2) { ToggleMaximize(); return; }
            try { DragMove(); } catch { }
        };

        var wrap = new Border { Child = bar, BorderBrush = StrokeLine, BorderThickness = new Thickness(0, 0, 0, 1) };
        return wrap;
    }

    private Border ChromeButton(string glyph, Action action, bool isClose = false)
    {
        var text = new TextBlock
        {
            Text = glyph,
            FontFamily = new System.Windows.Media.FontFamily("Segoe MDL2 Assets"),
            FontSize = 10,
            Foreground = FgSecond,
            HorizontalAlignment = HAlign.Center,
            VerticalAlignment = VAlign.Center
        };
        var btn = new Border
        {
            Width = 44,
            Height = 42,
            Child = text,
            Background = Brushes.Transparent,
            Cursor = WpfCursors.Arrow
        };
        // The chrome hit test resolves the topmost element under the cursor and
        // checks IsHitTestVisibleInChrome on that element only (no walk up the
        // visual tree). If the glyph TextBlock wins the hit test it has the
        // property set to false, so the whole press is handed back to the OS as
        // HTCAPTION and becomes a caption drag. Keep the glyph out of hit
        // testing so every pixel of the button resolves to this Border.
        text.IsHitTestVisible = false;
        WindowChrome.SetIsHitTestVisibleInChrome(btn, true);
        btn.MouseEnter += (_, _) =>
        {
            btn.Background = isClose ? Frozen(Color.FromRgb(0xC4, 0x2B, 0x1C)) : BgHover;
            text.Foreground = FgPrimary;
        };
        btn.MouseLeave += (_, _) =>
        {
            btn.Background = Brushes.Transparent;
            text.Foreground = FgSecond;
        };
        // MouseLeftButtonDown is a Direct event raised along the tree as the
        // underlying Mouse.MouseDown bubbles; marking it handled marks that
        // bubbling event handled too, which stops it reaching the title bar.
        // Without this the press bubbles to BuildTitleBar's handler, whose
        // DragMove() enters a modal drag loop that consumes the matching
        // WM_LBUTTONUP, so MouseLeftButtonUp would never fire here and every
        // window button would appear dead.
        btn.MouseLeftButtonDown += (_, e) => e.Handled = true;
        btn.MouseLeftButtonUp += (_, e) => { e.Handled = true; action(); };
        return btn;
    }

    private void ToggleMaximize() =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    private UIElement BuildToolbar()
    {
        var bar = new StackPanel
        {
            Orientation = WpfOrientation.Horizontal,
            Height = 58,
            Background = BgCard
        };

        AddToolGroup(bar, new[]
        {
            new ToolChipDef(Tool.Pen,       IcoPen,   "P", "ToolPen"),
            new ToolChipDef(Tool.Rectangle, IcoRect,  "R", "ToolRectangle"),
            new ToolChipDef(Tool.Arrow,     IcoArrow, "A", "ToolArrow"),
            new ToolChipDef(Tool.Text,      IcoText,  "T", "ToolText"),
        });

        bar.Children.Add(Separator());

        AddToolGroup(bar, new[]
        {
            new ToolChipDef(Tool.Select, IcoSelect, "V", "ToolSelect"),
            new ToolChipDef(Tool.Erase,  IcoEraser, "E", "ToolEraser"),
        });

        bar.Children.Add(Separator());

        // Colours
        var swatches = new StackPanel
        {
            Orientation = WpfOrientation.Horizontal,
            VerticalAlignment = VAlign.Center,
            Margin = new Thickness(10, 0, 10, 0)
        };
        foreach (var c in new[]
                 {
                     Color.FromRgb(0xFF, 0x4D, 0x4D), Color.FromRgb(0xFF, 0x9F, 0x43),
                     Color.FromRgb(0xFF, 0xD9, 0x3D), Color.FromRgb(0x5F, 0xD9, 0x7A),
                     Color.FromRgb(0x4C, 0xC2, 0xFF), Colors.White, Color.FromRgb(0x11, 0x11, 0x11),
                 })
        {
            swatches.Children.Add(MakeSwatch(c));
        }
        swatches.Children.Add(MakeMoreColorsSwatch());
        bar.Children.Add(swatches);

        bar.Children.Add(Separator());

        // Stroke width presets. A system combo box was the previous approach
        // and it drops a light-themed popup over the dark UI.
        var widths = new StackPanel
        {
            Orientation = WpfOrientation.Horizontal,
            VerticalAlignment = VAlign.Center,
            Margin = new Thickness(10, 0, 10, 0)
        };
        widths.Children.Add(MakeWidthDot(2, "StrokeThin"));
        widths.Children.Add(MakeWidthDot(4, "StrokeMedium"));
        widths.Children.Add(MakeWidthDot(8, "StrokeThick"));
        bar.Children.Add(widths);

        bar.Children.Add(Separator());

        var history = new StackPanel
        {
            Orientation = WpfOrientation.Horizontal,
            VerticalAlignment = VAlign.Center,
            Margin = new Thickness(10, 0, 10, 0)
        };
        _undoButton = MakeIconButton(IcoUndo, "Undo", "Ctrl+Z", Undo);
        _redoButton = MakeIconButton(IcoRedo, "Redo", "Ctrl+Y", Redo);
        history.Children.Add(_undoButton);
        history.Children.Add(_redoButton);
        history.Children.Add(MakeIconButton(IcoTrash, "ClearAnnotations", null, ClearAll));
        bar.Children.Add(history);

        // Actions. A StackPanel left-packs its children, so the groups above
        // go in one, and the buttons go in a second one layered on top that
        // stretches and right-aligns them.
        var host = new Grid { Height = 58, Background = BgCard };
        host.Children.Add(bar);
        var right = new StackPanel
        {
            Orientation = WpfOrientation.Horizontal,
            HorizontalAlignment = HAlign.Right,
            VerticalAlignment = VAlign.Center,
            Margin = new Thickness(0, 0, 12, 0)
        };
        right.Children.Add(MakeTextButton(I18n.Get("Cancel"), "Esc", primary: false, Close));
        right.Children.Add(MakeTextButton(I18n.Get("Save"), "Ctrl+S", primary: true, Save));
        host.Children.Add(right);

        // Remember both halves. They are stacked in one Grid cell rather than
        // laid out side by side, so the Grid's own DesiredSize is only as wide
        // as the wider of the two - not wide enough for both. The real
        // requirement is the sum, and BuildUi measures it once laid out.
        _toolbarLeft = bar;
        _toolbarRight = right;

        var wrap = new Border { Child = host, BorderBrush = StrokeLine, BorderThickness = new Thickness(0, 0, 0, 1) };
        return wrap;
    }

    private sealed record ToolChipDef(Tool Tool, string Icon, string Shortcut, string LabelKey);

    private void AddToolGroup(Panel parent, ToolChipDef[] defs)
    {
        var group = new StackPanel
        {
            Orientation = WpfOrientation.Horizontal,
            VerticalAlignment = VAlign.Center,
            Margin = new Thickness(8, 0, 0, 0)
        };
        foreach (var def in defs)
        {
            var captured = def;
            group.Children.Add(MakeToolChip(captured.Icon, captured.Shortcut, captured.LabelKey,
                                            () => SetTool(captured.Tool),
                                            () => _currentTool == captured.Tool));
        }
        parent.Children.Add(group);
    }

    private static Border Separator() => new()
    {
        Width = 1,
        Height = 26,
        Background = StrokeLine2,
        Margin = new Thickness(8, 0, 8, 0),
        VerticalAlignment = VAlign.Center
    };

    // MARK: toolbar pieces

    private Border MakeToolChip(string iconData, string shortcut, string labelKey,
                                Action onClick, Func<bool> isActive)
    {
        var icon = new WpfPath
        {
            Data = Geometry.Parse(iconData),
            Stroke = FgSecond,
            StrokeThickness = 1.7,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            StrokeLineJoin = PenLineJoin.Round,
            Fill = Brushes.Transparent,
            Width = 17,
            Height = 17,
            Stretch = Stretch.Uniform,
            HorizontalAlignment = HAlign.Center,
            VerticalAlignment = VAlign.Center
        };

        var badge = new TextBlock
        {
            Text = shortcut,
            FontSize = 8,
            FontWeight = FontWeights.Bold,
            Foreground = FgMuted,
            HorizontalAlignment = HAlign.Right,
            VerticalAlignment = VAlign.Bottom,
            Margin = new Thickness(0, 0, 4, 1)
        };

        var content = new Grid { Width = 38, Height = 38 };
        content.Children.Add(icon);
        content.Children.Add(badge);

        var chip = new Border
        {
            Child = content,
            CornerRadius = new CornerRadius(7),
            Background = Brushes.Transparent,
            Cursor = WpfCursors.Hand,
            ToolTip = $"{I18n.Get(labelKey)} ({shortcut})"
        };

        var entry = new ToolChip { Root = chip, Icon = icon, IsActive = isActive };
        _toolChips.Add(entry);

        chip.MouseEnter += (_, _) => { entry.Hover = true; RefreshChips(); };
        chip.MouseLeave += (_, _) => { entry.Hover = false; RefreshChips(); };
        chip.MouseLeftButtonUp += (_, _) => onClick();
        return chip;
    }

    private Border MakeIconButton(string iconData, string labelKey, string? shortcut, Action onClick)
    {
        var icon = new WpfPath
        {
            Data = Geometry.Parse(iconData),
            Stroke = FgSecond,
            StrokeThickness = 1.7,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            StrokeLineJoin = PenLineJoin.Round,
            Fill = Brushes.Transparent,
            Width = 17,
            Height = 17,
            Stretch = Stretch.Uniform,
            HorizontalAlignment = HAlign.Center,
            VerticalAlignment = VAlign.Center
        };

        var chip = new Border
        {
            Child = icon,
            Width = 38,
            Height = 38,
            CornerRadius = new CornerRadius(7),
            Background = Brushes.Transparent,
            Cursor = WpfCursors.Hand,
            ToolTip = shortcut is null ? I18n.Get(labelKey) : $"{I18n.Get(labelKey)} ({shortcut})"
        };
        chip.MouseEnter += (_, _) => chip.Background = BgHover;
        chip.MouseLeave += (_, _) => chip.Background = Brushes.Transparent;
        chip.MouseLeftButtonUp += (_, _) => onClick();
        return chip;
    }

    private Border MakeTextButton(string label, string shortcut, bool primary, Action onClick)
    {
        var content = new StackPanel
        {
            Orientation = WpfOrientation.Horizontal,
            VerticalAlignment = VAlign.Center
        };
        content.Children.Add(new TextBlock
        {
            Text = label,
            FontWeight = primary ? FontWeights.Bold : FontWeights.SemiBold,
            FontSize = 13,
            VerticalAlignment = VAlign.Center
        });
        content.Children.Add(new TextBlock
        {
            Text = shortcut,
            FontSize = 10.5,
            Opacity = 0.6,
            Margin = new Thickness(6, 1, 0, 0),
            VerticalAlignment = VAlign.Center
        });

        var btn = new Border
        {
            Child = content,
            Height = 34,
            CornerRadius = new CornerRadius(7),
            Padding = new Thickness(16, 0, 16, 0),
            Margin = new Thickness(6, 0, 0, 0),
            Cursor = WpfCursors.Hand,
            Background = primary ? Accent : Brushes.Transparent,
            BorderBrush = primary ? Brushes.Transparent : StrokeLine2,
            BorderThickness = new Thickness(1)
        };
        if (btn.Child is Panel p)
            foreach (var tb in p.Children.OfType<TextBlock>())
                tb.Foreground = primary ? AccentInk : FgSecond;

        btn.MouseEnter += (_, _) =>
        {
            if (primary) btn.Opacity = 0.88;
            else btn.Background = BgHover;
        };
        btn.MouseLeave += (_, _) =>
        {
            if (primary) btn.Opacity = 1.0;
            else btn.Background = Brushes.Transparent;
        };
        btn.MouseLeftButtonUp += (_, _) => onClick();
        return btn;
    }

    private Border MakeSwatch(Color color)
    {
        var fill = new Border
        {
            Width = 22,
            Height = 22,
            CornerRadius = new CornerRadius(11),
            Background = new SolidColorBrush(color)
        };

        var root = new Border
        {
            Child = fill,
            Padding = new Thickness(2),
            CornerRadius = new CornerRadius(13),
            BorderThickness = new Thickness(2),
            BorderBrush = Brushes.Transparent,
            Cursor = WpfCursors.Hand,
            Margin = new Thickness(0, 0, 3, 0),
            ToolTip = color.ToString(CultureInfo.InvariantCulture)
        };
        _swatches.Add(new ColorSwatch { Root = root, Color = color });

        root.MouseLeftButtonUp += (_, _) =>
        {
            _currentColor = color;
            _ink.DefaultDrawingAttributes.Color = color;
            SetTool(Tool.Pen);
            RefreshSwatches();
        };
        return root;
    }

    private Border MakeMoreColorsSwatch()
    {
        var plus = new TextBlock
        {
            Text = "+",
            FontSize = 14,
            Foreground = FgSecond,
            HorizontalAlignment = HAlign.Center,
            VerticalAlignment = VAlign.Center,
            Margin = new Thickness(0, -1, 0, 0)
        };
        var root = new Border
        {
            Child = plus,
            Width = 26,
            Height = 26,
            CornerRadius = new CornerRadius(13),
            Background = BgElevated,
            Cursor = WpfCursors.Hand,
            ToolTip = I18n.Get("MoreColors")
        };
        root.MouseLeftButtonUp += (_, _) => PickCustomColor();
        return root;
    }

    private void PickCustomColor()
    {
        using var dlg = new System.Windows.Forms.ColorDialog
        {
            FullOpen = true,
            Color = System.Drawing.Color.FromArgb(_currentColor.R, _currentColor.G, _currentColor.B)
        };
        if (dlg.ShowDialog() != System.Windows.Forms.DialogResult.OK) return;
        _currentColor = Color.FromRgb(dlg.Color.R, dlg.Color.G, dlg.Color.B);
        _ink.DefaultDrawingAttributes.Color = _currentColor;
        SetTool(Tool.Pen);
        RefreshSwatches();
    }

    private Border MakeWidthDot(double width, string labelKey)
    {
        var dot = new Ellipse
        {
            Width = 5 + width * 1.4,
            Height = 5 + width * 1.4,
            Fill = FgSecond,
            HorizontalAlignment = HAlign.Center,
            VerticalAlignment = VAlign.Center
        };
        var root = new Border
        {
            Child = dot,
            Width = 30,
            Height = 38,
            CornerRadius = new CornerRadius(7),
            Background = Brushes.Transparent,
            Cursor = WpfCursors.Hand,
            ToolTip = I18n.Get(labelKey)
        };
        _widthDots.Add(new WidthDot { Root = root, Dot = dot, Width = width });

        root.MouseEnter += (_, _) => { if (_strokeWidth != width) root.Background = BgHover; };
        root.MouseLeave += (_, _) => RefreshWidthDots();
        root.MouseLeftButtonUp += (_, _) =>
        {
            _strokeWidth = width;
            _ink.DefaultDrawingAttributes.Width = width;
            _ink.DefaultDrawingAttributes.Height = width;
            RefreshWidthDots();
        };
        return root;
    }

    // MARK: viewer

    private UIElement BuildViewer()
    {
        _scroll = new WpfScrollViewer
        {
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalContentAlignment = HAlign.Center,
            VerticalContentAlignment = VAlign.Center,
            Background = BgCanvas
        };

        _container = new Grid
        {
            Width = _original.PixelWidth,
            Height = _original.PixelHeight
        };
        _scroll.Content = _container;

        var img = new WpfImage
        {
            Source = _original,
            Width = _original.PixelWidth,
            Height = _original.PixelHeight,
            Stretch = Stretch.None
        };
        _container.Children.Add(img);

        _shapeCanvas = new Canvas
        {
            Width = _original.PixelWidth,
            Height = _original.PixelHeight,
            Background = Brushes.Transparent
        };
        _container.Children.Add(_shapeCanvas);

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
        _container.Children.Add(_ink);

        _shapeCanvas.MouseLeftButtonDown += OnShapeDown;
        _shapeCanvas.MouseMove += OnShapeMove;
        _shapeCanvas.MouseLeftButtonUp += OnShapeUp;
        _ink.PreviewMouseLeftButtonDown += (_, _) =>
        {
            // Snapshot before the stroke lands so undo can take it back.
            if (_currentTool is Tool.Pen or Tool.Erase) PushUndo();
        };

        // Space-to-pan
        _scroll.PreviewMouseLeftButtonDown += (_, e) =>
        {
            if (!_spaceDown) return;
            _panning = true;
            _panStart = e.GetPosition(_scroll);
            _panH = _scroll.HorizontalOffset;
            _panV = _scroll.VerticalOffset;
            _scroll.CaptureMouse();
            e.Handled = true;
        };
        _scroll.PreviewMouseMove += (_, e) =>
        {
            if (!_panning) return;
            var p = e.GetPosition(_scroll);
            _scroll.ScrollToHorizontalOffset(_panH - (p.X - _panStart.X));
            _scroll.ScrollToVerticalOffset(_panV - (p.Y - _panStart.Y));
            e.Handled = true;
        };
        _scroll.PreviewMouseLeftButtonUp += (_, _) =>
        {
            if (!_panning) return;
            _panning = false;
            _scroll.ReleaseMouseCapture();
        };

        return _scroll;
    }

    private UIElement BuildStatusBar()
    {
        var grid = new Grid { Height = 34, Background = BgCard };

        var left = new StackPanel
        {
            Orientation = WpfOrientation.Horizontal,
            VerticalAlignment = VAlign.Center,
            Margin = new Thickness(14, 0, 0, 0)
        };
        _statusInfo.Text = DescribeFile();
        _statusInfo.Foreground = FgMuted;
        _statusInfo.FontSize = 11.5;
        _statusInfo.VerticalAlignment = VAlign.Center;
        left.Children.Add(_statusInfo);

        left.Children.Add(new Border
        {
            Width = 1,
            Height = 14,
            Background = StrokeLine2,
            Margin = new Thickness(16, 0, 16, 0),
            VerticalAlignment = VAlign.Center
        });
        left.Children.Add(new TextBlock
        {
            Text = I18n.Get("HoldSpaceToPan"),
            Foreground = FgMuted,
            FontSize = 11.5,
            VerticalAlignment = VAlign.Center
        });
        grid.Children.Add(left);

        var right = new StackPanel
        {
            Orientation = WpfOrientation.Horizontal,
            HorizontalAlignment = HAlign.Right,
            VerticalAlignment = VAlign.Center,
            Margin = new Thickness(0, 0, 12, 0)
        };
        right.Children.Add(ZoomButton(IcoMinus, "ZoomOut", () => SetZoom(_zoom / 1.25)));
        _zoomLabel.Foreground = FgSecond;
        _zoomLabel.FontSize = 11.5;
        _zoomLabel.FontWeight = FontWeights.SemiBold;
        _zoomLabel.Width = 46;
        _zoomLabel.TextAlignment = TextAlignment.Center;
        _zoomLabel.VerticalAlignment = VAlign.Center;
        right.Children.Add(_zoomLabel);
        right.Children.Add(ZoomButton(IcoPlus, "ZoomIn", () => SetZoom(_zoom * 1.25)));
        right.Children.Add(ZoomButton(null, "FitToWindow", FitToWindow, text: I18n.Get("FitToWindow")));
        grid.Children.Add(right);

        return new Border { Child = grid, BorderBrush = StrokeLine, BorderThickness = new Thickness(0, 1, 0, 0) };
    }

    private Border ZoomButton(string? iconData, string labelKey, Action onClick, string? text = null)
    {
        UIElement content;
        if (text is not null)
        {
            content = new TextBlock
            {
                Text = text,
                FontSize = 11.5,
                Foreground = FgSecond,
                VerticalAlignment = VAlign.Center
            };
        }
        else
        {
            content = new WpfPath
            {
                Data = Geometry.Parse(iconData!),
                Stroke = FgSecond,
                StrokeThickness = 1.9,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round,
                Width = 12,
                Height = 12,
                Stretch = Stretch.Uniform,
                HorizontalAlignment = HAlign.Center,
                VerticalAlignment = VAlign.Center
            };
        }

        var btn = new Border
        {
            Child = content,
            Height = 22,
            MinWidth = 24,
            CornerRadius = new CornerRadius(5),
            Padding = text is null ? new Thickness(0) : new Thickness(8, 0, 8, 0),
            Background = Brushes.Transparent,
            Cursor = WpfCursors.Hand,
            ToolTip = I18n.Get(labelKey)
        };
        btn.MouseEnter += (_, _) => btn.Background = BgHover;
        btn.MouseLeave += (_, _) => btn.Background = Brushes.Transparent;
        btn.MouseLeftButtonUp += (_, _) => onClick();
        return btn;
    }

    private string DescribeFile()
    {
        try
        {
            var info = new FileInfo(_path);
            var kb = Math.Max(1, info.Length / 1024);
            var ext = Path.GetExtension(_path).TrimStart('.').ToUpperInvariant();
            return $"{_original.PixelWidth} × {_original.PixelHeight} · {ext} · {kb} KB";
        }
        catch
        {
            return $"{_original.PixelWidth} × {_original.PixelHeight}";
        }
    }

    // MARK: zoom

    private void SetZoom(double zoom)
    {
        _zoom = Math.Clamp(zoom, 0.1, 8.0);
        _container.LayoutTransform = new ScaleTransform(_zoom, _zoom);
        UpdateZoomLabel();
    }

    private void FitToWindow()
    {
        double vw = _scroll.ViewportWidth, vh = _scroll.ViewportHeight;
        if (vw <= 0 || vh <= 0) return;
        double fit = Math.Min(vw / _original.PixelWidth, vh / _original.PixelHeight);
        SetZoom(fit);
    }

    private void UpdateZoomLabel() =>
        _zoomLabel.Text = (_zoom * 100).ToString("0", CultureInfo.CurrentCulture) + "%";

    // MARK: tool state

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
        RefreshChips();
    }

    private void RefreshChips()
    {
        foreach (var chip in _toolChips)
        {
            bool active = chip.IsActive();
            chip.Root.Background = active ? AccentSoft
                                 : chip.Hover ? BgHover
                                 : Brushes.Transparent;
            chip.Root.BorderThickness = new Thickness(active ? 1 : 0);
            chip.Root.BorderBrush = active ? AccentRing : Brushes.Transparent;
            chip.Icon.Stroke = active ? Accent : FgSecond;
        }
    }

    private void RefreshSwatches()
    {
        foreach (var s in _swatches)
            s.Root.BorderBrush = s.Color == _currentColor ? Accent : Brushes.Transparent;
    }

    private void RefreshWidthDots()
    {
        foreach (var d in _widthDots)
        {
            bool on = Math.Abs(d.Width - _strokeWidth) < 0.01;
            d.Root.Background = on ? AccentSoft : Brushes.Transparent;
            d.Dot.Fill = on ? Accent : FgSecond;
        }
    }

    private void RefreshHistoryButtons()
    {
        if (_undoButton is not null) _undoButton.Opacity = _undo.Count > 0 ? 1.0 : 0.34;
        if (_redoButton is not null) _redoButton.Opacity = _redo.Count > 0 ? 1.0 : 0.34;
    }

    // MARK: undo / redo

    private Snapshot Capture() => new(
        new List<AnnotationShape>(_shapes),
        new List<AnnotationText>(_texts),
        _ink.Strokes.Clone());

    private void PushUndo()
    {
        _undo.Add(Capture());
        if (_undo.Count > UndoDepth) _undo.RemoveAt(0);
        _redo.Clear();
        RefreshHistoryButtons();
    }

    private void Undo()
    {
        if (_undo.Count == 0) return;
        _redo.Add(Capture());
        Restore(_undo[^1]);
        _undo.RemoveAt(_undo.Count - 1);
        RefreshHistoryButtons();
    }

    private void Redo()
    {
        if (_redo.Count == 0) return;
        _undo.Add(Capture());
        Restore(_redo[^1]);
        _redo.RemoveAt(_redo.Count - 1);
        RefreshHistoryButtons();
    }

    private void Restore(Snapshot snapshot)
    {
        _shapes.Clear();
        _shapes.AddRange(snapshot.Shapes);
        _texts.Clear();
        _texts.AddRange(snapshot.Texts);
        _ink.Strokes = snapshot.Strokes.Clone();
        RedrawShapes();
    }

    // MARK: shape / text input

    private void OnShapeDown(object sender, MouseButtonEventArgs e)
    {
        if (_currentTool is not (Tool.Rectangle or Tool.Arrow or Tool.Text)) return;
        _shapeStart = e.GetPosition(_shapeCanvas);

        if (_currentTool == Tool.Text)
        {
            // For Text we must NOT call CaptureMouse: the canvas would
            // swallow the next click on a toolbar button (e.g. switching
            // to Rectangle, or Save/Cancel), making it impossible to
            // leave the text tool. The TextBox commits itself via
            // LostFocus when the user clicks anywhere else.
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
            // Cancel text edit on Escape so the user is never stuck.
            tb.KeyDown += (_, ev) =>
            {
                if (ev.Key == Key.Escape) _shapeCanvas.Children.Remove(tb);
            };
            e.Handled = true;
            return;
        }

        _shapeCanvas.CaptureMouse();

        var previewBrush = new SolidColorBrush(_currentColor);
        if (_currentTool == Tool.Rectangle)
        {
            _previewShape = new WpfRectangle
            {
                Stroke = previewBrush,
                StrokeThickness = _strokeWidth,
                Fill = Brushes.Transparent
            };
            _shapeCanvas.Children.Add(_previewShape);
        }
        else
        {
            // Arrow preview = shaft + filled head, so the drag shows the real
            // arrow rather than a bare line. The head is a separate Path so the
            // shaft can stop at the head's base instead of poking through it.
            _previewShape = new WpfLine
            {
                Stroke = previewBrush,
                StrokeThickness = _strokeWidth,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Flat,
                IsHitTestVisible = false
            };
            _previewHead = new WpfPath { Fill = previewBrush, IsHitTestVisible = false };
            _shapeCanvas.Children.Add(_previewShape);
            _shapeCanvas.Children.Add(_previewHead);
        }
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
        else if (_previewShape is WpfLine line)
        {
            var head = BuildArrowhead(_shapeStart, pos, _strokeWidth, out var shaftEnd);
            line.X1 = _shapeStart.X;
            line.Y1 = _shapeStart.Y;
            line.X2 = shaftEnd.X;
            line.Y2 = shaftEnd.Y;
            if (_previewHead is not null) _previewHead.Data = head;
        }
    }

    private void OnShapeUp(object sender, MouseButtonEventArgs e)
    {
        if (_previewShape == null) return;
        _shapeCanvas.ReleaseMouseCapture();

        var pos = e.GetPosition(_shapeCanvas);
        bool isArrow = _previewShape is WpfLine;
        bool added = false;

        if (isArrow)
        {
            if (Math.Abs(pos.X - _shapeStart.X) > 4 || Math.Abs(pos.Y - _shapeStart.Y) > 4)
            {
                PushUndo();
                _shapes.Add(new AnnotationShape
                {
                    IsArrow = true,
                    Start = _shapeStart,
                    End = pos,
                    Color = _currentColor,
                    Thickness = _strokeWidth
                });
                added = true;
            }
        }
        else if (_previewShape is WpfRectangle)
        {
            var w = Math.Abs(pos.X - _shapeStart.X);
            var h = Math.Abs(pos.Y - _shapeStart.Y);
            if (w > 4 && h > 4)
            {
                PushUndo();
                _shapes.Add(new AnnotationShape
                {
                    IsArrow = false,
                    Start = new Point(Math.Min(_shapeStart.X, pos.X), Math.Min(_shapeStart.Y, pos.Y)),
                    End = new Point(Math.Max(_shapeStart.X, pos.X), Math.Max(_shapeStart.Y, pos.Y)),
                    Color = _currentColor,
                    Thickness = _strokeWidth
                });
                added = true;
            }
        }

        _shapeCanvas.Children.Remove(_previewShape);
        if (_previewHead is not null)
        {
            _shapeCanvas.Children.Remove(_previewHead);
            _previewHead = null;
        }
        _previewShape = null;
        if (added) RedrawShapes();
    }

    private void CommitWpfTextBox(WpfTextBox tb)
    {
        var text = tb.Text?.Trim();
        if (!string.IsNullOrEmpty(text))
        {
            PushUndo();
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
                // A real arrow: shaft plus a filled triangular head whose size
                // tracks the stroke width. The previous code only set
                // PenLineCap.Triangle, which draws a nub half the stroke width
                // long at the very tip and reads as a plain line.
                var brush = new SolidColorBrush(shape.Color);
                var head = BuildArrowhead(shape.Start, shape.End, shape.Thickness, out var shaftEnd);
                _shapeCanvas.Children.Add(new WpfLine
                {
                    X1 = shape.Start.X, Y1 = shape.Start.Y,
                    X2 = shaftEnd.X, Y2 = shaftEnd.Y,
                    Stroke = brush,
                    StrokeThickness = shape.Thickness,
                    StrokeStartLineCap = PenLineCap.Round,
                    StrokeEndLineCap = PenLineCap.Flat,
                    IsHitTestVisible = false
                });
                if (head is not null)
                    _shapeCanvas.Children.Add(new WpfPath
                    {
                        Data = head,
                        Fill = brush,
                        IsHitTestVisible = false
                    });
            }
            else
            {
                var rect = new WpfRectangle
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

    /// <summary>
    /// Build the filled arrowhead for a Start→End arrow. The head length grows
    /// with the stroke so thin arrows get a proportionate tip, but it is capped
    /// at 60% of the shaft so a short arrow still reads as an arrow instead of
    /// a blob. <paramref name="shaftEnd"/> is pulled back to the base of the
    /// head so the shaft never pokes through the tip. Returns null (leaving
    /// shaftEnd at end) for a degenerate vector, so no NaN geometry reaches WPF.
    /// </summary>
    private static StreamGeometry? BuildArrowhead(Point start, Point end, double thickness, out Point shaftEnd)
    {
        shaftEnd = end;
        double dx = end.X - start.X;
        double dy = end.Y - start.Y;
        double len = Math.Sqrt(dx * dx + dy * dy);
        if (len < 0.5) return null;

        double headLen = Math.Min(6 + thickness * 2.5, len * 0.6);
        double halfWidth = headLen * 0.45;

        double ux = dx / len, uy = dy / len;   // unit direction Start→End
        double px = -uy, py = ux;              // unit perpendicular

        shaftEnd = new Point(end.X - ux * headLen, end.Y - uy * headLen);
        var left  = new Point(shaftEnd.X + px * halfWidth, shaftEnd.Y + py * halfWidth);
        var right = new Point(shaftEnd.X - px * halfWidth, shaftEnd.Y - py * halfWidth);

        var geo = new StreamGeometry();
        using (var ctx = geo.Open())
        {
            ctx.BeginFigure(end, isFilled: true, isClosed: true);
            ctx.LineTo(left, isStroked: false, isSmoothJoin: false);
            ctx.LineTo(right, isStroked: false, isSmoothJoin: false);
        }
        geo.Freeze();
        return geo;
    }

    private void ClearAll()
    {
        if (_shapes.Count == 0 && _texts.Count == 0 && _ink.Strokes.Count == 0) return;
        PushUndo();
        _shapes.Clear();
        _texts.Clear();
        _ink.Strokes.Clear();
        _shapeCanvas.Children.Clear();
    }

    // MARK: keyboard

    private void OnPreviewKeyDown(object sender, WpfKeyEventArgs e)
    {
        // Never swallow keys aimed at a text box being edited.
        if (Keyboard.FocusedElement is WpfTextBox) return;

        bool ctrl = (Keyboard.Modifiers & ModifierKeys.Control) != 0;

        switch (e.Key)
        {
            case Key.Escape:
                Close();
                e.Handled = true;
                return;
            case Key.Space:
                if (!_spaceDown)
                {
                    _spaceDown = true;
                    _scroll.Cursor = WpfCursors.Hand;
                }
                e.Handled = true;
                return;
        }

        if (ctrl)
        {
            switch (e.Key)
            {
                case Key.S: Save(); e.Handled = true; return;
                case Key.Z: Undo(); e.Handled = true; return;
                case Key.Y: Redo(); e.Handled = true; return;
                case Key.D0: SetZoom(1.0); e.Handled = true; return;
            }
        }

        switch (e.Key)
        {
            case Key.P: SetTool(Tool.Pen); e.Handled = true; return;
            case Key.R: SetTool(Tool.Rectangle); e.Handled = true; return;
            case Key.A: SetTool(Tool.Arrow); e.Handled = true; return;
            case Key.T: SetTool(Tool.Text); e.Handled = true; return;
            case Key.V: SetTool(Tool.Select); e.Handled = true; return;
            case Key.E: SetTool(Tool.Erase); e.Handled = true; return;
        }
    }

    private void OnPreviewKeyUp(object sender, WpfKeyEventArgs e)
    {
        if (e.Key != Key.Space) return;
        _spaceDown = false;
        _panning = false;
        _scroll.Cursor = null;
    }

    // MARK: save

    private static BitmapSource LoadImage(string path)
    {
        // Read through a stream we own and close it again, so the original is
        // not held open while the editor is up. Decoding a Uri would also go
        // through WPF's decoder cache and could hand back a stale frame.
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete);
        var decoder = BitmapDecoder.Create(stream,
            BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
        var frame = decoder.Frames[0];
        frame.Freeze();
        return frame;
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
                    var brush = new SolidColorBrush(shape.Color);
                    if (shape.IsArrow)
                    {
                        // Mirror RedrawShapes so the exported PNG matches what
                        // the canvas showed: shaft trimmed to the head's base,
                        // then the filled head itself.
                        var head = BuildArrowhead(shape.Start, shape.End, shape.Thickness, out var shaftEnd);
                        var pen = new WpfPen(brush, shape.Thickness)
                        {
                            StartLineCap = PenLineCap.Round,
                            EndLineCap = PenLineCap.Flat
                        };
                        dc.DrawLine(pen, shape.Start, shaftEnd);
                        if (head is not null) dc.DrawGeometry(brush, null, head);
                    }
                    else
                    {
                        dc.DrawRectangle(null, new WpfPen(brush, shape.Thickness), new Rect(shape.Start, shape.End));
                    }
                }

                // Draw texts from model
                foreach (var txt in _texts)
                {
                    var ft = new FormattedText(
                        txt.Text,
                        System.Globalization.CultureInfo.CurrentCulture,
                        System.Windows.FlowDirection.LeftToRight,
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

            // Render PNG into memory first so we only touch the disk for
            // the final write - no half-written files even on exception.
            byte[] pngBytes;
            using (var ms = new MemoryStream())
            {
                encoder.Save(ms);
                pngBytes = ms.ToArray();
            }

            // Overwriting the original is the expected behaviour: the card on
            // the line is a view of this exact file, so a separate
            // "<name>-annotated.png" would leave the line still showing the
            // pre-edit picture. The retry loop inside TryWriteAtomic absorbs
            // the momentary locks (Explorer preview pane, indexer, AV scan).
            Diag.Write("save", $"saving {pngBytes.Length / 1024} KB over {Path.GetFileName(_path)}");
            string? savedPath = TryWriteAtomic(_path, pngBytes);

            if (savedPath is null)
            {
                // Something outside Clothesline is genuinely holding the file
                // open. Do not silently scatter copies around the inbox - ask.
                Diag.Write("save", "original still locked after 5 attempts - asking the user");
                var answer = WpfMessageBox.Show(
                    I18n.Get("SaveBlockedBody", _path),
                    I18n.Get("SaveBlockedTitle"),
                    MessageBoxButton.YesNo, MessageBoxImage.Question);

                if (answer != MessageBoxResult.Yes)
                {
                    Diag.Write("save", "user declined the copy - nothing written");
                    return;
                }

                savedPath = TryWriteAtomic(GetAnnotatedSiblingPath(_path), pngBytes);
                if (savedPath is null)
                {
                    Diag.Write("save", "sibling fallback ALSO failed - giving up");
                    WpfMessageBox.Show(I18n.Get("SaveFailedBody", _path),
                        I18n.Get("SaveFailedTitle"),
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                Diag.Write("save", $"wrote SIBLING (line will not refresh): {Path.GetFileName(savedPath)}");
            }
            else
            {
                Diag.Write("save", $"overwrote original: {Path.GetFileName(savedPath)}");
            }

            // Tell the caller where it landed. Line.Markup() uses this to decide
            // between refreshing the existing card in place (overwrite) and
            // hanging a second one (explicit copy).
            SavedPath = savedPath;
            Close();
        }
        catch (Exception ex)
        {
            Diag.Fatal("save", ex);
            WpfMessageBox.Show(I18n.Get("SaveErrorBody", ex.Message), I18n.Get("SaveFailedTitle"),
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    /// <summary>
    /// Write bytes to the target path. Returns the path on success, or
    /// null if writing failed (file locked, permission denied, etc).
    /// Strategy: write to a sibling .tmp file, then atomically replace
    /// the target. This avoids truncating the original before we know
    /// the new bytes are good, and sidesteps most file-lock scenarios.
    /// </summary>
    private static string? TryWriteAtomic(string path, byte[] bytes)
    {
        string tmp = path + ".tmp";

        // Explorer's preview pane, the thumbnail cache, an indexer or a virus
        // scanner can hold the original for a moment. Retry briefly before
        // falling back to a sibling file, otherwise a save that would have
        // worked 50ms later silently produces "<name>-annotated.png" instead.
        for (int attempt = 0; attempt < 5; attempt++)
        {
            try
            {
                var dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);

                // Write+flush the temp file fully.
                using (var fs = new FileStream(tmp,
                    FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    fs.Write(bytes, 0, bytes.Length);
                    fs.Flush(true);
                }

                // Atomic replace. .NET 9's File.Move has an overload with
                // overwrite=true that does an atomic rename on Windows.
                if (File.Exists(path))
                    File.Move(tmp, path, overwrite: true);
                else
                    File.Move(tmp, path);
                return path;
            }
            catch (Exception ex)
            {
                // This catch used to be empty, which is why a save that fell
                // back to "<name>-annotated.png" gave no clue as to who was
                // holding the file. Name the blocker and the attempt number.
                Diag.Write("save", $"attempt {attempt + 1}/5 on {Path.GetFileName(path)} failed: " +
                                   $"{ex.GetType().Name}: {ex.Message}");
                // Clean up the orphan temp file if we left one.
                try { if (File.Exists(tmp)) File.Delete(tmp); } catch { }
                if (attempt < 4) System.Threading.Thread.Sleep(70 * (attempt + 1));
            }
        }
        return null;
    }

    /// <summary>
    /// Build a sibling path like "Foo.png" -> "Foo-annotated.png" so
    /// the user can save their edited version even when the original
    /// file is locked by Explorer's preview pane.
    /// </summary>
    private static string GetAnnotatedSiblingPath(string path)
    {
        var dir = Path.GetDirectoryName(path) ?? "";
        var name = Path.GetFileNameWithoutExtension(path);
        var ext = Path.GetExtension(path);
        return Path.Combine(dir, $"{name}-annotated{ext}");
    }

    // MARK: icon geometry (24x24, stroked)

    private const string IcoPen    = "M16.5 3.5a2.1 2.1 0 0 1 3 3L7 19l-4 1 1-4Z M14.5 5.5 18.5 9.5";
    private const string IcoRect   = "M3.5 5.5h17v13h-17z";
    private const string IcoArrow  = "M4 20 19 5 M12 5h7v7";
    private const string IcoText   = "M5 6.5V4.5h14v2 M12 4.5v15 M8.5 19.5h7";
    private const string IcoSelect = "M5 3l14 8.5-6 1.5-2.5 6z";
    private const string IcoEraser = "M8 20h11 M15.5 4.5 19.5 8.5a1.6 1.6 0 0 1 0 2.3L11 19.5H6.5l-2.4-2.4a1.6 1.6 0 0 1 0-2.3l9.1-10.3a1.6 1.6 0 0 1 2.3 0Z";
    private const string IcoUndo   = "M4 9h10a5.5 5.5 0 0 1 0 11h-4 M4 9l4-4M4 9l4 4";
    private const string IcoRedo   = "M20 9H10a5.5 5.5 0 0 0 0 11h4 M20 9l-4-4M20 9l-4 4";
    private const string IcoTrash  = "M4 7h16 M9 7V4.5h6V7 M6 7l1 12.5h10L18 7 M10 11v5M14 11v5";
    private const string IcoMinus  = "M5 12h14";
    private const string IcoPlus   = "M12 5v14M5 12h14";
}
