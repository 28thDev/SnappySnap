using SnappySnap.Core;
using Xunit;

namespace SnappySnap.Application.Tests;

public sealed class ScreenshotRecoveryTests
{
    [Fact]
    public async Task Snapshot_preparation_does_not_block_caller_during_synchronous_backend_startup()
    {
        using var gate = new ManualResetEventSlim();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var backend = new BlockingCapture(gate, entered);
        var coordinator = new ScreenshotCaptureCoordinator(backend);
        var plan = new CapturePlanBuilder().Build(new(0, 0, 2, 2), [new("display", new(0, 0, 10, 10), new(0, 0, 10, 10), 96, 96, true)]);
        coordinator.Begin();
        // A dedicated caller avoids competing with the deliberately blocked backend
        // for thread-pool workers on small CI machines.
        var caller = Task.Factory.StartNew(
            () => coordinator.PrepareSelectionAsync(plan, default),
            CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
        Task capture;
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(30));
            capture = await caller.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.False(capture.IsCompleted);
        }
        finally { gate.Set(); }
        await capture.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(ScreenshotState.SelectingRegion, coordinator.Snapshot.State);
    }

    [Fact]
    public async Task Direct_shelf_capture_uses_backend_without_a_frozen_selector_and_can_save()
    {
        var backend = new Capture { Fail = false };
        var coordinator = new ScreenshotCaptureCoordinator(backend);
        var plan = new CapturePlanBuilder().Build(new(0, 0, 2, 2), [new("display", new(0, 0, 10, 10), new(0, 0, 10, 10), 96, 96, true)]);

        coordinator.Begin();
        var image = await coordinator.CaptureDirectAsync(plan, default);

        Assert.Equal(1, backend.Calls);
        Assert.Equal(2, image.Width);
        Assert.Equal(ScreenshotState.Editing, coordinator.Snapshot.State);
        Assert.False(coordinator.HasFrozenSnapshot);
        coordinator.BeginExport();
        coordinator.ConfirmOriginalSaved();
        Assert.Equal(ScreenshotState.Editing, coordinator.Snapshot.State);
        coordinator.Cancel();
        Assert.Equal(ScreenshotState.Idle, coordinator.Snapshot.State);
    }

    [Fact]
    public async Task Direct_capture_failure_returns_to_idle_for_retry()
    {
        var coordinator = new ScreenshotCaptureCoordinator(new Capture());
        var plan = new CapturePlanBuilder().Build(new(0, 0, 2, 2), [new("display", new(0, 0, 10, 10), new(0, 0, 10, 10), 96, 96, true)]);

        coordinator.Begin();
        await Assert.ThrowsAsync<IOException>(() => coordinator.CaptureDirectAsync(plan, default));
        Assert.Equal(ScreenshotState.Error, coordinator.Snapshot.State);
        coordinator.Cancel();
        Assert.Equal(ScreenshotState.Idle, coordinator.Snapshot.State);
    }

    [Fact]
    public async Task Capture_failure_can_be_cancelled_and_retried()
    {
        var backend = new Capture();
        var coordinator = new ScreenshotCaptureCoordinator(backend);
        var plan = new CapturePlanBuilder().Build(new(0, 0, 2, 2), [new("display", new(0, 0, 10, 10), new(0, 0, 10, 10), 96, 96, true)]);
        coordinator.Begin();
        await Assert.ThrowsAsync<IOException>(() => coordinator.PrepareSelectionAsync(plan, default));
        Assert.Equal(ScreenshotState.Error, coordinator.Snapshot.State);
        Assert.False(coordinator.HasFrozenSnapshot);
        coordinator.Cancel();
        Assert.Equal(ScreenshotState.Idle, coordinator.Snapshot.State);
        backend.Fail = false;
        coordinator.Begin();
        await coordinator.PrepareSelectionAsync(plan, default);
        await coordinator.CaptureAsync(plan, default);
        coordinator.BeginExport();
        coordinator.ConfirmExported();
        Assert.Equal(ScreenshotState.Idle, coordinator.Snapshot.State);
        Assert.Null(coordinator.Snapshot.Image);
    }

    [Fact]
    public async Task Completion_publishes_the_final_idle_snapshot_without_retaining_image()
    {
        var coordinator = new ScreenshotCaptureCoordinator(new Capture { Fail = false });
        ScreenshotSnapshot? last = null;
        coordinator.StateChanged += (_, snapshot) => last = snapshot;
        coordinator.Begin();
        var plan = new CapturePlanBuilder().Build(new(0, 0, 2, 2), [new("display", new(0, 0, 10, 10), new(0, 0, 10, 10), 96, 96, true)]);
        await coordinator.PrepareSelectionAsync(plan, default);
        await coordinator.CaptureAsync(plan, default);
        coordinator.BeginExport(); coordinator.ConfirmExported();
        Assert.Equal(ScreenshotState.Idle, last!.State);
        Assert.Null(last.Image);
    }

    [Fact]
    public async Task Cancel_during_selection_releases_frozen_snapshot_and_returns_to_idle()
    {
        var coordinator = new ScreenshotCaptureCoordinator(new Capture { Fail = false });
        var plan = new CapturePlanBuilder().Build(new(0, 0, 2, 2), [new("display", new(0, 0, 10, 10), new(0, 0, 10, 10), 96, 96, true)]);

        coordinator.Begin();
        await coordinator.PrepareSelectionAsync(plan, default);
        Assert.True(coordinator.HasFrozenSnapshot);
        coordinator.Cancel();

        Assert.Equal(ScreenshotState.Idle, coordinator.Snapshot.State);
        Assert.False(coordinator.HasFrozenSnapshot);
    }

    private sealed class Capture : IScreenshotCaptureService
    {
        public bool Fail { get; set; } = true;
        public int Calls { get; private set; }
        public Task<CapturedImage> CaptureAsync(CapturePlan plan, CancellationToken token)
        {
            Calls++;
            return Fail
            ? throw new IOException("Injected capture failure") : Task.FromResult(new CapturedImage(2, 2, new byte[16]));
        }
    }

    private sealed class BlockingCapture(ManualResetEventSlim gate, TaskCompletionSource entered) : IScreenshotCaptureService
    {
        public Task<CapturedImage> CaptureAsync(CapturePlan plan, CancellationToken token)
        {
            entered.SetResult();
            gate.Wait(token);
            return Task.FromResult(new CapturedImage(plan.OutputWidth, plan.OutputHeight, new byte[plan.OutputWidth * plan.OutputHeight * 4]));
        }
    }
}
