using System.Diagnostics;
using System.Drawing;
using System.Windows;
using SnappySnap.Application;
using SnappySnap.Capture;
using SnappySnap.Core;
using SnappySnap.Editor;
using SnappySnap.History;
using SnappySnap.Infrastructure;
using SnappySnap.Localization;
using SnappySnap.Presentation;
using Windows.Media.Editing;
using Windows.Storage;

namespace SnappySnap.App;

public partial class App : System.Windows.Application
{
    private SnappySnapRuntime? _runtime;
    private ApplicationLifetimeGate? _lifetimeGate;
    private readonly ExitPreparation _exitPreparation = new();

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        try
        {
#if PERFORMANCE_HARNESS
            var cadenceOutput = e.Args.FirstOrDefault(arg => arg.StartsWith("--cadence-harness=", StringComparison.Ordinal));
            if (cadenceOutput is not null)
            {
                var directory = Path.GetFullPath(cadenceOutput["--cadence-harness=".Length..]);
                try { await PerformanceHarness.RunCadenceAsync(directory); Shutdown(0); }
                catch (Exception ex) { Directory.CreateDirectory(directory); await File.WriteAllTextAsync(Path.Combine(directory, "failure.txt"), ex.ToString()); Shutdown(1); }
                return;
            }
            var acceptanceOutput = e.Args.FirstOrDefault(arg => arg.StartsWith("--acceptance-harness=", StringComparison.Ordinal));
            if (acceptanceOutput is not null)
            {
                var directory = Path.GetFullPath(acceptanceOutput["--acceptance-harness=".Length..]);
                try { await AcceptanceHarness.RunAsync(directory, e.Args.Contains("--interactive")); Shutdown(0); }
                catch (Exception ex) { Directory.CreateDirectory(directory); await File.WriteAllTextAsync(Path.Combine(directory, "failure.txt"), ex.ToString()); Shutdown(1); }
                return;
            }
            var performanceOutput = e.Args.FirstOrDefault(arg => arg.StartsWith("--performance-harness=", StringComparison.Ordinal));
            if (performanceOutput is not null)
            {
                var directory = Path.GetFullPath(performanceOutput["--performance-harness=".Length..]);
                try { await PerformanceHarness.RunAsync(directory, e.Args.Contains("--interactive"), e.Args.Contains("--video-only")); Shutdown(0); }
                catch (Exception ex) { Directory.CreateDirectory(directory); await File.WriteAllTextAsync(Path.Combine(directory, "failure.txt"), ex.ToString()); Shutdown(1); }
                return;
            }
#endif
#if DEBUG
            var mediaOutput = e.Args.FirstOrDefault(arg => arg.StartsWith("--media-optimization-harness=", StringComparison.Ordinal));
            if (mediaOutput is not null)
            {
                var directory = Path.GetFullPath(mediaOutput["--media-optimization-harness=".Length..]);
                try
                {
                    if (e.Args.Contains("--export-only")) await VisualHarness.RunExportSizeAsync(directory, e.Args.Single(arg => arg.StartsWith("--source-directory=", StringComparison.Ordinal))["--source-directory=".Length..]);
                    else if (e.Args.Contains("--video")) await VisualHarness.RunVideoSizeAsync(directory, e.Args.Contains("--full-corpus"));
                    else await VisualHarness.RunMediaOptimizationAsync(directory);
                    Shutdown(0);
                }
                catch (Exception ex) { Directory.CreateDirectory(directory); await File.WriteAllTextAsync(Path.Combine(directory, "failure.txt"), ex.ToString()); Shutdown(1); }
                return;
            }
            var reviewOutput = e.Args.FirstOrDefault(arg => arg.StartsWith("--release-review-harness=", StringComparison.Ordinal));
            if (reviewOutput is not null)
            {
                var directory = Path.GetFullPath(reviewOutput["--release-review-harness=".Length..]);
                try { await VisualHarness.RunReleaseReviewAsync(directory); Shutdown(0); }
                catch (Exception ex) { Directory.CreateDirectory(directory); await File.WriteAllTextAsync(Path.Combine(directory, "failure.txt"), ex.ToString()); Shutdown(1); }
                return;
            }
            var revisionOutput = e.Args.FirstOrDefault(arg => arg.StartsWith("--ui-revision-harness=", StringComparison.Ordinal));
            if (revisionOutput is not null)
            {
                var directory = Path.GetFullPath(revisionOutput["--ui-revision-harness=".Length..]);
                try { await VisualHarness.RunRevisionAsync(directory); Shutdown(0); }
                catch (Exception ex) { Directory.CreateDirectory(directory); await File.WriteAllTextAsync(Path.Combine(directory, "failure.txt"), ex.ToString()); Shutdown(1); }
                return;
            }
            var shelfOutput = e.Args.FirstOrDefault(arg => arg.StartsWith("--shelf-harness=", StringComparison.Ordinal));
            if (shelfOutput is not null)
            {
                var directory = Path.GetFullPath(shelfOutput["--shelf-harness=".Length..]);
                try { await VisualHarness.RunShelfAsync(directory, e.Args.Contains("--restore-only")); Shutdown(0); }
                catch (Exception ex) { Directory.CreateDirectory(directory); await File.WriteAllTextAsync(Path.Combine(directory, "failure.txt"), ex.ToString()); Shutdown(1); }
                return;
            }
            var editorRedesignOutput = e.Args.FirstOrDefault(arg => arg.StartsWith("--editor-redesign-harness=", StringComparison.Ordinal));
            if (editorRedesignOutput is not null)
            {
                var directory = Path.GetFullPath(editorRedesignOutput["--editor-redesign-harness=".Length..]);
                try { await EditorRedesignHarness.RunAsync(directory); Shutdown(0); }
                catch (Exception ex) { Directory.CreateDirectory(directory); await File.WriteAllTextAsync(Path.Combine(directory, "failure.txt"), ex.ToString()); Shutdown(1); }
                return;
            }
            var updatesOutput = e.Args.FirstOrDefault(arg => arg.StartsWith("--updates-harness=", StringComparison.Ordinal));
            if (updatesOutput is not null)
            {
                var directory = Path.GetFullPath(updatesOutput["--updates-harness=".Length..]);
                try { await VisualHarness.RunUpdatesAsync(directory); Shutdown(0); }
                catch (Exception ex) { Directory.CreateDirectory(directory); await File.WriteAllTextAsync(Path.Combine(directory, "failure.txt"), ex.ToString()); Shutdown(1); }
                return;
            }
            var usabilityOutput = e.Args.FirstOrDefault(arg => arg.StartsWith("--usability-harness=", StringComparison.Ordinal));
            if (usabilityOutput is not null)
            {
                var directory = Path.GetFullPath(usabilityOutput["--usability-harness=".Length..]);
                try { await UsabilityHarness.RunAsync(directory); Shutdown(0); }
                catch (Exception ex) { Directory.CreateDirectory(directory); await File.WriteAllTextAsync(Path.Combine(directory, "failure.txt"), ex.ToString()); Shutdown(1); }
                return;
            }
            var nativeOutput = e.Args.FirstOrDefault(arg => arg.StartsWith("--native-visual-harness=", StringComparison.Ordinal));
            if (nativeOutput is not null)
            {
                var directory = Path.GetFullPath(nativeOutput["--native-visual-harness=".Length..]);
                try { await VisualHarness.RunNativeAsync(directory); Shutdown(0); }
                catch (Exception ex) { Directory.CreateDirectory(directory); await File.WriteAllTextAsync(Path.Combine(directory, "failure.txt"), ex.ToString()); Shutdown(1); }
                return;
            }
            var publicScreenshots = e.Args.FirstOrDefault(arg => arg.StartsWith("--public-screenshots=", StringComparison.Ordinal));
            if (publicScreenshots is not null)
            {
                var directory = Path.GetFullPath(publicScreenshots["--public-screenshots=".Length..]);
                if (Directory.Exists(directory)) { Shutdown(1); return; }
                try { await VisualHarness.RunPublicScreenshotsAsync(directory); Shutdown(0); }
                catch (Exception ex) { Directory.CreateDirectory(directory); await File.WriteAllTextAsync(Path.Combine(directory, "failure.txt"), ex.ToString()); Shutdown(1); }
                return;
            }
            var visualOutput = e.Args.FirstOrDefault(arg => arg.StartsWith("--visual-harness=", StringComparison.Ordinal));
            if (visualOutput is not null)
            {
                var directory = Path.GetFullPath(visualOutput["--visual-harness=".Length..]);
                try { await VisualHarness.RunAsync(directory); Shutdown(0); }
                catch (Exception ex) { Directory.CreateDirectory(directory); await File.WriteAllTextAsync(Path.Combine(directory, "failure.txt"), ex.ToString()); Shutdown(1); }
                return;
            }
#endif
            var configureArgument = e.Args.FirstOrDefault(arg => arg.StartsWith("--configure-startup=", StringComparison.Ordinal));
            _lifetimeGate = ApplicationLifetimeGate.TryEnter(configureArgument is not null, out var lifetimeError);
            if (_lifetimeGate is null)
            {
                if (configureArgument is null) MessageBox.Show(L.T(lifetimeError ?? ""), "SnappySnap", MessageBoxButton.OK, MessageBoxImage.Information);
                Shutdown(2);
                return;
            }
            if (configureArgument is not null)
            {
                var paths = new AppPaths();
                using var logger = new FileLogger(paths.LogsPath);
                try
                {
                    await StartupConfiguration.ApplyAsync(configureArgument["--configure-startup=".Length..],
                        new JsonSettingsStore(paths, logger),
                        enabled => new WindowsStartupRegistration().SetEnabled(enabled, Environment.ProcessPath!));
                    Shutdown(0);
                }
                catch (Exception ex) { logger.Error("Installer startup configuration failed.", ex); Shutdown(3); }
                return;
            }
            _runtime = await SnappySnapRuntime.CreateAsync().ConfigureAwait(true);
            _runtime.Start();
            if (e.Args.Any(arg => string.Equals(arg, "--screenshot-harness", StringComparison.OrdinalIgnoreCase)))
            {
                _ = _runtime.TriggerScreenshotForHarnessAsync();
            }
            if (e.Args.Any(arg => string.Equals(arg, "--recording-harness", StringComparison.OrdinalIgnoreCase)))
            {
                var qualityArgument = e.Args.FirstOrDefault(arg => arg.StartsWith("--quality-harness=", StringComparison.OrdinalIgnoreCase));
                _ = _runtime.TriggerRecordingForHarnessAsync(qualityArgument?[(qualityArgument.IndexOf('=') + 1)..], e.Args.Any(arg => string.Equals(arg, "--audio-harness", StringComparison.OrdinalIgnoreCase)));
            }
            if (e.Args.Any(arg => string.Equals(arg, "--video-edit-harness", StringComparison.OrdinalIgnoreCase)))
            {
                _ = _runtime.TriggerVideoEditForHarnessAsync();
            }
            if (e.Args.Any(arg => string.Equals(arg, "--editor-harness", StringComparison.OrdinalIgnoreCase)))
            {
                _ = _runtime.TriggerEditorForHarnessAsync();
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(L.F("SnappySnap could not start.\n\n{0}", ex.Message), "SnappySnap", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _lifetimeGate?.Dispose();
        base.OnExit(e);
    }

    public async Task RequestExitAsync() => await ExitAsync(null);

    public async Task RequestUpdateAsync(UpdateCoordinator updates)
    {
        try
        {
            SnappySnap.Updates.UpdateInstallPolicy.RequireInstalledExecutable(Environment.ProcessPath);
            if (!await updates.PrepareAsync()) return;
            var result = await ExitAsync(updates.Download);
            if (result != ExitPreparationResult.Ready) updates.PreparationCancelled();
        }
        catch (Exception ex) { updates.Fail(ex); }
    }

    private async Task<ExitPreparationResult> ExitAsync(DownloadedUpdate? update)
    {
        if (_runtime is null) return ExitPreparationResult.Busy;
        UpdateHandoff? handoff = null;
        void Block(bool value) { if (_runtime is not null) _runtime.IsExiting = value; }
        try
        {
            var preparation = await _exitPreparation.PrepareAsync(
                () => !_runtime.CanExit || Windows.OfType<SettingsWindow>().Any(window => window.IsSaving)
                    || Windows.OfType<EditorWindow>().Any(window => window.IsSaving)
                    || Windows.OfType<VideoEditorWindow>().Any(window => window.IsExporting)
                    || Windows.OfType<ShelfWindow>().Any(window => window.IsSaving),
                () =>
                {
                    foreach (var window in Windows.OfType<Window>().Where(window => window is EditorWindow or VideoEditorWindow).ToArray())
                    {
                        window.Close(); if (window.IsVisible) return false;
                    }
                    return true;
                },
                async () =>
                {
                    // Close command surfaces before awaiting a helper, so no new export or settings save can start.
                    foreach (var window in Windows.OfType<Window>().Where(window => window.IsVisible).ToArray()) window.Close();
                    if (update is not null) handoff = await UpdateHandoff.StartAsync(update);
                }, Block);
            if (preparation != ExitPreparationResult.Ready)
            {
                if (preparation == ExitPreparationResult.Busy)
                    MessageBox.Show(L.T("Finish the capture, save or export before exiting SnappySnap."), "SnappySnap", MessageBoxButton.OK, MessageBoxImage.Information);
                return preparation;
            }
            await _runtime.DisposeAsync(); _runtime = null;
            if (handoff is not null) await handoff.CommitAsync();
            Shutdown(); return ExitPreparationResult.Ready;
        }
        catch (Exception ex)
        {
            using var shutdownLogger = new FileLogger(new AppPaths().LogsPath);
            shutdownLogger.Error("Could not finish shutdown or update.", ex);
            MessageBox.Show(L.T("Could not finish shutdown or update. Check the local log and try again."), "SnappySnap", MessageBoxButton.OK, MessageBoxImage.Error);
            if (_runtime is null) Shutdown(1);
            return ExitPreparationResult.Error;
        }
        finally
        {
            if (handoff is not null) await handoff.DisposeAsync();
            _exitPreparation.Reset(Block);
        }
    }

}

public sealed class SnappySnapRuntime : IAsyncDisposable
{
    private const int ScreenshotHotkeyId = 1001;
    private const int VideoHotkeyId = 1002;
    private const int PauseHotkeyId = 1003;
    private readonly AppPaths _paths;
    private readonly FileLogger _logger;
    private SettingsSession _settingsSession = null!;
    private readonly System.Windows.Threading.DispatcherTimer _preferenceTimer = new() { Interval = TimeSpan.FromMilliseconds(500) };
    private long _preferenceRevision, _savedPreferenceRevision;
    private readonly Win32MonitorTopologyService _topology;
    private readonly CapturePlanBuilder _planBuilder = new();
    private readonly IHistoryRepository _history;
    private readonly IThumbnailService _thumbnails;
    private readonly ShelfService _shelf;
    private readonly IRecoveryStore _recoveryStore;
    private readonly RecordingSessionCoordinator _recording;
    private readonly ScreenshotCaptureCoordinator _screenshots;
    private readonly WindowsStartupRegistration _startup = new();
    private readonly HiddenHostWindow _host;
    private readonly GlobalHotkeyService _hotkeys;
    private AppSettings _settings = AppSettings.Defaults();
    private RegionSelectorService? _selector;
    private RecordingOverlayController? _recordingOverlays;
    private ClickRippleOverlayController? _clickOverlay;
    private ShelfWindow? _shelfWindow;
    private ShelfWindow? _compactWindow;
    private ShelfState? _shelfState;
    private ShelfState? _compactState;
    private SettingsWindow? _settingsWindow;
    private UpdateCoordinator _updates = null!;
    private SnappySnap.Updates.GitHubUpdateService _githubUpdates = null!;
    private SnappySnap.Updates.UpdateCheckSchedule _updateSchedule = null!;
    private readonly System.Windows.Threading.DispatcherTimer _updateTimer = new();
    private bool _automaticUpdateCheckRunning;
    private bool _started;
    private bool _screenshotRunning;
    private bool _videoStarting;
    private bool _videoStopping;
    private string? _recoveryFolder;
    private bool _recordingHarness;
    private bool _audioHarness;
    private int _recordingHarnessRun;
    private bool _hotkeyEventsAttached;
    private Task _recoveryTask = Task.CompletedTask;
    public bool IsExiting { get; set; }
    public bool CanExit => _shelfState?.IsSaving != true && _compactState?.IsSaving != true && (!_screenshotRunning || _screenshots.Snapshot.State == ScreenshotState.Editing) && !_videoStarting &&
        _recording.Snapshot.State is RecordingState.Idle or RecordingState.Completed or RecordingState.Error or RecordingState.RecoveryRequired;

    private SnappySnapRuntime(
        AppPaths paths,
        FileLogger logger,
        JsonSettingsStore settingsStore,
        Win32MonitorTopologyService topology,
        IHistoryRepository history,
        IThumbnailService thumbnails,
        ShelfService shelf,
        IRecoveryStore recoveryStore,
        RecordingSessionCoordinator recording,
        ScreenshotCaptureCoordinator screenshots)
    {
        _paths = paths;
        _logger = logger;
        _preferenceTimer.Tick += async (_, _) => { _preferenceTimer.Stop(); await SavePreferencesAsync(); };
        _topology = topology;
        _history = history;
        _thumbnails = thumbnails;
        _shelf = shelf;
        _recoveryStore = recoveryStore;
        _recording = recording;
        _screenshots = screenshots;
        _host = new HiddenHostWindow();
        _host.Show();
        _host.Hide();
        _hotkeys = new GlobalHotkeyService(_host, logger);
        _recording.StateChanged += OnRecordingStateChanged;
    }

    public static async Task<SnappySnapRuntime> CreateAsync()
    {
        var paths = new AppPaths();
        paths.EnsureDirectories();
        var logger = new FileLogger(paths.LogsPath);
        logger.Info("Runtime creation started.");
        var settingsStore = new JsonSettingsStore(paths, logger);
        var settings = await settingsStore.LoadAsync(CancellationToken.None).ConfigureAwait(true);
        L.SetLanguage(settings.General.Language);
        Ui.ApplyTheme(settings.General.Theme);
        logger.Info("Settings loaded.");
        var topology = new Win32MonitorTopologyService();
        var history = new SqliteHistoryRepository(paths.DatabasePath, logger);
        var thumbnails = new ThumbnailService(paths.ThumbnailPath, history, logger);
        var shelf = new ShelfService(history, thumbnails);
        var recovery = new JsonRecoveryStore(paths);
        var screenshotCapture = new WindowsGraphicsCaptureScreenshotService(logger, topology);
        var screenshotCoordinator = new ScreenshotCaptureCoordinator(screenshotCapture);
        var backendFactory = new ScreenRecorderLibVideoBackendFactory(logger);
        var recording = new RecordingSessionCoordinator(backendFactory, history, thumbnails, recovery, logger, new SystemMonotonicClock());
        var runtime = new SnappySnapRuntime(paths, logger, settingsStore, topology, history, thumbnails, shelf, recovery, recording, screenshotCoordinator)
        {
            _settings = settings
        };
        runtime._settingsSession = new SettingsSession(settings, settingsStore);
        runtime._githubUpdates = SnappySnap.Updates.GitHubUpdateService.Production(
            SnappySnap.Updates.ReleaseVerifier.Production(), AppVersion.Current,
            SnappySnap.Updates.UpdateInstallPolicy.CacheRoot, Environment.OSVersion.Version.Build);
        runtime._updates = new UpdateCoordinator(runtime._githubUpdates, logger);
        runtime._updateSchedule = new SnappySnap.Updates.UpdateCheckSchedule(paths.UpdateCheckStatePath, logger);
        await runtime._updateSchedule.LoadAsync(CancellationToken.None).ConfigureAwait(true);
        runtime._updates.Changed += (_, _) => runtime.UpdateNotices();
        logger.Info("Runtime creation completed.");
        return runtime;
    }

    public void Start()
    {
        if (_started) return;
        _started = true;
        _logger.Info("Runtime start entered.");
        _updateTimer.Interval = TimeSpan.FromSeconds(30);
        _updateTimer.Tick += async (_, _) =>
        {
            _updateTimer.Interval = TimeSpan.FromMinutes(1);
            if (!IsExiting && _settingsWindow?.IsSaving != true && _settings.Updates.AutomaticChecks && !_updates.Busy)
                await CheckForUpdatesAsync(automatic: true);
        };
        _updateTimer.Start();
        _ = RestoreCachedUpdateAsync();
        RegisterHotkeys();
        _selector = new RegionSelectorService(_topology, _logger);
        CreateTrayIcon();
        _startupNotice = new StartupNoticeWindow(_settings.Hotkeys, _hotkeys.Results.Where(result => !result.Registered).Select(result => result.Id).ToHashSet(), _logger);
        _startupNotice.Closed += (_, _) => _startupNotice = null;
        _startupNotice.Show();
        Microsoft.Win32.SystemEvents.UserPreferenceChanged += OnSystemAppearanceChanged;
        try
        {
            if (!string.IsNullOrWhiteSpace(Environment.ProcessPath))
            {
                _startup.SetEnabled(_settings.General.StartWithWindows, Environment.ProcessPath);
            }
        }
        catch (Exception ex) { _logger.Warn("Could not apply Windows startup setting.", new Dictionary<string, object?> { ["error"] = ex.Message }); }
        _recoveryTask = ReconcileRecoveryAsync();
        _logger.Info("SnappySnap started.", new Dictionary<string, object?> { ["monitors"] = _topology.GetMonitors().Count, ["hotkeysRegistered"] = _hotkeys.Results.All(r => r.Registered) });
    }

    public Task TriggerScreenshotForHarnessAsync() => StartScreenshotAsync();

    public async Task TriggerVideoEditForHarnessAsync()
    {
        try
        {
            var source = Directory.EnumerateFiles(_paths.ExpandCaptureRoot(_settings.General.CaptureRoot), "*.mp4", SearchOption.AllDirectories)
                .Where(path => !path.EndsWith("-edited-smoke.mp4", StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(File.GetLastWriteTimeUtc)
                .FirstOrDefault();
            if (source is null)
            {
                throw new FileNotFoundException("The video edit harness could not find a recorded MP4.");
            }

            var sourceFile = await StorageFile.GetFileFromPathAsync(source).AsTask().ConfigureAwait(true);
            var sourceClip = await MediaClip.CreateFromFileAsync(sourceFile).AsTask().ConfigureAwait(true);
            var duration = sourceClip.OriginalDuration;
            if (duration < TimeSpan.FromSeconds(4))
            {
                throw new InvalidOperationException($"The source video is too short for the edit harness ({duration.TotalSeconds:F2}s).");
            }

            var trimStart = TimeSpan.FromMilliseconds(250);
            var trimEnd = TimeSpan.FromMilliseconds(250);
            var middleStart = TimeSpan.FromSeconds(1.25);
            var middleEnd = TimeSpan.FromSeconds(Math.Min(duration.TotalSeconds - 1.25, 2.75));
            var timeline = new VideoEditTimeline(duration, trimStart, trimEnd, new[] { new TimeRange(middleStart, middleEnd) });
            var destination = Path.Combine(_paths.TempPath, $"{Path.GetFileNameWithoutExtension(source)}-edited-smoke.mp4");
            var service = new WindowsMediaVideoEditingService();
            var result = await service.ExportAsync(source, destination, timeline, new Progress<double>(value => _logger.Info("Video edit harness progress.", new Dictionary<string, object?> { ["progress"] = value })), CancellationToken.None).ConfigureAwait(true);
            _logger.Info("Video edit harness completed.", new Dictionary<string, object?>
            {
                ["sourcePath"] = source,
                ["destinationPath"] = result.OutputPath,
                ["sourceDurationSeconds"] = duration.TotalSeconds,
                ["outputDurationSeconds"] = result.Duration.TotalSeconds,
                ["backend"] = result.BackendDiagnostics
            });
            ShowBalloon("Video edit harness complete", Path.GetFileName(result.OutputPath));
        }
        catch (Exception ex)
        {
            _logger.Error("Video edit harness failed.", ex, new Dictionary<string, object?> { ["hresult"] = ex.HResult });
            ShowBalloon("Video edit harness failed", ex.Message);
        }
    }

    public async Task TriggerEditorForHarnessAsync()
    {
        try
        {
            const int width = 640;
            const int height = 400;
            var pixels = new byte[width * height * 4];
            for (var y = 0; y < height; y++) for (var x = 0; x < width; x++)
            {
                var offset = (y * width + x) * 4;
                pixels[offset] = (byte)(x * 255 / width);
                pixels[offset + 1] = (byte)(y * 255 / height);
                pixels[offset + 2] = (byte)((x + y) * 255 / (width + height));
                pixels[offset + 3] = 255;
            }

            var document = new EditorDocument(new CapturedImage(width, height, pixels));
            document.Elements.Add(new RectangleElement(new System.Windows.Rect(30, 30, 180, 90)));
            document.Elements.Add(new ArrowElement(new System.Windows.Point(40, 160), new System.Windows.Point(250, 220)));
            document.Elements.Add(new LineElement(new System.Windows.Point(260, 40), new System.Windows.Point(420, 110)));
            document.Elements.Add(new FreehandElement(new[] { new System.Windows.Point(280, 150), new System.Windows.Point(300, 175), new System.Windows.Point(330, 145), new System.Windows.Point(360, 185) }));
            document.Elements.Add(new TextElement("SnappySnap", new System.Windows.Point(40, 260)));
            document.Elements.Add(new HighlightElement(new System.Windows.Rect(220, 250, 150, 35)));
            document.Elements.Add(new BlurElement(new System.Windows.Rect(390, 40, 90, 90)));
            document.Elements.Add(new PixelateElement(new System.Windows.Rect(500, 200, 100, 100)));
            document.Elements.Add(new StepMarkerElement(1, new System.Windows.Point(530, 80)));
            document.CropRect = new System.Windows.Rect(20, 20, 580, 340);
            var output = Path.Combine(_paths.TempPath, "editor-harness.png");
            await EditorRenderer.SavePngAsync(document, output, CancellationToken.None).ConfigureAwait(true);
            _logger.Info("Editor harness completed.", new Dictionary<string, object?> { ["path"] = output, ["elements"] = document.Elements.Count, ["crop"] = document.CropRect.ToString(System.Globalization.CultureInfo.InvariantCulture) });
            ShowBalloon("Editor harness complete", Path.GetFileName(output));
        }
        catch (Exception ex)
        {
            _logger.Error("Editor harness failed.", ex);
            ShowBalloon("Editor harness failed", ex.Message);
        }
    }

    public async Task TriggerRecordingForHarnessAsync(string? qualityOverride = null, bool audioHarness = false)
    {
        _logger.Info("Recording harness requested.");
        var originalQuality = _settings.Recording.QualityProfile;
        if (!string.IsNullOrWhiteSpace(qualityOverride))
        {
            _settings.Recording.QualityProfile = QualityProfileCatalog.Resolve(qualityOverride).Key;
            _logger.Info("Recording harness quality override applied.", new Dictionary<string, object?> { ["qualityProfile"] = _settings.Recording.QualityProfile });
        }
        _recordingHarness = true;
        _audioHarness = audioHarness;
        try
        {
            var monitors = _topology.GetMonitors();
            if (monitors.Count == 0) throw new InvalidOperationException("No display is available for the recording harness.");
            var monitor = monitors[0];
            var requested = new VirtualPixelRect(monitor.Bounds.X + 100, monitor.Bounds.Y + 100, 400, 300);
            var plan = _planBuilder.Build(requested, monitors);
            _logger.Info("Recording harness using a deterministic capture plan.", new Dictionary<string, object?>
            {
                ["widthPx"] = plan.OutputWidth,
                ["heightPx"] = plan.OutputHeight,
                ["segments"] = plan.Segments.Count
            });
            await StartVideoWithPlanAsync(plan).ConfigureAwait(true);
        }
        finally
        {
            _settings.Recording.QualityProfile = originalQuality;
        }
    }

    private void RegisterHotkeys()
    {
        _hotkeys.Apply(GlobalHotkeyService.Bindings(_settings.Hotkeys), rollbackOnFailure: false);
        if (!_hotkeyEventsAttached)
        {
            _hotkeys.HotkeyPressed += OnHotkeyPressed;
            _hotkeys.RegistrationsChanged += (_, _) =>
            {
                _shelfWindow?.Shelf.SetWarning(_hotkeys.Warning);
                _compactWindow?.Shelf.SetWarning(_hotkeys.Warning);
            };
            _hotkeyEventsAttached = true;
        }
    }

    private void OnHotkeyPressed(object? sender, int id)
    {
        switch (id)
        {
            case ScreenshotHotkeyId:
                _ = StartScreenshotAsync();
                break;
            case VideoHotkeyId:
                _ = ToggleVideoAsync();
                break;
            case PauseHotkeyId:
                _ = PauseVideoAsync();
                break;
            case 1004:
                OpenShelf(true);
                break;
        }
    }

    private async Task StartScreenshotAsync()
    {
        if (IsExiting) return;
        if (_screenshotRunning || _videoStarting || _recording.Snapshot.State is not (RecordingState.Idle or RecordingState.Completed)) return;
        _screenshotRunning = true;
        SetShelfCaptureInProgress(true);
        FrozenDesktopSnapshot? frozenSnapshot = null;
        try
        {
            _screenshots.Begin();
            _topology.Invalidate();
            var monitors = _topology.GetMonitors();
            var fullDesktopPlan = _planBuilder.Build(_topology.VirtualDesktopBounds, monitors);
            var preparationTimer = Stopwatch.StartNew();
            frozenSnapshot = await _screenshots.PrepareSelectionAsync(fullDesktopPlan, CancellationToken.None).ConfigureAwait(true);
            preparationTimer.Stop();
            _logger.Info("Screenshot selector snapshot prepared.", new Dictionary<string, object?>
            {
                ["backend"] = _screenshots.CaptureBackendName,
                ["monitors"] = monitors.Count,
                ["widthPx"] = frozenSnapshot.CapturedImage.Width,
                ["heightPx"] = frozenSnapshot.CapturedImage.Height,
                ["elapsedMs"] = preparationTimer.Elapsed.TotalMilliseconds,
                ["bitmapBytes"] = checked((long)frozenSnapshot.CapturedImage.Width * frozenSnapshot.CapturedImage.Height * 4)
            });
            var region = await _selector!.SelectAsync(frozenSnapshot, monitors, allowFullMonitor: true).ConfigureAwait(true);
            frozenSnapshot = null;
            if (region is null) { _screenshots.Cancel(); return; }
            var plan = _planBuilder.Build(region.Value, monitors);
            await CompleteScreenshotWithPlanAsync(plan);
        }
        catch (Exception ex)
        {
            _logger.Error("Screenshot flow failed.", ex);
            if (_screenshots.Snapshot.State == ScreenshotState.Exporting) _screenshots.FailExport(ex);
            _screenshots.Cancel();
            ShowBalloon("Screenshot failed", "Could not capture the selected region. Try again or check the local log.");
        }
        finally
        {
            frozenSnapshot = null;
            SetShelfCaptureInProgress(false);
            _screenshotRunning = false;
        }
    }

    private async Task CaptureShelfAsync(ShelfWindow shelf)
    {
        if (IsExiting || _screenshotRunning || _videoStarting || _recording.Snapshot.State is not (RecordingState.Idle or RecordingState.Completed)) return;
        shelf.UpdateLayout();
        if (!shelf.TryGetCaptureBounds(out var bounds))
        {
            ShowBalloon("Shelf screenshot failed", "The Shelf window is not ready to capture. Try again.");
            return;
        }

        _screenshotRunning = true;
        SetShelfCaptureInProgress(true);
        try
        {
            _topology.Invalidate();
            _screenshots.Begin();
            var plan = _planBuilder.Build(bounds, _topology.GetMonitors());
            var image = await _screenshots.CaptureDirectAsync(plan, CancellationToken.None);
            await CompleteScreenshotAsync(plan, image);
        }
        catch (Exception ex)
        {
            _logger.Error("Shelf screenshot flow failed.", ex);
            if (_screenshots.Snapshot.State == ScreenshotState.Exporting) _screenshots.FailExport(ex);
            _screenshots.Cancel();
            ShowBalloon("Shelf screenshot failed", "Could not capture the selected region. Try again or check the local log.");
        }
        finally
        {
            SetShelfCaptureInProgress(false);
            _screenshotRunning = false;
        }
    }

    private async Task CompleteScreenshotWithPlanAsync(CapturePlan plan)
    {
        var image = await _screenshots.CaptureAsync(plan, CancellationToken.None).ConfigureAwait(true);
        await CompleteScreenshotAsync(plan, image);
    }

    private async Task CompleteScreenshotAsync(CapturePlan plan, CapturedImage image)
    {
        SetShelfCaptureInProgress(false);
        var editor = new EditorWindow(image, _logger, _settings.Screenshot.Format, _settings.Editor);
        ConfigureEditor(editor);
        var save = new ScreenshotSaveSession(editor.Document, _shelf,
            () => _paths.ExpandCaptureRoot(_settings.General.CaptureRoot), plan.SelectedVirtualBounds,
            bitmap => { if (_settings.Screenshot.CopyToClipboard) Clipboard.SetImage(bitmap); },
            message => ShowBalloon("Screenshot saved", message), _logger);
        _screenshots.BeginExport();
        try
        {
            await save.SaveOriginalAsync(_settings.Screenshot.Format);
            _screenshots.ConfirmOriginalSaved();
            await ShowSavedShelfAsync(plan);
        }
        catch (Exception ex)
        {
            _logger.Error("Initial screenshot save failed; image retained in editor.", ex);
            _screenshots.FailExport(ex);
            editor.ShowInitialSaveFailure(ex.Message);
        }
        editor.SaveRequestedAsync = async request =>
        {
            _screenshots.BeginExport();
            try { await save.SaveAsync(request); _screenshots.ConfirmExported(); }
            catch (Exception ex) { _screenshots.FailExport(ex); throw; }
        };
        var accepted = await editor.ShowAsync();
        if (!accepted) { _screenshots.Cancel(); return; }
        await ShowSavedShelfAsync(plan);
    }

    private void SetShelfCaptureInProgress(bool value)
    {
        _shelfWindow?.Shelf.SetCaptureInProgress(value);
        _compactWindow?.Shelf.SetCaptureInProgress(value);
    }

    private async Task ToggleVideoAsync()
    {
        if (IsExiting) return;
        _logger.Info("Video toggle entered.", new Dictionary<string, object?> { ["state"] = _recording.Snapshot.State.ToString(), ["harness"] = _recordingHarness });
        if (_videoStarting || _screenshotRunning) return;
        if (_recording.Snapshot.State is RecordingState.Recording or RecordingState.Paused)
        {
            await StopVideoAsync().ConfigureAwait(true);
            return;
        }
        if (_recording.Snapshot.State != RecordingState.Idle) return;
        _videoStarting = true;
        try
        {
            _logger.Info("Opening region selector for video.");
            var region = await _selector!.SelectAsync().ConfigureAwait(true);
            _logger.Info("Video region selector completed.", new Dictionary<string, object?> { ["selected"] = region is not null, ["widthPx"] = region?.Width, ["heightPx"] = region?.Height });
            if (region is null) return;
            var plan = _planBuilder.Build(region.Value, _topology.GetMonitors());
            await StartVideoWithPlanAsync(plan).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            _logger.Error("Video start flow failed.", ex);
            _clickOverlay?.Dispose(); _clickOverlay = null;
            _recordingOverlays?.Dispose(); _recordingOverlays = null;
            ShowBalloon("Recording failed", "Could not start recording. Check audio devices and the destination folder.");
        }
        finally { _videoStarting = false; }
    }

    private async Task StartVideoWithPlanAsync(CapturePlan plan)
    {
        if (_recording.Snapshot.State != RecordingState.Idle) return;
        var ownsStartGate = !_videoStarting;
        if (ownsStartGate) _videoStarting = true;
        try
        {
            _recordingOverlays = new RecordingOverlayController(plan.SelectedVirtualBounds, _topology.GetMonitors(), _settings, _logger, () => _recording.Snapshot);
            _recordingOverlays.PauseClicked += async (_, _) => await PauseVideoAsync();
            _recordingOverlays.StopClicked += async (_, _) => await StopVideoAsync();
            _recordingOverlays.SystemAudioClicked += async (_, enabled) => await SetRecordingAudioAsync(false, enabled);
            _recordingOverlays.MicrophoneClicked += async (_, enabled) => await SetRecordingAudioAsync(true, enabled);
            _recordingOverlays.Show();
            _clickOverlay = new ClickRippleOverlayController(_topology.GetMonitors(), _settings, _logger);
            await _recording.StartAsync(plan, _paths.ExpandCaptureRoot(_settings.General.CaptureRoot), _paths.TempPath, _settings, CancellationToken.None).ConfigureAwait(true);
            ShowBalloon("Recording started", "Ctrl+Alt+E stops; Ctrl+Alt+Space pauses.");
        }
        catch (Exception ex)
        {
            _logger.Error("Video start flow failed.", ex);
            _clickOverlay?.Dispose(); _clickOverlay = null;
            _recordingOverlays?.Dispose(); _recordingOverlays = null;
            ShowBalloon("Recording failed", "Could not start recording. Check audio devices and the destination folder.");
        }
        finally
        {
            if (ownsStartGate) _videoStarting = false;
        }
    }

    private async Task PauseVideoAsync()
    {
        if (_recording.Snapshot.State is not (RecordingState.Recording or RecordingState.Paused)) return;
        try { await _recording.PauseOrResumeAsync(CancellationToken.None).ConfigureAwait(true); }
        catch (Exception ex) { _logger.Error("Video pause/resume failed.", ex); ShowBalloon("Recording interrupted", "The recording artifact was retained. Try starting a new recording."); }
    }

    private async Task SetRecordingAudioAsync(bool microphone, bool enabled)
    {
        try
        {
            if (microphone) await _recording.SetMicrophoneEnabledAsync(enabled, CancellationToken.None);
            else await _recording.SetSystemAudioEnabledAsync(enabled, CancellationToken.None);
        }
        catch (Exception ex)
        {
            _logger.Error("Could not change recording audio.", ex);
            ShowBalloon("Audio control failed", "Check the audio device and try again.");
        }
        finally { _recordingOverlays?.Update(_recording.Snapshot); }
    }

    private async Task StopVideoAsync()
    {
        if (_videoStopping) return;
        _videoStopping = true;
        _clickOverlay?.Dispose(); _clickOverlay = null;
        try
        {
            var savedPlan = _recording.Snapshot.CapturePlan;
            await _recording.StopAsync(CancellationToken.None).ConfigureAwait(true);
            await ShowSavedShelfAsync(savedPlan);
        }
        catch (Exception ex)
        {
            _logger.Error("Video stop flow failed.", ex);
            ShowBalloon("Recording finalization failed", "The temporary artifact was retained for recovery.");
            _recoveryFolder = _paths.TempPath;
            UpdateNotices();
        }
        finally { _recordingOverlays?.Dispose(); _recordingOverlays = null; _videoStopping = false; }
    }

    private void OnRecordingStateChanged(object? sender, RecordingSnapshot snapshot)
    {
        if (!System.Windows.Application.Current.Dispatcher.CheckAccess())
        {
            System.Windows.Application.Current.Dispatcher.BeginInvoke(() => OnRecordingStateChanged(sender, snapshot));
            return;
        }
        _recordingOverlays?.Update(snapshot);
        _clickOverlay?.SetRecording(snapshot.State == RecordingState.Recording);
        if (snapshot.State is RecordingState.Error or RecordingState.RecoveryRequired)
        {
            _clickOverlay?.Dispose(); _clickOverlay = null;
            _recordingOverlays?.Dispose(); _recordingOverlays = null;
            ShowBalloon("Recording interrupted", "The recording artifact was retained for recovery. Try starting a new recording.");
            _recoveryFolder = _paths.TempPath; UpdateNotices();
        }
        if (_recordingHarness && snapshot.State == RecordingState.Recording && Interlocked.Exchange(ref _recordingHarnessRun, 1) == 0)
        {
            _ = RunRecordingHarnessAsync();
        }
    }

    private async Task RunRecordingHarnessAsync()
    {
        try
        {
            var audioTask = _audioHarness ? RunAudioHarnessAsync() : null;
            // Keep the real selection/overlay/backend path, but make pause/resume
            // deterministic so the Windows acceptance harness does not depend on
            // a potentially-conflicting global hotkey.
            await Task.Delay(TimeSpan.FromSeconds(5)).ConfigureAwait(true);
            await InvokeOnUiAsync(PauseVideoAsync).ConfigureAwait(false);
            await Task.Delay(TimeSpan.FromSeconds(3)).ConfigureAwait(true);
            await InvokeOnUiAsync(PauseVideoAsync).ConfigureAwait(false);
            await Task.Delay(TimeSpan.FromSeconds(5)).ConfigureAwait(true);
            await InvokeOnUiAsync(StopVideoAsync).ConfigureAwait(false);
            if (audioTask is not null) await audioTask.ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.Error("Recording harness failed.", ex);
        }
        finally
        {
            _recordingHarness = false;
            _audioHarness = false;
        }
    }

    private async Task RunAudioHarnessAsync()
    {
        await Task.Delay(TimeSpan.FromSeconds(2)).ConfigureAwait(true);
        await InvokeOnUiAsync(() => _recording.SetSystemAudioEnabledAsync(false, CancellationToken.None)).ConfigureAwait(false);
        _logger.Info("Audio harness toggled system audio OFF.");
        await Task.Delay(TimeSpan.FromSeconds(2)).ConfigureAwait(true);
        await InvokeOnUiAsync(() => _recording.SetSystemAudioEnabledAsync(true, CancellationToken.None)).ConfigureAwait(false);
        _logger.Info("Audio harness toggled system audio ON.");
    }

    private static async Task InvokeOnUiAsync(Func<Task> operation)
    {
        var dispatcher = System.Windows.Application.Current.Dispatcher;
        if (dispatcher.CheckAccess())
        {
            await operation().ConfigureAwait(true);
            return;
        }

        await dispatcher.InvokeAsync(operation).Task.Unwrap().ConfigureAwait(false);
    }

    private void CreateTrayIcon()
    {
        var tray = new System.Windows.Forms.NotifyIcon
        {
            Icon = LoadTrayIcon(),
            Visible = true,
            Text = "SnappySnap"
        };
        var menu = new System.Windows.Forms.ContextMenuStrip();
        menu.Items.Add("Take screenshot", null, async (_, _) => await StartScreenshotAsync());
        menu.Items.Add("Start/stop recording", null, async (_, _) => await ToggleVideoAsync());
        menu.Items.Add("Pause/resume recording", null, async (_, _) => await PauseVideoAsync());
        menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
        menu.Items.Add("Open Shelf", null, (_, _) => OpenShelf());
        menu.Items.Add("Settings", null, (_, _) => OpenSettings());
        menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
        menu.Items.Add("Exit", null, async (_, _) => await ((App)System.Windows.Application.Current).RequestExitAsync());
        foreach (System.Windows.Forms.ToolStripItem item in menu.Items) item.Tag = item.Text;
        menu.Opening += (_, _) => { foreach (System.Windows.Forms.ToolStripItem item in menu.Items) if (item.Tag is string key) item.Text = L.T(key); };
        tray.ContextMenuStrip = menu;
        tray.MouseClick += (_, e) => { if (e.Button == System.Windows.Forms.MouseButtons.Left) OpenShelf(true); };
        _tray = tray;
    }

    private System.Windows.Forms.NotifyIcon? _tray;
    private StartupNoticeWindow? _startupNotice;
    private void OnSystemAppearanceChanged(object sender, Microsoft.Win32.UserPreferenceChangedEventArgs e)
    {
        if (e.Category is not (Microsoft.Win32.UserPreferenceCategory.General or Microsoft.Win32.UserPreferenceCategory.Color or Microsoft.Win32.UserPreferenceCategory.VisualStyle or Microsoft.Win32.UserPreferenceCategory.Accessibility)) return;
        _host.Dispatcher.BeginInvoke(() =>
        {
            if (IsExiting) return;
            Ui.ApplyTheme(_settingsWindow?.PreviewTheme ?? _settings.General.Theme);
            if (_tray is not null) { var previous = _tray.Icon; _tray.Icon = LoadTrayIcon(); previous?.Dispose(); }
        });
    }
    private static Icon LoadTrayIcon()
    {
        var asset = Ui.SystemUsesLightTheme(taskbar: true) ? "TrayLight.ico" : "TrayDark.ico";
        using var stream = System.Windows.Application.GetResourceStream(new Uri("/SnappySnap;component/Resources/" + asset, UriKind.Relative)).Stream;
        using var icon = new Icon(stream); return (Icon)icon.Clone();
    }

    private void OpenShelf(bool compact = false, CapturePlan? plan = null)
    {
        if (IsExiting) return;
        ShelfState CreateState()
        {
            var state = new ShelfState { ConfigureEditor = ConfigureEditor, Notify = message => ShowBalloon("Screenshot saved", message) };
            state.Saved += async (_, _) => await ShowSavedShelfAsync(null);
            state.Deleted += async (_, _) => await RefreshShelvesAsync();
            return state;
        }
        var state = compact ? _compactState ??= CreateState() : _shelfState ??= CreateState();
        var window = compact ? _compactWindow : _shelfWindow;
        if (window is not { IsVisible: true })
        {
            state.Loaded = false;
            var content = new ShelfContent(_shelf, _settings, _logger, state);
            content.ExpandRequested += (_, _) => { OpenShelf(); _compactWindow?.Close(); };
            content.ThumbnailHeightChanged += height =>
            {
                _settingsSession.SetShelfThumbnailHeight(height);
                _shelfWindow?.Shelf.UpdateSettings(_settings); _compactWindow?.Shelf.UpdateSettings(_settings);
                QueuePreferencesSave();
            };
            var placement = compact ? _settings.Shelf.Compact : _settings.Shelf.History;
            window = new ShelfWindow(content, compact, placement);
            window.PlacementChanged += value => { _settingsSession.SetShelfPlacement(compact, value); QueuePreferencesSave(); };
            window.Closed += async (_, _) => await SavePreferencesAsync();
            window.SettingsRequested += (_, _) => { OpenSettings(); _compactWindow?.Close(); };
            window.ScreenshotRequested += OnShelfScreenshotRequested;
            window.Closed += (_, _) => window.ScreenshotRequested -= OnShelfScreenshotRequested;
            if (compact) _compactWindow = window; else _shelfWindow = window;
        }
        else _ = window.Shelf.LoadAsync();
        window.Shelf.SetWarning(_hotkeys.Warning);
        UpdateNotices();
        if (compact)
        {
            if (!window.IsVisible)
            {
                if (_settings.Shelf.Compact is null)
                {
                    var pointer = System.Windows.Forms.Cursor.Position;
                    var point = plan is null ? new VirtualPixelPoint(pointer.X, pointer.Y) : new VirtualPixelPoint(plan.SelectedVirtualBounds.X, plan.SelectedVirtualBounds.Y);
                    _topology.Invalidate();
                    var monitors = _topology.GetMonitors();
                    window.Place(monitors.FirstOrDefault(m => m.Bounds.Contains(point)) ?? monitors.First(m => m.IsPrimary));
                }
                window.Show();
            }
            if (window.WindowState == WindowState.Minimized) window.WindowState = WindowState.Normal;
        }
        else { if (!window.IsVisible) window.Show(); if (window.WindowState == WindowState.Minimized) window.WindowState = WindowState.Normal; window.Activate(); }
    }

    private void OnShelfScreenshotRequested(object? sender, EventArgs e)
    {
        if (sender is ShelfWindow shelf) _ = CaptureShelfAsync(shelf);
    }

    private async Task ShowSavedShelfAsync(CapturePlan? plan)
    {
        try
        {
            OpenShelf(true, plan);
            await RefreshShelvesAsync();
        }
        catch (Exception ex) { _logger.Error("Capture saved, but Shelf could not open.", ex); ShowBalloon("Capture saved", "Could not open Shelf. Use the tray menu to try again."); }
    }

    private async Task RefreshShelvesAsync()
    {
        if (_compactWindow is { IsVisible: true }) await _compactWindow.Shelf.LoadAsync();
        if (_shelfWindow is { IsVisible: true }) await _shelfWindow.Shelf.LoadAsync();
    }

    private void UpdateNotices()
    {
        var message = _updates.Offer is null ? "" : L.F("SnappySnap {0} is available · Open update settings", _updates.Offer.Release.Version);
        _shelfWindow?.Shelf.SetUpdateNotice(message); _compactWindow?.Shelf.SetUpdateNotice(message);
        _shelfWindow?.Shelf.SetRecoveryNotice(_recoveryFolder, _paths.LogsPath); _compactWindow?.Shelf.SetRecoveryNotice(_recoveryFolder, _paths.LogsPath);
    }

    private void ConfigureEditor(EditorWindow editor)
    {
        editor.StyleChanged += (tool, style) =>
        {
            _settingsSession.SetStyle(tool, style); QueuePreferencesSave();
        };
        editor.Closed += async (_, _) => await SavePreferencesAsync();
    }

    private void QueuePreferencesSave()
    {
        _preferenceRevision++;
        _preferenceTimer.Stop(); _preferenceTimer.Start();
    }

    private async Task SavePreferencesAsync()
    {
        var revision = _preferenceRevision;
        if (revision == _savedPreferenceRevision) return;
        try
        {
            await Task.Run(() => _settingsSession.FlushPreferencesAsync(CancellationToken.None));
            _savedPreferenceRevision = Math.Max(_savedPreferenceRevision, revision);
        }
        catch (Exception ex)
        {
            _logger.Error("Preferences could not be saved.", ex);
            ShowBalloon("Settings", "Changes work for this session but could not be saved for the next launch.");
        }
    }

    private void OpenSettings()
    {
        if (IsExiting) return;
        if (_settingsWindow is { IsVisible: true }) { _settingsWindow.Activate(); return; }
        _settingsWindow = new SettingsWindow(_settings, _settingsSession, _startup, _logger, _hotkeys, _updates,
            () => ((App)System.Windows.Application.Current).RequestUpdateAsync(_updates), () => CheckForUpdatesAsync());
        _settingsWindow.Closed += (_, _) => _settingsWindow = null;
        _settingsWindow.SettingsSaved += (_, settings) =>
        {
            _settings = settings;
            L.SetLanguage(settings.General.Language); Ui.ApplyTheme(settings.General.Theme);
            if (!settings.Updates.AutomaticChecks && _automaticUpdateCheckRunning) _updates.Cancel();
            _shelfWindow?.Shelf.UpdateSettings(settings);
            _compactWindow?.Shelf.UpdateSettings(settings);
            UpdateNotices();
        };
        _settingsWindow.Show();
    }

    private async Task CheckForUpdatesAsync(bool automatic = false)
    {
        if (_updates.Busy || IsExiting) return;
        if (automatic)
        {
            if (!_settings.Updates.AutomaticChecks) return;
            try
            {
                if (!await _updateSchedule.BeginAutomaticAsync(CancellationToken.None)) return;
                if (!_settings.Updates.AutomaticChecks) return;
            }
            catch (Exception ex)
            {
                _logger.Error("Could not persist automatic update check schedule.", ex);
                return;
            }
            _automaticUpdateCheckRunning = true;
        }
        try
        {
            var outcome = await _updates.CheckAsync();
            try { await _updateSchedule.RecordAsync(outcome, CancellationToken.None); }
            catch (Exception ex) { _logger.Error("Could not save update check result.", ex); }
        }
        finally { if (automatic) _automaticUpdateCheckRunning = false; }
    }

    private async Task RestoreCachedUpdateAsync()
    {
        try
        {
            var cached = await _githubUpdates.FindCachedAsync(CancellationToken.None);
            if (!IsExiting && cached is not null) _updates.RestoreCached(cached);
        }
        catch (Exception ex) { _logger.Error("Could not restore cached update.", ex); }
    }

    private async Task ReconcileRecoveryAsync()
    {
        try
        {
            var records = await _recoveryStore.ScanAsync(CancellationToken.None).ConfigureAwait(true);
            foreach (var record in records.Where(x => x.State is not RecoveryState.Recovered))
            {
                if (record.SessionId == Guid.Empty)
                {
                    _logger.Warn("Invalid recovery record requires manual cleanup.", new Dictionary<string, object?> { ["path"] = record.TempPath, ["error"] = record.Error });
                    continue;
                }

                if (File.Exists(record.IntendedFinalPath) && new FileInfo(record.IntendedFinalPath).Length > 0)
                {
                    try
                    {
                        var existing = await _history.FindByPathAsync(record.IntendedFinalPath, CancellationToken.None).ConfigureAwait(true);
                        if (existing is null)
                        {
                            var info = await ReadMediaInfoAsync(record.IntendedFinalPath, CancellationToken.None).ConfigureAwait(true);
                            existing = await _history.AddAsync(new NewHistoryItem(
                                info.MediaType,
                                record.IntendedFinalPath,
                                record.StartedAtUtc,
                                info.WidthPx,
                                info.HeightPx,
                                info.Duration,
                                null,
                                info.SourceBounds), CancellationToken.None).ConfigureAwait(true);
                            _ = Task.Run(() => _thumbnails.EnsureThumbnailAsync(existing, CancellationToken.None));
                        }

                        await _recoveryStore.MarkAsync(record.SessionId, RecoveryState.Recovered, null, CancellationToken.None).ConfigureAwait(true);
                        _logger.Info("Recovery record reconciled into history.", new Dictionary<string, object?> { ["sessionId"] = record.SessionId, ["path"] = record.IntendedFinalPath });
                    }
                    catch (Exception ex)
                    {
                        _logger.Error("Finalized media could not be reconciled into history.", ex, new Dictionary<string, object?> { ["sessionId"] = record.SessionId, ["path"] = record.IntendedFinalPath });
                    }
                }
                else if (File.Exists(record.TempPath) && new FileInfo(record.TempPath).Length > 0)
                {
                    _logger.Warn("Recoverable temporary recording found at startup; it was retained for manual recovery.", new Dictionary<string, object?> { ["sessionId"] = record.SessionId, ["tempPath"] = record.TempPath, ["state"] = record.State.ToString() });
                    ShowBalloon("Recovery needed", Path.GetFileName(record.TempPath));
                    _recoveryFolder = Path.GetDirectoryName(record.TempPath); UpdateNotices();
                }
                else
                {
                    await _recoveryStore.MarkAsync(record.SessionId, RecoveryState.Unrecoverable, "Neither the intended final file nor a temporary artifact exists.", CancellationToken.None).ConfigureAwait(true);
                    _logger.Warn("Recovery record is unrecoverable because its media artifact is missing.", new Dictionary<string, object?> { ["sessionId"] = record.SessionId, ["finalPath"] = record.IntendedFinalPath, ["tempPath"] = record.TempPath });
                }
            }
        }
        catch (Exception ex) { _logger.Error("Recovery scan failed.", ex); }
    }

    private static async Task<RecoveredMediaInfo> ReadMediaInfoAsync(string path, CancellationToken cancellationToken)
    {
        if (new[] { ".png", ".jpg", ".jpeg" }.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase))
        {
            return await Task.Run(() =>
            {
                var image = EditorRenderer.LoadImage(path);
                return new RecoveredMediaInfo(MediaType.Screenshot, image.PixelWidth, image.PixelHeight, null, new VirtualPixelRect(0, 0, image.PixelWidth, image.PixelHeight));
            }, cancellationToken).ConfigureAwait(false);
        }

        var source = await StorageFile.GetFileFromPathAsync(path).AsTask(cancellationToken).ConfigureAwait(true);
        var clip = await MediaClip.CreateFromFileAsync(source).AsTask(cancellationToken).ConfigureAwait(true);
        var properties = clip.GetVideoEncodingProperties();
        return new RecoveredMediaInfo(MediaType.Video, checked((int)properties.Width), checked((int)properties.Height), clip.OriginalDuration, new VirtualPixelRect(0, 0, checked((int)properties.Width), checked((int)properties.Height)));
    }

    private sealed record RecoveredMediaInfo(MediaType MediaType, int WidthPx, int HeightPx, TimeSpan? Duration, VirtualPixelRect SourceBounds);

    private void ShowBalloon(string title, string text)
    {
        try { _tray?.ShowBalloonTip(2500, L.T(title), L.T(text), System.Windows.Forms.ToolTipIcon.Info); }
        catch { }
    }

    public async ValueTask DisposeAsync()
    {
        _preferenceTimer.Stop(); await SavePreferencesAsync();
        _updateTimer.Stop(); _updates.Dispose(); _updateSchedule.Dispose();
        await _recoveryTask.ConfigureAwait(true);
        _hotkeys.Dispose();
        _screenshots.Cancel();
        _selector?.Dispose();
        _clickOverlay?.Dispose();
        _startupNotice?.Close();
        _recordingOverlays?.Dispose();
        Microsoft.Win32.SystemEvents.UserPreferenceChanged -= OnSystemAppearanceChanged;
        var trayIcon = _tray?.Icon; _tray?.Dispose(); trayIcon?.Dispose();
        _host.Close();
        await _recording.DisposeAsync().ConfigureAwait(true);
        if (_history is IAsyncDisposable asyncDisposable) await asyncDisposable.DisposeAsync().ConfigureAwait(true);
        _settingsSession.Dispose();
        _logger.Dispose();
    }
}
