using SnappySnap.Core;
using Xunit;

namespace SnappySnap.Core.Tests;

public sealed class ShelfPlacementTests
{
    [Theory]
    [InlineData(96, -1920, 1920, 700, 510)]
    [InlineData(144, -2880, 2880, 1050, 765)]
    [InlineData(192, -3840, 3840, 1400, 1020)]
    public void Restores_dip_size_on_negative_monitor_with_current_dpi(uint dpi, int left, int width, int expectedWidth, int expectedHeight)
    {
        var monitor = new MonitorDescriptor("left", new(left, 0, width, 2160), new(left, 0, width, 2100), dpi, dpi, false);
        var placement = new ShelfPlacement("left", new DipRect(30, 40, 700, 510));
        var bounds = placement.Restore(monitor, true);
        Assert.Equal(left + (int)(30 * monitor.ScaleX), bounds.X);
        Assert.Equal((int)(40 * monitor.ScaleY), bounds.Y);
        Assert.Equal(expectedWidth, bounds.Width); Assert.Equal(expectedHeight, bounds.Height);
    }

    [Fact]
    public void Missing_monitor_placement_fits_smaller_replacement_work_area()
    {
        var replacement = new MonitorDescriptor("primary", new(0, 0, 800, 600), new(0, 20, 800, 550), 192, 192, true);
        var placement = new ShelfPlacement("disconnected", new DipRect(800, -50, 1160, 900));
        Assert.Equal(replacement.WorkArea, placement.Restore(replacement, false));
    }

    [Fact]
    public void Tiny_saved_window_uses_the_modes_minimum_size()
    {
        var monitor = new MonitorDescriptor("main", new(0, 0, 1920, 1080), new(0, 0, 1920, 1040), 96, 96, true);
        var placement = new ShelfPlacement("main", new DipRect(5000, 5000, 1, 1));
        Assert.Equal(new VirtualPixelRect(1360, 620, 560, 420), placement.Restore(monitor, true));
        Assert.Equal(new VirtualPixelRect(1220, 560, 700, 480), placement.Restore(monitor, false));
    }
}
