using System.Text.Json;
using SnappySnap.Core;
using SnappySnap.Infrastructure;
using SnappySnap.Application;
using SnappySnap.History;
using Xunit;

namespace SnappySnap.IntegrationTests;

public sealed class PersistenceRecoveryTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "SnappySnapTests", Guid.NewGuid().ToString("N"));
    private AppPaths Paths => new(_root, _root);

    [Fact]
    public async Task Repeating_video_index_after_a_callback_failure_keeps_one_history_item()
    {
        Paths.EnsureDirectories();
        var path = Path.Combine(_root, "export.mp4"); await File.WriteAllBytesAsync(path, [1, 2, 3]);
        await using var repository = new SqliteHistoryRepository(Paths.DatabasePath, new Logger());
        var service = new ShelfService(repository, new ThumbnailService(Paths.ThumbnailPath, repository, new Logger()));
        var first = await service.AddVideoAsync(path, 40, 30, TimeSpan.FromSeconds(2), default);
        var retry = await service.AddVideoAsync(path, 40, 30, TimeSpan.FromSeconds(2), default);
        Assert.Equal(first.Id, retry.Id);
        Assert.Single(await repository.GetRecentAsync(20, default));
        Assert.Equal(new byte[] { 1, 2, 3 }, await File.ReadAllBytesAsync(path));
    }

    [Theory]
    [InlineData("ru", "Light")]
    [InlineData("en", "Dark")]
    [InlineData("zh-CN", "Dark")]
    [InlineData("ja-JP", "Light")]
    [InlineData("es-ES", "Dark")]
    public async Task Locale_and_theme_round_trip_without_losing_existing_preferences(string language, string theme)
    {
        var store = new JsonSettingsStore(Paths, new Logger());
        var settings = AppSettings.Defaults(); settings.General.Language = language; settings.General.Theme = theme;
        settings.General.ShelfRecentCount = 73; settings.Hotkeys.OpenShelf = "Ctrl+Shift+F8";
        await store.SaveAsync(settings, default);
        var loaded = await store.LoadAsync(default);
        Assert.Equal(language, loaded.General.Language); Assert.Equal(theme, loaded.General.Theme);
        Assert.Equal(73, loaded.General.ShelfRecentCount); Assert.Equal("Ctrl+Shift+F8", loaded.Hotkeys.OpenShelf);
    }

    [Fact]
    public async Task Old_and_invalid_locale_settings_keep_the_rest_of_the_profile()
    {
        Paths.EnsureDirectories();
        await File.WriteAllTextAsync(Paths.SettingsPath, """{"schemaVersion":3,"general":{"shelfRecentCount":73,"language":"bad","theme":"bad"}}""");
        var loaded = await new JsonSettingsStore(Paths, new Logger()).LoadAsync(default);
        Assert.Equal("en", loaded.General.Language); Assert.Equal("System", loaded.General.Theme);
        Assert.Equal(73, loaded.General.ShelfRecentCount);
    }

    [Fact]
    public async Task Screenshot_replacement_updates_one_row_and_rejects_stale_thumbnail_completion()
    {
        Paths.EnsureDirectories();
        var path = Path.Combine(_root, "снимок.png");
        await File.WriteAllBytesAsync(path, [1, 2, 3]);
        await using var repository = new SqliteHistoryRepository(Paths.DatabasePath, new Logger());
        var service = new ShelfService(repository, new ThumbnailService(Paths.ThumbnailPath, repository, new Logger()));
        var before = await service.IndexScreenshotAsync(path, 10, 20, new(0, 0, 10, 20), default);
        await repository.UpdateThumbnailAsync(before.Id, before.CreatedAtUtc, "old.jpg", ThumbnailState.Ready, default);
        await File.WriteAllBytesAsync(path, [4, 5, 6, 7]);
        var after = await service.IndexScreenshotAsync(path, 30, 40, new(1, 2, 30, 40), default);
        Assert.Equal(before.Id, after.Id);
        Assert.Null(after.ThumbnailPath);
        Assert.Equal(30, after.WidthPx);
        Assert.Equal(4, after.FileSizeBytes);
        await repository.UpdateThumbnailAsync(before.Id, before.CreatedAtUtc, "stale.jpg", ThumbnailState.Ready, default);
        Assert.Null((await repository.GetByIdAsync(after.Id, default))!.ThumbnailPath);
        await repository.UpdateThumbnailAsync(after.Id, after.CreatedAtUtc, "new.jpg", ThumbnailState.Ready, default);
        Assert.Equal("new.jpg", (await repository.GetByIdAsync(after.Id, default))!.ThumbnailPath);
        Assert.Single(await repository.GetRecentAsync(20, default));
        Assert.Equal(after.Id, (await repository.FindByPathAsync(path.ToUpperInvariant(), default))!.Id);
    }

    [Fact]
    public async Task Schema_zero_is_persisted_as_current_without_losing_preferences()
    {
        Paths.EnsureDirectories();
        await File.WriteAllTextAsync(Paths.SettingsPath, """{"schemaVersion":0,"general":{"shelfRecentCount":73}}""");
        var loaded = await new JsonSettingsStore(Paths, new Logger()).LoadAsync(default);
        Assert.Equal(73, loaded.General.ShelfRecentCount);
        using var json = JsonDocument.Parse(await File.ReadAllTextAsync(Paths.SettingsPath));
        Assert.Equal(6, json.RootElement.GetProperty("schemaVersion").GetInt32());
    }

    [Fact]
    public async Task Settings_io_failure_preserves_valid_profile_and_does_not_restore_defaults()
    {
        Paths.EnsureDirectories();
        var json = """{"schemaVersion":1,"general":{"shelfRecentCount":73}}""";
        await File.WriteAllTextAsync(Paths.SettingsPath, json);
        using (var lease = new FileStream(Paths.SettingsPath, FileMode.Open, FileAccess.Read, FileShare.Delete))
            await Assert.ThrowsAsync<IOException>(() => new JsonSettingsStore(Paths, new Logger()).LoadAsync(default));
        Assert.Equal(json, await File.ReadAllTextAsync(Paths.SettingsPath));
        Assert.Empty(Directory.GetFiles(_root, "*.corrupt-*"));
    }

    [Fact]
    public async Task Corrupt_settings_are_backed_up_before_defaults_are_written()
    {
        Paths.EnsureDirectories();
        await File.WriteAllTextAsync(Paths.SettingsPath, "{broken");
        var loaded = await new JsonSettingsStore(Paths, new Logger()).LoadAsync(default);
        Assert.Equal(50, loaded.General.ShelfRecentCount);
        Assert.Equal("{broken", await File.ReadAllTextAsync(Assert.Single(Directory.GetFiles(_root, "*.corrupt-*"))));
    }

    [Fact]
    public async Task Cancelled_recovery_write_preserves_the_previous_record()
    {
        var store = new JsonRecoveryStore(Paths);
        var record = new RecoverySessionRecord(Guid.NewGuid(), RecoveryState.RecordingStarted, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, "temp.mp4", "final.mp4", "test");
        await store.WriteAsync(record, default);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => store.WriteAsync(record with { State = RecoveryState.Finalizing }, new CancellationToken(true)));
        Assert.Equal(record, Assert.Single(await store.ScanAsync(default)));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Cancelled_or_locked_delete_preserves_media_and_history(bool locked)
    {
        Paths.EnsureDirectories();
        var path = Path.Combine(_root, "capture.png"); await File.WriteAllBytesAsync(path, [1, 2, 3]);
        await using var repository = new SqliteHistoryRepository(Paths.DatabasePath, new Logger());
        var item = await repository.AddAsync(new(MediaType.Screenshot, path, DateTimeOffset.UtcNow, 1, 1, null, null, new(0, 0, 1, 1)), default);
        if (locked)
        {
            using var lease = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            await Assert.ThrowsAsync<IOException>(() => repository.DeleteAsync(item.Id, default));
        }
        else await Assert.ThrowsAnyAsync<OperationCanceledException>(() => repository.DeleteAsync(item.Id, new CancellationToken(true)));
        Assert.Equal(new byte[] { 1, 2, 3 }, await File.ReadAllBytesAsync(path));
        Assert.Equal(item.Id, Assert.Single(await repository.GetRecentAsync(20, default)).Id);
    }

    [Fact]
    public async Task Older_pages_reach_all_525_captures_including_equal_timestamp_boundaries()
    {
        Paths.EnsureDirectories();
        await using var repository = new SqliteHistoryRepository(Paths.DatabasePath, new Logger());
        var time = DateTimeOffset.UtcNow;
        for (var i = 0; i < 525; i++)
        {
            var path = Path.Combine(_root, $"{i}.png"); await File.WriteAllBytesAsync(path, [1]);
            await repository.AddAsync(new(MediaType.Screenshot, path, time.AddSeconds(-(i / 125)), 1, 1, null, null, new(0, 0, 1, 1)), default);
        }
        var service = new ShelfService(repository, new ThumbnailService(Paths.ThumbnailPath, repository, new Logger()));
        var loaded = (await service.LoadRecentAsync(50, default)).ToList();
        var recentIds = loaded.Select(item => item.Id).ToArray();
        Assert.Equal(50, loaded.Count);
        Assert.All(loaded, item => Assert.Equal(time, item.CreatedAtUtc));
        var pages = 0;
        while (pages++ < 10)
        {
            var page = await service.LoadOlderAsync(loaded, 100, default);
            if (page.Count == 0) break;
            loaded.InsertRange(0, page);
        }
        Assert.Equal(525, loaded.Count);
        Assert.Equal(525, loaded.Select(item => item.Id).Distinct().Count());
        Assert.Equal(loaded.OrderBy(item => item.CreatedAtUtc).ThenBy(item => item.Id.ToString(), StringComparer.Ordinal).Select(item => item.Id), loaded.Select(item => item.Id));
        Assert.Equal(recentIds, (await service.LoadRecentAsync(50, default)).Select(item => item.Id));
    }

    [Fact]
    public async Task Repeated_delete_cannot_remove_a_replacement_file_and_reused_path_can_be_indexed()
    {
        Paths.EnsureDirectories();
        var path = Path.Combine(_root, "reused.png"); await File.WriteAllBytesAsync(path, [1]);
        await using var repository = new SqliteHistoryRepository(Paths.DatabasePath, new Logger());
        var input = new NewHistoryItem(MediaType.Screenshot, path, DateTimeOffset.UtcNow, 1, 1, null, null, new(0, 0, 1, 1));
        var old = await repository.AddAsync(input, default);
        await repository.DeleteAsync(old.Id, default);
        await File.WriteAllBytesAsync(path, [2, 3]);
        await repository.DeleteAsync(old.Id, default);
        Assert.Equal(new byte[] { 2, 3 }, await File.ReadAllBytesAsync(path));
        var replacement = await repository.AddAsync(input, default);
        Assert.NotEqual(old.Id, replacement.Id);
        Assert.Equal(2, replacement.FileSizeBytes);
        Assert.Equal(replacement.Id, Assert.Single(await repository.GetRecentAsync(20, default)).Id);
        await Assert.ThrowsAsync<Microsoft.Data.Sqlite.SqliteException>(() => repository.AddAsync(input, default));
        Assert.Equal(replacement.Id, Assert.Single(await repository.GetRecentAsync(20, default)).Id);
    }

    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }
    private sealed class Logger : IAppLogger
    {
        public void Info(string message, IReadOnlyDictionary<string, object?>? properties = null) { }
        public void Warn(string message, IReadOnlyDictionary<string, object?>? properties = null) { }
        public void Error(string message, Exception? exception = null, IReadOnlyDictionary<string, object?>? properties = null) { }
    }
}
