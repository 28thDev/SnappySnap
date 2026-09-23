using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;
using SnappySnap.Core;
using SnappySnap.Presentation;
using SnappySnap.Localization;
using System.Windows.Controls.Primitives;

namespace SnappySnap.Editor;

public sealed class EditorWindow : Window
{
    private readonly ComboBox _format;
    public string SaveFormat => (string)_format.SelectedItem;
    private readonly IAppLogger _logger;
    private readonly EditorCommandHistory _history = new();
    private readonly EditorDocument _document;
    private readonly EditorSurface _surface;
    private readonly TextBlock _status;
    private readonly StackPanel _saveRecovery;
    private readonly Canvas _canvas;
    private readonly Canvas _viewport;
    private readonly TextBlock _dimensions;
    private Rect _visibleBounds;
    private EditorTool _tool = EditorTool.Arrow;
    public EditorTool CurrentTool => _tool;
    private Point _dragStart;
    private Point _lastPoint;
    private EditorElement? _selected;
    private ElementGeometry? _dragGeometry;
    private ResizeHandle _resizeHandle;
    private readonly List<Point> _freehandPoints = new();
    private System.Windows.Shapes.Shape? _gesturePreview;

    private enum ResizeHandle
    {
        None,
        TopLeft,
        TopRight,
        BottomLeft,
        BottomRight,
        ArrowStart,
        ArrowMiddle,
        ArrowEnd
    }

    public EditorWindow(CapturedImage image, IAppLogger logger, string defaultFormat = "Png", EditorSettings? styles = null)
    {
        _logger = logger;
        _styles = new EditorToolStyles(styles);
        _document = new EditorDocument(image);
        Title = "SnappySnap — Screenshot Editor";
        Width = 1260; Height = 820; MinWidth = 900; MinHeight = 560;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        AllowDrop = true; KeyDown += OnKeyDown; Drop += OnDrop;
        CommandBindings.Add(new CommandBinding(ApplicationCommands.Copy,
            async (_, e) => { e.Handled = true; await CopyAsync(); },
            (_, e) => { e.CanExecute = !IsSaving; e.Handled = true; }));
        PreviewKeyDown += OnEditorPreviewKeyDown;
        PreviewMouseDown += OnEditorPreviewMouseDown;
        _status = Ui.Text("Arrow · Drag to draw · Select to bend or move · Enter to save", 11, "Muted");
        var actions = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
        _undo = IconButton("Undo (Ctrl+Z)", "\uE7A7", (_, _) => { FinishTextEdit(true); _history.Undo(_document); Refresh(); });
        _redo = IconButton("Redo (Ctrl+Y)", "\uE7A6", (_, _) => { FinishTextEdit(true); _history.Redo(_document); Refresh(); }); actions.Children.Add(_undo); actions.Children.Add(_redo);
        _format = new ComboBox { ItemsSource = new[] { "Png", "Jpg" }, SelectedItem = defaultFormat.Equals("Jpg", StringComparison.OrdinalIgnoreCase) ? "Jpg" : "Png", Width = 85, Margin = new Thickness(8, 0, 8, 0) };
        Ui.Localize(_format, AutomationProperties.NameProperty, "New copy format"); Ui.Localize(_format, FrameworkElement.ToolTipProperty, "Format for new copies; Save keeps the current file format");
        var saveOptions = new StackPanel { Margin = new Thickness(8) };
        saveOptions.Children.Add(_format);
        var saveAs = Ui.Button("Save to file…", "\uE792", (_, _) => SaveAs()); Ui.Localize(saveAs, FrameworkElement.ToolTipProperty, "Choose a file name and location"); saveOptions.Children.Add(saveAs);
        var savePopup = new Popup { Child = Ui.Card(saveOptions, 4), Placement = PlacementMode.Bottom, StaysOpen = false, AllowsTransparency = true };
        var save = Ui.Button("Save", "\uE74E", (_, _) => Commit(), "PrimaryButton"); save.Height = 36; Ui.Localize(save, FrameworkElement.ToolTipProperty, "Replace this screenshot and close (Ctrl+S / Enter)");
        var saveNew = Ui.Button("Save as new", "\uE792", (_, _) => SaveNew()); saveNew.Height = 36; Ui.Localize(saveNew, FrameworkElement.ToolTipProperty, "Save a separate copy to Shelf and close (Ctrl+Shift+S)");
        var copy = Ui.Button("Copy", "\uE8C8", async (_, _) => await CopyAsync()); copy.Height = 36; Ui.Localize(copy, FrameworkElement.ToolTipProperty, "Copy the current image without saving or closing (Ctrl+C)");
        var options = IconButton("Save options", "\uE70D", (_, _) => savePopup.IsOpen = !savePopup.IsOpen); savePopup.PlacementTarget = options;
        var tools = new WrapPanel { VerticalAlignment = VerticalAlignment.Center };
        var toolCaptions = new List<TextBlock>();
        var toolStyle = new Style(typeof(ToggleButton), (Style)FindResource(typeof(ToggleButton)));
        var pressed = new Trigger { Property = ButtonBase.IsPressedProperty, Value = true };
        pressed.Setters.Add(new Setter(OpacityProperty, .7)); toolStyle.Triggers.Add(pressed);
        foreach (var (label, glyph, tool) in new[] {
            ("Select", "\uE8B0", EditorTool.Select), ("Arrow", "\uE7AA", EditorTool.Arrow), ("Rectangle", "\uE739", EditorTool.Rectangle),
            ("Line", "\uE738", EditorTool.Line), ("Freehand", "\uEDC6", EditorTool.Freehand), ("Text", "\uE8D2", EditorTool.Text),
            ("Highlight", "\uE7E6", EditorTool.Highlight), ("Blur", "\uE81E", EditorTool.Blur), ("Pixelate", "\uE80A", EditorTool.Pixelate),
            ("Step marker", "\uE8D4", EditorTool.StepMarker), ("Crop", "\uE7A8", EditorTool.Crop), ("Insert image", "\uEB9F", EditorTool.Image) })
        {
            var labelPanel = new StackPanel();
            FrameworkElement icon;
            if (tool == EditorTool.Arrow)
            {
                var arrowIcon = new System.Windows.Shapes.Path { Data = Geometry.Parse("M2,14 L14,2 M6,2 L14,2 L14,10"), Width = 16, Height = 18, StrokeThickness = 1.4, Stretch = Stretch.Uniform };
                arrowIcon.SetBinding(System.Windows.Shapes.Shape.StrokeProperty, new System.Windows.Data.Binding("Foreground") { RelativeSource = new System.Windows.Data.RelativeSource(System.Windows.Data.RelativeSourceMode.FindAncestor, typeof(ToggleButton), 1) });
                icon = arrowIcon;
            }
            else { icon = Ui.Icon(glyph, 16); icon.Height = 18; }
            icon.HorizontalAlignment = HorizontalAlignment.Center; labelPanel.Children.Add(icon);
            var toolCaption = Ui.Text(tool == EditorTool.StepMarker ? "Step" : tool == EditorTool.Image ? "Image" : label, 10); toolCaption.Margin = new Thickness(0, 3, 0, 0); toolCaption.HorizontalAlignment = HorizontalAlignment.Center; toolCaption.TextWrapping = TextWrapping.NoWrap;
            toolCaptions.Add(toolCaption); labelPanel.Children.Add(toolCaption);
            var button = new ToggleButton { Content = labelPanel, MinWidth = 43, Height = 46, Padding = new Thickness(5, 4, 5, 4), Margin = new Thickness(1, 0, 1, 0), IsChecked = tool == _tool, ToolTip = label };
            button.Style = toolStyle;
            Ui.Localize(button, AutomationProperties.NameProperty, label); Ui.Localize(button, ToolTipProperty, label); _toolButtons.Add(tool, button);
            button.Click += (_, _) => { FinishTextEdit(true); if (tool == EditorTool.Image) { button.IsChecked = false; OpenImage(); return; } if (tool != EditorTool.Select && _selected is not null) { _selected.IsSelected = false; _selected = null; } _tool = tool; _status.Text = tool == EditorTool.Select ? L.T("Select · Drag body to move · Drag handles to reshape") : L.F("{0} · Drag on the image", L.T(label)); Refresh(); }; tools.Children.Add(button);
        }
        SizeChanged += (_, _) =>
        {
            var compactTools = ActualWidth < 1040;
            foreach (var caption in toolCaptions) caption.Visibility = compactTools ? Visibility.Collapsed : Visibility.Visible;
            foreach (var button in _toolButtons.Values) { button.MinWidth = compactTools ? 36 : 43; button.Height = compactTools ? 38 : 46; }
        };
        var center = new DockPanel();
        var bottom = new DockPanel { Margin = new Thickness(12, 4, 12, 4) };
        var zoomBar = new StackPanel { Orientation = Orientation.Horizontal }; DockPanel.SetDock(zoomBar, Dock.Right);
        zoomBar.Children.Add(IconButton("Zoom out", "\uE71F", (_, _) => SetZoom(_zoom / 1.25)));
        _zoomText = Ui.Text("100%", 12); _zoomText.Width = 42; zoomBar.Children.Add(_zoomText);
        zoomBar.Children.Add(IconButton("Zoom in", "\uE8A3", (_, _) => SetZoom(_zoom * 1.25)));
        zoomBar.Children.Add(IconButton("Fit to window (Ctrl+0)", "\uE9A6", (_, _) => Fit())); actions.Children.Add(zoomBar); actions.Children.Add(copy); actions.Children.Add(save); actions.Children.Add(saveNew); actions.Children.Add(options);
        _saveRecovery = new StackPanel { Orientation = Orientation.Horizontal, Visibility = Visibility.Collapsed };
        _saveRecovery.Children.Add(Ui.Button("Retry", "", async (_, _) => await SaveAndCloseAsync(_lastSaveRequest ?? new(SaveFormat))));
        _saveRecovery.Children.Add(Ui.Button("Save to file…", "", (_, _) => SaveAs()));
        DockPanel.SetDock(_saveRecovery, Dock.Right); bottom.Children.Add(_saveRecovery);
        _status.TextWrapping = TextWrapping.Wrap;
        AutomationProperties.SetLiveSetting(_status, AutomationLiveSetting.Polite);
        _dimensions = Ui.Text($"{image.Width} × {image.Height} px", 11, "Muted"); DockPanel.SetDock(_dimensions, Dock.Right); bottom.Children.Add(_dimensions); bottom.Children.Add(_status); DockPanel.SetDock(bottom, Dock.Bottom); center.Children.Add(bottom);
        _scroll = new ScrollViewer { HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Background = Ui.Brush("Background"), Padding = new Thickness(8), HorizontalContentAlignment = HorizontalAlignment.Center, VerticalContentAlignment = VerticalAlignment.Center };
        _canvas = new Canvas { Background = Brushes.Transparent, Width = image.Width, Height = image.Height };
        _surface = new EditorSurface(_document) { Width = image.Width, Height = image.Height }; _canvas.Children.Add(_surface);
        _visibleBounds = _document.VisibleBounds;
        _viewport = new Canvas { Width = image.Width, Height = image.Height, ClipToBounds = true };
        _viewport.Children.Add(_canvas); _scroll.Content = _viewport;
        var properties = new WrapPanel { Margin = new Thickness(8, 3, 8, 3), VerticalAlignment = VerticalAlignment.Center };
        _selectionLabel = Ui.Text("New annotation", 12, "Muted"); properties.Children.Add(_selectionLabel);
        _strokePalette = AddPalette(properties, "Stroke color", false); _fillPalette = AddPalette(properties, "Fill color", true);
        _stroke = PropertySlider(properties, "Stroke width", 1, EditorSettings.MaximumStrokeWidth, EditorSettings.DefaultStrokeWidth, value => ApplyStyle(style => style with { StrokeWidth = value }));
        _font = PropertySlider(properties, "Text size", 12, 48, 20, value => ApplyStyle(style => style with { FontSize = value }));
        _stepSize = PropertySlider(properties, "Step size", EditorSettings.MinimumStepDiameter, EditorSettings.MaximumStepDiameter,
            EditorSettings.DefaultStepDiameter, value => ApplyStyle(style => style with { StepDiameter = value }));
        _opacity = PropertySlider(properties, "Opacity", 10, 100, 100, value => ApplyStyle(style => style with { Opacity = value / 100 }));
        _delete = IconButton("Delete selection (Delete)", "\uE74D", (_, _) => DeleteSelection()); properties.Children.Add(_delete);
        _properties = new Border { Background = Ui.Brush("Surface"), Child = properties, Visibility = Visibility.Collapsed };
        _properties.VerticalAlignment = VerticalAlignment.Top; _properties.HorizontalAlignment = HorizontalAlignment.Center;
        _properties.Margin = new Thickness(8); _properties.CornerRadius = new CornerRadius(6);
        var canvasHost = new Grid(); canvasHost.Children.Add(_scroll); canvasHost.Children.Add(_properties); center.Children.Add(canvasHost);
        var toolbar = new StackPanel { Margin = new Thickness(8, 4, 8, 4) }; toolbar.Children.Add(tools); toolbar.Children.Add(actions);
        var workspace = new DockPanel(); DockPanel.SetDock(toolbar, Dock.Top); workspace.Children.Add(toolbar); workspace.Children.Add(center);
        Ui.Shell(this, "", workspace, compact: true);
        _surface.MouseLeftButtonDown += OnMouseDown; _surface.MouseMove += OnMouseMove; _surface.MouseLeftButtonUp += OnMouseUp;
        L.Current.PropertyChanged += OnLanguageChanged; Closed += (_, _) => L.Current.PropertyChanged -= OnLanguageChanged;
        Loaded += (_, _) => { _selected = _document.Elements.FirstOrDefault(x => x.IsSelected); Fit(); Refresh(); };
        Closing += (_, e) => { if (IsSaving) { e.Cancel = true; return; } FinishTextEdit(true); if (!_committing && (_history.CanUndo || _initialSaveFailed) && MessageBox.Show(this, L.T(_initialSaveFailed ? "This screenshot was not saved to Shelf. Close without saving?" : "Discard your unsaved annotations? The saved screenshot will stay in Shelf."), "SnappySnap", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) e.Cancel = true; };
    }
    private void OnLanguageChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e) => Refresh();
    private readonly Dictionary<EditorTool, ToggleButton> _toolButtons = new();
    private readonly Button _undo, _redo, _delete;
    private readonly TextBlock _zoomText, _selectionLabel;
    private readonly Slider _stroke, _font, _stepSize, _opacity;
    private readonly WrapPanel _fillPalette, _strokePalette;
    private readonly ScrollViewer _scroll;
    private readonly Border _properties;
    private readonly EditorToolStyles _styles;
    private ElementStyle _defaultStyle => _styles.Get(_tool);
    public event Action<string, AnnotationStyleSettings>? StyleChanged;
    private bool _syncingProperties, _committing, _saving, _initialSaveFailed;
    public bool IsSaving => _saving;

    public Task<bool> ShowAsync()
    {
        var closed = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        Closed += (_, _) => closed.TrySetResult(_committing);
        Show();
        return closed.Task;
    }
    public Func<EditorSaveRequest, Task>? SaveRequestedAsync { get; set; }
    public void ShowInitialSaveFailure(string message)
    {
        _initialSaveFailed = true;
        // Loaded refreshes the tool status; retain the actionable failure after that refresh.
        Loaded += (_, _) => { _status.Text = message; _saveRecovery.Visibility = Visibility.Visible; };
    }
    private double _zoom = 1;
    public EditorDocument Document => _document;

    private static Button IconButton(string label, string glyph, RoutedEventHandler action)
    {
        var button = Ui.Button(label, "", action, "GhostButton"); button.Content = Ui.Icon(glyph, 16);
        button.Width = 32; button.MinHeight = 32; button.Padding = new Thickness(6); button.Margin = new Thickness(1);
        Ui.Localize(button, ToolTipProperty, label); return button;
    }

    private async void SaveAs()
    {
        if (IsSaving) return;
        FinishTextEdit(true);
        var dialog = new SaveFileDialog { Filter = L.T("PNG image|*.png|JPEG image|*.jpg"), FilterIndex = SaveFormat == "Jpg" ? 2 : 1,
            FileName = L.T("Screenshot"), AddExtension = true, OverwritePrompt = true };
        if (dialog.ShowDialog(this) != true) return;
        var extension = System.IO.Path.GetExtension(dialog.FileName).ToLowerInvariant();
        var format = extension switch { ".jpg" or ".jpeg" => "Jpg", ".png" => "Png", _ => null };
        if (format is null) { Ui.Localize(_status, TextBlock.TextProperty, "Choose a .png or .jpg file name."); return; }
        await SaveAndCloseAsync(new(format, dialog.FileName, EditorSaveMode.File));
    }

    private void SetZoom(double zoom) { _zoom = Math.Clamp(zoom, .1, 4); _viewport.LayoutTransform = new ScaleTransform(_zoom, _zoom); _surface.Zoom = _zoom; _surface.InvalidateVisual(); _zoomText.Text = $"{_zoom:P0}"; }
    private void Fit() => SetZoom(Math.Min(1, Math.Min(Math.Max(100, _scroll.ActualWidth - 20) / _document.VisibleBounds.Width, Math.Max(100, _scroll.ActualHeight - 20) / _document.VisibleBounds.Height)));
    private Slider PropertySlider(Panel panel, string label, double min, double max, double value, Action<double> changed)
    {
        var group = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(8, 0, 4, 0) }; panel.Children.Add(group);
        string Display(double number) => Math.Round(number).ToString("0", L.Culture) + (label == "Opacity" ? "%" : "");
        var caption = Ui.Text(L.T(label) + " " + Display(value), 11, "Muted"); caption.MinWidth = 76; Ui.Localize(caption, TextBlock.TextProperty, label + " {0}", Display(value)); group.Children.Add(caption);
        var slider = new Slider { Minimum = min, Maximum = max, Value = value, Width = 70, VerticalAlignment = VerticalAlignment.Center, TickFrequency = 1, IsSnapToTickEnabled = true }; Ui.Localize(slider, ToolTipProperty, label + " {0}", Display(value));
        Ui.Localize(slider, AutomationProperties.NameProperty, label); slider.ValueChanged += (_, _) => { if (!_syncingProperties) changed(slider.Value); Ui.Localize(slider, ToolTipProperty, label + " {0}", Display(slider.Value)); Ui.Localize(caption, TextBlock.TextProperty, label + " {0}", Display(slider.Value)); }; group.Children.Add(slider); return slider;
    }
    private WrapPanel AddPalette(Panel panel, string label, bool fill)
    {
        var group = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(8, 0, 4, 0) }; panel.Children.Add(group);
        var caption = Ui.Text(fill ? "Fill" : "Stroke", 11, "Muted"); caption.Margin = new Thickness(0, 0, 6, 0); group.Children.Add(caption); var row = new WrapPanel();
        foreach (var color in new[] { Colors.Transparent, Color.FromRgb(255, 59, 48), Colors.Gold, Colors.MediumSpringGreen, Colors.White, Colors.DodgerBlue })
        {
            var b = new ToggleButton { Tag = color, Width = 27, Height = 27, MinHeight = 27, Margin = new Thickness(0, 0, 3, 3), Padding = new Thickness(4), ToolTip = color.ToString(System.Globalization.CultureInfo.InvariantCulture) };
            b.Content = color == Colors.Transparent ? Ui.Text("∅", 14) : new Border { Width = 16, Height = 16, CornerRadius = new CornerRadius(3), Background = new SolidColorBrush(color) };
            Ui.Localize(b, AutomationProperties.NameProperty, label + " {0}", color); b.Click += (_, _) => ApplyStyle(style => fill ? style with { FillColor = color } : style with { Color = color }); row.Children.Add(b);
        }
        group.Children.Add(row); return row;
    }
    private void ApplyStyle(Func<ElementStyle, ElementStyle> change)
    {
        var tool = _selected is null ? _tool : EditorToolStyles.ToolOf(_selected);
        var before = _selected is null ? _defaultStyle : ElementStyle.Read(_selected);
        var after = change(before);
        if (before == after) return;
        if (_selected is not null) _history.Execute(new StyleElementCommand(_selected, before, after), _document);
        if (tool is EditorTool.Arrow or EditorTool.Rectangle or EditorTool.Line or EditorTool.Freehand or EditorTool.Text or EditorTool.Highlight or EditorTool.StepMarker)
        {
            var preference = _styles.Set(tool.Value, after);
            StyleChanged?.Invoke(tool.Value.ToString(), preference);
        }
        Refresh();
    }
    private void DeleteSelection() { if (_selected is null) return; _history.Execute(new DeleteElementCommand(_selected), _document); _selected = null; Refresh(); }
    private void OnMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2 && _tool is EditorTool.Select or EditorTool.Text && HitTest(e.GetPosition(_surface)) is TextElement text)
        {
            BeginTextEdit(text, text.Bounds.Location); e.Handled = true; return;
        }
        Focus();
        _surface.CaptureMouse();
        BeginInteraction(e.GetPosition(_surface));
        e.Handled = true;
    }

    private void BeginInteraction(Point point)
    {
        _dragStart = point;
        _lastPoint = _dragStart;
        _freehandPoints.Clear();
        _freehandPoints.Add(_dragStart);
        var handle = _selected is null ? ResizeHandle.None : GetElementHandle(_selected, _dragStart);
        if (_tool == EditorTool.Select || (_tool == EditorTool.Text && HitTest(point) is TextElement) || handle != ResizeHandle.None || (_selected is ArrowElement && _tool == EditorTool.Arrow
            && AnnotationHitTesting.Contains(_selected, _dragStart, _zoom)))
        {
            if (handle == ResizeHandle.None)
            {
                if (_selected is not null) _selected.IsSelected = false;
                _selected = HitTest(_dragStart);
                if (_selected is not null) _selected.IsSelected = true;
                handle = _selected is null ? ResizeHandle.None : GetElementHandle(_selected, _dragStart);
            }

            _resizeHandle = handle;
            _dragGeometry = _selected is null ? null : EditorGeometry.Capture(_selected);
            Refresh();
        }
        else if (_tool != EditorTool.Select)
        {
            if (_selected is not null) _selected.IsSelected = false;
            _selected = null; Refresh(); BeginPreview();
        }
    }

    private void OnMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || !_surface.IsMouseCaptured)
        {
            var handle = _selected is null ? ResizeHandle.None : GetElementHandle(_selected, e.GetPosition(_surface));
            _surface.Cursor = handle is ResizeHandle.TopLeft or ResizeHandle.BottomRight ? Cursors.SizeNWSE :
                handle is ResizeHandle.TopRight or ResizeHandle.BottomLeft ? Cursors.SizeNESW :
                handle is ResizeHandle.ArrowStart or ResizeHandle.ArrowMiddle or ResizeHandle.ArrowEnd ? Cursors.Hand :
                (_tool == EditorTool.Select && HitTest(e.GetPosition(_surface)) is not null)
                    || (_tool == EditorTool.Arrow && _selected is ArrowElement && AnnotationHitTesting.Contains(_selected, e.GetPosition(_surface), _zoom))
                    ? Cursors.SizeAll : Cursors.Cross;
            return;
        }
        var point = e.GetPosition(_surface);
        ContinueInteraction(point);
    }

    private void ContinueInteraction(Point point)
    {
        UpdatePreview(point);
        if (_dragGeometry is not null && _selected is not null)
        {
            var original = _dragGeometry;
            var geometry = _resizeHandle == ResizeHandle.None
                ? EditorGeometry.Translate(original, point - _dragStart)
                : _selected is ArrowElement ? MoveArrowHandle(original, _resizeHandle, point)
                : EditorGeometry.Resize(original, ImageResizeRect(original.Bounds, _resizeHandle, point));
            EditorGeometry.Apply(_selected, geometry);
            _surface.InvalidateVisual();
        }
        else if (_tool == EditorTool.Freehand)
        {
            _freehandPoints.Add(point);
        }
    }

    private void OnMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (!_surface.IsMouseCaptured) return;
        var end = e.GetPosition(_surface);
        _surface.ReleaseMouseCapture();
        EndInteraction(end);
        e.Handled = true;
    }

    private void EndInteraction(Point end)
    {
        ContinueInteraction(end);
        if (_gesturePreview is not null) { _canvas.Children.Remove(_gesturePreview); _gesturePreview = null; }
        var rect = NormalizeRect(_dragStart, end);
        if (_dragGeometry is not null)
        {
            if (_selected is not null && _dragGeometry is not null)
            {
                var after = EditorGeometry.Capture(_selected);
                if (GeometryChanged(_dragGeometry, after))
                {
                    _history.Execute(new TransformElementCommand(_selected, _dragGeometry, after), _document);
                    if (_selected is StepMarkerElement && _dragGeometry.Bounds.Size != after.Bounds.Size)
                    {
                        var preference = _styles.Set(EditorTool.StepMarker, ElementStyle.Read(_selected));
                        StyleChanged?.Invoke(nameof(EditorTool.StepMarker), preference);
                    }
                }
            }

            _dragGeometry = null;
            _resizeHandle = ResizeHandle.None;
        }
        else if (_tool != EditorTool.Select && (_tool is EditorTool.Text or EditorTool.StepMarker or EditorTool.Image || rect.Width >= 2 || rect.Height >= 2))
        {
            switch (_tool)
            {
                case EditorTool.Arrow: Add(new ArrowElement(_dragStart, end)); break;
                case EditorTool.Rectangle: Add(new RectangleElement(rect)); break;
                case EditorTool.Line: Add(new LineElement(_dragStart, end)); break;
                case EditorTool.Freehand: Add(new FreehandElement(_freehandPoints)); break;
                case EditorTool.Text: AddText(_dragStart); break;
                case EditorTool.Highlight: Add(new HighlightElement(rect)); break;
                case EditorTool.Blur: Add(new BlurElement(rect)); break;
                case EditorTool.Pixelate: Add(new PixelateElement(rect)); break;
                case EditorTool.StepMarker: Add(new StepMarkerElement(_document.NextStepNumber, _dragStart)); break;
                case EditorTool.Crop:
                    rect = _document.ConstrainCrop(rect);
                    if (!rect.IsEmpty && rect != _document.VisibleBounds)
                    {
                        _history.Execute(new CropCommand(_document.CropRect, rect), _document);
                        Ui.Localize(_status, TextBlock.TextProperty, "Cropped · Ctrl+Z to restore · Drag to crop again");
                    }
                    break;
                case EditorTool.Image: PasteImageAt(_dragStart); break;
            }
        }
        Refresh();
    }

    private void Add(EditorElement element)
    {
        if (_selected is not null) _selected.IsSelected = false;
        _styles.Get(EditorToolStyles.ToolOf(element) ?? _tool).Apply(element);
        element.IsSelected = true; _selected = element;
        _history.Execute(new AddElementCommand(element), _document);
        if (element is ImageElement) _tool = EditorTool.Select;
        Refresh();
    }

    private void BeginPreview()
    {
        _gesturePreview = _tool switch
        {
            EditorTool.Arrow => new System.Windows.Shapes.Path { Fill = new SolidColorBrush(_defaultStyle.Color) },
            EditorTool.Line => new System.Windows.Shapes.Line { X1 = _dragStart.X, Y1 = _dragStart.Y, X2 = _dragStart.X, Y2 = _dragStart.Y },
            EditorTool.Freehand => new System.Windows.Shapes.Polyline { Points = new PointCollection { _dragStart } },
            _ => new System.Windows.Shapes.Rectangle()
        };
        _gesturePreview.Stroke = new SolidColorBrush(_defaultStyle.Color); _gesturePreview.StrokeThickness = _defaultStyle.StrokeWidth; _gesturePreview.Opacity = _defaultStyle.Opacity; _gesturePreview.IsHitTestVisible = false;
        if (_gesturePreview is System.Windows.Shapes.Path) _gesturePreview.StrokeThickness = 0;
        _canvas.Children.Add(_gesturePreview);
    }
    private void UpdatePreview(Point end)
    {
        if (_gesturePreview is System.Windows.Shapes.Path arrow) arrow.Data = ArrowGeometry.Outline(new ArrowElement(_dragStart, end) { StrokeWidth = _defaultStyle.StrokeWidth });
        else if (_gesturePreview is System.Windows.Shapes.Line line) { line.X2 = end.X; line.Y2 = end.Y; }
        else if (_gesturePreview is System.Windows.Shapes.Polyline freehand) freehand.Points.Add(end);
        else if (_gesturePreview is not null) { var rect = NormalizeRect(_dragStart, end); Canvas.SetLeft(_gesturePreview, rect.X); Canvas.SetTop(_gesturePreview, rect.Y); _gesturePreview.Width = rect.Width; _gesturePreview.Height = rect.Height; }
    }
    private void CancelInteraction()
    {
        if (_dragGeometry is not null && _selected is not null) EditorGeometry.Apply(_selected, _dragGeometry);
        _dragGeometry = null; _resizeHandle = ResizeHandle.None; _surface.ReleaseMouseCapture();
        if (_gesturePreview is not null) { _canvas.Children.Remove(_gesturePreview); _gesturePreview = null; }
        if (_selected is not null) _selected.IsSelected = false; _selected = null; _tool = EditorTool.Select; Refresh();
    }
    private TextBox? _textInput;
    private TextElement? _editingText;
    private Point _textPosition;

    private void AddText(Point position) => BeginTextEdit(null, position);

    private void BeginTextEdit(TextElement? element, Point position)
    {
        FinishTextEdit(true);
        _surface.ReleaseMouseCapture();
        if (_selected is not null) _selected.IsSelected = false;
        _selected = element;
        if (element is not null) element.IsSelected = true;
        _editingText = element; _textPosition = position;
        var style = element is null ? _defaultStyle : ElementStyle.Read(element);
        _textInput = new TextBox
        {
            Text = element?.Text ?? "", FontFamily = new FontFamily("Segoe UI"), FontSize = style.FontSize,
            Foreground = new SolidColorBrush(style.Color), AcceptsReturn = true, AcceptsTab = false,
            TextWrapping = TextWrapping.NoWrap, Padding = new Thickness(0), MinHeight = 0, MinWidth = 64,
            MaxWidth = Math.Max(64, _document.VisibleBounds.Right - position.X), MaxHeight = Math.Max(40, _document.VisibleBounds.Bottom - position.Y),
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalContentAlignment = VerticalAlignment.Top
        };
        Ui.Localize(_textInput, AutomationProperties.NameProperty, "Annotation text");
        Ui.Localize(_textInput, AutomationProperties.HelpTextProperty, "Enter for a new line; Ctrl+Enter to apply; Escape to cancel.");
        _textInput.PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) { FinishTextEdit(false); e.Handled = true; }
            else if (e.Key == Key.Enter && Keyboard.Modifiers == ModifierKeys.Control) { FinishTextEdit(true); e.Handled = true; }
        };
        Canvas.SetLeft(_textInput, position.X - 1); Canvas.SetTop(_textInput, position.Y - 1);
        _canvas.Children.Add(_textInput); _surface.EditingElement = element;
        Refresh(); _textInput.Focus(); _textInput.SelectAll(); _textInput.BringIntoView();
        Ui.Localize(_status, TextBlock.TextProperty, "Enter for a new line · Ctrl+Enter to apply · Esc to cancel");
    }

    private void FinishTextEdit(bool commit)
    {
        if (_textInput is null) return;
        var input = _textInput; var element = _editingText;
        _textInput = null; _editingText = null;
        _canvas.Children.Remove(input); _surface.EditingElement = null;
        if (commit)
        {
            if (element is null)
            {
                if (!string.IsNullOrWhiteSpace(input.Text)) Add(new TextElement(input.Text, _textPosition));
            }
            else if (string.IsNullOrWhiteSpace(input.Text))
            { _history.Execute(new DeleteElementCommand(element), _document); _selected = null; }
            else if (element.Text != input.Text)
                _history.Execute(new ChangeTextCommand(element, element.Text, input.Text), _document);
        }
        Ui.Localize(_status, TextBlock.TextProperty, "Text · Double-click a label or select it and press F2 to edit");
        Refresh(); _surface.Focus();
    }

    private void OnEditorPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (_textInput is null || e.Key != Key.S || !Keyboard.Modifiers.HasFlag(ModifierKeys.Control)) return;
        FinishTextEdit(true);
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)) SaveNew(); else Commit();
        e.Handled = true;
    }

    private void OnEditorPreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (_textInput is null) return;
        var source = e.OriginalSource as DependencyObject;
        var inText = false; var action = false;
        while (source is not null)
        {
            if (source == _textInput) { inText = true; break; }
            if (source is ButtonBase or ComboBox or Slider) action = true;
            source = source is Visual ? VisualTreeHelper.GetParent(source) : LogicalTreeHelper.GetParent(source);
        }
        if (inText) return;
        FinishTextEdit(true);
        if (!action) e.Handled = true;
    }

    private void PasteImageAt(Point position)
    {
        if (!Clipboard.ContainsImage()) return;
        var image = Clipboard.GetImage();
        if (image is null) return;
        image.Freeze();
        InsertImage(image, position);
    }

    private Point ImageInsertionPoint => _document.VisibleBounds.TopLeft + new Vector(
        Math.Min(20, _document.VisibleBounds.Width / 10), Math.Min(20, _document.VisibleBounds.Height / 10));

    private void InsertImage(System.Windows.Media.Imaging.BitmapSource image, Point position)
    {
        var bounds = _document.VisibleBounds;
        var scale = Math.Min(1, Math.Min(Math.Min(320, bounds.Right - position.X) / image.PixelWidth,
            Math.Min(240, bounds.Bottom - position.Y) / image.PixelHeight));
        if (scale > 0) Add(new ImageElement(image, new Rect(position, new Size(image.PixelWidth * scale, image.PixelHeight * scale))));
    }

    private async void OpenImage()
    {
        var dialog = new OpenFileDialog { Filter = L.T("Images|*.png;*.jpg;*.jpeg;*.bmp;*.gif|All files|*.*") };
        if (dialog.ShowDialog(this) == true)
        {
            try
            {
                var image = await Task.Run(() => EditorRenderer.LoadImage(dialog.FileName));
                if (!IsLoaded) return;
                InsertImage(image, ImageInsertionPoint);
            }
            catch (Exception ex)
            {
                _logger.Error("Could not open image for the editor.", ex);
                MessageBox.Show(this, L.T("Could not open the image. Check the file and try again."), "SnappySnap");
            }
        }
    }

    private async void OnDrop(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;
        var paths = (string[])e.Data.GetData(DataFormats.FileDrop);
        var imagePath = paths.FirstOrDefault(path => path.EndsWith(".png", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".bmp", StringComparison.OrdinalIgnoreCase));
        if (imagePath is null) return;
        try
        {
            var image = await Task.Run(() => EditorRenderer.LoadImage(imagePath));
            if (!IsLoaded) return;
            InsertImage(image, ImageInsertionPoint);
        }
        catch (Exception ex) { _logger.Error("Could not drop image into the editor.", ex); if (IsLoaded) MessageBox.Show(this, L.T("Could not open the dropped image."), "SnappySnap"); }
    }

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.OriginalSource is TextBox) return;
        if (e.OriginalSource is Slider || e.OriginalSource is ComboBox || e.OriginalSource is ComboBoxItem) return;
        if (e.Key == Key.F2 && _selected is TextElement text) { BeginTextEdit(text, text.Bounds.Location); e.Handled = true; }
        else if (e.Key == Key.Enter && Keyboard.Modifiers == ModifierKeys.None) { Commit(); e.Handled = true; }
        else if (e.Key == Key.S && Keyboard.Modifiers == (ModifierKeys.Control | ModifierKeys.Shift)) { SaveNew(); e.Handled = true; }
        else if (e.Key == Key.S && Keyboard.Modifiers == ModifierKeys.Control) { Commit(); e.Handled = true; }
        else if (e.Key == Key.D0 && Keyboard.Modifiers == ModifierKeys.Control) { Fit(); e.Handled = true; }
        else if (e.Key == Key.D1 && Keyboard.Modifiers == ModifierKeys.Control) { SetZoom(1); e.Handled = true; }
        else if (e.Key == Key.Escape) { if (_tool != EditorTool.Select || _selected is not null || _surface.IsMouseCaptured) CancelInteraction(); else Close(); e.Handled = true; }
        else if (e.Key == Key.Z && Keyboard.Modifiers == ModifierKeys.Control) { _history.Undo(_document); Refresh(); e.Handled = true; }
        else if ((e.Key == Key.Y && Keyboard.Modifiers == ModifierKeys.Control) || (e.Key == Key.Z && Keyboard.Modifiers == (ModifierKeys.Control | ModifierKeys.Shift))) { _history.Redo(_document); Refresh(); e.Handled = true; }
        else if (e.Key == Key.Delete && _selected is not null) { _history.Execute(new DeleteElementCommand(_selected), _document); _selected = null; Refresh(); e.Handled = true; }
        else if (_selected is not null && e.Key is Key.Left or Key.Right or Key.Up or Key.Down)
        {
            var amount = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) ? 10 : 1;
            var delta = e.Key switch { Key.Left => new Vector(-amount, 0), Key.Right => new Vector(amount, 0), Key.Up => new Vector(0, -amount), _ => new Vector(0, amount) };
            _history.Execute(new MoveElementCommand(_selected, delta), _document); Refresh(); e.Handled = true;
        }
        else if (e.Key == Key.V && Keyboard.Modifiers == ModifierKeys.Control) { PasteImageAt(ImageInsertionPoint); e.Handled = true; }
    }

    private async void Commit() => await CommitAsync();
    private Task CommitAsync() => SaveAndCloseAsync(new(SaveFormat));
    private async void SaveNew() => await SaveAndCloseAsync(new(SaveFormat, Mode: EditorSaveMode.NewCopy));
    private async Task CopyAsync()
    {
        if (IsSaving) return;
        FinishTextEdit(true);
        _saving = true; IsEnabled = false; Ui.Localize(_status, TextBlock.TextProperty, "Copying…");
        try
        {
            var image = await EditorRenderer.RenderAsync(_document, false, CancellationToken.None);
            Clipboard.SetImage(image);
            Ui.Localize(_status, TextBlock.TextProperty, "Copied");
        }
        catch (Exception ex)
        {
            _logger.Error("Could not copy the screenshot.", ex);
            Ui.Localize(_status, TextBlock.TextProperty, "Could not copy the image. Clipboard may be busy; press Copy or Ctrl+C to retry.");
        }
        finally { _saving = false; IsEnabled = true; _surface.Focus(); }
    }
    private EditorSaveRequest? _lastSaveRequest;
    private async Task SaveAndCloseAsync(EditorSaveRequest request)
    {
        if (IsSaving) return;
        FinishTextEdit(true);
        _lastSaveRequest = request;
        _saving = true; IsEnabled = false; Ui.Localize(_status, TextBlock.TextProperty, "Saving…");
        try
        {
            if (SaveRequestedAsync is not null) await SaveRequestedAsync(request);
            _saving = false;
            _committing = true; Close();
        }
        catch (Exception ex)
        {
            _logger.Error("Screenshot save failed; editor retained for retry.", ex);
            _status.Text = ex.Message;
            _saveRecovery.Visibility = Visibility.Visible;
        }
        finally { _saving = false; IsEnabled = true; }
    }
    private void Refresh()
    {
        if (_visibleBounds != _document.VisibleBounds)
        {
            _visibleBounds = _document.VisibleBounds;
            _viewport.Width = _visibleBounds.Width; _viewport.Height = _visibleBounds.Height;
            // All children keep original image coordinates; WPF maps mouse positions through this translation.
            _canvas.RenderTransform = new TranslateTransform(-_visibleBounds.X, -_visibleBounds.Y);
            _dimensions.Text = $"{_visibleBounds.Width:0} × {_visibleBounds.Height:0} px";
            Fit(); _scroll.ScrollToHome();
        }
        if (_selected is not null && !_document.Elements.Contains(_selected)) _selected = null;
        _selected ??= _document.Elements.LastOrDefault(x => x.IsSelected);
        _surface.InvalidateVisual(); _undo.IsEnabled = _history.CanUndo; _redo.IsEnabled = _history.CanRedo; _delete.IsEnabled = _selected is not null;
        foreach (var pair in _toolButtons) pair.Value.IsChecked = pair.Key == _tool;
        var style = _selected is null ? _defaultStyle : ElementStyle.Read(_selected);
        _syncingProperties = true; _stroke.Maximum = EditorSettings.MaximumStrokeWidth;
        _stroke.Value = style.StrokeWidth; _font.Value = style.FontSize; _stepSize.Value = style.StepDiameter; _opacity.Value = style.Opacity * 100; _syncingProperties = false;
        foreach (var swatch in _strokePalette.Children.OfType<ToggleButton>()) swatch.IsChecked = (Color)swatch.Tag == style.Color;
        foreach (var swatch in _fillPalette.Children.OfType<ToggleButton>()) swatch.IsChecked = (Color)swatch.Tag == style.FillColor;
        _fillPalette.IsEnabled = _selected is null or RectangleElement;
        _strokePalette.IsEnabled = _selected is not (BlurElement or PixelateElement or ImageElement);
        _stroke.IsEnabled = _selected is not (BlurElement or PixelateElement or ImageElement or HighlightElement or TextElement or StepMarkerElement);
        _font.IsEnabled = _selected is null or TextElement;
        _stepSize.IsEnabled = _selected is StepMarkerElement;
        _opacity.IsEnabled = _selected is not (BlurElement or PixelateElement);
        _selectionLabel.Text = _selected is null ? L.T("New annotation") : L.F("Selected: {0}", L.T(_selected.GetType().Name.Replace("Element", "")));
        _selectionLabel.Margin = new Thickness(0, 0, 8, 0);
        _properties.Visibility = _selected is null || _textInput is not null ? Visibility.Collapsed : Visibility.Visible;
        foreach (var control in new FrameworkElement[] { _strokePalette, _fillPalette, _stroke, _font, _stepSize, _opacity })
            ((FrameworkElement)control.Parent).Visibility = control.IsEnabled ? Visibility.Visible : Visibility.Collapsed;
    }

    private EditorElement? HitTest(Point point)
    {
        return _document.Elements.Reverse().FirstOrDefault(element => AnnotationHitTesting.Contains(element, point, _zoom));
    }

    private ResizeHandle GetResizeHandle(Rect bounds, Point point)
    {
        var tolerance = 10d / _zoom;
        var handles = new (ResizeHandle Handle, Point Point)[]
        {
            (ResizeHandle.TopLeft, new Point(bounds.Left, bounds.Top)),
            (ResizeHandle.TopRight, new Point(bounds.Right, bounds.Top)),
            (ResizeHandle.BottomLeft, new Point(bounds.Left, bounds.Bottom)),
            (ResizeHandle.BottomRight, new Point(bounds.Right, bounds.Bottom))
        };
        return handles.FirstOrDefault(item => Math.Abs(item.Point.X - point.X) <= tolerance && Math.Abs(item.Point.Y - point.Y) <= tolerance).Handle;
    }

    private ResizeHandle GetElementHandle(EditorElement element, Point point)
    {
        if (element is not ArrowElement arrow) return GetResizeHandle(element.Bounds, point);
        var tolerance = 8 / _zoom;
        foreach (var (handle, position) in new[] { (ResizeHandle.ArrowMiddle, ArrowGeometry.Middle(arrow)),
            (ResizeHandle.ArrowStart, arrow.Start), (ResizeHandle.ArrowEnd, arrow.End) })
            if ((position - point).Length <= tolerance) return handle;
        return ResizeHandle.None;
    }

    private static ElementGeometry MoveArrowHandle(ElementGeometry original, ResizeHandle handle, Point point)
    {
        var start = original.Start!.Value; var end = original.End!.Value;
        // Preserve the bend relative to the chord when either endpoint moves.
        return handle switch
        {
            ResizeHandle.ArrowStart => original with { Start = point, Control = original.Control!.Value + (point - start) / 2 },
            ResizeHandle.ArrowEnd => original with { End = point, Control = original.Control!.Value + (point - end) / 2 },
            ResizeHandle.ArrowMiddle => original with { Control = ArrowGeometry.ControlForMiddle(start, end, point) },
            _ => throw new InvalidOperationException("Unexpected arrow handle.")
        };
    }

    private Rect ImageResizeRect(Rect source, ResizeHandle handle, Point point)
    {
        var target = ResizeRect(source, handle, point);
        if (_selected is StepMarkerElement)
        {
            var diameter = Math.Clamp(Math.Max(target.Width, target.Height), EditorSettings.MinimumStepDiameter, EditorSettings.MaximumStepDiameter);
            return new Rect(handle is ResizeHandle.TopLeft or ResizeHandle.BottomLeft ? source.Right - diameter : source.Left,
                handle is ResizeHandle.TopLeft or ResizeHandle.TopRight ? source.Bottom - diameter : source.Top, diameter, diameter);
        }
        if (_selected is not ImageElement || Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)) return target;
        var height = target.Width * source.Height / source.Width;
        return new Rect(target.Left, handle is ResizeHandle.TopLeft or ResizeHandle.TopRight ? source.Bottom - height : target.Top, target.Width, height);
    }
    private static Rect ResizeRect(Rect source, ResizeHandle handle, Point point)
    {
        const double minimum = 4;
        var left = source.Left;
        var top = source.Top;
        var right = source.Right;
        var bottom = source.Bottom;
        switch (handle)
        {
            case ResizeHandle.TopLeft:
                left = Math.Min(point.X, right - minimum); top = Math.Min(point.Y, bottom - minimum); break;
            case ResizeHandle.TopRight:
                right = Math.Max(point.X, left + minimum); top = Math.Min(point.Y, bottom - minimum); break;
            case ResizeHandle.BottomLeft:
                left = Math.Min(point.X, right - minimum); bottom = Math.Max(point.Y, top + minimum); break;
            case ResizeHandle.BottomRight:
                right = Math.Max(point.X, left + minimum); bottom = Math.Max(point.Y, top + minimum); break;
        }

        return new Rect(left, top, right - left, bottom - top);
    }

    private static bool GeometryChanged(ElementGeometry before, ElementGeometry after) =>
        before.Bounds != after.Bounds || before.Start != after.Start || before.End != after.End || before.Control != after.Control || !before.Points.SequenceEqual(after.Points);

    private static Rect NormalizeRect(Point a, Point b) => new(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Abs(b.X - a.X), Math.Abs(b.Y - a.Y));
}
