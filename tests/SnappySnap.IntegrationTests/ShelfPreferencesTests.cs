using System.Text.Json;
using SnappySnap.Application;
using SnappySnap.Core;
using SnappySnap.Infrastructure;
using Xunit;

namespace SnappySnap.IntegrationTests;

public sealed class ShelfPreferencesTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "SnappySnapTests", Guid.NewGuid().ToString("N"));
    private AppPaths Paths => new(_root, _root);
    private JsonSettingsStore Store => new(Paths, new Logger());

    [Theory]
    [InlineData(1, 20, 50)]
    [InlineData(2, 20, 50)]
    [InlineData(2, 73, 73)]
    [InlineData(3, 20, 20)]
    public async Task Migration_changes_only_the_old_default_once(int version, int count, int expected)
    {
        Paths.EnsureDirectories();
        await File.WriteAllTextAsync(Paths.SettingsPath, JsonSerializer.Serialize(new { schemaVersion = version, general = new { shelfRecentCount = count } }));
        var loaded = await Store.LoadAsync(default);
        Assert.Equal(5, loaded.SchemaVersion); Assert.Equal(expected, loaded.General.ShelfRecentCount);
        Assert.Equal(expected, (await Store.LoadAsync(default)).General.ShelfRecentCount);
    }

    [Theory]
    [InlineData("{\"schemaVersion\":2}")]
    [InlineData("{\"schemaVersion\":2,\"general\":{}}")]
    public async Task Missing_values_use_new_defaults(string json)
    {
        Paths.EnsureDirectories(); await File.WriteAllTextAsync(Paths.SettingsPath, json);
        var loaded = await Store.LoadAsync(default);
        Assert.Equal(50, loaded.General.ShelfRecentCount); Assert.Equal(164, loaded.Shelf.ThumbnailHeight);
        Assert.Null(loaded.Shelf.Compact); Assert.Null(loaded.Shelf.History);
    }

    [Fact]
    public async Task New_profile_uses_fifty_and_preferences_survive_restart_and_stale_settings_draft()
    {
        var current = await Store.LoadAsync(default); Assert.Equal(50, current.General.ShelfRecentCount);
        var session = new SettingsSession(current, Store);
        var draft = await session.LoadAsync(default); draft.General.ShelfRecentCount = 81;
        var compact = new ShelfPlacement("left", new DipRect(30, 45, 700, 510));
        var history = new ShelfPlacement("primary", new DipRect(100, 70, 1100, 850));
        session.SetShelfPlacement(true, compact); session.SetShelfPlacement(false, history); session.SetShelfThumbnailHeight(224);
        await session.SaveAsync(draft, default);
        var restarted = await Store.LoadAsync(default);
        Assert.Equal(81, restarted.General.ShelfRecentCount); Assert.Equal(224, restarted.Shelf.ThumbnailHeight);
        Assert.Equal(compact, restarted.Shelf.Compact); Assert.Equal(history, restarted.Shelf.History);
        Assert.Equal(restarted.Shelf, draft.Shelf);
    }

    [Fact]
    public async Task Failed_write_preserves_preferences_for_retry()
    {
        var session = new SettingsSession(await Store.LoadAsync(default), Store); session.SetShelfThumbnailHeight(244);
        using (var lease = new FileStream(Paths.SettingsPath, FileMode.Open, FileAccess.Read, FileShare.Read))
            await Assert.ThrowsAnyAsync<IOException>(() => session.FlushPreferencesAsync(default));
        Assert.Equal(244, (await session.LoadAsync(default)).Shelf.ThumbnailHeight);
        await session.FlushPreferencesAsync(default); Assert.Equal(244, (await Store.LoadAsync(default)).Shelf.ThumbnailHeight);
    }

    [Fact]
    public async Task Invalid_shelf_geometry_does_not_reset_other_preferences()
    {
        Paths.EnsureDirectories();
        await File.WriteAllTextAsync(Paths.SettingsPath, """{"schemaVersion":3,"general":{"shelfRecentCount":73},"shelf":{"thumbnailHeight":999,"compact":{"monitorId":"left","bounds":{"x":0,"y":0,"width":-1,"height":500}}}}""");
        var loaded = await Store.LoadAsync(default);
        Assert.Equal(73, loaded.General.ShelfRecentCount); Assert.Equal(244, loaded.Shelf.ThumbnailHeight); Assert.Null(loaded.Shelf.Compact);
    }

    private sealed class Logger : IAppLogger
    {
        public void Info(string m, IReadOnlyDictionary<string, object?>? p = null) { }
        public void Warn(string m, IReadOnlyDictionary<string, object?>? p = null) { }
        public void Error(string m, Exception? e = null, IReadOnlyDictionary<string, object?>? p = null) { }
    }
    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }
}
