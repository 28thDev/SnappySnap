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

    [Fact]
    public void Video_plan_trims_odd_edges_without_moving_the_selected_origin()
    {
        var monitors = new[]
        {
            new MonitorDescriptor("left", new(-1280, 0, 1280, 1024), new(-1280, 0, 1280, 984), 120, 120, false),
            new MonitorDescriptor("primary", new(0, 0, 1920, 1080), new(0, 0, 1920, 1040), 96, 96, true)
        };

        var plan = new CapturePlanBuilder().BuildForVideo(new(-101, 100, 301, 203), monitors);

        Assert.Equal(new(-101, 100, 300, 202), plan.SelectedVirtualBounds);
        Assert.Equal(300, plan.OutputWidth);
        Assert.Equal(202, plan.OutputHeight);
        Assert.Equal(2, plan.Segments.Count);
        Assert.Equal(new(0, 0, 101, 202), plan.Segments[0].DestinationRectInOutputPx);
        Assert.Equal(new(101, 0, 199, 202), plan.Segments[1].DestinationRectInOutputPx);
        Assert.Equal(new(-101, 100, 301, 203), new CapturePlanBuilder().Build(new(-101, 100, 301, 203), monitors).SelectedVirtualBounds);
    }

    [Fact]
    public void Video_plan_rejects_a_region_too_small_for_even_output()
    {
        var monitor = new MonitorDescriptor("primary", new(0, 0, 100, 100), new(0, 0, 100, 100), 96, 96, true);

        Assert.Throws<ArgumentException>(() => new CapturePlanBuilder().BuildForVideo(new(20, 20, 1, 10), [monitor]));
    }
}
