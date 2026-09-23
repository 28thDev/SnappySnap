#if DEBUG
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SnappySnap.Core;
using SnappySnap.Application;
using SnappySnap.History;
using SnappySnap.Editor;
using SnappySnap.Infrastructure;
using SnappySnap.Localization;
using SnappySnap.Presentation;

namespace SnappySnap.App;

internal static partial class VisualHarness
{
    private static readonly System.Text.Json.JsonSerializerOptions MediaMeasurementJson = new() { WriteIndented = true };
    public static async Task RunMediaOptimizationAsync(string output)
    {
        Directory.CreateDirectory(output);
        L.SetLanguage("ru"); Ui.ApplyTheme("Dark");
        var paths = new AppPaths(Path.Combine(output, "profile"), output); paths.EnsureDirectories();
        using var logger = new FileLogger(paths.LogsPath);
        var settings = AppSettings.Defaults(); settings.General.StartWithWindows = false; settings.General.Theme = "Dark"; settings.General.CaptureRoot = output;
        settings.Screenshot.CopyToClipboard = false;
        var store = new JsonSettingsStore(paths, logger); await store.SaveAsync(settings, default);
        var measurements = new List<object>();
        await VerifyModelessDragAsync(output, paths, settings, logger);
        await Snapshot(new SettingsWindow(settings, store, new WindowsStartupRegistration(), logger), output, "settings", exercise: async window =>
        {
            var language = RevisionField<ComboBox>(window, "_language"); var theme = RevisionField<ComboBox>(window, "_theme");
            for (var i = 0; i < 8; i++)
            {
                theme.SelectedValue = i % 2 == 0 ? "Light" : "Dark";
                language.IsDropDownOpen = true; await Task.Delay(40); language.IsDropDownOpen = false;
                window.Width = i % 2 == 0 ? 780 : 1040; window.UpdateLayout(); await Task.Delay(40);
                if (language.ActualWidth > 200 || language.ActualHeight > 50 || theme.ActualHeight > 50) throw new InvalidOperationException("Settings selectors grew after opening/resizing.");
                var expected = i % 2 == 0 ? Color.FromRgb(244, 245, 243) : Color.FromRgb(32, 35, 37);
                if (((SolidColorBrush)window.Background).Color != expected) throw new InvalidOperationException("Theme did not preview immediately.");
                if ((await store.LoadAsync(default)).General.Theme != "Dark") throw new InvalidOperationException("Preview persisted before Save.");
            }
            theme.SelectedValue = "Light";
            measurements.Add(new { Test = "settings", language.ActualWidth, language.ActualHeight, ThemeHeight = theme.ActualHeight });
        });
        if (((SolidColorBrush)Ui.Brush("Background")).Color != Color.FromRgb(32, 35, 37)) throw new InvalidOperationException("Cancel did not restore the saved theme.");
        await Snapshot(new SettingsWindow(settings, store, new WindowsStartupRegistration(), logger), output, "settings-save", exercise: async window =>
        {
            RevisionField<ComboBox>(window, "_theme").SelectedValue = "Light";
            await RevisionTask(window, "SaveAsync");
            if (window.IsVisible) throw new InvalidOperationException("Settings save did not close.");
        });
        if ((await store.LoadAsync(default)).General.Theme != "Light" || ((SolidColorBrush)Ui.Brush("Background")).Color != Color.FromRgb(244, 245, 243)) throw new InvalidOperationException("Save did not persist and retain the preview theme.");
        foreach (var locale in new[] { "ru", "en" }) foreach (var appearance in new[] { "Light", "Dark" })
        {
            L.SetLanguage(locale); Ui.ApplyTheme(appearance);
            settings.General.Theme = appearance; settings.General.Language = locale;
            await Snapshot(new SettingsWindow(settings, store, new WindowsStartupRegistration(), logger) { Width = 780, Height = 600 }, output, $"settings-min-{locale}-{appearance}");
            await using (var repository = new SqliteHistoryRepository(paths.DatabasePath, logger))
            {
                var service = new ShelfService(repository, new ThumbnailService(paths.ThumbnailPath, repository, logger));
                foreach (var compact in new[] { true, false })
                {
                    var shelf = new ShelfContent(service, settings, logger);
                    await Snapshot(new ShelfWindow(shelf, compact) { Width = compact ? 560 : 780, Height = compact ? 420 : 600 }, output, $"shelf-{locale}-{appearance}-{compact}", exercise: async window =>
                    {
                        await shelf.LoadAsync(); window.UpdateLayout();
                        var opened = false; shelf.SettingsRequested += (_, _) => opened = true;
                        var button = Descendants<Button>(window).Single(x => Name(x) == L.T("Settings")); button.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
                        if (!opened || !button.IsEnabled) throw new InvalidOperationException("Settings is not directly accessible.");
                        var list = Descendants<ListView>(window).Single();
                        if (button.TranslatePoint(new Point(0, button.ActualHeight), shelf).Y > list.TranslatePoint(new Point(), shelf).Y) throw new InvalidOperationException("Settings overlaps Shelf content.");
                    });
                }
            }
            var notice = new StartupNoticeWindow(settings.Hotkeys, new HashSet<int>(), logger);
            notice.Left = -15000; notice.Top = -15000;
            notice.Loaded += (_, _) => { notice.Left = -15000; notice.Top = -15000; };
            var watch = Stopwatch.StartNew(); var closed = new TaskCompletionSource();
            notice.Closed += (_, _) => closed.TrySetResult();
            notice.Show(); await Task.Delay(180);
            if (notice.IsActive) throw new InvalidOperationException("Startup notice stole focus.");
            CaptureContent(notice, Path.Combine(output, $"startup-{locale}-{appearance}.png"));
            await closed.Task.WaitAsync(TimeSpan.FromSeconds(7));
            if (watch.Elapsed.TotalSeconds < 4.8 || watch.Elapsed.TotalSeconds > 6) throw new InvalidOperationException("Startup notice lifetime is not five seconds.");
            measurements.Add(new { Test = "startup", Locale = locale, Appearance = appearance, Seconds = watch.Elapsed.TotalSeconds });
        }
        foreach (var (name, bitmap) in new[] { ("ui-alpha", Fixture()), ("ui", OpaqueMediaFixture()), ("gradient", GradientFixture()) })
        {
            foreach (var format in new[] { "Pbgra32", "Bgr24", "Indexed8", "Jpg90", "Jpg85", "Jpg80" })
            {
                var clock = Stopwatch.StartNew();
                BitmapSource source = bitmap;
                if (format is "Bgr24" or "Indexed8") source = new FormatConvertedBitmap(bitmap, format == "Bgr24" ? PixelFormats.Bgr24 : PixelFormats.Indexed8, null, 0);
                var jpeg = format.StartsWith("Jpg", StringComparison.Ordinal);
                BitmapEncoder encoder = jpeg ? new JpegBitmapEncoder { QualityLevel = int.Parse(format[3..], System.Globalization.CultureInfo.InvariantCulture) } : new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(source));
                var file = Path.Combine(output, name + "-" + format + (jpeg ? ".jpg" : ".png"));
                using (var stream = File.Create(file)) encoder.Save(stream);
                measurements.Add(new { Test = name, Format = format, Bytes = new FileInfo(file).Length, Ms = clock.Elapsed.TotalMilliseconds });
            }
            var target = Path.Combine(output, name + "-production.png");
            var watch = Stopwatch.StartNew(); await EditorRenderer.SaveBitmapAsync(bitmap, target, "Png", default);
            measurements.Add(new { Test = name, Format = "production", Bytes = new FileInfo(target).Length, Ms = watch.Elapsed.TotalMilliseconds });
        }
        await File.WriteAllTextAsync(Path.Combine(output, "measurements.json"), System.Text.Json.JsonSerializer.Serialize(measurements, MediaMeasurementJson));
    }
    private static async Task VerifyModelessDragAsync(string output, AppPaths paths, AppSettings settings, IAppLogger logger)
    {
        await using var repository = new SqliteHistoryRepository(paths.DatabasePath, logger);
        var service = new ShelfService(repository, new ThumbnailService(paths.ThumbnailPath, repository, logger));
        var path = Path.Combine(output, "drag-source.png"); await EditorRenderer.SaveBitmapAsync(Fixture(), path, "Png", default);
        await service.AddScreenshotAsync(path, 960, 600, default);
        var original = await File.ReadAllBytesAsync(path);
        var state = new ShelfState { ConfigureEditor = editor => { editor.WindowStartupLocation = WindowStartupLocation.Manual; editor.Left = -15000; editor.Top = -15000; editor.ShowActivated = false; } };
        var shelf = new ShelfContent(service, settings, logger, state);
        var window = new Window { Content = shelf, Width = 780, Height = 600, Left = -15000, Top = -15000, ShowActivated = false };
        EditorWindow? editor = null;
        try
        {
            window.Show(); await shelf.LoadAsync(); await Task.Delay(100); window.UpdateLayout();
            var list = Descendants<ListView>(shelf).Single(); list.SelectedIndex = 0;
            RevisionInvoke(shelf, "EditSelected");
            for (var attempt = 0; attempt < 100 && editor is null; attempt++)
            {
                await Task.Delay(30); editor = System.Windows.Application.Current.Windows.OfType<EditorWindow>().FirstOrDefault();
            }
            if (editor is null || !IsWindowEnabled(new System.Windows.Interop.WindowInteropHelper(window).Handle)) throw new InvalidOperationException("Editor disabled Shelf's native window.");
            var data = shelf.CreateSelectedFileDrop();
            if (data?.GetData(DataFormats.FileDrop) is not string[] files || files.Length != 1 || files[0] != path) throw new InvalidOperationException("Open editor blocked saved-file drag payload.");
            var afterOpen = await File.ReadAllBytesAsync(path);
            if (!original.SequenceEqual(afterOpen)) throw new InvalidOperationException("Opening editor changed the dragged source.");
            var entered = new TaskCompletionSource(); var release = new TaskCompletionSource();
            var save = editor.SaveRequestedAsync!;
            editor.SaveRequestedAsync = async request => { entered.SetResult(); await release.Task; await save(request); };
            var saving = RevisionTask(editor, "CommitAsync"); await entered.Task;
            editor.Close();
            if (!editor.IsVisible || !editor.IsSaving) throw new InvalidOperationException("Modeless editor closed during save.");
            release.SetResult(); await saving;
            for (var attempt = 0; attempt < 100 && state.IsEditing; attempt++) await Task.Delay(30);
            if (editor.IsVisible || state.IsEditing || shelf.IsSaving) throw new InvalidOperationException("Successful modeless save did not release editing state.");
            await File.WriteAllTextAsync(Path.Combine(output, "modeless-results.txt"), "PASS: real Shelf edit leaves its native HWND enabled; FileDrop payload contains unchanged saved image; in-flight save cannot close; completed save closes and releases Shelf state. Physical external OLE drop was not simulated.\n");
        }
        finally
        {
            if (editor?.IsVisible == true) { typeof(EditorWindow).GetField("_committing", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.SetValue(editor, true); editor.Close(); }
            window.Close();
        }
    }
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
    private static extern bool IsWindowEnabled(nint hwnd);
    private static BitmapSource OpaqueMediaFixture()
    {
        // WPF's text rasterizer leaves some alpha=254 edges even over an opaque brush.
        // A desktop capture is already composited; model that separate opaque input explicitly.
        var image = EditorRenderer.ToCapturedImage(Fixture());
        for (var i = 3; i < image.Bgra32.Length; i += 4) image.Bgra32[i] = 255;
        return EditorRenderer.ToBitmapSource(image);
    }
    private static BitmapSource GradientFixture()
    {
        var pixels = new byte[1920 * 1080 * 4];
        for (var y = 0; y < 1080; y++) for (var x = 0; x < 1920; x++)
        {
            var offset = (y * 1920 + x) * 4; pixels[offset] = (byte)(x * 255 / 1919); pixels[offset + 1] = (byte)(y * 255 / 1079); pixels[offset + 2] = (byte)((x + y) * 255 / 2998); pixels[offset + 3] = 255;
        }
        var image = BitmapSource.Create(1920, 1080, 96, 96, PixelFormats.Bgra32, null, pixels, 1920 * 4); image.Freeze(); return image;
    }
}
#endif
