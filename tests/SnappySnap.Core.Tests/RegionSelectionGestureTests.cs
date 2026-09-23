using SnappySnap.Core;
using Xunit;

namespace SnappySnap.Core.Tests;

public sealed class RegionSelectionGestureTests
{
    private static readonly SelectionInputSettings Settings = new(500, 8, 8, 4, 4);
    private static readonly MonitorDescriptor[] Monitors = [
        new("left", new(-2560, -300, 2560, 1440), new(-2560, -300, 2560, 1400), 144, 144, false),
        new("primary", new(0, 0, 3840, 2400), new(0, 0, 3840, 2320), 192, 192, true)];

    [Theory]
    [InlineData(-100, 50, 0)]
    [InlineData(800, 400, 1)]
    public void DoubleClickSelectsPhysicalMonitorUnderPointer(int x, int y, int monitor)
    {
        var gesture = new RegionSelectionGesture(); var point = new VirtualPixelPoint(x, y);
        gesture.Begin(point, 100, Settings);
        Assert.Equal(SelectionGestureKind.None, gesture.End(point, Monitors, true).Kind);
        gesture.Begin(point, 250, Settings);
        var result = gesture.End(point, Monitors, true);
        Assert.Equal(SelectionGestureKind.Monitor, result.Kind);
        Assert.Equal(Monitors[monitor].Bounds, result.Region);
        var plan = new CapturePlanBuilder().Build(result.Region, Monitors);
        Assert.Single(plan.Segments); Assert.Equal(Monitors[monitor].Id, plan.Segments[0].MonitorId);
        Assert.Equal(Monitors[monitor].Bounds.Width, plan.OutputWidth);
    }

    [Fact]
    public void DragAcrossMonitorsKeepsPhysicalCoordinates()
    {
        var gesture = new RegionSelectionGesture();
        gesture.Begin(new(-100, 100), 100, Settings); gesture.Move(new(200, 500));
        var result = gesture.End(new(200, 500), Monitors, true);
        Assert.Equal(SelectionGestureKind.Region, result.Kind);
        Assert.Equal(new VirtualPixelRect(-100, 100, 300, 400), result.Region);
        Assert.Equal(2, new CapturePlanBuilder().Build(result.Region, Monitors).Segments.Count);
    }

    [Fact]
    public void SecondPressDragWinsOverDoubleClick()
    {
        var gesture = new RegionSelectionGesture();
        gesture.Begin(new(100, 100), 100, Settings); gesture.End(new(100, 100), Monitors, true);
        gesture.Begin(new(100, 100), 200, Settings); gesture.Move(new(200, 200));
        Assert.Equal(SelectionGestureKind.Region, gesture.End(new(200, 200), Monitors, true).Kind);
    }

    [Fact]
    public void DragAwayAndBackInvalidatesClickSequence()
    {
        var gesture = new RegionSelectionGesture();
        gesture.Begin(new(100, 100), 100, Settings); gesture.Move(new(150, 100));
        Assert.Equal(SelectionGestureKind.None, gesture.End(new(100, 100), Monitors, true).Kind);
        gesture.Begin(new(100, 100), 200, Settings);
        Assert.Equal(SelectionGestureKind.None, gesture.End(new(100, 100), Monitors, true).Kind);
    }

    [Theory]
    [InlineData(701, 100, true)]
    [InlineData(200, 110, true)]
    [InlineData(200, 104, true)]
    [InlineData(200, 100, false)]
    public void TimingToleranceAndScreenshotOnlyScopeAreRespected(uint time, int x, bool enabled)
    {
        var gesture = new RegionSelectionGesture();
        gesture.Begin(new(100, 100), 100, Settings); gesture.End(new(100, 100), Monitors, enabled);
        gesture.Begin(new(x, 100), time, Settings);
        Assert.Equal(SelectionGestureKind.None, gesture.End(new(x, 100), Monitors, enabled).Kind);
    }

    [Fact]
    public void SmallJitterAndTickRolloverStillAllowDoubleClick()
    {
        var gesture = new RegionSelectionGesture();
        gesture.Begin(new(100, 100), uint.MaxValue - 100, Settings); gesture.End(new(101, 101), Monitors, true);
        gesture.Begin(new(102, 102), 50, Settings);
        Assert.Equal(SelectionGestureKind.Monitor, gesture.End(new(103, 103), Monitors, true).Kind);
    }
}
