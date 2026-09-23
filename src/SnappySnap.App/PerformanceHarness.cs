#if PERFORMANCE_HARNESS
using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using SnappySnap.Application;
using SnappySnap.Core;
using SnappySnap.Editor;
using SnappySnap.Infrastructure;
using SnappySnap.Presentation;
using Windows.Media.Editing;
using Windows.Media.MediaProperties;
using Windows.Storage;
using Point = System.Windows.Point;

namespace SnappySnap.App;

// Explicit opt-in Release harness; absent from normal Release/Setup. Never creates the runtime/profile.
internal static class PerformanceHarness
{
    public static async Task RunCadenceAsync(string output)
    {
        Directory.CreateDirectory(output);
        using var logger = new FileLogger(Path.Combine(output, "logs"));
        var results = new List<object>();
        var pixels = new byte[3840 * 2160 * 4];
        Array.Fill(pixels, (byte)255);
        foreach (var count in new[] { 20, 100 })
        {
            var window = new EditorWindow(new CapturedImage(3840, 2160, pixels), logger);
            for (var i = 0; i < count; i++) window.Document.Elements.Add(new TextElement("4K moving annotation " + i, new Point(100 + i % 10 * 300, 100 + i / 10 * 150)));
            window.Show();
            var surface = Field<EditorSurface>(window, "_surface");
            foreach (var zoom in new[] { .25, 1d, 2d })
            {
                Call(window, "SetZoom", zoom); await Drain();
                var frames = new List<double>(); var times = new List<double>();
                var watch = Stopwatch.StartNew(); double last = -1;
                EventHandler rendered = (_, args) =>
                {
                    var now = ((RenderingEventArgs)args).RenderingTime.TotalMilliseconds;
                    if (now == last) return;
                    if (last >= 0 && watch.ElapsedMilliseconds > 500) { frames.Add(now - last); times.Add(now); }
                    last = now;
                    var element = window.Document.Elements[0]; var bounds = element.Bounds;
                    element.Bounds = new Rect(bounds.X == 100 ? 120 : 100, bounds.Y, bounds.Width, bounds.Height);
                    surface.InvalidateVisual();
                };
                CompositionTarget.Rendering += rendered;
                try { surface.InvalidateVisual(); await Task.Delay(5500); }
                finally { CompositionTarget.Rendering -= rendered; }
                results.Add(new { scenario = $"cadence-{count}-zoom{zoom}", frames = frames.Count, wpfCallbacksPerSecond = times.Count > 1 ? (times.Count - 1) * 1000 / (times[^1] - times[0]) : 0, intervalP95Ms = P95(frames), maxIntervalMs = frames.DefaultIfEmpty().Max(), intervalsOver100 = frames.Count(ms => ms > 100) });
            }
            window.Close();
        }
        await File.WriteAllTextAsync(Path.Combine(output, "cadence.json"), JsonSerializer.Serialize(results, new JsonSerializerOptions { WriteIndented = true }));
    }
    private static readonly List<object> Results = new();
    private static string _output = "";
    private static readonly BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private static T Field<T>(object target, string name) => (T)target.GetType().GetField(name, Private)!.GetValue(target)!;
    private static void Call(object target, string name, params object[] args) => target.GetType().GetMethod(name, Private)!.Invoke(target, args);
    private static void Set(object target, string name, object value) => target.GetType().GetField(name, Private)!.SetValue(target, value);
    private static async Task Drain() => await System.Windows.Application.Current.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Render);
    private static double P95(List<double> values) => values.Count == 0 ? 0 : values.Order().ElementAt((int)Math.Ceiling(values.Count * .95) - 1);

    public static async Task RunAsync(string output, bool interactive, bool videoOnly = false)
    {
        Directory.CreateDirectory(output);
        _output = output;
        using var logger = new FileLogger(Path.Combine(output, "logs"));
        await File.WriteAllTextAsync(Path.Combine(output, "environment.txt"), $"Release={ !Debugger.IsAttached }; runtime={Environment.Version}; OS={Environment.OSVersion}; processors={Environment.ProcessorCount}; version={typeof(App).Assembly.GetName().Version}\n" +
            string.Join("\n", new SnappySnap.Capture.Win32MonitorTopologyService().GetMonitors()));
        if (videoOnly) { await VideoAsync(output, logger, 120); await Write(output); return; }
        var pixels = new byte[3840 * 2160 * 4];
        for (var y = 0; y < 2160; y++) for (var x = 0; x < 3840; x++)
        { var i = (y * 3840 + x) * 4; pixels[i] = (byte)(35 + x % 180); pixels[i + 1] = (byte)(40 + y % 140); pixels[i + 2] = (byte)(30 + (x + y) % 170); pixels[i + 3] = 255; }
        foreach (var (count, effects) in new[] { (20, false), (100, false), (20, true) })
        {
            var open = Stopwatch.StartNew();
            var window = new EditorWindow(new CapturedImage(3840, 2160, pixels), logger);
            var doc = window.Document;
            for (var i = 0; i < count; i++)
            {
                var x = 90 + (i % 10) * 310; var y = 100 + (i / 10) * 140;
                doc.Elements.Add(i % 3 == 0 ? new TextElement("4K annotation " + i, new Point(x, y)) : i % 3 == 1 ? new RectangleElement(new Rect(x, y, 240, 105)) : new ArrowElement(new Point(x, y), new Point(x + 220, y + 90)));
            }
            if (effects) { doc.Elements.Add(new BlurElement(new Rect(200, 200, 800, 500))); doc.Elements.Add(new PixelateElement(new Rect(1100, 600, 640, 350))); }
            var selected = new ImageElement(doc.BaseImage, new Rect(250, 260, 420, 236)) { IsSelected = true }; doc.Elements.Add(selected);
            window.Show(); await Drain(); Call(window, "SetZoom", .25); await Drain();
            Results.Add(new { scenario = $"open-{count}-{effects}", elapsedMs = open.Elapsed.TotalMilliseconds });
            if (interactive) { await WaitForClose(window); return; }
            var surface = Field<EditorSurface>(window, "_surface");
            foreach (var zoom in new[] { .25, 1d, 2d })
            {
                Call(window, "SetZoom", zoom); await Drain();
                await Measure($"move-{count}-effects{effects}-zoom{zoom}", () => { var b = selected.Bounds; selected.Bounds = new Rect(b.X == 250 ? 270 : 250, b.Y, b.Width, b.Height); Call(window, "Refresh"); });
            }
            Call(window, "SetZoom", .5); await Drain();
            Set(window, "_tool", EditorTool.Freehand); Set(window, "_dragStart", new Point(200, 200)); Call(window, "BeginPreview");
            var n = 0;
            await Measure($"freehand-{count}-effects{effects}", () => { Call(window, "UpdatePreview", new Point(200 + n * 5, 200 + Math.Sin(n++ * .3) * 40)); Field<List<Point>>(window, "_freehandPoints").Add(new Point(n, n)); Call(window, "Refresh"); });
            Call(window, "CancelInteraction"); await Drain();
            Set(window, "_selected", doc.Elements[1]);
            await Measure($"style-{count}-effects{effects}", () => { var element = doc.Elements[1]; element.StrokeWidth = element.StrokeWidth == 5 ? 8 : 5; Call(window, "Refresh"); });
            await Measure($"zoom-{count}-effects{effects}", () => Call(window, "SetZoom", surface.Zoom == .25 ? 2d : .25));
            var export = Stopwatch.StartNew(); await EditorRenderer.SavePngAsync(doc, Path.Combine(output, $"export-{count}-{effects}.png"), CancellationToken.None);
            Results.Add(new { scenario = $"export-{count}-{effects}", elapsedMs = export.Elapsed.TotalMilliseconds });
            Snapshot(window, Path.Combine(output, $"editor-{count}-{effects}.png")); window.Close(); await Drain(); await Write(output);
        }
        await ShelfAsync(output, logger);
        await VideoAsync(output, logger);
        await Write(output);
    }

    private static async Task Measure(string name, Action action, int samples = 24)
    {
        var dispatcher = System.Windows.Application.Current.Dispatcher;
        for (var i = 0; i < 3; i++) { action(); await Drain(); await Task.Delay(16); }
        await File.AppendAllTextAsync(Path.Combine(_output, "progress.txt"), name + " start\n");
        var turns = new List<double>(); var stalls = new List<double>();
        var pending = 0;
        using var heartbeat = new System.Threading.Timer(_ =>
        {
            if (Interlocked.Exchange(ref pending, 1) != 0) return;
            var posted = Stopwatch.GetTimestamp();
            dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() => { stalls.Add(Stopwatch.GetElapsedTime(posted).TotalMilliseconds); Interlocked.Exchange(ref pending, 0); }));
        }, null, 0, 16);
        var allocated = GC.GetTotalAllocatedBytes(true); var collections = new[] { GC.CollectionCount(0), GC.CollectionCount(1), GC.CollectionCount(2) };
        try
        {
            for (var i = 0; i < samples; i++)
            {
                // Input callback -> WPF render/layout/dispatcher drain. Does not claim monitor presentation latency.
                var sw = Stopwatch.StartNew(); action(); await Drain(); turns.Add(sw.Elapsed.TotalMilliseconds); await Task.Delay(16);
            }
        }
        finally { heartbeat.Change(Timeout.Infinite, Timeout.Infinite); }
        Results.Add(new { scenario = name, samples = turns.Count, p95Ms = P95(turns), maxMs = turns.Max(), meanMs = turns.Average(), allocatedMB = (GC.GetTotalAllocatedBytes(true) - allocated) / 1e6, gc = collections.Select((c, i) => GC.CollectionCount(i) - c).ToArray(), dispatcherP95Ms = P95(stalls), dispatcherOver100 = stalls.Count(t => t > 100) });
        await Write(_output);
    }

    private static async Task ShelfAsync(string output, IAppLogger logger)
    {
        var paths = new AppPaths(Path.Combine(output, "shelf-data"), output); paths.EnsureDirectories();
        var repository = new SnappySnap.History.SqliteHistoryRepository(paths.DatabasePath, logger);
        var service = new ShelfService(repository, new SnappySnap.History.ThumbnailService(paths.ThumbnailPath, repository, logger));
        var settings = AppSettings.Defaults(); settings.General.ShelfRecentCount = 500;
        var source = Path.Combine(output, "export-20-False.png");
        var small = new TransformedBitmap(EditorRenderer.LoadImage(source), new ScaleTransform(1d / 12, 1d / 12)); small.Freeze();
        var thumb = Path.Combine(output, "thumb.png"); var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(small)); using (var stream = File.Create(thumb)) png.Save(stream);
        for (var i = 0; i < 500; i++) { var fixture = Path.Combine(output, "shelf-data", $"capture-{i}.png"); File.Copy(thumb, fixture); await service.AddScreenshotAsync(fixture, 3840, 2160, CancellationToken.None); }
        // Independent deterministic metadata; each row has a real cached thumbnail, no working history involved.
        var state = new ShelfState { Loaded = true };
        var items = await service.LoadRecentAsync(500, CancellationToken.None);
        var watch = Stopwatch.StartNew();
        foreach (var item in items) state.Rows.Add(new ShelfRow(item with { ThumbnailPath = thumb }));
        var window = new ShelfWindow(new ShelfContent(service, settings, logger, state), false) { Width = 1000, Height = 820 };
        window.Show(); await Drain();
        var list = Descendants<ListView>(window).Single(); var scroller = Descendants<ScrollViewer>(list).First();
        Results.Add(new { scenario = "shelf500-open", elapsedMs = watch.Elapsed.TotalMilliseconds, containers = Descendants<ListViewItem>(list).Count() });
        await Measure("shelf500-scroll", () => scroller.ScrollToVerticalOffset(scroller.VerticalOffset + 120));
        Results.Add(new { scenario = "shelf500-scrolled", containers = Descendants<ListViewItem>(list).Count() });
        Snapshot(window, Path.Combine(output, "shelf500.png")); window.Close();
    }

    private static async Task VideoAsync(string output, IAppLogger logger, int samples = 24)
    {
        var composition = new MediaComposition();
        for (var i = 0; i < 12; i++) composition.Clips.Add(MediaClip.CreateFromColor(Windows.UI.Color.FromArgb(255, (byte)(30 + i * 15), 70, 110), TimeSpan.FromSeconds(1)));
        var file = await (await StorageFolder.GetFolderFromPathAsync(output)).CreateFileAsync("video-fixture.mp4", CreationCollisionOption.FailIfExists);
        var failure = await composition.RenderToFileAsync(file, MediaTrimmingPreference.Precise, MediaEncodingProfile.CreateMp4(VideoEncodingQuality.HD1080p));
        if (failure != Windows.Media.Transcoding.TranscodeFailureReason.None) throw new InvalidOperationException(failure.ToString());
        var window = new VideoEditorWindow(file.Path, logger); window.Show();
        await WaitUntil(() => Field<bool>(window, "_previewReady"), 30000);
        var edit = Field<VideoEditSession>(window, "_edit"); var timeline = Field<VideoTimeline>(window, "_timeline");
        edit.Delete(TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(3)); edit.Delete(TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(6));
        var reload = Stopwatch.StartNew(); await (Task)window.GetType().GetMethod("ReloadPreviewAsync", Private)!.Invoke(window, null)!;
        Results.Add(new { scenario = "video-two-cuts-rebuild", elapsedMs = reload.Elapsed.TotalMilliseconds });
        await Measure("video-timeline", () => { timeline.SelectionStart = 1; timeline.SelectionEnd = timeline.SelectionEnd == 2 ? 4 : 2; Call(window, "UpdateSelection"); }, samples);
        var preview = Field<IVideoPreviewSession>(window, "_preview"); preview.IsMuted = true; preview.Play();
        await Measure("video-play-seek", () => { timeline.Position = timeline.Position > 4 ? 1 : 5; Set(window, "_pendingSeek", (double?)timeline.Position!); Call(window, "Tick"); }, samples);
        preview.Pause(); edit.Undo(); edit.Undo(); window.Close(); await Drain();
        Results.Add(new { scenario = "video-closed", timerStopped = !Field<DispatcherTimer>(window, "_timer").IsEnabled });
    }
    private static async Task WaitUntil(Func<bool> ready, int ms) { var sw = Stopwatch.StartNew(); while (!ready()) { if (sw.ElapsedMilliseconds > ms) throw new TimeoutException("Window did not become ready."); await Task.Delay(20); } }
    private static Task WaitForClose(Window window) { var done = new TaskCompletionSource(); window.Closed += (_, _) => done.TrySetResult(); return done.Task; }
    internal static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject { for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++) { var child = VisualTreeHelper.GetChild(root, i); if (child is T typed) yield return typed; foreach (var nested in Descendants<T>(child)) yield return nested; } }
    internal static void Snapshot(Window window, string path) { var bmp = new RenderTargetBitmap((int)window.ActualWidth, (int)window.ActualHeight, 96, 96, PixelFormats.Pbgra32); bmp.Render(window); var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bmp)); using var stream = File.Create(path); encoder.Save(stream); }
    private static Task Write(string output) => File.WriteAllTextAsync(Path.Combine(output, "measurements.json"), JsonSerializer.Serialize(Results, new JsonSerializerOptions { WriteIndented = true }));
}
#endif


