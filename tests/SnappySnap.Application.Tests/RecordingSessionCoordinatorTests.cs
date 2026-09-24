using SnappySnap.Core;
using Xunit;

namespace SnappySnap.Application.Tests;

public sealed partial class RecordingSessionCoordinatorTests
{
    [Fact]
    public async Task Start_pause_resume_stop_creates_one_history_item()
    {
        var root = Path.Combine(Path.GetTempPath(), "SnappySnapTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var backend = new FakeBackend();
            var history = new FakeHistory();
            var coordinator = new SnappySnap.Application.RecordingSessionCoordinator(
                new FakeBackendFactory(backend), history, new FakeThumbnail(), new FakeRecovery(), new FakeLogger(), new SystemMonotonicClock());
            var plan = new CapturePlan(new VirtualPixelRect(0, 0, 320, 200), 320, 200, [new CaptureSegment("display", new VirtualPixelRect(0, 0, 320, 200), new VirtualPixelRect(0, 0, 320, 200))]);

            await coordinator.StartAsync(plan, Path.Combine(root, "captures"), Path.Combine(root, "temp"), AppSettings.Defaults(), CancellationToken.None);
            await coordinator.PauseOrResumeAsync(CancellationToken.None);
            await coordinator.PauseOrResumeAsync(CancellationToken.None);
            await coordinator.StopAsync(CancellationToken.None);

            Assert.Equal(1, backend.StartCount);
            Assert.Equal(1, backend.PauseCount);
            Assert.Equal(1, backend.ResumeCount);
            Assert.Equal(1, backend.StopCount);
            Assert.Single(history.Items);
            Assert.Equal(RecordingState.Idle, coordinator.Snapshot.State);
            await coordinator.DisposeAsync();
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Fact]
    public async Task Duplicate_stop_is_idempotent()
    {
        var root = Path.Combine(Path.GetTempPath(), "SnappySnapTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var backend = new FakeBackend();
            var history = new FakeHistory();
            var coordinator = new SnappySnap.Application.RecordingSessionCoordinator(new FakeBackendFactory(backend), history, new FakeThumbnail(), new FakeRecovery(), new FakeLogger(), new SystemMonotonicClock());
            var plan = new CapturePlan(new VirtualPixelRect(0, 0, 20, 20), 20, 20, [new CaptureSegment("display", new VirtualPixelRect(0, 0, 20, 20), new VirtualPixelRect(0, 0, 20, 20))]);
            await coordinator.StartAsync(plan, Path.Combine(root, "captures"), Path.Combine(root, "temp"), AppSettings.Defaults(), CancellationToken.None);
            await coordinator.StopAsync(CancellationToken.None);
            await coordinator.StopAsync(CancellationToken.None);
            Assert.Equal(1, backend.StopCount);
            Assert.Single(history.Items);
            await coordinator.DisposeAsync();
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Fact]
    public async Task Missing_microphone_is_reported_without_blocking_recording()
    {
        var root = Path.Combine(Path.GetTempPath(), "SnappySnapTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var backend = new FakeBackend(microphoneAvailable: false);
            var coordinator = new SnappySnap.Application.RecordingSessionCoordinator(
                new FakeBackendFactory(backend), new FakeHistory(), new FakeThumbnail(), new FakeRecovery(), new FakeLogger(), new SystemMonotonicClock());
            var plan = new CapturePlan(new VirtualPixelRect(0, 0, 20, 20), 20, 20, [new CaptureSegment("display", new VirtualPixelRect(0, 0, 20, 20), new VirtualPixelRect(0, 0, 20, 20))]);

            await coordinator.StartAsync(plan, Path.Combine(root, "captures"), Path.Combine(root, "temp"), AppSettings.Defaults(), CancellationToken.None);

            Assert.Equal(RecordingState.Recording, coordinator.Snapshot.State);
            Assert.False(coordinator.Snapshot.MicrophoneAvailable);
            Assert.False(coordinator.Snapshot.MicrophoneEnabled);
            await coordinator.StopAsync(CancellationToken.None);
            await coordinator.DisposeAsync();
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Theory]
    [InlineData(false, RecoveryState.Unrecoverable)]
    [InlineData(true, RecoveryState.Finalizing)]
    public async Task Start_failure_only_marks_recovery_when_media_exists(bool afterFileCreated, RecoveryState expectedState)
    {
        var root = Path.Combine(Path.GetTempPath(), "SnappySnapTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var backend = new FakeBackend();
            var recovery = new FakeRecovery();
            if (afterFileCreated) backend.Starting = () => throw new InvalidOperationException("Injected start failure");
            else backend.BeforeStarting = () => throw new InvalidOperationException("Injected start failure");
            var coordinator = new SnappySnap.Application.RecordingSessionCoordinator(
                new FakeBackendFactory(backend), new FakeHistory(), new FakeThumbnail(), recovery, new FakeLogger(), new SystemMonotonicClock());
            var plan = new CapturePlan(new VirtualPixelRect(0, 0, 20, 20), 20, 20, [new CaptureSegment("display", new VirtualPixelRect(0, 0, 20, 20), new VirtualPixelRect(0, 0, 20, 20))]);

            await Assert.ThrowsAsync<InvalidOperationException>(() => coordinator.StartAsync(plan, Path.Combine(root, "captures"), Path.Combine(root, "temp"), AppSettings.Defaults(), CancellationToken.None));

            Assert.Equal(RecordingState.Idle, coordinator.Snapshot.State);
            Assert.Equal(expectedState, Assert.Single(recovery.Records).State);
            Assert.Equal(afterFileCreated, Directory.EnumerateFiles(Path.Combine(root, "temp"), "*.partial.mp4").Any());
            await coordinator.DisposeAsync();
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    private sealed class FakeBackendFactory(FakeBackend backend) : IVideoCaptureBackendFactory
    {
        public IVideoCaptureBackend Create() => backend;
    }

    private sealed class FakeBackend : IVideoCaptureBackend
    {
        private VideoRecordingRequest? _request;
        private readonly bool _microphoneAvailable, _systemAudioAvailable;
        public FakeBackend(bool microphoneAvailable = true, bool systemAudioAvailable = true) { _microphoneAvailable = microphoneAvailable; _systemAudioAvailable = systemAudioAvailable; }
        public int StartCount { get; private set; }
        public int PauseCount { get; private set; }
        public int ResumeCount { get; private set; }
        public int StopCount { get; private set; }
        public int DisposeCount { get; private set; }
        public Func<Task>? Starting { get; set; }
        public Func<Task>? BeforeStarting { get; set; }
        public Func<Task>? Pausing { get; set; }
        public Func<Task>? Resuming { get; set; }
        public Func<Task>? Stopping { get; set; }
        public Action? Disposing { get; set; }
        public void RaiseFailure() => Failed?.Invoke(this, new RecordingBackendError("Injected native failure"));
        public RecordingBackendCapabilities Capabilities { get; } = new(true, true, true, false, true);
        public RecordingBackendState State { get; private set; } = new(false, false, TimeSpan.Zero, false, true);
        public event EventHandler<RecordingBackendState>? StateChanged { add { } remove { } }
        public event EventHandler<RecordingBackendError>? Failed;
        public async Task StartAsync(VideoRecordingRequest request, CancellationToken cancellationToken) { _request = request; if (BeforeStarting is not null) await BeforeStarting(); Directory.CreateDirectory(Path.GetDirectoryName(request.TempPath)!); File.WriteAllBytes(request.TempPath, [1, 2, 3]); StartCount++; if (Starting is not null) await Starting(); State = new(true, false, TimeSpan.FromSeconds(1), false, true, MicrophoneAvailable: _microphoneAvailable, SystemAudioAvailable: _systemAudioAvailable); }
        public async Task PauseAsync(CancellationToken cancellationToken) { PauseCount++; if (Pausing is not null) await Pausing(); State = State with { IsPaused = true }; }
        public async Task ResumeAsync(CancellationToken cancellationToken) { ResumeCount++; if (Resuming is not null) await Resuming(); State = State with { IsPaused = false }; }
        public Task SetSystemAudioMutedAsync(bool muted, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task SetMicrophoneMutedAsync(bool muted, CancellationToken cancellationToken) => Task.CompletedTask;
        public async Task<VideoRecordingResult> StopAsync(CancellationToken cancellationToken) { StopCount++; if (Stopping is not null) await Stopping(); return new VideoRecordingResult(_request!.TempPath, TimeSpan.FromSeconds(2), _request.CapturePlan.OutputWidth, _request.CapturePlan.OutputHeight, false, "fake"); }
        public ValueTask DisposeAsync() { DisposeCount++; Disposing?.Invoke(); return ValueTask.CompletedTask; }
    }

    private sealed class FakeHistory : IHistoryRepository
    {
        public List<HistoryItem> Items { get; } = new();
        public Task<HistoryItem> AddAsync(NewHistoryItem item, CancellationToken cancellationToken) { var history = new HistoryItem(Guid.NewGuid(), item.MediaType, item.FilePath, item.CreatedAtUtc, item.WidthPx, item.HeightPx, item.Duration, new FileInfo(item.FilePath).Length, item.ThumbnailPath, ThumbnailState.Pending, item.SourceBounds, null, null, false, null); Items.Add(history); return Task.FromResult(history); }
        public Task<IReadOnlyList<HistoryItem>> GetRecentAsync(int count, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<HistoryItem>>(Items);
        public async IAsyncEnumerable<HistoryItem> EnumerateOlderAsync(DateTimeOffset? before, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken) { foreach (var item in Items) { yield return item; await Task.Yield(); } }
        public Task DeleteAsync(Guid id, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<HistoryItem?> GetByIdAsync(Guid id, CancellationToken cancellationToken) => Task.FromResult<HistoryItem?>(Items.FirstOrDefault(x => x.Id == id));
        public Task<HistoryItem?> FindByPathAsync(string filePath, CancellationToken cancellationToken) => Task.FromResult<HistoryItem?>(Items.FirstOrDefault(x => x.FilePath == filePath));
        public Task<HistoryItem> UpdateScreenshotAsync(Guid id, NewHistoryItem item, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task UpdateThumbnailAsync(Guid id, DateTimeOffset createdAtUtc, string? thumbnailPath, ThumbnailState state, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class FakeThumbnail : IThumbnailService
    {
        public Task<ThumbnailResult> EnsureThumbnailAsync(HistoryItem item, CancellationToken cancellationToken) => Task.FromResult(new ThumbnailResult(null, ThumbnailState.Pending));
    }

    private sealed class FakeRecovery : IRecoveryStore
    {
        public List<RecoverySessionRecord> Records { get; } = [];
        public Task WriteAsync(RecoverySessionRecord record, CancellationToken cancellationToken) { Records.Add(record); return Task.CompletedTask; }
        public Task<IReadOnlyList<RecoverySessionRecord>> ScanAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<RecoverySessionRecord>>(Records);
        public Task MarkAsync(Guid sessionId, RecoveryState state, string? error, CancellationToken cancellationToken) { var i = Records.FindIndex(r => r.SessionId == sessionId); if (i >= 0) Records[i] = Records[i] with { State = state, Error = error }; return Task.CompletedTask; }
    }

    private sealed class FakeLogger : IAppLogger
    {
        public void Info(string message, IReadOnlyDictionary<string, object?>? properties = null) { }
        public void Warn(string message, IReadOnlyDictionary<string, object?>? properties = null) { }
        public void Error(string message, Exception? exception = null, IReadOnlyDictionary<string, object?>? properties = null) { }
    }
}
