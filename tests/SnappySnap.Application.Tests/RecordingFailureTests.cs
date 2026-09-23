using SnappySnap.Core;
using Xunit;

namespace SnappySnap.Application.Tests;

public sealed partial class RecordingSessionCoordinatorTests
{
    [Fact]
    public async Task Native_failure_callback_returns_before_backend_disposal_finishes()
    {
        await using var fixture = new FailureFixture();
        await fixture.Start();
        var disposing = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Backend.Disposing = () => { disposing.TrySetResult(); release.Task.GetAwaiter().GetResult(); };
        var callback = Task.Run(fixture.Backend.RaiseFailure);
        try
        {
            await disposing.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await callback.WaitAsync(TimeSpan.FromSeconds(5));
        }
        finally { release.TrySetResult(); }
    }

    [Fact]
    public async Task Duplicate_pause_while_transition_is_pending_is_not_a_queued_resume()
    {
        await using var fixture = new FailureFixture();
        await fixture.Start();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Backend.Pausing = async () => { entered.SetResult(); await release.Task; };
        var pause = fixture.Coordinator.PauseOrResumeAsync(default);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        try { await fixture.Coordinator.PauseOrResumeAsync(default).WaitAsync(TimeSpan.FromSeconds(5)); }
        finally { release.SetResult(); }
        await pause;
        Assert.Equal(RecordingState.Paused, fixture.Coordinator.Snapshot.State);
        Assert.Equal(1, fixture.Backend.PauseCount);
        Assert.Equal(0, fixture.Backend.ResumeCount);
        await fixture.Coordinator.StopAsync(default);
    }

    [Fact]
    public async Task Failed_start_releases_backend_and_allows_another_recording()
    {
        await using var fixture = new FailureFixture();
        fixture.Backend.Starting = () => throw new IOException("start failed");
        await Assert.ThrowsAsync<IOException>(fixture.Start);
        Assert.Equal(RecordingState.Idle, fixture.Coordinator.Snapshot.State);
        Assert.Equal(1, fixture.Backend.DisposeCount);
        Assert.True(File.Exists(Assert.Single(fixture.Recovery.Records).TempPath));
        fixture.Backend.Starting = null;
        await fixture.Start();
        await fixture.Coordinator.StopAsync(default);
        Assert.Single(fixture.History.Items);
    }

    [Fact]
    public async Task Duplicate_start_does_not_dispose_or_replace_the_active_session()
    {
        await using var fixture = new FailureFixture();
        await fixture.Start();
        await fixture.Start();
        Assert.Equal(1, fixture.Backend.StartCount);
        Assert.Equal(0, fixture.Backend.DisposeCount);
        Assert.Equal(RecordingState.Recording, fixture.Coordinator.Snapshot.State);
        await fixture.Coordinator.StopAsync(default);
        Assert.Single(fixture.History.Items);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Failed_pause_or_resume_does_not_strand_capture(bool resume)
    {
        await using var fixture = new FailureFixture();
        await fixture.Start();
        if (resume) await fixture.Coordinator.PauseOrResumeAsync(default);
        if (resume) fixture.Backend.Resuming = () => throw new IOException("resume failed");
        else fixture.Backend.Pausing = () => throw new IOException("pause failed");
        await Assert.ThrowsAsync<IOException>(() => fixture.Coordinator.PauseOrResumeAsync(default));
        Assert.Equal(RecordingState.Idle, fixture.Coordinator.Snapshot.State);
        Assert.Equal(1, fixture.Backend.DisposeCount);
        Assert.True(File.Exists(Assert.Single(fixture.Recovery.Records).TempPath));
    }

    [Fact]
    public async Task Native_failure_during_pause_is_serialized_and_retains_recovery()
    {
        await using var fixture = new FailureFixture();
        await fixture.Start();
        var idle = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Coordinator.StateChanged += (_, snapshot) => { if (snapshot.State == RecordingState.Idle) idle.TrySetResult(); };
        fixture.Backend.Pausing = () => { fixture.Backend.RaiseFailure(); return Task.CompletedTask; };
        await fixture.Coordinator.PauseOrResumeAsync(default);
        await idle.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var elapsed = fixture.Coordinator.Snapshot.ActiveDuration;
        fixture.Clock.Timestamp += 1000;
        Assert.Equal(elapsed, fixture.Coordinator.Snapshot.ActiveDuration);
        Assert.Equal(1, fixture.Backend.DisposeCount);
        Assert.Empty(fixture.History.Items);
        Assert.True(File.Exists(Assert.Single(fixture.Recovery.Records).TempPath));
    }

    [Fact]
    public async Task Failed_finalization_retains_media_and_allows_retry_with_a_new_session()
    {
        await using var fixture = new FailureFixture();
        await fixture.Start();
        fixture.Backend.Stopping = () => throw new IOException("stop failed");
        await Assert.ThrowsAsync<IOException>(() => fixture.Coordinator.StopAsync(default));
        Assert.Equal(RecordingState.Idle, fixture.Coordinator.Snapshot.State);
        Assert.Equal(RecoveryState.Finalizing, Assert.Single(fixture.Recovery.Records).State);
        Assert.True(File.Exists(fixture.Recovery.Records[0].TempPath));
        Assert.Empty(fixture.History.Items);
        fixture.Backend.Stopping = null;
        await fixture.Start();
        await fixture.Coordinator.StopAsync(default);
        Assert.Single(fixture.History.Items);
    }

    private sealed class FailureClock : IMonotonicClock
    {
        public long Timestamp { get; set; }
        public long Frequency => 1000;
    }
    private sealed class FailureFixture : IAsyncDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), "SnappySnapTests", Guid.NewGuid().ToString("N"));
        public FakeBackend Backend { get; } = new();
        public FakeHistory History { get; } = new();
        public FakeRecovery Recovery { get; } = new();
        public FailureClock Clock { get; } = new();
        public RecordingSessionCoordinator Coordinator { get; }
        public FailureFixture() => Coordinator = new(new FakeBackendFactory(Backend), History, new FakeThumbnail(), Recovery, new FakeLogger(), Clock);
        public Task Start() => Coordinator.StartAsync(new CapturePlanBuilder().Build(new(0, 0, 20, 20),
            [new("display", new(0, 0, 100, 100), new(0, 0, 100, 100), 96, 96, true)]),
            Path.Combine(_root, "captures"), Path.Combine(_root, "temp"), AppSettings.Defaults(), default);
        public async ValueTask DisposeAsync() { await Coordinator.DisposeAsync(); if (Directory.Exists(_root)) Directory.Delete(_root, true); }
    }
}
