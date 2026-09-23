using SnappySnap.Core;
using Xunit;

namespace SnappySnap.Core.Tests;

public sealed class WindowRegionSelectionTests
{
    [Fact]
    public void Topmost_window_wins_and_empty_desktop_has_no_candidate()
    {
        WindowRegionCandidate[] windows = [
            new(1, new(-300, -200, 300, 250), false),
            new(2, new(-500, -300, 600, 500), false)];
        Assert.Equal(1, WindowRegionSelection.Resolve(new(-100, -100), windows)?.Id);
        Assert.Null(WindowRegionSelection.Resolve(new(500, 500), windows));
    }

    [Fact]
    public void Browser_hover_selects_window_address_or_page_by_pointer_zone()
    {
        var window = new VirtualPixelRect(-1200, -100, 1000, 800);
        var chrome = new BrowserChromeBounds(new(-1100, -40, 650, 40), new(-1200, 80, 1000, 620));
        WindowRegionCandidate[] candidates = [new(3, window, true, chrome)];
        Assert.True(chrome.IsValidFor(window));
        Assert.Equal(new HoverRegionResult(3, window, HoverRegionKind.BrowserFull),
            WindowRegionSelection.Resolve(new(-800, -80), candidates));
        Assert.Equal(new HoverRegionResult(3, new(-1200, -40, 1000, 740), HoverRegionKind.BrowserWithAddress),
            WindowRegionSelection.Resolve(new(-800, -20), candidates));
        Assert.Equal(new HoverRegionResult(3, chrome.Content, HoverRegionKind.BrowserContent),
            WindowRegionSelection.Resolve(new(-800, 400), candidates));
    }

    [Fact]
    public void Missing_or_invalid_browser_geometry_never_pretends_to_hide_tabs()
    {
        var window = new VirtualPixelRect(100, 100, 900, 700);
        var invalid = new BrowserChromeBounds(new(150, 190, 500, 40), new(100, 170, 900, 620));
        Assert.Equal(HoverRegionKind.BrowserUnknown,
            WindowRegionSelection.Resolve(new(500, 500), [new(1, window, true, invalid)])?.Kind);
        Assert.Equal(HoverRegionKind.BrowserPending,
            WindowRegionSelection.Resolve(new(500, 500), [new(1, window, true, null, true)])?.Kind);
        Assert.False(WindowRegionSelection.Resolve(new(500, 500), [new(1, window, true, null, true)])!.Value.CanSelect);
    }
}
