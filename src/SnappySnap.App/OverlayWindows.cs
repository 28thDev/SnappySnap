using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Threading;
using SnappySnap.Core;
using SnappySnap.Capture;
using SnappySnap.Presentation;
using SnappySnap.Localization;
using WpfButton = System.Windows.Controls.Button;
using WpfPanel = System.Windows.Controls.Panel;

namespace SnappySnap.App;

internal static class OverlayWindowStyles
{
    private const int GwlExstyle = -20;
    private const int WsExToolWindow = 0x80;
    private const int WsExNoActivate = 0x08000000;
    private const int WsExTransparent = 0x20;
    private const int WsExLayered = 0x80000;

    public static void MakeClickThrough(nint hwnd)
    {
        var current = GetWindowLongPtr(hwnd, GwlExstyle).ToInt64();
        SetWindowLongPtr(hwnd, GwlExstyle, new nint(current | WsExToolWindow | WsExNoActivate | WsExTransparent | WsExLayered));
    }

    public static void MakeNoActivate(nint hwnd)
    {
        var current = GetWindowLongPtr(hwnd, GwlExstyle).ToInt64();
        SetWindowLongPtr(hwnd, GwlExstyle, new nint(current | WsExToolWindow | WsExNoActivate | WsExLayered));
    }

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern nint GetWindowLongPtr(nint hwnd, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static extern nint SetWindowLongPtr(nint hwnd, int index, nint value);
}

public sealed class RecordingOverlayController : IDisposable
{
    private readonly RecordingBorderWindow _border;
    private readonly RecordingPillWindow _pill;
    private readonly DispatcherTimer _refresh;

    public RecordingOverlayController(VirtualPixelRect bounds, IReadOnlyList<MonitorDescriptor> monitors, AppSettings settings, IAppLogger logger, Func<RecordingSnapshot> currentSnapshot)
    {
        _refresh = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        _refresh.Tick += (_, _) => Update(currentSnapshot());
        _border = new RecordingBorderWindow(bounds, monitors, logger);
        _pill = new RecordingPillWindow(bounds, monitors, settings, logger);
        _pill.PauseClicked += (_, _) => PauseClicked?.Invoke(this, EventArgs.Empty);
        _pill.StopClicked += (_, _) => StopClicked?.Invoke(this, EventArgs.Empty);
        _pill.SystemAudioClicked += (_, enabled) => SystemAudioClicked?.Invoke(this, enabled);
        _pill.MicrophoneClicked += (_, enabled) => MicrophoneClicked?.Invoke(this, enabled);
    }

    public event EventHandler? PauseClicked;
    public event EventHandler? StopClicked;
    public event EventHandler<bool>? SystemAudioClicked;
    public event EventHandler<bool>? MicrophoneClicked;

    public void Show() { _border.Show(); _pill.Show(); _refresh.Start(); }
    public void Update(RecordingSnapshot snapshot) { _pill.Update(snapshot); _border.Update(snapshot); }
    public void Dispose() { _refresh.Stop(); _pill.Close(); _border.Close(); }
}

public sealed class RecordingBorderWindow : Window
{
    private readonly Border _outline;
    public void Update(RecordingSnapshot snapshot) => Dispatcher.BeginInvoke(() => _outline.BorderBrush = Ui.Brush(snapshot.State == RecordingState.Paused ? "Warning" : "Accent"));
    public RecordingBorderWindow(VirtualPixelRect bounds, IReadOnlyList<MonitorDescriptor> monitors, IAppLogger logger)
    {
        Configure(bounds, monitors);
        _outline = new Border { BorderBrush = Ui.Brush("Accent"), BorderThickness = new Thickness(1.5), Background = Brushes.Transparent, IsHitTestVisible = false }; Content = _outline;
        SourceInitialized += (_, _) =>
        {
            OverlayWindowStyles.MakeClickThrough(new WindowInteropHelper(this).Handle);
            _ = new SnappySnap.Capture.WindowCaptureExclusionService(logger).TryExcludeWindow(new WindowInteropHelper(this).Handle);
        };
    }

    private void Configure(VirtualPixelRect bounds, IReadOnlyList<MonitorDescriptor> monitors)
    {
        var monitor = monitors.FirstOrDefault(x => x.Bounds.Contains(new VirtualPixelPoint(bounds.X + bounds.Width / 2, bounds.Y + bounds.Height / 2))) ?? monitors[0];
        WindowStyle = WindowStyle.None; AllowsTransparency = true; Background = Brushes.Transparent; ShowInTaskbar = false; Topmost = true; ShowActivated = false; IsHitTestVisible = false;
        Left = bounds.X / monitor.ScaleX; Top = bounds.Y / monitor.ScaleY; Width = bounds.Width / monitor.ScaleX; Height = bounds.Height / monitor.ScaleY;
    }
}

public sealed class RecordingPillWindow : Window
{
    private readonly WpfButton _pause;
    private readonly WpfButton _stop;
    private readonly WpfButton _systemAudio;
    private readonly WpfButton _microphone;
    private readonly TextBlock _timer;
    private readonly IAppLogger _logger;
    private bool _systemAudioEnabled = true;
    private bool _microphoneEnabled;
    private bool _microphoneAvailable = true;
    private bool _systemAudioAvailable = true;

    public RecordingPillWindow(VirtualPixelRect bounds, IReadOnlyList<MonitorDescriptor> monitors, AppSettings settings, IAppLogger logger)
    {
        _logger = logger;
        _systemAudioEnabled = settings.Recording.SystemAudioDefault;
        _microphoneEnabled = settings.Recording.MicrophoneDefault;
        WindowStyle = WindowStyle.None; AllowsTransparency = true; Background = Brushes.Transparent; ShowInTaskbar = false; Topmost = true; ShowActivated = false; SizeToContent = SizeToContent.WidthAndHeight;
        var panel = new StackPanel { Orientation = Orientation.Horizontal, Background = Ui.Brush("Surface"), Margin = new Thickness(10, 6, 10, 6) };
        _timer = new TextBlock { Text = "● 00:00", Foreground = Ui.Brush("Text"), MinWidth = 84, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 5, 12, 5), FontWeight = FontWeights.SemiBold };
        _pause = AddButton(panel, "Pause", (_, _) => PauseClicked?.Invoke(this, EventArgs.Empty));
        _systemAudio = AddButton(panel, "Audio ON", (_, _) => { _systemAudioEnabled = !_systemAudioEnabled; UpdateAudioLabels(); SystemAudioClicked?.Invoke(this, _systemAudioEnabled); });
        _microphone = AddButton(panel, "Mic OFF", (_, _) => { _microphoneEnabled = !_microphoneEnabled; UpdateAudioLabels(); MicrophoneClicked?.Invoke(this, _microphoneEnabled); });
        _stop = AddButton(panel, "Stop", (_, _) => StopClicked?.Invoke(this, EventArgs.Empty));
        _stop.SetResourceReference(StyleProperty, "DangerButton");
        panel.Children.Insert(0, _timer);
        Content = new Border { CornerRadius = new CornerRadius(12), BorderBrush = Ui.Brush("Border"), BorderThickness = new Thickness(1), Background = panel.Background, Child = panel };
        Loaded += (_, _) => Position(bounds, monitors);
        SizeChanged += (_, _) => { if (IsLoaded) Position(bounds, monitors); };
        SourceInitialized += (_, _) =>
        {
            OverlayWindowStyles.MakeNoActivate(new WindowInteropHelper(this).Handle);
            _ = new SnappySnap.Capture.WindowCaptureExclusionService(logger).TryExcludeWindow(new WindowInteropHelper(this).Handle);
        };
        UpdateAudioLabels();
    }

    public event EventHandler? PauseClicked;
    public event EventHandler? StopClicked;
    public event EventHandler<bool>? SystemAudioClicked;
    public event EventHandler<bool>? MicrophoneClicked;

    public void Update(RecordingSnapshot snapshot)
    {
        Dispatcher.BeginInvoke(() =>
        {
            var elapsed = snapshot.ActiveDuration.ToString(@"mm\:ss", System.Globalization.CultureInfo.InvariantCulture);
            _timer.Text = snapshot.State == RecordingState.Paused ? L.F("{0} · Paused", elapsed) : $"● {elapsed}";
            _timer.Foreground = Ui.Brush(snapshot.State == RecordingState.Paused ? "Warning" : "Text");
            _pause.Content = Ui.Label(snapshot.State == RecordingState.Paused ? "\uE768" : "\uE769", snapshot.State == RecordingState.Paused ? "Resume" : "Pause");
            Ui.Localize(_pause, AutomationProperties.NameProperty, snapshot.State == RecordingState.Paused ? "Resume recording" : "Pause recording");
            var active = snapshot.State is RecordingState.Recording or RecordingState.Paused;
            _pause.IsEnabled = active; _stop.IsEnabled = active;
            if (!active) _timer.Text = L.T(snapshot.State == RecordingState.Finalizing ? "Saving recording…" : snapshot.State.ToString());
            _systemAudioAvailable = snapshot.SystemAudioAvailable;
            _systemAudioEnabled = snapshot.SystemAudioEnabled;
            _microphoneAvailable = snapshot.MicrophoneAvailable;
            _microphoneEnabled = _microphoneAvailable && snapshot.MicrophoneEnabled;
            UpdateAudioLabels();
            _systemAudio.IsEnabled = active && _systemAudioAvailable;
            _microphone.IsEnabled = active && _microphoneAvailable;
        });
    }

    private void Position(VirtualPixelRect bounds, IReadOnlyList<MonitorDescriptor> monitors)
    {
        var monitor = monitors.FirstOrDefault(x => x.Bounds.Contains(new VirtualPixelPoint(bounds.X + bounds.Width / 2, bounds.Y + bounds.Height / 2))) ?? monitors[0];
        var pillWidth = (int)Math.Ceiling(ActualWidth * monitor.ScaleX); var pillHeight = (int)Math.Ceiling(ActualHeight * monitor.ScaleY); var gap = (int)(8 * monitor.ScaleY);
        var x = bounds.X + (bounds.Width - pillWidth) / 2;
        var y = bounds.Top - pillHeight - gap;
        if (x < monitor.WorkArea.Left || x + pillWidth > monitor.WorkArea.Right || y < monitor.WorkArea.Top) y = bounds.Bottom + gap;
        if (y + pillHeight > monitor.WorkArea.Bottom) y = bounds.Top + gap;
        x = Math.Clamp(x, monitor.WorkArea.Left, Math.Max(monitor.WorkArea.Left, monitor.WorkArea.Right - pillWidth));
        y = Math.Clamp(y, monitor.WorkArea.Top, Math.Max(monitor.WorkArea.Top, monitor.WorkArea.Bottom - pillHeight));
        Left = x / monitor.ScaleX; Top = y / monitor.ScaleY;
    }

    private static WpfButton AddButton(WpfPanel panel, string text, RoutedEventHandler handler)
    {
        var button = new WpfButton { Margin = new Thickness(2), Padding = new Thickness(8, 4, 8, 4), MinWidth = 70 };
        Ui.Localize(button, ContentControl.ContentProperty, text); Ui.Localize(button, AutomationProperties.NameProperty, text);
        button.Click += handler; panel.Children.Add(button); return button;
    }

    private void UpdateAudioLabels()
    {
        _systemAudio.Content = Ui.Label(_systemAudioEnabled ? "\uE767" : "\uE74F", !_systemAudioAvailable ? "Audio unavailable" : _systemAudioEnabled ? "System ON" : "System OFF");
        Ui.Localize(_systemAudio, AutomationProperties.NameProperty, !_systemAudioAvailable ? "System audio unavailable" : _systemAudioEnabled ? "System audio ON" : "System audio OFF");
        Ui.Localize(_microphone, AutomationProperties.NameProperty, _microphoneAvailable ? (_microphoneEnabled ? "Microphone ON" : "Microphone OFF") : "Microphone unavailable");
        _systemAudio.Background = Ui.Brush(_systemAudioEnabled ? "AccentSurface" : "Surface");
        _microphone.IsEnabled = _microphoneAvailable;
        _systemAudio.IsEnabled = _systemAudioAvailable;
        _microphone.Content = Ui.Label("\uE720", _microphoneAvailable ? (_microphoneEnabled ? "Mic ON" : "Mic OFF") : "No mic");
        Ui.Localize(_microphone, ToolTipProperty, _microphoneAvailable ? "Toggle microphone in this recording" : "No microphone is available. Recording continues without it.");
        Ui.Localize(_systemAudio, ToolTipProperty, _systemAudioAvailable ? "Toggle system audio in this recording" : "System audio is unavailable. Recording continues without it.");
        _microphone.Background = Ui.Brush(_microphoneEnabled ? "AccentSurface" : "Surface");
    }
}

public sealed class ClickRippleOverlayController : IDisposable
{
    private bool _recording;
    private readonly List<RippleWindow> _windows = new();
    private readonly GlobalMouseClickSource _mouse = new();
    private readonly IReadOnlyList<MonitorDescriptor> _monitors;
    private readonly AppSettings _settings;
    private readonly IAppLogger _logger;

    public ClickRippleOverlayController(IReadOnlyList<MonitorDescriptor> monitors, AppSettings settings, IAppLogger logger)
    {
        _monitors = monitors; _settings = settings; _logger = logger;
        foreach (var monitor in monitors) _windows.Add(new RippleWindow(monitor, settings));
        _mouse.Clicked += OnClick;
    }

    public void Start()
    {
        if (_recording) return;
        _recording = true;
        foreach (var window in _windows) window.Show();
        _mouse.Start();
    }

    public void SetRecording(bool recording)
    {
        if (recording) { Start(); return; }
        _recording = false;
        _mouse.Stop();
        foreach (var window in _windows) { window.ClearRipples(); window.Hide(); }
    }

    private void OnClick(object? sender, MouseClickEvent click)
    {
        if (!_recording) return;
        var monitor = _monitors.FirstOrDefault(x => x.Bounds.Contains(click.Position));
        var window = _windows.FirstOrDefault(x => x.Monitor.Id == monitor?.Id);
        _logger.Info("Recording click observed.", new Dictionary<string, object?>
        {
            ["button"] = click.Button.ToString(),
            ["x"] = click.Position.X,
            ["y"] = click.Position.Y,
            ["monitor"] = monitor?.Id,
            ["overlayMatched"] = window is not null
        });
        if (window is null) return;
        window.Dispatcher.BeginInvoke(() => { if (_recording) window.AddRipple(click); });
    }

    public void Dispose()
    {
        _recording = false;
        _mouse.Dispose();
        foreach (var window in _windows) window.Close();
        _windows.Clear();
    }
}

public sealed class RippleWindow : Window
{
    private readonly Canvas _canvas = new();
    private readonly AppSettings _settings;
    public RippleWindow(MonitorDescriptor monitor, AppSettings settings)
    {
        Monitor = monitor; _settings = settings;
        WindowStyle = WindowStyle.None; AllowsTransparency = true; Background = Brushes.Transparent; ShowInTaskbar = false; Topmost = true; ShowActivated = false; IsHitTestVisible = false;
        Left = monitor.Bounds.X / monitor.ScaleX; Top = monitor.Bounds.Y / monitor.ScaleY; Width = monitor.Bounds.Width / monitor.ScaleX; Height = monitor.Bounds.Height / monitor.ScaleY;
        Content = _canvas;
        SourceInitialized += (_, _) => OverlayWindowStyles.MakeClickThrough(new WindowInteropHelper(this).Handle);
    }
    public MonitorDescriptor Monitor { get; }
    public void ClearRipples() => _canvas.Children.Clear();

    public void AddRipple(MouseClickEvent click)
    {
        var colorText = click.Button == SnappySnap.Core.MouseButton.Left ? _settings.Recording.LeftClickColor : _settings.Recording.RightClickColor;
        var color = (Color)ColorConverter.ConvertFromString(colorText);
        var brush = new SolidColorBrush(color); brush.Freeze();
        var ellipse = new Ellipse { Width = 4, Height = 4, Stroke = brush, StrokeThickness = 2, Fill = new SolidColorBrush(Color.FromArgb(45, color.R, color.G, color.B)), Opacity = 0.95, IsHitTestVisible = false };
        var radius = _settings.Recording.ClickRippleRadiusPx / Monitor.ScaleX;
        var point = new Point((click.Position.X - Monitor.Bounds.X) / Monitor.ScaleX, (click.Position.Y - Monitor.Bounds.Y) / Monitor.ScaleY);
        Canvas.SetLeft(ellipse, point.X - 2); Canvas.SetTop(ellipse, point.Y - 2); _canvas.Children.Add(ellipse);
        var duration = new Duration(TimeSpan.FromMilliseconds(_settings.Recording.ClickRippleDurationMs));
        var width = new DoubleAnimation(4, radius * 2, duration); var height = new DoubleAnimation(4, radius * 2, duration); var opacity = new DoubleAnimation(0.95, 0, duration);
        ellipse.BeginAnimation(Canvas.LeftProperty, new DoubleAnimation(point.X - 2, point.X - radius, duration));
        ellipse.BeginAnimation(Canvas.TopProperty, new DoubleAnimation(point.Y - 2, point.Y - radius, duration));
        ellipse.BeginAnimation(WidthProperty, width); ellipse.BeginAnimation(HeightProperty, height); ellipse.BeginAnimation(OpacityProperty, opacity);
        DispatcherTimer timer = new() { Interval = duration.TimeSpan };
        timer.Tick += (_, _) => { timer.Stop(); _canvas.Children.Remove(ellipse); };
        timer.Start();
    }
}
