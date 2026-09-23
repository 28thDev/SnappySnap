using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using SnappySnap.Core;
using SnappySnap.Localization;
using SnappySnap.Presentation;

namespace SnappySnap.App;

public sealed class RegionSelectorService : IDisposable
{
    private readonly IMonitorTopologyService _topology;
    private readonly IAppLogger _logger;
    private readonly List<RegionSelectorWindow> _windows = new();
    private TaskCompletionSource<VirtualPixelRect?>? _completion;
    private TaskCompletionSource<WindowRegionCatalog?>? _catalogReady;
    private WindowRegionCatalog? _catalog;

    public RegionSelectorService(IMonitorTopologyService topology, IAppLogger logger)
    {
        _topology = topology;
        _logger = logger;
    }

    public Task<VirtualPixelRect?> SelectAsync(bool allowFullMonitor = false)
    {
        return SelectCoreAsync(_topology.GetMonitors(), null, null, allowFullMonitor);
    }

    public Task<VirtualPixelRect?> SelectAsync(
        FrozenDesktopSnapshot frozenSnapshot,
        IReadOnlyList<MonitorDescriptor> monitors,
        bool allowFullMonitor = false)
    {
        ArgumentNullException.ThrowIfNull(frozenSnapshot);
        ArgumentNullException.ThrowIfNull(monitors);
        if (monitors.Count == 0) throw new InvalidOperationException("No display is available. Reconnect a display and try again.");

        var bitmap = ToBitmapSource(frozenSnapshot.CapturedImage);
        return SelectCoreAsync(monitors, bitmap, frozenSnapshot.VirtualDesktopBounds, allowFullMonitor);
    }

    private async Task<VirtualPixelRect?> SelectCoreAsync(
        IReadOnlyList<MonitorDescriptor> monitors,
        BitmapSource? frozenImage,
        VirtualPixelRect? frozenBounds,
        bool allowFullMonitor)
    {
        if (_completion is not null) throw new InvalidOperationException("A region selection is already active.");
        _completion = new TaskCompletionSource<VirtualPixelRect?>(TaskCreationOptions.RunContinuationsAsynchronously);
        _catalogReady = new TaskCompletionSource<WindowRegionCatalog?>(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            if (monitors.Count == 0) throw new InvalidOperationException("No display is available. Reconnect a display and try again.");
            if (frozenImage is not null && frozenBounds is null) throw new InvalidOperationException("Frozen selector bounds are missing.");
            var gesture = new RegionSelectionGesture();
            foreach (var monitor in monitors)
            {
                var window = new RegionSelectorWindow(monitor, Complete, Cancel, allowFullMonitor, monitors, gesture, frozenImage, frozenBounds,
                    UpdateHover, ResolveHoverAsync, CancelPendingClicks);
                window.SelectionChanged += region => { foreach (var overlay in _windows) overlay.DrawSelection(region); };
                _windows.Add(window);
                window.Show();
            }
            _windows.FirstOrDefault()?.Activate();
            var selectorHandles = _windows.Select(window => new WindowInteropHelper(window).Handle).ToArray();
            var desktop = frozenBounds ?? _topology.VirtualDesktopBounds;
            _ = LoadCandidatesAsync(selectorHandles, desktop, _completion, _catalogReady);
            return await _completion.Task.ConfigureAwait(true);
        }
        finally { CloseWindows(); _catalogReady?.TrySetResult(null); _catalogReady = null; _catalog = null; _completion = null; }
    }

    private async Task LoadCandidatesAsync(nint[] selectorHandles, VirtualPixelRect desktop,
        TaskCompletionSource<VirtualPixelRect?> completion, TaskCompletionSource<WindowRegionCatalog?> ready)
    {
        try
        {
            var catalog = await Task.Run(() => WindowRegionCatalog.Enumerate(selectorHandles, desktop, _logger));
            ready.TrySetResult(catalog);
            if (_completion != completion || completion.Task.IsCompleted) return;
            _catalog = catalog;
            UpdateHover(RegionSelectorWindow.CursorPosition());
        }
        catch (Exception ex) { ready.TrySetResult(null); _logger.Error("Could not enumerate selectable windows.", ex); }
    }

    private void UpdateHover(VirtualPixelPoint point)
    {
        if (_completion is null || _completion.Task.IsCompleted) return;
        if (_windows.Any(window => window.IsDragging)) return;
        var hover = _catalog?.Resolve(point, () => UpdateHover(RegionSelectorWindow.CursorPosition()));
        foreach (var window in _windows) window.DrawHover(hover);
    }

    private async Task<HoverRegionResult?> ResolveHoverAsync(VirtualPixelPoint point)
    {
        try
        {
            var catalog = _catalog ?? (await _catalogReady!.Task);
            return catalog is null ? null : await catalog.ResolveReadyAsync(point,
                () => UpdateHover(RegionSelectorWindow.CursorPosition()));
        }
        catch (Exception ex)
        {
            _logger.Error("Could not resolve the suggested capture region.", ex);
            return null;
        }
    }

    private static BitmapSource ToBitmapSource(CapturedImage image)
    {
        var source = BitmapSource.Create(image.Width, image.Height, 96, 96, PixelFormats.Bgra32, null, image.Bgra32, image.Width * 4);
        source.Freeze();
        return source;
    }

    private void Complete(VirtualPixelRect region)
    {
        if (region.Width < 4 || region.Height < 4) return;
        _completion?.TrySetResult(region);
    }

    private void Cancel() => _completion?.TrySetResult(null);

    private void CancelPendingClicks()
    {
        foreach (var window in _windows) window.CancelPendingClick();
    }

    private void CloseWindows()
    {
        foreach (var window in _windows.ToArray())
        {
            try { window.Close(); } catch (Exception ex) { _logger.Error("Could not close region selector window.", ex); }
        }
        _windows.Clear();
    }

    public void Dispose()
    {
        _completion?.TrySetResult(null);
        CloseWindows();
    }
}

public sealed class RegionSelectorWindow : Window
{
    private readonly MonitorDescriptor _monitor;
    private readonly Action<VirtualPixelRect> _selected;
    private readonly Action _cancelled;
    private readonly System.Windows.Shapes.Rectangle _selectionBorder;
    private readonly System.Windows.Shapes.Path _dimmer;
    private readonly List<System.Windows.Shapes.Rectangle> _handles = new();
    public event Action<VirtualPixelRect>? SelectionChanged;
    private readonly TextBlock _sizeText;
    private readonly double _scaleX;
    private readonly double _scaleY;
    private VirtualPixelPoint _start;
    private bool _dragging;
    public bool IsDragging => _dragging;
    private readonly RegionSelectionGesture _gesture;
    private readonly IReadOnlyList<MonitorDescriptor> _monitors;
    private readonly bool _allowFullMonitor;
    private readonly Action<VirtualPixelPoint>? _hoverMoved;
    private readonly Func<VirtualPixelPoint, Task<HoverRegionResult?>>? _resolveHover;
    private readonly Action? _cancelPendingClicks;
    private CancellationTokenSource? _pendingClick;

    public RegionSelectorWindow(MonitorDescriptor monitor, Action<VirtualPixelRect> selected, Action cancelled,
        bool allowFullMonitor = false, IReadOnlyList<MonitorDescriptor>? monitors = null, RegionSelectionGesture? gesture = null,
        BitmapSource? frozenImage = null, VirtualPixelRect? frozenBounds = null,
        Action<VirtualPixelPoint>? hoverMoved = null,
        Func<VirtualPixelPoint, Task<HoverRegionResult?>>? resolveHover = null,
        Action? cancelPendingClicks = null)
    {
        _monitor = monitor;
        _gesture = gesture ?? new RegionSelectionGesture();
        _monitors = monitors ?? new[] { monitor };
        _allowFullMonitor = allowFullMonitor;
        _hoverMoved = hoverMoved;
        _resolveHover = resolveHover;
        _cancelPendingClicks = cancelPendingClicks;
        _selected = selected;
        _cancelled = cancelled;
        _scaleX = monitor.ScaleX;
        _scaleY = monitor.ScaleY;
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = frozenImage is null
            ? new SolidColorBrush(Color.FromArgb(1, 0, 0, 0))
            : Brushes.Black;
        ShowInTaskbar = false;
        Topmost = true;
        ShowActivated = true;
        Focusable = true;
        Left = monitor.Bounds.X / _scaleX;
        Top = monitor.Bounds.Y / _scaleY;
        Width = monitor.Bounds.Width / _scaleX;
        Height = monitor.Bounds.Height / _scaleY;
        Cursor = Cursors.Cross;
        KeyDown += OnKeyDown;
        MouseLeftButtonDown += OnMouseDown;
        MouseMove += OnMouseMove;
        MouseLeftButtonUp += OnMouseUp;

        var canvas = new Canvas { ClipToBounds = true };
        if (frozenImage is not null)
        {
            if (frozenBounds is null) throw new InvalidOperationException("Frozen selector bounds are missing.");
            var image = new Image
            {
                Source = frozenImage,
                Width = frozenImage.PixelWidth / _scaleX,
                Height = frozenImage.PixelHeight / _scaleY,
                Stretch = Stretch.Fill,
                IsHitTestVisible = false
            };
            Canvas.SetLeft(image, (frozenBounds.Value.X - monitor.Bounds.X) / _scaleX);
            Canvas.SetTop(image, (frozenBounds.Value.Y - monitor.Bounds.Y) / _scaleY);
            canvas.Children.Add(image);
        }
        _dimmer = new System.Windows.Shapes.Path { Fill = new SolidColorBrush(Color.FromArgb(140, 8, 14, 20)), IsHitTestVisible = false, Data = new RectangleGeometry(new Rect(0, 0, Width, Height)) }; canvas.Children.Add(_dimmer);
        var hint = Ui.Text(L.T(allowFullMonitor
            ? "Click: suggested area · Drag: custom area · Double-click: monitor · Esc: cancel"
            : "Click: suggested area · Drag: custom area · Esc: cancel"), 12);
        hint.IsHitTestVisible = false; Canvas.SetLeft(hint, 20); Canvas.SetTop(hint, 20); canvas.Children.Add(hint);
        _selectionBorder = new System.Windows.Shapes.Rectangle { Stroke = Ui.Brush("Accent"), StrokeThickness = 1.5, StrokeDashArray = new DoubleCollection { 5, 3 }, Fill = Brushes.Transparent, Visibility = Visibility.Collapsed, IsHitTestVisible = false };
        canvas.Children.Add(_selectionBorder);
        _sizeText = new TextBlock { Foreground = Brushes.White, Background = new SolidColorBrush(Color.FromArgb(210, 20, 20, 20)), Padding = new Thickness(5), Visibility = Visibility.Collapsed };
        canvas.Children.Add(_sizeText);
        for (var i = 0; i < 8; i++) { var handle = new System.Windows.Shapes.Rectangle { Width = 6, Height = 6, Fill = Ui.Brush("Accent"), Stroke = Brushes.White, StrokeThickness = 1, Visibility = Visibility.Collapsed, IsHitTestVisible = false }; _handles.Add(handle); canvas.Children.Add(handle); }
        Content = canvas;
        Loaded += (_, _) => { if (ShowActivated) Focus(); };
        Closed += (_, _) => _pendingClick?.Cancel();
    }

    private void OnMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != System.Windows.Input.MouseButton.Left) return;
        if (_cancelPendingClicks is null) CancelPendingClick();
        else _cancelPendingClicks();
        _start = GetCursorPosition();
        _hoverMoved?.Invoke(_start);
        _gesture.Begin(_start, unchecked((uint)e.Timestamp), new SelectionInputSettings(GetDoubleClickTime(),
            GetSystemMetricsForDpi(36, _monitor.DpiX), GetSystemMetricsForDpi(37, _monitor.DpiY),
            GetSystemMetricsForDpi(68, _monitor.DpiX), GetSystemMetricsForDpi(69, _monitor.DpiY)));
        _dragging = true;
        CaptureMouse();
        e.Handled = true;
    }

    private void OnMouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (!_dragging) { _hoverMoved?.Invoke(GetCursorPosition()); return; }
        var point = GetCursorPosition();
        _gesture.Move(point);
        if (_gesture.HasDragged) UpdateSelection(_start, point);
    }

    private void OnMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (!_dragging || e.ChangedButton != System.Windows.Input.MouseButton.Left) return;
        var end = GetCursorPosition();
        _dragging = false;
        ReleaseMouseCapture();
        var result = _gesture.End(end, _monitors, _allowFullMonitor);
        if (result.Kind != SelectionGestureKind.None) _selected(result.Region);
        else if (!_gesture.HasDragged)
        {
            _hoverMoved?.Invoke(end);
            if (_resolveHover is not null) _ = ConfirmHoverAsync(end);
        }
        else _hoverMoved?.Invoke(end);
        e.Handled = true;
    }

    private async Task ConfirmHoverAsync(VirtualPixelPoint point)
    {
        var pending = new CancellationTokenSource();
        _pendingClick = pending;
        try
        {
            var hoverTask = _resolveHover!(point);
            if (_allowFullMonitor) await Task.Delay(checked((int)GetDoubleClickTime()) + 15, pending.Token);
            var hover = await hoverTask.WaitAsync(pending.Token);
            if (!pending.IsCancellationRequested && hover is { CanSelect: true } && hover.Value.Bounds.Width >= 4 && hover.Value.Bounds.Height >= 4)
                _selected(hover.Value.Bounds);
        }
        catch (OperationCanceledException) { }
        finally
        {
            if (ReferenceEquals(_pendingClick, pending)) _pendingClick = null;
            pending.Dispose();
        }
    }

    public void CancelPendingClick()
    {
        _pendingClick?.Cancel();
        _pendingClick = null;
    }

    private void UpdateSelection(VirtualPixelPoint start, VirtualPixelPoint end)
    {
        var rect = Normalize(start, end);
        DrawSelection(rect); SelectionChanged?.Invoke(rect);
    }

    public void DrawSelection(VirtualPixelRect rect) => DrawRegion(rect, true, null);

    public void DrawHover(HoverRegionResult? hover)
    {
        if (hover is null || !hover.Value.Bounds.TryIntersect(_monitor.Bounds, out _))
        {
            _dimmer.Data = new RectangleGeometry(new Rect(0, 0, Width, Height));
            _selectionBorder.Visibility = Visibility.Collapsed;
            _sizeText.Visibility = Visibility.Collapsed;
            foreach (var handle in _handles) handle.Visibility = Visibility.Collapsed;
            return;
        }
        var description = hover.Value.Kind switch
        {
            HoverRegionKind.BrowserWithAddress => L.T("Address bar and page · click to capture"),
            HoverRegionKind.BrowserContent => L.T("Page content · click to capture"),
            HoverRegionKind.BrowserFull or HoverRegionKind.BrowserUnknown => L.T("Full browser · tabs included"),
            HoverRegionKind.BrowserPending => L.T("Detecting browser area…"),
            _ => L.T("Window · click to capture")
        };
        DrawRegion(hover.Value.Bounds, false, description);
    }

    private void DrawRegion(VirtualPixelRect rect, bool showHandles, string? description)
    {
        var local = new Rect((rect.X - _monitor.Bounds.X) / _scaleX, (rect.Y - _monitor.Bounds.Y) / _scaleY, rect.Width / _scaleX, rect.Height / _scaleY);
        _dimmer.Data = new CombinedGeometry(GeometryCombineMode.Exclude, new RectangleGeometry(new Rect(0, 0, Width, Height)), new RectangleGeometry(local));
        _selectionBorder.Visibility = Visibility.Visible; _sizeText.Visibility = Visibility.Visible;
        Canvas.SetLeft(_selectionBorder, local.X); Canvas.SetTop(_selectionBorder, local.Y); _selectionBorder.Width = local.Width; _selectionBorder.Height = local.Height;
        _sizeText.Text = description is null ? $"{rect.Width} × {rect.Height} px" : $"{description} · {rect.Width} × {rect.Height} px";
        _sizeText.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        Canvas.SetLeft(_sizeText, Math.Clamp(local.X + 8, 8, Math.Max(8, Width - _sizeText.DesiredSize.Width - 8)));
        Canvas.SetTop(_sizeText, Math.Clamp(local.Bottom + 14, 8, Math.Max(8, Height - _sizeText.DesiredSize.Height - 8)));
        var points = new[] { local.TopLeft, new Point(local.X + local.Width / 2, local.Top), local.TopRight, new Point(local.Right, local.Y + local.Height / 2), local.BottomRight, new Point(local.X + local.Width / 2, local.Bottom), local.BottomLeft, new Point(local.Left, local.Y + local.Height / 2) };
        for (var i = 0; i < points.Length; i++) { var h = _handles[i]; Canvas.SetLeft(h, points[i].X - 3); Canvas.SetTop(h, points[i].Y - 3); h.Visibility = showHandles ? Visibility.Visible : Visibility.Collapsed; }
    }
    private void OnKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.Escape) { _cancelled(); e.Handled = true; }
    }

    public static VirtualPixelPoint CursorPosition() => GetCursorPosition();

    private static VirtualPixelPoint GetCursorPosition()
    {
        _ = GetCursorPos(out var point);
        return new VirtualPixelPoint(point.X, point.Y);
    }

    private static VirtualPixelRect Normalize(VirtualPixelPoint start, VirtualPixelPoint end) => new(
        Math.Min(start.X, end.X), Math.Min(start.Y, end.Y), Math.Abs(end.X - start.X), Math.Abs(end.Y - start.Y));

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out NativePoint point);
    [DllImport("user32.dll")] private static extern uint GetDoubleClickTime();
    [DllImport("user32.dll")] private static extern int GetSystemMetricsForDpi(int index, uint dpi);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint { public int X; public int Y; }
}
