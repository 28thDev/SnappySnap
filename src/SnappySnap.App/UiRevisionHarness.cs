#if DEBUG
using System.Reflection;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Media;
using SnappySnap.Application;
using SnappySnap.Core;
using SnappySnap.Editor;
using SnappySnap.History;
using SnappySnap.Infrastructure;
using SnappySnap.Localization;
using SnappySnap.Presentation;

namespace SnappySnap.App;

internal static partial class VisualHarness
{
    public static async Task RunRevisionAsync(string output)
    {
        Directory.CreateDirectory(output);
        var baseline = Path.Combine(output, "flows");
        await RunAsync(baseline);
        var paths = new AppPaths(Path.Combine(output, "profile"), output); paths.EnsureDirectories();
        using var logger = new FileLogger(paths.LogsPath);
        await using var repository = new SqliteHistoryRepository(paths.DatabasePath, logger);
        var service = new ShelfService(repository, new ThumbnailService(paths.ThumbnailPath, repository, logger));
        var store = new JsonSettingsStore(paths, logger);
        var settings = AppSettings.Defaults(); settings.General.StartWithWindows = false; settings.General.Language = "en"; settings.General.Theme = "Dark";
        settings.General.CaptureRoot = output;
        await store.SaveAsync(settings, default);
        var checks = new List<string>();
        void Check(bool value, string description) { if (!value) throw new InvalidOperationException(description); checks.Add("PASS: " + description); }

        var label = Ui.Button("Save changes", "", (_, _) => { });
        L.SetLanguage("ru"); await System.Windows.Application.Current.Dispatcher.InvokeAsync(() => { });
        Check(Equals(label.Content, L.T("Save changes")) && Name(label) == L.T("Save changes"), "Open control text and accessible name switch to Russian");
        L.SetLanguage("en"); await System.Windows.Application.Current.Dispatcher.InvokeAsync(() => { });
        Check(Equals(label.Content, "Save changes") && Name(label) == "Save changes", "Open control switches back to English");

        var saved = false;
        await Snapshot(new SettingsWindow(settings, store, new WindowsStartupRegistration(), logger), output, "settings-language-save", exercise: async window =>
        {
            ((SettingsWindow)window).SettingsSaved += (_, value) => { settings = value; saved = true; L.SetLanguage(value.General.Language); Ui.ApplyTheme(value.General.Theme); };
            RevisionField<ComboBox>(window, "_language").SelectedValue = "ru";
            RevisionField<ComboBox>(window, "_theme").SelectedValue = "Light";
            await RevisionTask(window, "SaveAsync");
        });
        var persisted = await store.LoadAsync(default);
        Check(saved && persisted.General.Language == "ru" && persisted.General.Theme == "Light", "Settings save persists Russian and Light without changing startup registration");
        await System.Windows.Application.Current.Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        var themeWindow = new Window { Width = 500, Height = 260, Title = "Appearance check" };
        Ui.Shell(themeWindow, "", Ui.Text("Save changes"));
        await Snapshot(themeWindow, output, "live-theme-switch", exercise: async window =>
        {
            Ui.ApplyTheme("Dark"); await window.Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            Check(((SolidColorBrush)window.Background).Color == Color.FromRgb(32, 35, 37), "Open WPF window applies Dark palette");
            Ui.ApplyTheme("Light"); await window.Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            Check(L.Current.Language == "ru" && ((SolidColorBrush)window.Background).Color == Color.FromRgb(244, 245, 243), "Open WPF window applies Light palette and saved Russian locale");
        });
        await Snapshot(new SettingsWindow(settings, store, new WindowsStartupRegistration(), logger), output, "settings-language-cancel", window =>
        {
            RevisionField<ComboBox>(window, "_language").SelectedValue = "en";
            RevisionField<ComboBox>(window, "_theme").SelectedValue = "Dark";
        });
        persisted = await store.LoadAsync(default);
        Check(persisted.General.Language == "ru" && persisted.General.Theme == "Light" && L.Current.Language == "ru", "Closing unsaved settings preserves saved language and appearance");

        var updates = new UpdateCoordinator(new RevisionUpdateService(), logger);
        await updates.CheckAsync(); await updates.DownloadAsync();
        var previousOffer = updates.Offer; var previousDownload = updates.Download;
        await Snapshot(new SettingsWindow(settings, store, new WindowsStartupRegistration(), logger, updates: updates, installUpdate: () => throw new InvalidOperationException("Installation is forbidden in this harness."), checkUpdate: async () => { await updates.CheckAsync(); }), output, "updates-draft", window =>
        {
            ((SettingsWindow)window).SelectSection("Updates"); window.UpdateLayout();
            var draft = RevisionField<AppSettings>(window, "_settings"); draft.Updates.AutomaticChecks = false;
            Check(Descendants<Button>(window).Single(x => Name(x) == L.T("Check now")).IsEnabled, "Manual check remains available with automatic checks disabled");
        });
        Check(updates.Offer == previousOffer && updates.Download == previousDownload && updates.State == UpdateState.Ready, "Cancelling update settings retains verified offer and package");

        var missingPath = Path.Combine(output, "missing.mp4");
        await File.WriteAllBytesAsync(missingPath, [0]);
        var missing = await repository.AddAsync(new NewHistoryItem(MediaType.Video, missingPath, DateTimeOffset.UtcNow, 960, 600, TimeSpan.FromSeconds(12), null, new VirtualPixelRect(0, 0, 960, 600)), default);
        File.Delete(missingPath);
        var state = new ShelfState { Loaded = true }; state.Rows.Add(new ShelfRow(missing));
        var shelf = new ShelfContent(service, settings, logger, state); var dismissed = false;
        shelf.DismissRequested += (_, _) => dismissed = true;
        await Snapshot(new ShelfWindow(shelf, true), output, "shelf-missing-video", window =>
        {
            RevisionField<System.Windows.Controls.ListView>(shelf, "_list").SelectedIndex = 0;
            RevisionInvoke(shelf, "EditSelected");
            Check(!dismissed && RevisionField<TextBlock>(shelf, "_message").Visibility == Visibility.Visible, "Missing video shows an actionable error while Shelf remains open");
        });

        var video = Path.Combine(baseline, "video-fixture.mp4");
        var original = await File.ReadAllBytesAsync(video);
        await Snapshot(new VideoEditorWindow(video, logger), output, "video-retry", asyncWindow: true, exercise: async window =>
        {
            var editor = (VideoEditorWindow)window;
            var timeline = RevisionField<VideoTimeline>(window, "_timeline");
            await RevisionReady(editor);
            RevisionField<Expander>(window, "_exactTime").IsExpanded = true;
            RevisionField<TextBox>(window, "_from").Text = "1,5"; RevisionField<TextBox>(window, "_to").Text = "2,5";
            RevisionInvoke(window, "SetSelection");
            Check(timeline.SelectionStart == 1.5 && timeline.SelectionEnd == 2.5, "Russian exact time accepts decimal comma");
            var peer = UIElementAutomationPeer.CreatePeerForElement(timeline)!;
            var range = (IRangeValueProvider)peer.GetPattern(PatternInterface.RangeValue)!;
            var selection = (IValueProvider)peer.GetPattern(PatternInterface.Value)!;
            range.SetValue(3);
            Check(Math.Abs(range.Maximum - 12) < .1 && range.Value == 3 && selection.Value.Contains("1,5", StringComparison.Ordinal), "Timeline exposes accessible playhead and localized selection values: " + range.Maximum + " / " + range.Value + " / " + selection.Value);
            L.SetLanguage("en");
            await window.Dispatcher.InvokeAsync(() => { });
            Check(Equals(Descendants<Slider>(window).Single(x => Name(x) == "Timeline zoom").ToolTip, "Timeline zoom") &&
                Equals(Descendants<CheckBox>(window).Single().ToolTip, "Preview volume does not affect export."), "Open video editor tooltips switch locale with their controls");
            RevisionField<TextBox>(window, "_from").Text = "1.5"; RevisionField<TextBox>(window, "_to").Text = "2.5";
            RevisionInvoke(window, "SetSelection");
            Check(timeline.SelectionStart == 1.5 && timeline.SelectionEnd == 2.5, "English exact time accepts decimal point");
            RevisionInvoke(window, "DeleteSelection"); await RevisionReady(editor);
            var calls = 0; var outputs = new List<string>();
            editor.SavedAsync = async result =>
            {
                calls++; outputs.Add(result.OutputPath);
                // Simulate a failure after the row was committed, before the UI acknowledgement.
                await service.AddVideoAsync(result.OutputPath, 960, 600, result.Duration, default);
                if (calls == 1) throw new IOException("Injected history acknowledgement failure.");
            };
            await RevisionTask(window, "ExportAsync");
            Check(RevisionField<object?>(window, "_pendingExport") is not null && !timeline.IsEnabled && !RevisionField<Expander>(window, "_exactTime").IsEnabled, "Completed MP4 is retained and editing locked while history acknowledgement is pending");
            CaptureContent(window, Path.Combine(output, "video-history-retry-state.png"));
            await RevisionTask(window, "ExportAsync");
            Check(calls == 2 && outputs[0] == outputs[1] && RevisionField<object?>(window, "_pendingExport") is null, "Retry reuses the same MP4 and clears pending state");
            var indexed = await repository.GetRecentAsync(100, default);
            Check(indexed.Count(x => x.FilePath == outputs[0]) == 1, "Retry after committed history row creates no duplicate");
            var after = await File.ReadAllBytesAsync(video);
            Check(original.SequenceEqual(after), "Original MP4 bytes remain unchanged after edit and retry");
        });

        var image = EditorRenderer.ToCapturedImage(Fixture());
        var monitor = new MonitorDescriptor("revision-fixture", new VirtualPixelRect(0, 0, 1280, 800), new VirtualPixelRect(0, 0, 1280, 760), 96, 96, true);
        var region = new VirtualPixelRect(1100, 100, 180, 500);
        foreach (var language in new[] { "en", "ru" })
        foreach (var theme in new[] { "Dark", "Light" })
        {
            L.SetLanguage(language); Ui.ApplyTheme(theme); settings.General.Language = language; settings.General.Theme = theme;
            var prefix = language + "-" + theme.ToLowerInvariant();
            foreach (var section in new[] { "General", "Hotkeys", "Screenshots", "Recording", "Updates" })
                await Snapshot(new SettingsWindow(settings, store, new WindowsStartupRegistration(), logger, updates: updates, installUpdate: () => Task.CompletedTask) { Width = 780, Height = 600 }, output, prefix + "-settings-" + section.ToLowerInvariant(), window => ((SettingsWindow)window).SelectSection(section));
            await Snapshot(new EditorWindow(image, logger) { Width = 900, Height = 560 }, output, prefix + "-image-editor");
            await Snapshot(new VideoEditorWindow(video, logger) { Width = 940, Height = 620 }, output, prefix + "-video-editor", asyncWindow: true, exercise: async window => await RevisionReady((VideoEditorWindow)window));
            await Snapshot(new ShelfWindow(service, settings, logger) { Width = 780, Height = 600 }, output, prefix + "-shelf");
            foreach (var mode in new[] { RecordingState.Paused, RecordingState.Finalizing })
                await Snapshot(new RecordingPillWindow(region, new[] { monitor }, settings, logger), output, prefix + "-" + mode.ToString().ToLowerInvariant(), window => ((RecordingPillWindow)window).Update(new RecordingSnapshot(mode, TimeSpan.FromSeconds(72), false, false, false, null, null, null, false)), exercise: window =>
                {
                    Check(window.Left >= 0 && window.Left + window.ActualWidth <= monitor.WorkArea.Right + 1, prefix + " localized " + mode + " pill stays within the monitor after resizing");
                    return Task.CompletedTask;
                });
            checks.Add("RENDERED: " + prefix + " five settings pages, both editors, Shelf, recording paused/finalizing");
        }
        using var icon = (System.Drawing.Icon)typeof(SnappySnapRuntime).GetMethod("LoadTrayIcon", BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, null)!;
        Check(icon.Width > 0, "Embedded taskbar-appropriate tray icon decodes");
        await File.WriteAllLinesAsync(Path.Combine(output, "revision-results.txt"), checks);
    }

    private static T RevisionField<T>(object value, string name) => (T)value.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(value)!;
    private static object? RevisionInvoke(object value, string name) => value.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(value, null);
    private static Task RevisionTask(object value, string name) => (Task)RevisionInvoke(value, name)!;
    private static async Task RevisionReady(VideoEditorWindow window)
    {
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (!RevisionField<Button>(window, "_export").IsEnabled && DateTime.UtcNow < deadline) await Task.Delay(100);
        if (!RevisionField<Button>(window, "_export").IsEnabled) throw new InvalidOperationException("Video editor did not become ready.");
    }
    private sealed class RevisionUpdateService : IUpdateService
    {
        private readonly UpdateRelease _release = new("99.0.0", DateTimeOffset.UtcNow, "Isolated test fixture", "fixture.exe", 1, "fixture", "win-x64", 22000);
        public Task<UpdateOffer?> CheckAsync(CancellationToken cancellationToken) => Task.FromResult<UpdateOffer?>(new(_release, [], []));
        public Task<DownloadedUpdate> DownloadAsync(UpdateOffer offer, IProgress<double>? progress, CancellationToken cancellationToken) => Task.FromResult(new DownloadedUpdate(_release, "fixture-cache"));
        public Task VerifyAsync(DownloadedUpdate update, CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
#endif
