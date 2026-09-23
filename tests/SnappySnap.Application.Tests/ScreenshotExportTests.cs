using SnappySnap.Core;
using Xunit;

namespace SnappySnap.Application.Tests;

public sealed class ScreenshotExportTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Initial_save_keeps_image_and_allows_edit_export_or_discard(bool fail)
    {
        var coordinator = new ScreenshotCaptureCoordinator(new Capture());
        var bounds = new VirtualPixelRect(0, 0, 2, 2);
        var plan = new CapturePlanBuilder().Build(bounds,
            [new MonitorDescriptor("test", bounds, bounds, 96, 96, true)]);
        coordinator.Begin();
        await coordinator.PrepareSelectionAsync(plan, default);
        var image = await coordinator.CaptureAsync(plan, default);
        coordinator.BeginExport();
        Assert.Throws<InvalidOperationException>(() => coordinator.Begin());
        coordinator.Cancel();
        Assert.Equal(ScreenshotState.Exporting, coordinator.Snapshot.State);
        if (fail) coordinator.FailExport(new IOException("Initial write failed"));
        else coordinator.ConfirmOriginalSaved();
        Assert.Equal(ScreenshotState.Editing, coordinator.Snapshot.State);
        Assert.Same(image, coordinator.Snapshot.Image);
        if (fail)
        {
            coordinator.BeginExport();
            coordinator.ConfirmExported();
        }
        else coordinator.Cancel();
        Assert.Equal(ScreenshotState.Idle, coordinator.Snapshot.State);
        Assert.Null(coordinator.Snapshot.Image);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Export_remains_busy_until_the_write_completes_or_fails(bool fail)
    {
        var coordinator = new ScreenshotCaptureCoordinator(new Capture());
        var bounds = new VirtualPixelRect(0, 0, 2, 2);
        var plan = new CapturePlanBuilder().Build(bounds, [new MonitorDescriptor("test", bounds, bounds, 96, 96, true)]);
        coordinator.Begin();
        await coordinator.PrepareSelectionAsync(plan, CancellationToken.None);
        await coordinator.CaptureAsync(plan, CancellationToken.None);
        Assert.Equal(ScreenshotState.Editing, coordinator.Snapshot.State);
        coordinator.BeginExport();
        Assert.Equal(ScreenshotState.Exporting, coordinator.Snapshot.State);
        coordinator.Cancel(); // Closing an editor must not declare an in-flight save finished.
        Assert.Equal(ScreenshotState.Exporting, coordinator.Snapshot.State);
        if (fail)
        {
            coordinator.FailExport(new IOException("Disk full"));
            Assert.Equal(ScreenshotState.Editing, coordinator.Snapshot.State);
            coordinator.Cancel();
        }
        else coordinator.ConfirmExported();
        Assert.Equal(ScreenshotState.Idle, coordinator.Snapshot.State);
    }

    [Fact]
    public async Task Capture_crops_the_prepared_frame_without_calling_backend_again()
    {
        var backend = new Capture
        {
            Image = new CapturedImage(4, 3, Enumerable.Range(1, 48).Select(value => (byte)value).ToArray())
        };
        var monitor = new MonitorDescriptor("test", new(-2, -1, 4, 3), new(-2, -1, 4, 3), 96, 96, true);
        var fullPlan = new CapturePlanBuilder().Build(monitor.Bounds, [monitor]);
        var selectedPlan = new CapturePlanBuilder().Build(new(-1, 0, 2, 2), [monitor]);
        var coordinator = new ScreenshotCaptureCoordinator(backend);

        coordinator.Begin();
        await coordinator.PrepareSelectionAsync(fullPlan, CancellationToken.None);
        var image = await coordinator.CaptureAsync(selectedPlan, CancellationToken.None);

        Assert.Equal(1, backend.CallCount);
        Assert.Equal(2, image.Width);
        Assert.Equal(2, image.Height);
        var expected = Enumerable.Range(21, 8).Select(value => (byte)value)
            .Concat(Enumerable.Range(37, 8).Select(value => (byte)value));
        Assert.Equal(expected, image.Bgra32);
        Assert.False(coordinator.HasFrozenSnapshot);
    }

    private sealed class Capture : IScreenshotCaptureService
    {
        public int CallCount { get; private set; }
        public CapturedImage Image { get; set; } = new(2, 2, new byte[16]);

        public Task<CapturedImage> CaptureAsync(CapturePlan plan, CancellationToken cancellationToken)
        {
            CallCount++;
            return Task.FromResult(Image);
        }
    }
}
