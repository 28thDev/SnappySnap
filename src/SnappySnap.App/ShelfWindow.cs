using SnappySnap.Localization;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using SnappySnap.Application;
using SnappySnap.Capture;
using SnappySnap.Core;
using SnappySnap.Presentation;
namespace SnappySnap.App;

public sealed class ShelfWindow : Window, IDisposable
{
    public ShelfContent Shelf { get; }
    public bool IsSaving => Shelf.IsSaving;
    public event EventHandler? SettingsRequested;
    public event EventHandler? ScreenshotRequested;
    private readonly GlobalMouseClickSource? _outsideClicks;
    private readonly bool _compact;
    private HwndSource? _source;
    private ShelfPlacement? _normalPlacement;
    private bool _ready;
    private bool _disposed;
    public event Action<ShelfPlacement>? PlacementChanged;
    public ShelfWindow(ShelfService service, AppSettings settings, IAppLogger logger)
        : this(new ShelfContent(service, settings, logger), false) { }
    public ShelfWindow(ShelfContent shelf, bool compact, ShelfPlacement? placement = null)
    {
        Shelf = shelf; _compact = compact;
        Title = "SnappySnap — Capture History";
        Width = compact ? 900 : 1160; Height = compact ? 740 : 900;
        MinWidth = compact ? 560 : 700; MinHeight = compact ? 420 : 480;
        ShowInTaskbar = !compact; ShowActivated = !compact; Topmost = compact;
        WindowStartupLocation = compact || placement is not null ? WindowStartupLocation.Manual : WindowStartupLocation.CenterScreen;
        ResizeMode = ResizeMode.CanResize;
        SourceInitialized += (_, _) =>
        {
            _source = HwndSource.FromHwnd(new WindowInteropHelper(this).Handle); _source?.AddHook(WindowHook);
            if (placement is not null)
            {
                var monitors = new Win32MonitorTopologyService().GetMonitors();
                var monitor = monitors.FirstOrDefault(m => m.Id == placement.MonitorId) ?? monitors.First(m => m.IsPrimary);
                SetBounds(placement.Restore(monitor, compact), monitor);
            }
        };
        Loaded += (_, _) => { FitInitialWorkArea(); _ready = true; RememberNormalPlacement(); };
        LocationChanged += (_, _) => { if (_ready) FitInitialWorkArea(fitBounds: false); };
        DpiChanged += (_, _) => Dispatcher.BeginInvoke(new Action(() => { if (_ready) FitInitialWorkArea(fitBounds: false); }));
        shelf.SetCompact(compact);
        Ui.Shell(this, "", shelf);
        shelf.SettingsRequested += OnSettings;
        shelf.ScreenshotRequested += OnScreenshotRequested;
        Closing += (_, e) => { if (Shelf.IsSaving) { e.Cancel = true; MessageBox.Show(L.T("Wait for saving to finish."), "SnappySnap"); } };
        Closing += (_, e) => { if (!e.Cancel) { RememberNormalPlacement(); if (_normalPlacement is not null) PlacementChanged?.Invoke(_normalPlacement); } };
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key != Key.Escape) return;
            if (Shelf.CancelSelectionGesture()) e.Handled = true;
            else if (compact) { Close(); e.Handled = true; }
        };
        if (compact)
        {
            shelf.DismissRequested += OnDismiss;
            _outsideClicks = new GlobalMouseClickSource();
            _outsideClicks.Clicked += (_, click) =>
            {
                // Capture interaction state before the posted callback: menu closure can run first.
                if (Shelf.IsInteracting) return;
                // An overlapping selector/dialog is outside Shelf even at the same screen coordinates.
                var target = GetAncestor(WindowFromPoint(new NativePoint { X = click.Position.X, Y = click.Position.Y }), 2);
                var outside = target != new WindowInteropHelper(this).Handle;
                Dispatcher.BeginInvoke(() =>
                {
                    if (IsVisible && outside && !Shelf.IsInteracting) Close();
                });
            };
            Loaded += (_, _) => _outsideClicks.Start();
        }
        Closed += (_, _) => Dispose();
    }
    private nint WindowHook(nint hwnd, int message, nint wParam, nint lParam, ref bool handled)
    {
        if (message == 0x0232) // WM_EXITSIZEMOVE: one preference update per completed gesture.
        {
            RememberNormalPlacement();
            if (_normalPlacement is not null) PlacementChanged?.Invoke(_normalPlacement);
        }
        return 0;
    }
    private void RememberNormalPlacement()
    {
        if (!_ready || WindowState != WindowState.Normal) return;
        var hwnd = new WindowInteropHelper(this).Handle;
        var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        if (!GetWindowRect(hwnd, out var rect) || !GetMonitorInfo(MonitorFromWindow(hwnd, 2), ref info)) return;
        var scale = GetDpiForWindow(hwnd) / 96d;
        _normalPlacement = new ShelfPlacement(info.DeviceName, new DipRect((rect.Left - info.Work.Left) / scale, (rect.Top - info.Work.Top) / scale, (rect.Right - rect.Left) / scale, (rect.Bottom - rect.Top) / scale));
    }
    private void OnDismiss(object? sender, EventArgs e) => Close();
    private void FitInitialWorkArea(bool fitBounds = true)
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        if (!GetMonitorInfo(MonitorFromWindow(hwnd, 2), ref info) || !GetWindowRect(hwnd, out var rect)) return;
        var scale = GetDpiForWindow(hwnd) / 96d;
        MinWidth = Math.Min(_compact ? 560 : 700, (info.Work.Right - info.Work.Left) / scale);
        MinHeight = Math.Min(_compact ? 420 : 480, (info.Work.Bottom - info.Work.Top) / scale);
        if (!fitBounds) return;
        var width = Math.Min(rect.Right - rect.Left, info.Work.Right - info.Work.Left);
        var height = Math.Min(rect.Bottom - rect.Top, info.Work.Bottom - info.Work.Top);
        SetWindowPos(hwnd, 0, Math.Clamp(rect.Left, info.Work.Left, info.Work.Right - width), Math.Clamp(rect.Top, info.Work.Top, info.Work.Bottom - height), width, height, 0x0010 | 0x0004);
    }
    private void OnSettings(object? sender, EventArgs e) => SettingsRequested?.Invoke(this, e);
    private void OnScreenshotRequested(object? sender, EventArgs e) => ScreenshotRequested?.Invoke(this, e);

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _ready = false;
        _source?.RemoveHook(WindowHook);
        _source = null;
        _outsideClicks?.Dispose();
        Shelf.Dispose();
        Shelf.SettingsRequested -= OnSettings;
        Shelf.ScreenshotRequested -= OnScreenshotRequested;
        Shelf.DismissRequested -= OnDismiss;
        if (Shelf.Parent is System.Windows.Controls.Panel parent) parent.Children.Remove(Shelf);
        Content = null;
    }

    public bool TryGetCaptureBounds(out VirtualPixelRect bounds)
    {
        bounds = default;
        if (!GetWindowRect(new WindowInteropHelper(this).Handle, out var rect)) return false;
        var width = rect.Right - rect.Left;
        var height = rect.Bottom - rect.Top;
        if (width <= 0 || height <= 0) return false;
        bounds = new VirtualPixelRect(rect.Left, rect.Top, width, height);
        return true;
    }
    public void Place(MonitorDescriptor monitor)
    {
        // Set the HWND in physical pixels. WPF then handles WM_DPICHANGED on the target monitor.
        var hwnd = new WindowInteropHelper(this).EnsureHandle();
        var area = monitor.WorkArea;
        var width = Math.Min(area.Width, (int)Math.Round(900 * monitor.ScaleX));
        var height = Math.Min(area.Height, (int)Math.Round(740 * monitor.ScaleY));
        SetBounds(new VirtualPixelRect(area.Right - width, area.Bottom - height, width, height), monitor);
    }
    private void SetBounds(VirtualPixelRect bounds, MonitorDescriptor monitor)
    {
        MinWidth = Math.Min(_compact ? 560 : 700, monitor.WorkArea.Width / monitor.ScaleX);
        MinHeight = Math.Min(_compact ? 420 : 480, monitor.WorkArea.Height / monitor.ScaleY);
        SetWindowPos(new WindowInteropHelper(this).EnsureHandle(), 0, bounds.X, bounds.Y, bounds.Width, bounds.Height, 0x0010 | 0x0004);
    }
    [StructLayout(LayoutKind.Sequential)] private struct NativePoint { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] private struct NativeRect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] private struct MonitorInfo { public int Size; public NativeRect Monitor, Work; public uint Flags; [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string DeviceName; }
    [DllImport("user32.dll")] private static extern nint MonitorFromWindow(nint hwnd, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool GetMonitorInfo(nint monitor, ref MonitorInfo info);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(nint hwnd, out NativeRect rect);
    [DllImport("user32.dll")] private static extern uint GetDpiForWindow(nint hwnd);
    [DllImport("user32.dll")] private static extern nint WindowFromPoint(NativePoint point);
    [DllImport("user32.dll")] private static extern nint GetAncestor(nint window, uint flags);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(nint hwnd, nint after, int x, int y, int width, int height, uint flags);
}
