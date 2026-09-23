using SnappySnap.Core;
using Xunit;

namespace SnappySnap.Core.Tests;

public sealed class CaptureBoundaryTests
{
    private static readonly MonitorDescriptor[] Monitors =
    [
        new("above", new(-100, -200, 200, 200), new(-100, -200, 200, 180), 144, 144, false),
        new("below", new(-100, 0, 300, 300), new(-100, 0, 300, 280), 192, 192, true)
    ];

    [Fact]
    public void Vertical_boundary_uses_physical_coordinates_despite_different_dpi()
    {
        var plan = new CapturePlanBuilder().Build(new(-50, -50, 100, 100), Monitors);
        Assert.Equal(2, plan.Segments.Count);
        Assert.Equal(new(50, 150, 100, 50), plan.Segments[0].SourceRectOnMonitorPx);
        Assert.Equal(new(0, 0, 100, 50), plan.Segments[0].DestinationRectInOutputPx);
        Assert.Equal(new(50, 0, 100, 50), plan.Segments[1].SourceRectOnMonitorPx);
        Assert.Equal(new(0, 50, 100, 50), plan.Segments[1].DestinationRectInOutputPx);
    }

    [Fact]
    public void Touching_boundary_does_not_add_an_empty_segment()
    {
        var plan = new CapturePlanBuilder().Build(new(-50, -100, 100, 100), Monitors);
        Assert.Equal("above", Assert.Single(plan.Segments).MonitorId);
    }

    [Fact]
    public void Reverse_drag_is_normalized_without_clamping_negative_coordinates()
    {
        var plan = new CapturePlanBuilder().Build(new(50, 50, -100, -100), Monitors);
        Assert.Equal(new(-50, -50, 100, 100), plan.SelectedVirtualBounds);
        Assert.Equal(2, plan.Segments.Count);
    }

    [Theory]
    [InlineData(0, 50)]
    [InlineData(50, 0)]
    public void Zero_sized_capture_is_rejected(int width, int height) =>
        Assert.Throws<ArgumentException>(() => new CapturePlanBuilder().Build(new(0, 0, width, height), Monitors));

    [Theory]
    [InlineData(0, 1, 4)]
    [InlineData(1, -1, 4)]
    [InlineData(2, 2, 15)]
    public void Invalid_pixel_buffer_is_rejected_before_native_capture_or_rendering(int width, int height, int bytes) =>
        Assert.ThrowsAny<ArgumentException>(() => new CapturedImage(width, height, new byte[bytes]));

    [Theory]
    [InlineData(MediaType.Video, "Png", ".mp4")]
    [InlineData(MediaType.Screenshot, "Jpg", ".jpg")]
    [InlineData(MediaType.Screenshot, "Png", ".png")]
    public void File_paths_use_local_month_and_never_choose_an_existing_filename(MediaType type, string format, string extension)
    {
        var time = new DateTimeOffset(2026, 10, 1, 0, 1, 0, TimeSpan.Zero);
        var calls = 0;
        var path = MediaPathGenerator.Create("C:\\captures", type, time, _ => calls++ < 2, format);
        Assert.Equal(time.ToLocalTime().ToString("yyyy-MM", System.Globalization.CultureInfo.InvariantCulture), Path.GetFileName(Path.GetDirectoryName(path)));
        Assert.EndsWith("-3" + extension, path);
    }
}
