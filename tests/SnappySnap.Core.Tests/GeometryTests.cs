using SnappySnap.Core;
using Xunit;

namespace SnappySnap.Core.Tests;

public sealed class GeometryTests
{
    [Fact]
    public void Builds_single_monitor_segment_with_output_origin_zero()
    {
        var monitor = new MonitorDescriptor("primary", new VirtualPixelRect(0, 0, 1920, 1200), new VirtualPixelRect(0, 0, 1920, 1152), 96, 96, true);

        var plan = new CapturePlanBuilder().Build(new VirtualPixelRect(100, 200, 400, 300), [monitor]);

        var segment = Assert.Single(plan.Segments);
        Assert.Equal(new VirtualPixelRect(100, 200, 400, 300), segment.SourceRectOnMonitorPx);
        Assert.Equal(new VirtualPixelRect(0, 0, 400, 300), segment.DestinationRectInOutputPx);
    }

    [Fact]
    public void Splits_region_across_negative_left_monitor()
    {
        var monitors = new[]
        {
            new MonitorDescriptor("left", new VirtualPixelRect(-1280, 0, 1280, 1024), new VirtualPixelRect(-1280, 0, 1280, 984), 120, 120, false),
            new MonitorDescriptor("primary", new VirtualPixelRect(0, 0, 1920, 1200), new VirtualPixelRect(0, 0, 1920, 1152), 96, 96, true)
        };

        var plan = new CapturePlanBuilder().Build(new VirtualPixelRect(-100, 100, 300, 200), monitors);

        Assert.Equal(2, plan.Segments.Count);
        Assert.Equal(new VirtualPixelRect(1180, 100, 100, 200), plan.Segments[0].SourceRectOnMonitorPx);
        Assert.Equal(new VirtualPixelRect(0, 0, 100, 200), plan.Segments[0].DestinationRectInOutputPx);
        Assert.Equal(new VirtualPixelRect(0, 100, 200, 200), plan.Segments[1].SourceRectOnMonitorPx);
        Assert.Equal(new VirtualPixelRect(100, 0, 200, 200), plan.Segments[1].DestinationRectInOutputPx);
    }

    [Fact]
    public void Splits_region_across_three_monitors_without_gaps()
    {
        var monitors = new[]
        {
            new MonitorDescriptor("a", new VirtualPixelRect(0, 0, 500, 500), new VirtualPixelRect(0, 0, 500, 500), 96, 96, true),
            new MonitorDescriptor("b", new VirtualPixelRect(500, 0, 500, 500), new VirtualPixelRect(500, 0, 500, 500), 144, 144, false),
            new MonitorDescriptor("c", new VirtualPixelRect(1000, 0, 500, 500), new VirtualPixelRect(1000, 0, 500, 500), 96, 96, false)
        };

        var plan = new CapturePlanBuilder().Build(new VirtualPixelRect(250, 100, 1000, 200), monitors);

        Assert.Equal(3, plan.Segments.Count);
        Assert.Equal(1000, plan.Segments.Sum(s => s.DestinationRectInOutputPx.Width));
        Assert.Equal(200, plan.Segments.Max(s => s.DestinationRectInOutputPx.Height));
        Assert.All(plan.Segments, s => Assert.Equal(s.SourceRectOnMonitorPx.Width, s.DestinationRectInOutputPx.Width));
    }

    [Fact]
    public void Rejects_region_that_does_not_intersect_a_monitor()
    {
        var monitor = new MonitorDescriptor("primary", new VirtualPixelRect(0, 0, 100, 100), new VirtualPixelRect(0, 0, 100, 100), 96, 96, true);

        Assert.Throws<ArgumentException>(() => new CapturePlanBuilder().Build(new VirtualPixelRect(200, 200, 20, 20), [monitor]));
    }
}
