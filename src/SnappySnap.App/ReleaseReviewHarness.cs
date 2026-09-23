#if DEBUG
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using SnappySnap.Core;
using SnappySnap.Infrastructure;
using SnappySnap.Localization;
using SnappySnap.Presentation;

namespace SnappySnap.App;

internal static partial class VisualHarness
{
    public static async Task RunReleaseReviewAsync(string output)
    {
        Directory.CreateDirectory(output);
        L.SetLanguage("en"); Ui.ApplyTheme("Dark");
        var paths = new AppPaths(Path.Combine(output, "profile"), output); paths.EnsureDirectories();
        using var logger = new FileLogger(paths.LogsPath);
        var settings = AppSettings.Defaults(); settings.General.StartWithWindows = false; settings.General.CaptureRoot = output; settings.General.Theme = "Dark";
        var results = new List<string>();
        void Check(bool value, string message) => results.Add((value ? "PASS: " : "FAIL: ") + message);
        foreach (var fail in new[] { false, true })
        {
            var store = new ReviewDelayedStore();
            await Snapshot(new SettingsWindow(settings, store, new WindowsStartupRegistration(), logger), output, "settings-pending-" + fail, exercise: async window =>
            {
                var applied = false; ((SettingsWindow)window).SettingsSaved += (_, _) => applied = true;
                RevisionField<ComboBox>(window, "_theme").SelectedValue = "Light";
                var saving = RevisionTask(window, "SaveAsync");
                await store.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
                try
                {
                    Check(((SettingsWindow)window).IsSaving, "Settings reports pending disk write");
                    Check(!RevisionField<ComboBox>(window, "_language").IsEnabled, "Settings cannot be edited during a pending save");
                    window.Close();
                    Check(window.IsVisible, "Close/Cancel cannot dismiss an in-flight save");
                    if (window.IsVisible) CaptureContent(window, Path.Combine(output, "settings-saving.png"));
                }
                finally
                {
                    if (fail) store.Release.TrySetException(new IOException("Injected settings write failure."));
                    else store.Release.TrySetResult();
                    await saving;
                }
                Check(!((SettingsWindow)window).IsSaving, "Pending save state cleared");
                Check(((System.Windows.Media.SolidColorBrush)Ui.Brush("Background")).Color == System.Windows.Media.Color.FromRgb(244, 245, 243), "Save success or failure retains the preview theme");
                Check(fail ? !applied && window.IsVisible && RevisionField<ComboBox>(window, "_language").IsEnabled : applied && !window.IsVisible,
                    fail ? "Failed settings save retains editable window for retry" : "Successful settings save applies once and closes");
            });
        }
        var timeline = new VideoTimeline { Edit = new VideoEditSession(TimeSpan.FromSeconds(12)) };
        var host = new Window { Width = 650, Height = 230, Title = "Timeline capture check" };
        Ui.Shell(host, "", timeline);
        await Snapshot(host, output, "timeline-interruption", exercise: window =>
        {
            var started = 0; var completed = 0;
            timeline.ScrubStarted += () => started++;
            timeline.ScrubCompleted += () => completed++;
            typeof(VideoTimeline).GetMethod("OnMouseLeftButtonDown", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
                .Invoke(timeline, new object[] { new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, System.Windows.Input.MouseButton.Left) { RoutedEvent = Mouse.MouseDownEvent } });
            Check(started == 1 && timeline.IsMouseCaptured, "Timeline starts a captured scrub gesture");
            timeline.ReleaseMouseCapture();
            Check(completed == 1, "Interrupted timeline gesture completes scrubbing exactly once");
            Check(Math.Abs(timeline.Duration - 12) < .001, "Interrupted gesture keeps the video edit unchanged");
            return Task.CompletedTask;
        });
        await File.WriteAllLinesAsync(Path.Combine(output, "results.txt"), results);
        if (results.Any(line => line.StartsWith("FAIL:", StringComparison.Ordinal))) throw new InvalidOperationException("Release review regressions failed; see results.txt.");
    }

    private sealed class ReviewDelayedStore : ISettingsStore
    {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<AppSettings> LoadAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
        public async Task SaveAsync(AppSettings settings, CancellationToken cancellationToken) { Entered.TrySetResult(); await Release.Task; }
    }
}
#endif
