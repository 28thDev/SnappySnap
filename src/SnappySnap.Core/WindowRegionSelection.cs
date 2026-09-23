namespace SnappySnap.Core;

public readonly record struct BrowserChromeBounds(VirtualPixelRect Toolbar, VirtualPixelRect Address, VirtualPixelRect Content)
{
    public bool IsValidFor(VirtualPixelRect window) =>
        Inside(window, Toolbar) && Inside(window, Address) && Inside(window, Content)
        && Toolbar.Width >= window.Width * .7
        && Toolbar.Y < Address.Y && Toolbar.Y > window.Y
        && Toolbar.Y >= Address.Y - Address.Height
        && Toolbar.Height <= Address.Height * 2.5
        && Toolbar.Y + Toolbar.Height >= Address.Y + Address.Height
        && Toolbar.Y + Toolbar.Height <= Content.Y
        && Address.Width >= window.Width * .2 && Address.Height >= 12
        && Content.Width >= window.Width * .4 && Content.Height >= window.Height * .25
        && Address.Y < Content.Y && Address.Y + Address.Height <= Content.Y;

    private static bool Inside(VirtualPixelRect outer, VirtualPixelRect inner) =>
        inner.Width > 0 && inner.Height > 0 && inner.X >= outer.X && inner.Y >= outer.Y
        && inner.X + inner.Width <= outer.X + outer.Width && inner.Y + inner.Height <= outer.Y + outer.Height;
}

public readonly record struct WindowRegionCandidate(long Id, VirtualPixelRect Bounds, bool IsBrowser,
    BrowserChromeBounds? Chrome = null, bool IsPending = false);

public enum HoverRegionKind { Window, BrowserFull, BrowserWithAddress, BrowserContent, BrowserUnknown, BrowserPending }
public readonly record struct HoverRegionResult(long Id, VirtualPixelRect Bounds, HoverRegionKind Kind)
{
    public bool CanSelect => Kind != HoverRegionKind.BrowserPending;
}

public static class WindowRegionSelection
{
    // Candidates are supplied in native Z order, so the first hit is the visible top window.
    public static HoverRegionResult? Resolve(VirtualPixelPoint point, IReadOnlyList<WindowRegionCandidate> candidates)
    {
        foreach (var candidate in candidates)
        {
            var window = candidate.Bounds;
            if (!window.Contains(point)) continue;
            if (!candidate.IsBrowser) return new(candidate.Id, window, HoverRegionKind.Window);
            if (candidate.IsPending) return new(candidate.Id, window, HoverRegionKind.BrowserPending);
            if (candidate.Chrome is not { } chrome || !chrome.IsValidFor(window))
                return new(candidate.Id, window, HoverRegionKind.BrowserUnknown);
            if (chrome.Content.Contains(point)) return new(candidate.Id, chrome.Content, HoverRegionKind.BrowserContent);
            if (point.Y >= chrome.Toolbar.Y && point.Y < chrome.Content.Y)
                return new(candidate.Id, new VirtualPixelRect(window.X, chrome.Toolbar.Y,
                    window.Width, window.Y + window.Height - chrome.Toolbar.Y), HoverRegionKind.BrowserWithAddress);
            return new(candidate.Id, window, HoverRegionKind.BrowserFull);
        }
        return null;
    }
}
