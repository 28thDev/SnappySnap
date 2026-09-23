#if DEBUG
using System.Globalization;
using System.Runtime.InteropServices;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using SnappySnap.Application;
using SnappySnap.Capture;
using SnappySnap.Core;
using SnappySnap.Localization;
using SnappySnap.Editor;
using SnappySnap.History;
using SnappySnap.Infrastructure;
using SnappySnap.Presentation;
using Windows.Media.Editing;
using Windows.Media.MediaProperties;
using Windows.Storage;

namespace SnappySnap.App;

/// <summary>Opt-in, isolated real-WPF visual scenarios. Never registers hotkeys or modifies startup/user settings.</summary>
internal static partial class VisualHarness
{
    private static readonly double[] ResizeZooms = [.25, .5, 1d, 2d];
    private static readonly string[] QualityProfiles = ["Compact", "Balanced", "High"];
    private static readonly string[] DisplayOptions = ["Brightness", "Night light", "Display resolution", "Scale", "Advanced display"];

    public static async Task RunUpdatesAsync(string output)
    {
        L.SetLanguage("en"); Ui.ApplyTheme("Dark");
        Directory.CreateDirectory(output);
        var paths = new AppPaths(Path.Combine(output, "profile"), output); paths.EnsureDirectories();
        using var logger = new FileLogger(paths.LogsPath);
        using var key = System.Security.Cryptography.ECDsa.Create(System.Security.Cryptography.ECCurve.NamedCurves.nistP256);
        var source = Path.Combine(output, "releases"); Directory.CreateDirectory(source);
        var package = new byte[3_000_000]; new Random(42).NextBytes(package);
        var release = new UpdateRelease("0.4.1", DateTimeOffset.UtcNow,
            "Проверка локальных обновлений. Настройки, история и захваты сохраняются.\nТестовый пакет: установка не запускается.", "SnappySnap-Setup-0.4.1-x64.exe", package.Length,
            Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(package)), "win-x64", 22000);
        var manifest = System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(release, SnappySnap.Updates.ReleaseVerifier.JsonOptions);
        await File.WriteAllBytesAsync(Path.Combine(source, release.FileName), package);
        await File.WriteAllBytesAsync(Path.Combine(source, "latest.json"), manifest);
        await File.WriteAllBytesAsync(Path.Combine(source, "latest.sig"), key.SignData(manifest, System.Security.Cryptography.HashAlgorithmName.SHA256, System.Security.Cryptography.DSASignatureFormat.IeeeP1363FixedFieldConcatenation));
        var coordinator = new UpdateCoordinator(new SnappySnap.Updates.LocalUpdateService(new(key.ExportSubjectPublicKeyInfoPem()), "0.4.0", source, Path.Combine(output, "cache"), 26100), logger);
        var settings = AppSettings.Defaults(); settings.General.StartWithWindows = false;
        Window PanelWindow()
        {
            var window = new Window { Width = 860, Height = 530, Title = "SnappySnap — Updates" };
            Ui.Shell(window, "", Ui.Card(new UpdatePanel(settings.Updates, coordinator, () => Task.CompletedTask, async () => { await coordinator.CheckAsync(); }))); return window;
        }
        await Snapshot(PanelWindow(), output, "updates-unconfigured");
        await coordinator.CheckAsync();
        if (coordinator.State != UpdateState.Available) throw new InvalidOperationException("Signed fixture was not offered.");
        await Snapshot(PanelWindow(), output, "updates-available");
        await coordinator.DownloadAsync(); if (coordinator.State != UpdateState.Ready) throw new InvalidOperationException("Signed fixture did not download.");
        await Snapshot(PanelWindow(), output, "updates-ready");
        foreach (var width in new[] { 1180d, 940d })
            await Snapshot(new SettingsWindow(settings, new JsonSettingsStore(paths, logger), new WindowsStartupRegistration(), logger, updates: coordinator, installUpdate: () => Task.CompletedTask, checkUpdate: async () => { await coordinator.CheckAsync(); }) { Width = width, Height = 780 }, output, "updates-settings-" + width,
                window => ((SettingsWindow)window).SelectSection("Updates"));
        await File.WriteAllBytesAsync(Path.Combine(coordinator.Download!.Directory, release.FileName), new byte[package.Length]);
        if (await coordinator.PrepareAsync()) throw new InvalidOperationException("Damaged fixture was accepted.");
        await Snapshot(PanelWindow(), output, "updates-error");
        await File.WriteAllTextAsync(Path.Combine(output, "result.txt"), "PASS: actual WPF update windows; signed local check/copy/verification; tampered package rejected. Setup was not executed.\n");
    }

    public static async Task RunAsync(string output)
    {
        L.SetLanguage("en"); Ui.ApplyTheme("Dark");
        Directory.CreateDirectory(output);
        var paths = new AppPaths(Path.Combine(output, "data"), output); paths.EnsureDirectories();
        using var logger = new FileLogger(paths.LogsPath);
        var settings = AppSettings.Defaults(); settings.General.StartWithWindows = false; settings.General.CaptureRoot = output;
        var repository = new SqliteHistoryRepository(paths.DatabasePath, logger);
        var thumbnails = new ThumbnailService(paths.ThumbnailPath, repository, logger);
        var service = new ShelfService(repository, thumbnails);
        var fixture = Fixture(); var captured = EditorRenderer.ToCapturedImage(fixture);
        var source = Path.Combine(output, "capture-fixture.png"); SaveBitmap(fixture, source);
        for (var i = 0; i < 16; i++)
        {
            var file = Path.Combine(output, $"capture-{i + 1:00}.png"); File.Copy(source, file, true);
            await repository.AddAsync(new NewHistoryItem(MediaType.Screenshot, file, DateTimeOffset.UtcNow.AddMinutes(-i * 23), 960, 600, null, source, new VirtualPixelRect(0, 0, 960, 600)), CancellationToken.None);
        }
        settings.General.ShelfRecentCount = 5;
        await Snapshot(new ShelfWindow(service, settings, logger), output, "shelf", exercise: async window =>
        {
            var shelf = ((ShelfWindow)window).Shelf;
            await shelf.LoadAsync();
            if (Descendants<System.Windows.Controls.ListView>(window).Single().Items.Count != 5) throw new InvalidOperationException("Recent Shelf did not respect its configured count.");
            await (Task)typeof(ShelfContent).GetMethod("LoadOlderAsync", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(shelf, null)!;
            if (Descendants<System.Windows.Controls.ListView>(window).Single().Items.Count != 16) throw new InvalidOperationException("Older captures were not appended to the real Shelf.");
            if (Descendants<Button>(window).Single(button => Name(button) == "Load older").IsEnabled) throw new InvalidOperationException("Shelf still offers another page after reaching the end.");
        });
        settings.General.ShelfRecentCount = 20;
        await Snapshot(new ShelfWindow(new ShelfContent(service, settings, logger), true), output, "shelf-compact");
        await Snapshot(new SettingsWindow(settings, new JsonSettingsStore(paths, logger), new WindowsStartupRegistration(), logger), output, "settings");
        await Snapshot(new SettingsWindow(settings, new JsonSettingsStore(paths, logger), new WindowsStartupRegistration(), logger) { Width = 940, Height = 680 }, output, "settings-compact");
        var editor = new EditorWindow(captured, logger);
        editor.Document.Elements.Add(new ArrowElement(new Point(250, 160), new Point(570, 235)) { Color = Colors.Crimson, StrokeWidth = 4 });
        editor.Document.Elements.Add(new RectangleElement(new Rect(280, 355, 400, 72)) { Color = Colors.MediumSpringGreen, IsSelected = true, StrokeWidth = 3 });
        editor.Document.Elements.Add(new StepMarkerElement(1, new Point(250, 390)) { Color = Colors.MediumSpringGreen });
        editor.Document.Elements.Add(new TextElement("Check this setting", new Point(270, 115)) { Color = Colors.Crimson, FontSize = 25 });
        await Snapshot(editor, output, "annotation-editor", exercise: async window =>
        {
            var setZoom = typeof(EditorWindow).GetMethod("SetZoom", BindingFlags.Instance | BindingFlags.NonPublic)!;
            var getHandle = typeof(EditorWindow).GetMethod("GetResizeHandle", BindingFlags.Instance | BindingFlags.NonPublic)!;
            var bounds = new Rect(280, 355, 400, 72);
            foreach (var zoom in ResizeZooms)
            {
                setZoom.Invoke(window, [zoom]); await Task.Delay(40);
                if (getHandle.Invoke(window, [bounds, new Point(bounds.Left - 9 / zoom, bounds.Top - 9 / zoom)])!.ToString() != "TopLeft") throw new InvalidOperationException("Resize hit target shrank with zoom.");
                CaptureContent(window, Path.Combine(output, $"annotation-zoom-{zoom * 100:0}.png"));
            }
            setZoom.Invoke(window, [1d]);
            Descendants<ToggleButton>(window).Single(x => Name(x) == "Rectangle").RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            typeof(EditorWindow).GetField("_dragStart", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(window, new Point(100, 100));
            typeof(EditorWindow).GetMethod("BeginPreview", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, null);
            typeof(EditorWindow).GetMethod("UpdatePreview", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, [new Point(450, 280)]);
            var preview = (System.Windows.Shapes.Shape)typeof(EditorWindow).GetField("_gesturePreview", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!;
            if (preview.StrokeThickness != 5 || ((SolidColorBrush)preview.Stroke).Color != Color.FromRgb(255, 59, 48)) throw new InvalidOperationException("Annotation preview does not match the default style.");
            ((ComboBox)typeof(EditorWindow).GetField("_format", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!).SelectedItem = "Jpg";
            if (((EditorWindow)window).SaveFormat != "Jpg") throw new InvalidOperationException("Document save format did not update.");
            CaptureContent(window, Path.Combine(output, "annotation-red-preview-jpg.png"));
        });
        var monitor = new MonitorDescriptor("visual-fixture", new VirtualPixelRect(0, 0, 1280, 800), new VirtualPixelRect(0, 0, 1280, 760), 96, 96, true);
        var region = new VirtualPixelRect(180, 150, 840, 520);
        var selector = new RegionSelectorWindow(monitor, _ => { }, () => { }); selector.DrawSelection(region); await Snapshot(selector, output, "region-selector");
        var frozenSnapshot = new FrozenDesktopSnapshot(new VirtualPixelRect(160, 100, fixture.PixelWidth, fixture.PixelHeight), captured);
        var frozenBitmap = EditorRenderer.ToBitmapSource(frozenSnapshot.CapturedImage);
        var frozenSelector = new RegionSelectorWindow(monitor, _ => { }, () => { }, allowFullMonitor: true, monitors: new[] { monitor },
            gesture: new RegionSelectionGesture(), frozenImage: frozenBitmap, frozenBounds: frozenSnapshot.VirtualDesktopBounds);
        frozenSelector.DrawSelection(region); await Snapshot(frozenSelector, output, "region-selector-frozen", exercise: window =>
        {
            var images = Descendants<Image>(window).Where(image => image.Source is not null).ToArray();
            if (images.Length != 1 || !ReferenceEquals(images[0].Source, frozenBitmap)) throw new InvalidOperationException("Frozen selector did not render the shared frozen bitmap.");
            return Task.CompletedTask;
        });
        await File.WriteAllTextAsync(Path.Combine(output, "frozen-selector-results.txt"), "PASS: WPF selector rendered one shared frozen BitmapSource under dimmer/selection chrome; live selector path remains covered separately.\n");
        var border = new RecordingBorderWindow(region, new[] { monitor }, logger); await Snapshot(border, output, "recording-border");
        var pill = new RecordingPillWindow(region, new[] { monitor }, settings, logger); await Snapshot(pill, output, "recording", window => ((RecordingPillWindow)window).Update(new RecordingSnapshot(RecordingState.Recording, TimeSpan.FromSeconds(12), true, false, true, null, null, null)));
        await Snapshot(new RecordingPillWindow(region, new[] { monitor }, settings, logger), output, "recording-paused", window => ((RecordingPillWindow)window).Update(new RecordingSnapshot(RecordingState.Paused, TimeSpan.FromSeconds(12), false, false, false, null, null, null)));
        var clock = new ActiveMediaTimer(new SystemMonotonicClock()); clock.Start();
        var liveSnapshot = new RecordingSnapshot(RecordingState.Recording, TimeSpan.Zero, true, false, true, null, null, null);
        using (var controls = new RecordingOverlayController(region, new[] { monitor }, settings, logger, () => liveSnapshot with { ActiveDuration = clock.Elapsed }))
        {
            controls.Show(); await Task.Delay(1250);
            var livePill = System.Windows.Application.Current.Windows.OfType<RecordingPillWindow>().Single();
            if (!Descendants<TextBlock>(livePill).Any(x => x.Text.Contains("00:01", StringComparison.Ordinal))) throw new InvalidOperationException("Recording timer did not advance from the coordinator snapshot provider.");
            clock.Pause(); liveSnapshot = liveSnapshot with { State = RecordingState.Paused }; await Task.Run(() => controls.Update(liveSnapshot with { ActiveDuration = clock.Elapsed }));
            await Task.Delay(100); CaptureContent(livePill, Path.Combine(output, "recording-live-timer.png"));
        }
        var video = Path.Combine(output, "video-fixture.mp4");
        var previousVideo = Path.Combine(Path.GetDirectoryName(output)!, "visual-pass2", "video-fixture.mp4");
        if (!File.Exists(video) && File.Exists(previousVideo)) File.Copy(previousVideo, video);
        if (!File.Exists(video))
        {
            var imageFile = await StorageFile.GetFileFromPathAsync(source); var clip = await MediaClip.CreateFromImageFileAsync(imageFile, TimeSpan.FromSeconds(12));
            var composition = new MediaComposition(); composition.Clips.Add(clip);
            var folder = await StorageFolder.GetFolderFromPathAsync(output); var destination = await folder.CreateFileAsync("video-fixture.mp4", CreationCollisionOption.ReplaceExisting);
            var result = await composition.RenderToFileAsync(destination, MediaTrimmingPreference.Precise, MediaEncodingProfile.CreateMp4(VideoEncodingQuality.HD720p));
            if (result != Windows.Media.Transcoding.TranscodeFailureReason.None) throw new InvalidOperationException("Fixture video: " + result);
        }
        await Snapshot(new VideoEditorWindow(video, logger), output, "video-editor", asyncWindow: true, exercise: async window =>
{
    void Select(double from, double to)
    {
        ((Expander)typeof(VideoEditorWindow).GetField("_exactTime", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!).IsExpanded = true; window.UpdateLayout();
        Descendants<TextBox>(window).Single(x => Name(x) == "Selection start (seconds)").Text = from.ToString(CultureInfo.InvariantCulture);
        Descendants<TextBox>(window).Single(x => Name(x) == "Selection end (seconds)").Text = to.ToString(CultureInfo.InvariantCulture);
        Click(window, "Set selection");
    }
    async Task WaitReady()
    {
        var deadline = DateTime.UtcNow.AddSeconds(25);
        while (!Descendants<Button>(window).Single(x => Name(x) == "Export a copy").IsEnabled && DateTime.UtcNow < deadline) await Task.Delay(100);
        if (!Descendants<Button>(window).Single(x => Name(x) == "Export a copy").IsEnabled) throw new InvalidOperationException("Preview did not become ready.");
    }
    await WaitReady(); Select(2, 4); Click(window, "Delete fragment"); await WaitReady();
    Select(3, 5); Click(window, "Delete fragment"); await WaitReady();
    Click(window, "Undo"); await WaitReady(); Click(window, "Redo"); await WaitReady();
    Click(window, "Play"); await Task.Delay(1200);
    if (!Descendants<Image>(window).Any(x => x.Source is WriteableBitmap)) throw new InvalidOperationException("Native frame-server produced no WPF frame.");
    Click(window, "Pause");
    var export = Descendants<Button>(window).Single(x => Name(x) == "Export a copy");
    CaptureContent(window, Path.Combine(output, "video-two-cuts.png"));
    var originalLength = new FileInfo(video).Length; var originalWrite = File.GetLastWriteTimeUtc(video);
    Click(window, "Export a copy");
    await Task.Delay(100); CaptureContent(window, Path.Combine(output, "video-export-progress.png"));
    var deadline = DateTime.UtcNow.AddSeconds(60);
    while (!export.IsEnabled && DateTime.UtcNow < deadline) await Task.Delay(100);
    var edited = Directory.GetFiles(output, "video-fixture-edited-*.mp4").Single();
    var editedClip = await MediaClip.CreateFromFileAsync(await StorageFile.GetFileFromPathAsync(edited));
    if (Math.Abs(editedClip.OriginalDuration.TotalSeconds - 8) > .3) throw new InvalidOperationException("Edited duration did not match 8s.");
    if (new FileInfo(video).Length != originalLength || File.GetLastWriteTimeUtc(video) != originalWrite) throw new InvalidOperationException("Original changed.");
    Click(window, "Export a copy"); Click(window, "Cancel export");
    while (!export.IsEnabled && DateTime.UtcNow < deadline) await Task.Delay(100);
    if (!Descendants<TextBlock>(window).Any(x => x.Text.Contains("cancelled", StringComparison.OrdinalIgnoreCase))) throw new InvalidOperationException("Export cancellation was not surfaced.");
    await File.WriteAllTextAsync(Path.Combine(output, "interaction-results.txt"), "PASS: two ripple cuts, undo/redo, native frame-server playback, export to 8 seconds; original length/timestamp unchanged; cancellation surfaced.\n");
});
        await ComponentSnapshot(output);
        await File.WriteAllTextAsync(Path.Combine(output, "result.txt"), "PASS: real WPF windows opened, laid out and rendered. Fixtures are isolated; no user settings/startup/hotkeys modified.\n" + DateTimeOffset.Now.ToString("O", CultureInfo.InvariantCulture));
    }
    public static async Task RunNativeAsync(string output)
    {
        Directory.CreateDirectory(output); using var logger = new FileLogger(Path.Combine(output, "logs"));
        var topology = new Win32MonitorTopologyService(); var monitors = topology.GetMonitors(); var monitor = monitors.First(x => x.IsPrimary);
        var fixture = new Window { Title = "SnappySnap native acceptance fixture", WindowStyle = WindowStyle.None, ResizeMode = ResizeMode.NoResize, Width = 720, Height = 450, Left = monitor.WorkArea.Left / monitor.ScaleX + 70, Top = monitor.WorkArea.Top / monitor.ScaleY + 70, ShowInTaskbar = false, ShowActivated = false, Topmost = true, Content = new Image { Source = Fixture(), Stretch = Stretch.Fill } };
        RecordingPillWindow? pill = null; RecordingBorderWindow? border = null; RippleWindow? ripple = null;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(45));
        try
        {
            fixture.Show(); await Task.Delay(350, timeout.Token);
            var topLeft = fixture.PointToScreen(new Point(0, 0)); var bottomRight = fixture.PointToScreen(new Point(fixture.ActualWidth, fixture.ActualHeight));
            var bounds = new VirtualPixelRect((int)topLeft.X, (int)topLeft.Y, (int)(bottomRight.X - topLeft.X), (int)(bottomRight.Y - topLeft.Y));
            var plan = new CapturePlanBuilder().Build(bounds, monitors);
            var capture = new WindowsGraphicsCaptureScreenshotService(logger, topology);
            var screenshot = await capture.CaptureAsync(plan, timeout.Token);
            await EditorRenderer.SavePngAsync(new EditorDocument(screenshot), Path.Combine(output, "native-screenshot.png"), timeout.Token);
            if (!screenshot.Bgra32.Where((_, index) => index % 4 == 3).Any(alpha => alpha != 0))
            {
                var gdi = await new GdiScreenshotCaptureService(topology).CaptureAsync(plan, timeout.Token);
                await EditorRenderer.SavePngAsync(new EditorDocument(gdi), Path.Combine(output, "gdi-diagnostic.png"), timeout.Token);
                throw new InvalidOperationException($"Screenshot backend '{capture.BackendName}' returned a fully transparent frame.");
            }
            var settings = AppSettings.Defaults(); settings.Recording.SystemAudioDefault = false; settings.Recording.MicrophoneDefault = false; settings.Recording.ClickRippleDurationMs = 300;
            border = new RecordingBorderWindow(bounds, monitors, logger); border.Show();
            pill = new RecordingPillWindow(bounds, monitors, settings, logger); pill.Show();
            // Force the controls inside the capture for a meaningful exclusion test.
            pill.Left = fixture.Left + 30; pill.Top = fixture.Top + 20;
            pill.Update(new RecordingSnapshot(RecordingState.Recording, TimeSpan.FromSeconds(0), false, false, true, plan, null, null));
            CaptureContent(pill, Path.Combine(output, "visible-pill.png"));
            ripple = new RippleWindow(monitor, settings); ripple.Show();
            var paths = new AppPaths(Path.Combine(output, "profile"), output);
            await using var repository = new SqliteHistoryRepository(paths.DatabasePath, logger);
            await using var recording = new RecordingSessionCoordinator(new ScreenRecorderLibVideoBackendFactory(logger), repository,
                new ThumbnailService(paths.ThumbnailPath, repository, logger), new JsonRecoveryStore(paths), logger, new SystemMonotonicClock());
            await recording.StartAsync(plan, output, paths.TempPath, settings, timeout.Token);
            var video = recording.Snapshot.FinalPath!;
            await Task.Delay(600, timeout.Token);
            var left = new VirtualPixelPoint(bounds.X + bounds.Width / 3, bounds.Y + bounds.Height / 2);
            ripple.AddRipple(new MouseClickEvent(DateTimeOffset.UtcNow, left, SnappySnap.Core.MouseButton.Left));
            await Task.Delay(800, timeout.Token);
            await recording.PauseOrResumeAsync(timeout.Token);
            var paused = new RecordingSnapshot(RecordingState.Paused, TimeSpan.FromSeconds(1.4), false, false, true, plan, null, null); pill.Update(paused); border.Update(paused);
            await Task.Delay(1000, timeout.Token);
            await recording.PauseOrResumeAsync(timeout.Token); pill.Update(paused with { State = RecordingState.Recording }); border.Update(paused with { State = RecordingState.Recording });
            await Task.Delay(400, timeout.Token);
            var right = new VirtualPixelPoint(bounds.X + 2 * bounds.Width / 3, bounds.Y + bounds.Height / 2);
            ripple.AddRipple(new MouseClickEvent(DateTimeOffset.UtcNow, right, SnappySnap.Core.MouseButton.Right));
            await Task.Delay(1000, timeout.Token);
            await recording.StopAsync(timeout.Token);
            var saved = await repository.GetRecentAsync(20, timeout.Token);
            if (recording.Snapshot.State != RecordingState.Idle || saved.Count != 1 || saved[0].FilePath != video)
                throw new InvalidOperationException("Recording coordinator did not finalize and index exactly one video.");
            var clip = await MediaClip.CreateFromFileAsync(await StorageFile.GetFileFromPathAsync(video));
            if (clip.OriginalDuration.TotalSeconds < 2.5 || clip.OriginalDuration.TotalSeconds > 3.6) throw new InvalidOperationException("Unexpected active recording duration: " + clip.OriginalDuration);
            var composition = new MediaComposition(); composition.Clips.Add(clip);
            var frameIndex = 0;
            foreach (var second in new[] { .7, .8, 1.1, 1.95, 2.05, 2.5 })
            {
                using var thumb = await composition.GetThumbnailAsync(TimeSpan.FromSeconds(second), (int)bounds.Width, (int)bounds.Height, VideoFramePrecision.NearestFrame).AsTask(timeout.Token);
                using var stream = thumb.AsStreamForRead(); var bitmap = new BitmapImage(); bitmap.BeginInit(); bitmap.CacheOption = BitmapCacheOption.OnLoad; bitmap.StreamSource = stream; bitmap.EndInit(); bitmap.Freeze(); SaveBitmap(bitmap, Path.Combine(output, $"native-frame-{frameIndex++}.png"));
            }
            await File.WriteAllTextAsync(Path.Combine(output, "native-results.txt"), $"PASS: screenshot {screenshot.Width}x{screenshot.Height}; native MP4 {saved[0].WidthPx}x{saved[0].HeightPx}; active duration {clip.OriginalDuration.TotalSeconds:F3}s (1s pause excluded).\nPASS: application coordinator starts/pauses/resumes/stops the real backend, indexes one file and returns Idle.\nPill forced inside capture. Injected left/right events exercise the real ripple renderer; physical mouse hook and audio listening not tested here. Inspect extracted frames for visual exclusion and ripple position.\nBackend selection diagnostics are in the local harness logs.");
        }
        finally { ripple?.Close(); pill?.Close(); border?.Close(); fixture.Close(); }
    }
    private static async Task Snapshot(Window window, string output, string name, Action<Window>? arrange = null, bool asyncWindow = false, Func<Window, Task>? exercise = null)
    {
        window.WindowStartupLocation = WindowStartupLocation.Manual; window.Left = -15000; window.Top = -15000; window.ShowActivated = false; window.Topmost = false;
        var foreground = GetForegroundWindow();
        // The user's real outside clicks must not close off-screen fixtures during assertions.
        if (window is ShelfWindow) window.Loaded += (_, _) =>
        {
            ((GlobalMouseClickSource?)typeof(ShelfWindow).GetField("_outsideClicks", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window))?.Stop();
            window.Left = -15000; window.Top = -15000;
        };
        try
        {
            window.Show(); arrange?.Invoke(window);
            if (window is ShelfWindow && GetForegroundWindow() != foreground) throw new InvalidOperationException("Shelf stole foreground activation.");
            await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
            if (asyncWindow) await Task.Delay(4000); else await Task.Delay(150);
            if (exercise is not null) await exercise(window);
            window.UpdateLayout();
            if (window.IsVisible || window is not SettingsWindow) CaptureContent(window, Path.Combine(output, name + ".png"));
        }
        finally
        {
            // These are disposable fixture documents. A failed assertion must not open a discard dialog.
            if (window is EditorWindow) typeof(EditorWindow).GetField("_committing", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(window, true);
            if (window is VideoEditorWindow) { typeof(VideoEditorWindow).GetField("_savedSegments", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(window, null); typeof(VideoEditorWindow).GetField("_pendingExport", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(window, null); }
            window.Close();
        }
    }
    internal static void CaptureContent(Window window, string path)
    {
        window.UpdateLayout(); var content = (FrameworkElement)window.Content; var size = content.RenderSize;
        if (size.Width <= 0 || size.Height <= 0) throw new InvalidOperationException("Window did not render.");
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(size.Width), (int)Math.Ceiling(size.Height), 96, 96, PixelFormats.Pbgra32); bitmap.Render(content); SaveBitmap(bitmap, path);
    }
    [DllImport("user32.dll")] private static extern nint GetForegroundWindow();
    private static string Name(DependencyObject value) => System.Windows.Automation.AutomationProperties.GetName(value);
    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++) { var child = VisualTreeHelper.GetChild(root, i); if (child is T value) yield return value; foreach (var descendant in Descendants<T>(child)) yield return descendant; }
    }
    private static void Click(Window window, string name) => Descendants<Button>(window).Single(x => Name(x) == name).RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
    private static async Task ComponentSnapshot(string output)
    {
        var panel = new StackPanel { Margin = new Thickness(24) };
        panel.Children.Add(Ui.Text("SnappySnap · Component states", 24));
        foreach (var style in new[] { "PrimaryButton", "GhostButton", "DangerButton" }) { var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 14, 0, 0) }; row.Children.Add(Ui.Button(style, "\uE74E", (_, _) => { }, style)); var disabled = Ui.Button("Disabled", "", (_, _) => { }, style); disabled.IsEnabled = false; row.Children.Add(disabled); panel.Children.Add(row); }
        panel.Children.Add(new CheckBox { Content = "System audio ON", IsChecked = true, Margin = new Thickness(0, 18, 0, 0) }); panel.Children.Add(new CheckBox { Content = "Microphone OFF", IsChecked = false });
        panel.Children.Add(new TextBox { Text = "Capture name", Margin = new Thickness(0, 16, 0, 8) }); panel.Children.Add(new ComboBox { ItemsSource = QualityProfiles, SelectedIndex = 0 });
        panel.Children.Add(new Slider { Value = 65, Maximum = 100, Margin = new Thickness(0, 12, 0, 8) }); panel.Children.Add(new ProgressBar { Maximum = 100, Value = 68 });
        var chips = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 16, 0, 0) }; foreach (var (label, color) in new[] { ("● Ready", "Accent"), ("● Recording", "Warning"), ("● Processing", "Muted") }) { var chip = Ui.Chip(label, color); chip.Margin = new Thickness(0, 0, 8, 0); chips.Children.Add(chip); }
        panel.Children.Add(chips);
        var window = new Window { Width = 620, Height = 620, Title = "Components" }; Ui.Shell(window, "", panel); await Snapshot(window, output, "components");
    }
    internal static BitmapSource Fixture()
    {
        var visual = new DrawingVisual(); using (var dc = visual.RenderOpen())
        {
            dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(24, 30, 38)), null, new Rect(0, 0, 960, 600));
            void Text(string text, double x, double y, double size = 16, Brush? brush = null) => dc.DrawText(new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, new Typeface("Segoe UI"), size, brush ?? Brushes.WhiteSmoke, 1), new Point(x, y));
            Text("Settings", 24, 22, 20); Text("Windows 11 · Display", 280, 70, 30);
            var names = new[] { "System", "Bluetooth & devices", "Network & internet", "Personalization", "Apps", "Accessibility" };
            for (var i = 0; i < names.Length; i++) Text(names[i], 24, 120 + i * 48, 15, Brushes.LightSteelBlue);
            for (var i = 0; i < DisplayOptions.Length; i++) { dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromRgb(39, 48, 58)), null, new Rect(280, 140 + i * 78, 630, 64), 7, 7); Text(DisplayOptions[i], 304, 160 + i * 78); }
            Text("1920 × 1080", 740, 316, 14, Brushes.LightSteelBlue); Text("100%", 785, 394, 14, Brushes.LightSteelBlue);
        }
        var bitmap = new RenderTargetBitmap(960, 600, 96, 96, PixelFormats.Pbgra32); bitmap.Render(visual); bitmap.Freeze(); return bitmap;
    }
    private static void SaveBitmap(BitmapSource bitmap, string path) { var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap)); using var file = File.Create(path); encoder.Save(file); }
}
#endif
