using SnappySnap.Core;
using SnappySnap.History;
using SnappySnap.Infrastructure;
using Xunit;

namespace SnappySnap.IntegrationTests;

public sealed class HistoryRepositoryTests
{
    [Fact]
    public async Task Sqlite_history_round_trips_and_deletes_media()
    {
        var root = Path.Combine(Path.GetTempPath(), "SnappySnapTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var media = Path.Combine(root, "capture.png");
        File.WriteAllBytes(media, [1, 2, 3]);
        try
        {
            var repository = new SqliteHistoryRepository(Path.Combine(root, "db.sqlite"), new TestLogger());
            var created = await repository.AddAsync(new NewHistoryItem(MediaType.Screenshot, media, DateTimeOffset.UtcNow, 2, 2, null, null, new VirtualPixelRect(0, 0, 2, 2)), CancellationToken.None);
            var recent = await repository.GetRecentAsync(20, CancellationToken.None);
            Assert.Single(recent);
            Assert.Equal(created.Id, recent[0].Id);
            Assert.True(recent[0].FileSizeBytes > 0);
            await repository.DeleteAsync(created.Id, CancellationToken.None);
            Assert.False(File.Exists(media));
            Assert.Empty(await repository.GetRecentAsync(20, CancellationToken.None));
            await repository.DisposeAsync();
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Fact]
    public async Task Recovery_store_can_update_and_remove_a_record_without_file_lock_conflict()
    {
        var root = Path.Combine(Path.GetTempPath(), "SnappySnapTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var paths = new AppPaths(Path.Combine(root, "local"), Path.Combine(root, "pictures"));
            var store = new JsonRecoveryStore(paths);
            var sessionId = Guid.NewGuid();
            var record = new RecoverySessionRecord(sessionId, RecoveryState.RecordingStarted, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, Path.Combine(root, "temp.mp4"), Path.Combine(root, "final.mp4"), "test");

            await store.WriteAsync(record, CancellationToken.None);
            await store.MarkAsync(sessionId, RecoveryState.Finalizing, "test finalization", CancellationToken.None);
            var marked = await store.ScanAsync(CancellationToken.None);
            Assert.Single(marked);
            Assert.Equal(RecoveryState.Finalizing, marked[0].State);
            Assert.Equal("test finalization", marked[0].Error);

            await store.MarkAsync(sessionId, RecoveryState.Recovered, null, CancellationToken.None);
            Assert.Empty(await store.ScanAsync(CancellationToken.None));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    private sealed class TestLogger : IAppLogger
    {
        public void Info(string message, IReadOnlyDictionary<string, object?>? properties = null) { }
        public void Warn(string message, IReadOnlyDictionary<string, object?>? properties = null) { }
        public void Error(string message, Exception? exception = null, IReadOnlyDictionary<string, object?>? properties = null) { }
    }
}
