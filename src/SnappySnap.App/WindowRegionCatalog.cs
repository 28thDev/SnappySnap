using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Automation;
using SnappySnap.Core;

namespace SnappySnap.App;

// One native window snapshot per selector. UI Automation is used only for a hovered browser.
internal sealed class WindowRegionCatalog
{
    private const int DwmExtendedFrameBounds = 9;
    private const int DwmCloaked = 14;
    private readonly IReadOnlyList<WindowInfo> _windows;
    private readonly Dictionary<long, Task<BrowserChromeBounds?>> _browserProbes = new();
    private readonly HashSet<long> _reportedProbeFailures = new();
    private readonly object _probeLock = new();
    private readonly IAppLogger _logger;
    private readonly VirtualPixelRect _desktop;

    private WindowRegionCatalog(IReadOnlyList<WindowInfo> windows, VirtualPixelRect desktop, IAppLogger logger) => (_windows, _desktop, _logger) = (windows, desktop, logger);

    public static WindowRegionCatalog Enumerate(IReadOnlyCollection<nint> selectorHandles, VirtualPixelRect desktop, IAppLogger logger)
    {
        var ignored = selectorHandles.ToHashSet();
        var windows = new List<WindowInfo>();
        _ = EnumWindows((handle, parameter) =>
        {
            if (ignored.Contains(handle) || !IsWindowVisible(handle) || IsIconic(handle)) return true;
            if (DwmGetWindowAttribute(handle, DwmCloaked, out int cloaked, sizeof(int)) == 0 && cloaked != 0) return true;
            if (DwmGetWindowAttribute(handle, DwmExtendedFrameBounds, out NativeRect frame, Marshal.SizeOf<NativeRect>()) != 0) return true;
            var width = frame.Right - frame.Left;
            var height = frame.Bottom - frame.Top;
            if (width < 4 || height < 4) return true;
            var bounds = new VirtualPixelRect(frame.Left, frame.Top, width, height);
            if (!bounds.TryIntersect(desktop, out _)) return true;
            var browser = false;
            _ = GetWindowThreadProcessId(handle, out var processId);
            try
            {
                using var process = Process.GetProcessById((int)processId);
                var name = process.ProcessName;
                browser = name.Equals("chrome", StringComparison.OrdinalIgnoreCase)
                    || name.Equals("msedge", StringComparison.OrdinalIgnoreCase)
                    || name.Equals("firefox", StringComparison.OrdinalIgnoreCase);
            }
            catch (ArgumentException) { }
            catch (System.ComponentModel.Win32Exception) { }
            catch (InvalidOperationException) { }
            windows.Add(new WindowInfo(handle, bounds, browser));
            return true;
        }, 0);
        return new WindowRegionCatalog(windows, desktop, logger);
    }

    public HoverRegionResult? Resolve(VirtualPixelPoint point, Action refresh)
    {
        var window = _windows.FirstOrDefault(candidate => candidate.Bounds.Contains(point));
        if (window is null) return null;
        BrowserChromeBounds? chrome = null;
        var pending = false;
        if (window.IsBrowser)
        {
            var id = (long)window.Handle;
            if (!_browserProbes.TryGetValue(id, out var probe))
            {
                probe = Task.Run(() =>
                    {
                        lock (_probeLock) return ProbeBrowser(window.Handle, window.Bounds);
                    })
                    .WaitAsync(TimeSpan.FromMilliseconds(900));
                _browserProbes.Add(id, probe);
                _ = probe.ContinueWith(_ => refresh(), CancellationToken.None,
                    TaskContinuationOptions.None, TaskScheduler.FromCurrentSynchronizationContext());
            }
            pending = !probe.IsCompleted;
            if (probe.IsCompletedSuccessfully) chrome = probe.Result;
            else if (probe.IsFaulted) ReportProbeFailure(id, probe.Exception?.GetBaseException());
        }
        return WindowRegionSelection.Resolve(point, new[]
        {
            new WindowRegionCandidate((long)window.Handle, window.Bounds, window.IsBrowser, chrome, pending)
        }, _desktop);
    }

    public async Task<HoverRegionResult?> ResolveReadyAsync(VirtualPixelPoint point, Action refresh)
    {
        var result = Resolve(point, refresh);
        if (result?.Kind != HoverRegionKind.BrowserPending) return result;
        try { await _browserProbes[result.Value.Id]; }
        catch (Exception ex) { ReportProbeFailure(result.Value.Id, ex); }
        return Resolve(point, refresh);
    }

    private void ReportProbeFailure(long id, Exception? error)
    {
        if (_reportedProbeFailures.Add(id))
            _logger.Warn("Browser chrome detection failed; full window will be offered.",
                new Dictionary<string, object?> { ["error"] = error?.GetType().Name });
    }

    private static BrowserChromeBounds? ProbeBrowser(nint handle, VirtualPixelRect window)
    {
        var root = AutomationElement.FromHandle(handle);
        var documentCondition = new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Document);
        var documents = root.FindAll(TreeScope.Descendants, documentCondition);
        VirtualPixelRect? content = null;
        for (var i = 0; i < documents.Count; i++)
        {
            var bounds = PhysicalBounds(documents[i].Current.BoundingRectangle);
            if (bounds is null || bounds.Value.Y <= window.Y || bounds.Value.Width < window.Width * .4) continue;
            if (content is null || (long)bounds.Value.Width * bounds.Value.Height > (long)content.Value.Width * content.Value.Height)
                content = bounds;
        }
        if (content is null) return null;
        var editCondition = new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Edit);
        var edits = root.FindAll(TreeScope.Descendants, editCondition);
        VirtualPixelRect? address = null;
        AutomationElement? addressElement = null;
        for (var i = 0; i < edits.Count; i++)
        {
            var bounds = PhysicalBounds(edits[i].Current.BoundingRectangle);
            if (bounds is null || bounds.Value.Y >= content.Value.Y || bounds.Value.Width < window.Width * .2) continue;
            if (address is null || bounds.Value.Y < address.Value.Y)
            {
                address = bounds;
                addressElement = edits[i];
            }
        }
        if (address is null || addressElement is null) return null;
        VirtualPixelRect? toolbar = null;
        var walker = TreeWalker.ControlViewWalker;
        for (var ancestor = walker.GetParent(addressElement); ancestor is not null && ancestor != root; ancestor = walker.GetParent(ancestor))
        {
            var bounds = PhysicalBounds(ancestor.Current.BoundingRectangle);
            if (bounds is not { } candidate || candidate.Y <= window.Y || candidate.Y >= address.Value.Y
                || candidate.Y < address.Value.Y - address.Value.Height || candidate.Height > address.Value.Height * 2.5
                || candidate.Width < window.Width * .7 || candidate.Y + candidate.Height > content.Value.Y
                || candidate.X > address.Value.X || candidate.X + candidate.Width < address.Value.X + address.Value.Width
                || candidate.Y + candidate.Height < address.Value.Y + address.Value.Height) continue;
            toolbar = candidate;
            break;
        }
        if (toolbar is null) return null;
        var result = new BrowserChromeBounds(toolbar.Value, address.Value, content.Value);
        return result.IsValidFor(window) ? result : null;
    }

    private static VirtualPixelRect? PhysicalBounds(System.Windows.Rect bounds)
    {
        if (bounds.IsEmpty || !double.IsFinite(bounds.X) || !double.IsFinite(bounds.Y)
            || !double.IsFinite(bounds.Width) || !double.IsFinite(bounds.Height)) return null;
        var left = (int)Math.Floor(bounds.Left);
        var top = (int)Math.Floor(bounds.Top);
        var right = (int)Math.Ceiling(bounds.Right);
        var bottom = (int)Math.Ceiling(bounds.Bottom);
        return right > left && bottom > top ? new(left, top, right - left, bottom - top) : null;
    }

    private sealed record WindowInfo(nint Handle, VirtualPixelRect Bounds, bool IsBrowser);
    [StructLayout(LayoutKind.Sequential)] private struct NativeRect { public int Left, Top, Right, Bottom; }
    private delegate bool EnumWindowsCallback(nint handle, nint parameter);
    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindowsCallback callback, nint parameter);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(nint handle);
    [DllImport("user32.dll")] private static extern bool IsIconic(nint handle);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(nint handle, out uint processId);
    [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(nint handle, int attribute, out NativeRect value, int size);
    [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(nint handle, int attribute, out int value, int size);
}
