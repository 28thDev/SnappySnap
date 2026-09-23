using System.Text.Json;
using SnappySnap.Application;
using SnappySnap.Core;
using SnappySnap.Infrastructure;
using Xunit;

namespace SnappySnap.IntegrationTests;

public sealed class EditorPreferencesTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "SnappySnapTests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task Old_profile_migrates_and_styles_survive_a_new_store_instance()
    {
        var paths = new AppPaths(_root, _root); paths.EnsureDirectories();
        await File.WriteAllTextAsync(paths.SettingsPath, """{"schemaVersion":1,"general":{"shelfRecentCount":73},"screenshot":{"format":"Jpg"}}""");
        var store = new JsonSettingsStore(paths, new Logger()); var current = await store.LoadAsync(default);
        Assert.Equal(4, current.SchemaVersion); Assert.Equal(.35, current.Editor.Styles["Highlight"].Opacity);
        var session = new SettingsSession(current, store);
        session.SetStyle("Arrow", current.Editor.Styles["Arrow"] with { Color = "#FF123456", StrokeWidth = 9 });
        Assert.Equal(64, current.Editor.Styles["StepMarker"].StepDiameter);
        session.SetStyle("StepMarker", current.Editor.Styles["StepMarker"] with { StepDiameter = 144 });
        await session.FlushPreferencesAsync(default);
        var restarted = await new JsonSettingsStore(paths, new Logger()).LoadAsync(default);
        Assert.Equal("#FF123456", restarted.Editor.Styles["Arrow"].Color);
        Assert.Equal(9, restarted.Editor.Styles["Arrow"].StrokeWidth);
        Assert.Equal(20, restarted.Editor.Styles["Text"].FontSize);
        Assert.Equal(144, restarted.Editor.Styles["StepMarker"].StepDiameter);
        Assert.Equal(73, restarted.General.ShelfRecentCount); Assert.Equal("Jpg", restarted.Screenshot.Format);
    }

    [Fact]
    public async Task Invalid_individual_style_fields_do_not_discard_valid_settings()
    {
        var paths = new AppPaths(_root, _root); paths.EnsureDirectories();
        await File.WriteAllTextAsync(paths.SettingsPath, """
            {"schemaVersion":2,"general":{"shelfRecentCount":73},"editor":{"styles":{
              "Arrow":{"color":"broken","strokeWidth":"bad","opacity":0.7},
              "Text":{"color":"#FF123456","fontSize":999},"StepMarker":{"stepDiameter":999,"color":"#FF123456"},"Rectangle":null}}}
            """);
        var loaded = await new JsonSettingsStore(paths, new Logger()).LoadAsync(default);
        Assert.Equal(73, loaded.General.ShelfRecentCount);
        Assert.Equal("#FFFF3B30", loaded.Editor.Styles["Arrow"].Color);
        Assert.Equal(5, loaded.Editor.Styles["Arrow"].StrokeWidth);
        Assert.Equal(.7, loaded.Editor.Styles["Arrow"].Opacity);
        Assert.Equal("#FF123456", loaded.Editor.Styles["Text"].Color);
        Assert.Equal(20, loaded.Editor.Styles["Text"].FontSize);
        Assert.Equal(64, loaded.Editor.Styles["StepMarker"].StepDiameter);
        Assert.Equal("#FF123456", loaded.Editor.Styles["StepMarker"].Color);
        Assert.Empty(Directory.GetFiles(_root, "*.corrupt-*"));
    }

    [Fact]
    public async Task Older_settings_draft_and_concurrent_style_changes_preserve_latest_values()
    {
        var store = new PausingStore(); var current = AppSettings.Defaults();
        var session = new SettingsSession(current, store);
        var draft = await session.LoadAsync(default); draft.General.ShelfRecentCount = 87;
        session.SetStyle("Arrow", current.Editor.Styles["Arrow"] with { StrokeWidth = 8 });
        session.SetShelfThumbnailHeight(224);
        var save = session.SaveAsync(draft, default);
        await store.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        session.SetStyle("Text", current.Editor.Styles["Text"] with { FontSize = 32 });
        var placement = new ShelfPlacement("primary", new DipRect(20, 30, 800, 650));
        session.SetShelfPlacement(true, placement); session.SetShelfThumbnailHeight(114);
        var flush = session.FlushPreferencesAsync(default);
        store.Release.SetResult(); await Task.WhenAll(save, flush);
        Assert.Equal(1, store.MaxActive);
        Assert.Equal(87, store.Last!.General.ShelfRecentCount);
        Assert.Equal(8, store.Last.Editor.Styles["Arrow"].StrokeWidth);
        Assert.Equal(32, store.Last.Editor.Styles["Text"].FontSize);
        Assert.Equal(32, draft.Editor.Styles["Text"].FontSize);
        Assert.Equal(114, store.Last.Shelf.ThumbnailHeight); Assert.Equal(placement, store.Last.Shelf.Compact);
        Assert.Equal(store.Last.Shelf, draft.Shelf);
    }

    [Fact]
    public async Task Failed_preferences_write_keeps_current_styles_for_retry()
    {
        var paths = new AppPaths(_root, _root); paths.EnsureDirectories();
        var store = new JsonSettingsStore(paths, new Logger()); var current = await store.LoadAsync(default);
        var session = new SettingsSession(current, store);
        session.SetStyle("Text", current.Editor.Styles["Text"] with { FontSize = 36 });
        using (var lease = new FileStream(paths.SettingsPath, FileMode.Open, FileAccess.Read, FileShare.Read))
            await Assert.ThrowsAnyAsync<IOException>(() => session.FlushPreferencesAsync(default));
        Assert.Equal(36, (await session.LoadAsync(default)).Editor.Styles["Text"].FontSize);
        await session.FlushPreferencesAsync(default);
        Assert.Equal(36, (await store.LoadAsync(default)).Editor.Styles["Text"].FontSize);
    }

    private sealed class PausingStore : ISettingsStore
    {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _active;
        public int MaxActive { get; private set; }
        public AppSettings? Last { get; private set; }
        public Task<AppSettings> LoadAsync(CancellationToken token) => throw new NotSupportedException();
        public async Task SaveAsync(AppSettings settings, CancellationToken token)
        {
            MaxActive = Math.Max(MaxActive, Interlocked.Increment(ref _active));
            Entered.TrySetResult(); await Release.Task.WaitAsync(token);
            Last = JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(settings));
            Interlocked.Decrement(ref _active);
        }
    }
    private sealed class Logger : IAppLogger
    {
        public void Info(string m, IReadOnlyDictionary<string, object?>? p = null) { }
        public void Warn(string m, IReadOnlyDictionary<string, object?>? p = null) { }
        public void Error(string m, Exception? e = null, IReadOnlyDictionary<string, object?>? p = null) { }
    }
    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }
}
