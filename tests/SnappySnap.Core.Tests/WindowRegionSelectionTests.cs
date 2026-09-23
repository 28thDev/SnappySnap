using SnappySnap.Core;
using Xunit;

namespace SnappySnap.Core.Tests;

public sealed class WindowRegionSelectionTests
{
    [Theory]
    [InlineData(0, 0)]
    [InlineData(-1920, -240)]
    public void Offscreen_window_is_clipped_before_frozen_crop(int x, int y)
    {
        var desktop = new VirtualPixelRect(x, y, 3840, 2400);
        var window = new VirtualPixelRect(x - 13, y - 13, 3866, 2330);
        foreach (var browser in new[] { false, true })
        {
            var result = WindowRegionSelection.Resolve(new(x + 20, y + 20), [new(1, window, browser)], desktop)!.Value;
            Assert.Equal(new VirtualPixelRect(x, y, 3840, 2317), result.Bounds);
            var snapshot = new FrozenDesktopSnapshot(desktop, new CapturedImage(3840, 2400, new byte[3840 * 2400 * 4]));
            Assert.Equal(2317, snapshot.Crop(result.Bounds, default).Height);
        }
        Assert.Null(WindowRegionSelection.Resolve(new(x - 1, y), [new(1, window, false)], desktop));
    }

    [Theory]
    [InlineData(20, HoverRegionKind.BrowserFull, 0)]
    [InlineData(70, HoverRegionKind.BrowserWithAddress, 40)]
    [InlineData(200, HoverRegionKind.BrowserContent, 100)]
    public void Browser_zones_keep_native_geometry_but_return_only_visible_pixels(int y, HoverRegionKind kind, int top)
    {
        var window = new VirtualPixelRect(-13, -13, 1026, 826);
        var chrome = new BrowserChromeBounds(new(-13, 40, 1026, 50), new(100, 50, 700, 35), new(-13, 100, 1026, 713));
        var result = WindowRegionSelection.Resolve(new(500, y), [new(1, window, true, chrome)], new(0, 0, 1000, 800))!.Value;
        Assert.Equal(kind, result.Kind);
        Assert.Equal(new VirtualPixelRect(0, top, 1000, 800 - top), result.Bounds);
    }

    [Fact]
    public void Topmost_window_wins_and_empty_desktop_has_no_candidate()
    {
        WindowRegionCandidate[] windows = [
            new(1, new(-300, -200, 300, 250), false),
            new(2, new(-500, -300, 600, 500), false)];
        Assert.Equal(1, WindowRegionSelection.Resolve(new(-100, -100), windows, new(-2000, -1000, 4000, 3000))?.Id);
        Assert.Null(WindowRegionSelection.Resolve(new(500, 500), windows, new(-2000, -1000, 4000, 3000)));
    }

    [Fact]
    public void Browser_hover_selects_window_address_or_page_by_pointer_zone()
    {
        var window = new VirtualPixelRect(-1200, -100, 1000, 800);
        var chrome = new BrowserChromeBounds(new(-1200, -60, 1000, 60), new(-1100, -40, 650, 40), new(-1200, 80, 1000, 620));
        WindowRegionCandidate[] candidates = [new(3, window, true, chrome)];
        Assert.True(chrome.IsValidFor(window));
        Assert.Equal(new HoverRegionResult(3, window, HoverRegionKind.BrowserFull),
            WindowRegionSelection.Resolve(new(-800, -80), candidates, new(-2000, -1000, 4000, 3000)));
        Assert.Equal(new HoverRegionResult(3, new(-1200, -60, 1000, 760), HoverRegionKind.BrowserWithAddress),
            WindowRegionSelection.Resolve(new(-800, -20), candidates, new(-2000, -1000, 4000, 3000)));
        Assert.Equal(HoverRegionKind.BrowserWithAddress, WindowRegionSelection.Resolve(new(-800, -50), candidates, new(-2000, -1000, 4000, 3000))?.Kind);
        Assert.Equal(new HoverRegionResult(3, chrome.Content, HoverRegionKind.BrowserContent),
            WindowRegionSelection.Resolve(new(-800, 400), candidates, new(-2000, -1000, 4000, 3000)));
    }

    [Fact]
    public void Missing_or_invalid_browser_geometry_never_pretends_to_hide_tabs()
    {
        var window = new VirtualPixelRect(100, 100, 900, 700);
        var invalid = new BrowserChromeBounds(new(100, 165, 900, 45), new(150, 190, 500, 40), new(100, 170, 900, 620));
        Assert.Equal(HoverRegionKind.BrowserUnknown,
            WindowRegionSelection.Resolve(new(500, 500), [new(1, window, true, invalid)], new(-2000, -1000, 4000, 3000))?.Kind);
        var tabStripAncestor = new BrowserChromeBounds(new(100, 101, 900, 95), new(150, 160, 500, 25), new(100, 220, 900, 580));
        Assert.False(tabStripAncestor.IsValidFor(window));
        Assert.Equal(HoverRegionKind.BrowserPending,
            WindowRegionSelection.Resolve(new(500, 500), [new(1, window, true, null, true)], new(-2000, -1000, 4000, 3000))?.Kind);
        Assert.False(WindowRegionSelection.Resolve(new(500, 500), [new(1, window, true, null, true)], new(-2000, -1000, 4000, 3000))!.Value.CanSelect);
    }
}
