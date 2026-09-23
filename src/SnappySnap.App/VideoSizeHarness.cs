#if DEBUG
using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SnappySnap.Capture;
using SnappySnap.Core;
using SnappySnap.Infrastructure;
using Windows.Media.Editing;
using Windows.Media.MediaProperties;
using Windows.Storage;

namespace SnappySnap.App;

internal static partial class VisualHarness
{
    public static async Task RunExportSizeAsync(string output, string sourceDirectory)
    {
        Directory.CreateDirectory(output);
        var results = new List<object>();
        foreach (var path in Directory.GetFiles(sourceDirectory, "*-q45.mp4"))
        {
            var source = await StorageFile.GetFileFromPathAsync(path);
            var originalBytes = await File.ReadAllBytesAsync(path);
            var clip = await MediaClip.CreateFromFileAsync(source);
            var sourceProfile = await MediaEncodingProfile.CreateFromFileAsync(source);
            var composition = new MediaComposition(); composition.Clips.Add(clip);
            var defaultProfile = composition.CreateDefaultEncodingProfile();
            await File.WriteAllTextAsync(Path.Combine(output, "input-profile.json"), System.Text.Json.JsonSerializer.Serialize(new { DefaultVideo = defaultProfile.Video?.Subtype, DefaultVideoRate = defaultProfile.Video?.Bitrate, DefaultAudio = defaultProfile.Audio?.Subtype, DefaultContainer = defaultProfile.Container?.Subtype, SourceVideo = sourceProfile.Video?.Subtype, SourceVideoRate = sourceProfile.Video?.Bitrate, SourceAudio = sourceProfile.Audio?.Bitrate }, MediaMeasurementJson));
            var target = Path.Combine(output, Path.GetFileNameWithoutExtension(path) + "-edited.mp4");
            var clock = Stopwatch.StartNew();
            await new WindowsMediaVideoEditingService().ExportAsync(path, target, new VideoEditTimeline(clip.OriginalDuration, TimeSpan.Zero, TimeSpan.Zero, [new TimeRange(TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(4))]), null, default);
            var file = await StorageFile.GetFileFromPathAsync(target);
            var result = await MediaClip.CreateFromFileAsync(file); var profile = await MediaEncodingProfile.CreateFromFileAsync(file);
            var sourceVideo = sourceProfile.Video ?? throw new InvalidOperationException("Source video profile is missing.");
            var outputVideo = profile.Video ?? throw new InvalidOperationException("Output video profile is missing.");
            var after = await File.ReadAllBytesAsync(path);
            await File.WriteAllTextAsync(Path.Combine(output, "profile-diagnostic.json"), System.Text.Json.JsonSerializer.Serialize(new { SourceAudio = sourceProfile.Audio?.Bitrate, SourceSubtype = sourceProfile.Audio?.Subtype, DefaultAudio = defaultProfile.Audio?.Bitrate, OutputAudio = profile.Audio?.Bitrate, OriginalUnchanged = originalBytes.SequenceEqual(after), SourceDuration = clip.OriginalDuration.TotalSeconds, Duration = result.OriginalDuration.TotalSeconds, Width = outputVideo.Width, Height = outputVideo.Height }, MediaMeasurementJson));
            var audioBitrateChangedUnexpectedly = sourceProfile.Audio is { } sourceAudio
                ? profile.Audio is not { } outputAudio || outputAudio.Bitrate > sourceAudio.Bitrate * 1.01
                : profile.Audio is not null;
            if (!originalBytes.SequenceEqual(after) || Math.Abs((result.OriginalDuration - clip.OriginalDuration).TotalSeconds + 2) > .05 || outputVideo.Width != sourceVideo.Width || outputVideo.Height != sourceVideo.Height || audioBitrateChangedUnexpectedly)
                throw new InvalidOperationException("Export changed the original, cut geometry or increased audio bitrate.");
            results.Add(new { File = Path.GetFileName(path), Bytes = new FileInfo(target).Length, Seconds = result.OriginalDuration.TotalSeconds, SourceAudio = sourceProfile.Audio?.Bitrate, DefaultAudio = defaultProfile.Audio?.Bitrate, OutputAudio = profile.Audio?.Bitrate, Fps = outputVideo.FrameRate.Numerator / (double)outputVideo.FrameRate.Denominator, Ms = clock.Elapsed.TotalMilliseconds });
            var rendered = new MediaComposition(); rendered.Clips.Add(result);
            using var frame = await rendered.GetThumbnailAsync(TimeSpan.FromSeconds(2), 1920, 1080, VideoFramePrecision.NearestFrame);
            using var input = frame.AsStreamForRead(); using var image = File.Create(Path.ChangeExtension(target, ".jpg")); await input.CopyToAsync(image);
            using var cancelled = new CancellationTokenSource();
            var cancelledPath = Path.Combine(output, Path.GetFileNameWithoutExtension(path) + "-cancelled.mp4");
            var cancellationObserved = false;
            cancelled.CancelAfter(TimeSpan.FromMilliseconds(100));
            try { await new WindowsMediaVideoEditingService().ExportAsync(path, cancelledPath, new VideoEditSession(clip.OriginalDuration).ExportTimeline(), null, cancelled.Token); }
            catch (OperationCanceledException) { cancellationObserved = true; }
            if (!cancellationObserved || File.Exists(cancelledPath)) throw new InvalidOperationException($"Cancelled native export: requested={cancelled.IsCancellationRequested}, observed={cancellationObserved}, fileRemains={File.Exists(cancelledPath)}.");
        }
        await File.WriteAllTextAsync(Path.Combine(output, "export-measurements.json"), System.Text.Json.JsonSerializer.Serialize(results, MediaMeasurementJson));
    }
    public static async Task RunVideoSizeAsync(string output, bool full)
    {
        Directory.CreateDirectory(output);
        using var logger = new FileLogger(Path.Combine(output, "logs"));
        var monitors = new Win32MonitorTopologyService().GetMonitors(); var monitor = monitors.First(x => x.IsPrimary);
        var results = new List<object>();
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(12));
            var plan = new CapturePlanBuilder().Build(new VirtualPixelRect(monitor.Bounds.Left, monitor.Bounds.Top, 1920, 1080), monitors);
            foreach (var scene in new[] { "Q1-static", "Q2-scroll", "Q3-mixed", "Q4-code" })
            {
                var fixturePath = await CreateVideoSizeFixture(output, scene, full ? 62 : 10);
                foreach (var quality in full ? new[] { 45, 55 } : new[] { 45, 55, 65, 75 })
                {
                    var path = Path.Combine(output, $"{scene}-q{quality}.mp4");
                    await using var backend = new ScreenRecorderLibVideoBackend(logger) { FixtureVideoPath = fixturePath };
                    await backend.StartAsync(new VideoRecordingRequest(plan, path, path, new QualityProfile("measure", "measure", 30, quality, true, true), false, false, false), timeout.Token);
                    await Task.Delay(TimeSpan.FromSeconds(full ? 60 : 8), timeout.Token);
                    await backend.StopAsync(timeout.Token);
                    var source = await StorageFile.GetFileFromPathAsync(path); var clip = await MediaClip.CreateFromFileAsync(source);
                    var properties = clip.GetVideoEncodingProperties();
                    var bytes = new FileInfo(path).Length;
                    results.Add(new { Scene = scene, Quality = quality, Bytes = bytes, Seconds = clip.OriginalDuration.TotalSeconds, MBPerMinute = bytes / 1048576d * 60 / clip.OriginalDuration.TotalSeconds, properties.Width, properties.Height, properties.Bitrate });
                    var composition = new MediaComposition(); composition.Clips.Add(clip);
                    using (var frame = await composition.GetThumbnailAsync(TimeSpan.FromSeconds(2), 1920, 1080, VideoFramePrecision.NearestFrame))
                    using (var input = frame.AsStreamForRead())
                    using (var target = File.Create(Path.ChangeExtension(path, ".jpg"))) await input.CopyToAsync(target);
                    if (quality is 45 or 55)
                    {
                        var edited = Path.Combine(output, $"{scene}-q{quality}-edited.mp4");
                        var timeline = new VideoEditTimeline(clip.OriginalDuration, TimeSpan.Zero, TimeSpan.Zero, [new TimeRange(TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(4))]);
                        var watch = Stopwatch.StartNew(); await new WindowsMediaVideoEditingService().ExportAsync(path, edited, timeline, null, timeout.Token);
                        var editedClip = await MediaClip.CreateFromFileAsync(await StorageFile.GetFileFromPathAsync(edited));
                        var editedProperties = editedClip.GetVideoEncodingProperties();
                        if (new FileInfo(path).Length != bytes || Math.Abs((editedClip.OriginalDuration - clip.OriginalDuration).TotalSeconds + 2) > .05 || editedProperties.Width != 1920 || editedProperties.Height != 1080) throw new InvalidOperationException("Video export changed the source, geometry or cut duration.");
                        results.Add(new { Scene = scene, Quality = "edited-" + quality, Bytes = new FileInfo(edited).Length, Seconds = editedClip.OriginalDuration.TotalSeconds, editedProperties.Width, editedProperties.Height, editedProperties.Bitrate, ExportMs = watch.Elapsed.TotalMilliseconds });
                        var editedComposition = new MediaComposition(); editedComposition.Clips.Add(editedClip);
                        using var editedFrame = await editedComposition.GetThumbnailAsync(TimeSpan.FromSeconds(2), 1920, 1080, VideoFramePrecision.NearestFrame);
                        using var frameInput = editedFrame.AsStreamForRead(); using var frameTarget = File.Create(Path.ChangeExtension(edited, ".jpg")); await frameInput.CopyToAsync(frameTarget);
                    }
                    await File.WriteAllTextAsync(Path.Combine(output, "video-measurements.json"), System.Text.Json.JsonSerializer.Serialize(results, MediaMeasurementJson));
                }
            }
    }

    private static async Task<string> CreateVideoSizeFixture(string output, string scene, int seconds)
    {
        var clips = new List<MediaClip>();
        for (var i = 0; i < 20; i++)
        {
            var file = Path.Combine(output, $"{scene}-source-{i}.png"); SaveBitmap(VideoSizeFrame(scene, i), file);
            clips.Add(await MediaClip.CreateFromImageFileAsync(await StorageFile.GetFileFromPathAsync(file), TimeSpan.FromMilliseconds(100)));
        }
        var composition = new MediaComposition();
        for (var i = 0; i < seconds * 10; i++) composition.Clips.Add(clips[i % clips.Count].Clone());
        var path = Path.Combine(output, scene + "-source.mp4");
        var folder = await StorageFolder.GetFolderFromPathAsync(output);
        var target = await folder.CreateFileAsync(Path.GetFileName(path), CreationCollisionOption.FailIfExists);
        var profile = MediaEncodingProfile.CreateMp4(VideoEncodingQuality.HD1080p); profile.Video.Bitrate = 24000000;
        var failure = await composition.RenderToFileAsync(target, MediaTrimmingPreference.Precise, profile);
        if (failure != Windows.Media.Transcoding.TranscodeFailureReason.None) throw new InvalidOperationException("Fixture render failed: " + failure);
        return path;
    }

    private static RenderTargetBitmap VideoSizeFrame(string scene, int frame)
    {
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(28, 32, 36)), null, new Rect(0, 0, 1920, 1080));
            void Text(string text, double x, double y, double size, Brush brush) => dc.DrawText(new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, new Typeface("Consolas"), size, brush, 1), new Point(x, y));
            Text("SnappySnap · UI recording corpus / Интерфейс", 30, 20, 24, Brushes.White);
            for (var row = 0; row < 44; row++)
            {
                var y = 75 + row * 23 - (scene == "Q2-scroll" ? frame * 5 : 0);
                var text = scene == "Q4-code" ? $"{row + 1,3}  public async Task<Capture> SaveAsync(CancellationToken token) => await store.SaveAsync(image, token); // Сохранение 0123456789" : $"{row + 1,3}  Display resolution: 1920 × 1080     Scale: 100%     File: capture_{row:D3}.png     Размер: 128 КБ";
                Text(text, 30, y, scene == "Q4-code" ? 14 : 17, row % 3 == 0 ? Brushes.LightSteelBlue : Brushes.WhiteSmoke);
            }
            if (scene == "Q3-mixed")
            {
                var x = 650 + frame * 18; var y = 200 + frame * 9;
                dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromRgb(235, 238, 236)), new Pen(Brushes.Gray, 1), new Rect(x, y, 600, 360), 12, 12);
                Text("Save changes / Сохранить изменения", x + 28, y + 30, 22, Brushes.Black);
                Text("Capture folder: Pictures\\SnappySnap", x + 28, y + 100, 17, Brushes.Black);
            }
        }
        var bitmap = new RenderTargetBitmap(1920, 1080, 96, 96, PixelFormats.Pbgra32); bitmap.Render(visual); bitmap.Freeze(); return bitmap;
    }
}
#endif
