#if DEBUG
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SnappySnap.Application;
using SnappySnap.Core;
using SnappySnap.Editor;
using SnappySnap.History;
using SnappySnap.Infrastructure;
using SnappySnap.Localization;
using SnappySnap.Presentation;
using Windows.Media.Editing;
using Windows.Media.MediaProperties;
using Windows.Storage;

namespace SnappySnap.App;

internal static partial class VisualHarness
{
    // Publication artwork uses real product windows and isolated files, never the desktop/profile.
    public static async Task RunPublicScreenshotsAsync(string output)
    {
        Directory.CreateDirectory(output);
        L.SetLanguage("en"); Ui.ApplyTheme("Dark");
        var paths = new AppPaths(Path.Combine(output, "profile"), Path.Combine(output, "media"));
        paths.EnsureDirectories();
        var media = Path.Combine(output, "media"); Directory.CreateDirectory(media);
        using var logger = new FileLogger(paths.LogsPath);
        var settings = AppSettings.Defaults();
        settings.General.StartWithWindows = false; settings.General.CaptureRoot = media;
        settings.General.Language = "en"; settings.General.Theme = "Dark";
        settings.General.ShelfRecentCount = 6; settings.Shelf = settings.Shelf with { ThumbnailHeight = 184 };
        settings.Screenshot.CopyToClipboard = false;
        var names = new[] { "Project board.png", "Launch checklist.png", "Weekly overview.png" };
        var images = new BitmapSource[3];
        for (var i = 0; i < images.Length; i++)
        {
            images[i] = PublicDemo(i);
            SaveBitmap(images[i], Path.Combine(media, names[i]));
        }

        var editor = new EditorWindow(EditorRenderer.ToCapturedImage(images[0]), logger) { Width = 1200, Height = 820 };
        var red = Color.FromRgb(222, 65, 65);
        void Add(EditorElement element) => RevisionField<EditorCommandHistory>(editor, "_history").Execute(new AddElementCommand(element), editor.Document);
        Add(new RectangleElement(new Rect(436, 202, 226, 155)) { Color = red, StrokeWidth = 4 });
        Add(new ArrowElement(new Point(719, 426), new Point(618, 352)) { Color = red, StrokeWidth = 4 });
        Add(new TextElement("Check the mobile layout", new Point(559, 450)) { Color = red, FontSize = 23 });
        Add(new StepMarkerElement(1, new Point(432, 198)) { Color = red, Bounds = new Rect(410, 176, 44, 44) });
        RevisionInvoke(editor, "Refresh");
        var annotated = Path.Combine(media, "Mobile layout review.png");
        await EditorRenderer.SavePngAsync(editor.Document, annotated, default);
        await Snapshot(editor, output, "screenshot-editor", exercise: window =>
        {
            typeof(EditorWindow).GetMethod("SetZoom", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.Invoke(window, [1d]);
            return Task.CompletedTask;
        });

        var composition = new MediaComposition();
        foreach (var name in names)
            composition.Clips.Add(await MediaClip.CreateFromImageFileAsync(await StorageFile.GetFileFromPathAsync(Path.Combine(media, name)), TimeSpan.FromSeconds(4)));
        var video = Path.Combine(media, "Project walkthrough.mp4");
        var target = await (await StorageFolder.GetFolderFromPathAsync(media)).CreateFileAsync(Path.GetFileName(video), CreationCollisionOption.FailIfExists);
        var profile = MediaEncodingProfile.CreateMp4(VideoEncodingQuality.HD720p);
        profile.Video.Width = 960; profile.Video.Height = 600;
        var render = await composition.RenderToFileAsync(target, MediaTrimmingPreference.Precise, profile);
        if (render != Windows.Media.Transcoding.TranscodeFailureReason.None) throw new InvalidOperationException("Demo video failed: " + render);
        var original = await File.ReadAllBytesAsync(video);
        string? edited = null;
        await Snapshot(new VideoEditorWindow(video, logger) { Width = 1200, Height = 820 }, output, "video-editor", exercise: async window =>
        {
            var videoWindow = (VideoEditorWindow)window;
            await RevisionReady(videoWindow);
            void Select(double start, double end)
            {
                RevisionField<TextBox>(window, "_from").Text = start.ToString(CultureInfo.InvariantCulture);
                RevisionField<TextBox>(window, "_to").Text = end.ToString(CultureInfo.InvariantCulture);
                RevisionInvoke(window, "SetSelection");
            }
            Select(2, 4); Click(window, "Delete fragment"); await RevisionReady(videoWindow);
            Click(window, "Play"); await Task.Delay(1250); Click(window, "Pause");
            if (!Descendants<Image>(window).Any(x => x.Source is WriteableBitmap)) throw new InvalidOperationException("Demo preview produced no native frame.");
            Select(4.5, 6.5);
            await RevisionTask(window, "ExportAsync");
            edited = Directory.GetFiles(media, "Project walkthrough-edited-*.mp4").Single();
            // The normal export completion state is captured, with its real selection/timeline.
        });
        if (!(await File.ReadAllBytesAsync(video)).SequenceEqual(original)) throw new InvalidOperationException("Video export changed its source.");
        var editedClip = await MediaClip.CreateFromFileAsync(await StorageFile.GetFileFromPathAsync(edited!));
        if (Math.Abs(editedClip.OriginalDuration.TotalSeconds - 10) > .25) throw new InvalidOperationException("Demo edit duration is incorrect.");

        await using var repository = new SqliteHistoryRepository(paths.DatabasePath, logger);
        var thumbnails = new ThumbnailService(paths.ThumbnailPath, repository, logger);
        var service = new ShelfService(repository, thumbnails);
        var files = names.Select(name => Path.Combine(media, name)).Concat([video, annotated, edited!]).ToArray();
        for (var i = 0; i < files.Length; i++)
        {
            var isVideo = files[i].EndsWith(".mp4", StringComparison.Ordinal);
            var item = await repository.AddAsync(new NewHistoryItem(isVideo ? MediaType.Video : MediaType.Screenshot, files[i],
                DateTimeOffset.Now.Date.AddHours(10).AddMinutes(i * 8), 960, 600, isVideo ? TimeSpan.FromSeconds(i == 3 ? 12 : 10) : null,
                null, new VirtualPixelRect(0, 0, 960, 600)), default);
            if ((await thumbnails.EnsureThumbnailAsync(item, default)).State != ThumbnailState.Ready)
                throw new InvalidOperationException("Demo thumbnail was not generated.");
        }
        foreach (var compact in new[] { false, true })
        {
            var state = new ShelfState();
            var shelf = new ShelfContent(service, settings, logger, state);
            await Snapshot(new ShelfWindow(shelf, compact) { Width = compact ? 680 : 1080, Height = compact ? 690 : 680 }, output,
                compact ? "shelf-compact" : "shelf-history", exercise: async window =>
                {
                    await shelf.LoadAsync();
                    await Task.WhenAll(state.Rows.Select(row => row.EnsureThumbnailAsync(service, logger, default)));
                    if (state.Rows.Count != 6 || state.Rows.Any(row => row.Thumbnail is null)) throw new InvalidOperationException("Demo history is not fully loaded.");
                    var list = Descendants<ListView>(window).Single();
                    list.SelectedIndex = compact ? 5 : 4;
                    window.UpdateLayout();
                    if (!compact) Descendants<ScrollViewer>(list).First().ScrollToTop();
                    await window.Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
                });
        }
        await File.WriteAllTextAsync(Path.Combine(output, "result.txt"),
            "PASS: actual WPF editor/history; six real local files and loaded thumbnails; native MP4 preview; 12s source exported to 10s without changing original. Synthetic project content only. No desktop capture, clipboard, hotkeys, installation or working profile.\n");
    }

    private static RenderTargetBitmap PublicDemo(int page)
    {
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            Brush Ink(string hex) => (Brush)new BrushConverter().ConvertFromString(hex)!;
            void Box(double x, double y, double width, double height, string color, double radius = 9) =>
                dc.DrawRoundedRectangle(Ink(color), null, new Rect(x, y, width, height), radius, radius);
            void Text(string text, double x, double y, double size = 16, string color = "#263B36", bool bold = false) =>
                dc.DrawText(new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                    new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, bold ? FontWeights.SemiBold : FontWeights.Normal, FontStretches.Normal),
                    size, Ink(color), 1), new Point(x, y));
            Box(0, 0, 960, 600, "#F3F5F2", 0); Box(0, 0, 155, 600, "#E4EBE5", 0);
            Box(19, 24, 30, 30, "#386650"); Text("N", 27, 26, 20, "#FFFFFF", true); Text("Northstar", 59, 29, 17, bold: true);
            Text("WORKSPACE", 22, 96, 11, "#62786A", true);
            var menu = new[] { "Project board", "Checklist", "Overview" };
            for (var i = 0; i < menu.Length; i++)
            {
                if (page == i) Box(12, 124 + 49 * i, 131, 37, "#CFDED1");
                Text(menu[i], 23, 132 + 49 * i, 14, bold: page == i);
            }
            Text("Demo workspace", 22, 556, 12, "#62786A");
            Text(page switch { 0 => "Website refresh", 1 => "Launch checklist", _ => "Weekly overview" }, 186, 37, 30, bold: true);
            Text("September sprint  /  Fictional project", 187, 82, 14, "#6A7B71");
            Box(816, 36, 109, 34, "#386650"); Text("Share board", 831, 44, 13, "#FFFFFF");
            if (page == 0)
            {
                string[] titles = ["Planned", "In progress", "Done"];
                string[] cards = ["Write launch notes", "Mobile checkout", "Update navigation"];
                string[] subtitles = ["Summarize what's new", "Check the small-screen layout", "Simpler links and labels"];
                string[] labels = ["Content", "Needs review", "Complete"];
                string[] tones = ["#DDE7F5", "#FBE7C1", "#D3E8D7"];
                for (var i = 0; i < 3; i++)
                {
                    var x = 186 + 248 * i;
                    Box(x, 136, 233, 391, "#E8EDE7"); Text(titles[i], x + 14, 152, 16, bold: true);
                    Box(x + 10, 205, 213, 147, "#FFFFFF"); Box(x + 23, 219, 114, 25, tones[i]); Text(labels[i], x + 33, 222, 12);
                    Text(cards[i], x + 23, 257, 17, bold: true); Text(subtitles[i], x + 23, 290, 12, "#62786A");
                    Text("Sep 26", x + 23, 325, 12, "#62786A");
                }
                Text("3 tasks  ·  Updated just now", 187, 561, 13, "#62786A");
            }
            else if (page == 1)
            {
                Box(186, 133, 738, 70, "#D3E8D7"); Text("Ready for a final review", 208, 150, 19, bold: true);
                Text("Three checks complete. Two left before launch.", 208, 177, 13, "#52705B");
                string[] rows = ["Navigation links", "Desktop layout", "Image descriptions", "Mobile checkout", "Release notes"];
                for (var i = 0; i < rows.Length; i++)
                {
                    var y = 222 + i * 60;
                    Box(186, y, 738, 50, "#FFFFFF"); Box(204, y + 14, 22, 22, i < 3 ? "#386650" : "#E4EBE5", 5);
                    if (i < 3) Text("✓", 208, y + 14, 15, "#FFFFFF");
                    Text(rows[i], 242, y + 13, 16); Text(i < 3 ? "Done" : "To review", 815, y + 15, 13, "#62786A");
                }
            }
            else
            {
                string[] labels = ["Completed", "In progress", "Ready for review"];
                string[] values = ["12", "3", "2"];
                for (var i = 0; i < 3; i++)
                {
                    var x = 186 + i * 248;
                    Box(x, 133, 233, 119, "#FFFFFF"); Text(labels[i], x + 20, 152, 14, "#62786A"); Text(values[i], x + 20, 183, 35, bold: true);
                }
                Box(186, 279, 738, 248, "#FFFFFF"); Text("Tasks completed this week", 208, 300, 18, bold: true);
                string[] days = ["Mon", "Tue", "Wed", "Thu", "Fri"];
                for (var i = 0; i < 5; i++)
                {
                    var height = 35 + i * 20; var x = 244 + i * 128;
                    Box(x, 473 - height, 52, height, i == 4 ? "#386650" : "#AAC8B1", 5); Text(days[i], x + 12, 488, 13, "#62786A");
                }
            }
        }
        var bitmap = new RenderTargetBitmap(960, 600, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual); bitmap.Freeze(); return bitmap;
    }
}
#endif
