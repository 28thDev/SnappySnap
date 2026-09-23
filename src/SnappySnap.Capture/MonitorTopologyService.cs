using System.Runtime.InteropServices;
using SnappySnap.Core;

namespace SnappySnap.Capture;

public sealed class Win32MonitorTopologyService : IMonitorTopologyService
{
    private const int MonitorDefaultToNull = 0;
    private const int MonitorDpiTypeEffective = 0;
    private IReadOnlyList<MonitorDescriptor>? _cached;

    public IReadOnlyList<MonitorDescriptor> GetMonitors()
    {
        if (_cached is not null)
        {
            return _cached;
        }

        var monitors = new List<MonitorDescriptor>();
        EnumDisplayMonitors(0, 0, (monitor, _, _, _) =>
        {
            var info = new MonitorInfo { CbSize = Marshal.SizeOf<MonitorInfo>() };
            if (!GetMonitorInfo(monitor, ref info))
            {
                return true;
            }

            var dpiX = 96u;
            var dpiY = 96u;
            try
            {
                _ = GetDpiForMonitor(monitor, MonitorDpiTypeEffective, out dpiX, out dpiY);
                dpiX = dpiX == 0 ? 96u : dpiX;
                dpiY = dpiY == 0 ? 96u : dpiY;
            }
            catch (DllNotFoundException)
            {
            }
            catch (EntryPointNotFoundException)
            {
            }

            var bounds = ToRect(info.Monitor);
            var workArea = ToRect(info.Work);
            var id = info.DeviceName.TrimEnd('\0');
            monitors.Add(new MonitorDescriptor(id, bounds, workArea, dpiX, dpiY, (info.Flags & 1) != 0));
            return true;
        }, 0);

        if (monitors.Count == 0)
        {
            throw new InvalidOperationException("Windows did not report any display monitors.");
        }

        _cached = monitors.OrderByDescending(x => x.IsPrimary).ThenBy(x => x.Bounds.X).ThenBy(x => x.Bounds.Y).ToArray();
        return _cached;
    }

    public VirtualPixelRect VirtualDesktopBounds
    {
        get
        {
            var monitors = GetMonitors();
            var left = monitors.Min(x => x.Bounds.Left);
            var top = monitors.Min(x => x.Bounds.Top);
            var right = monitors.Max(x => x.Bounds.Right);
            var bottom = monitors.Max(x => x.Bounds.Bottom);
            return new VirtualPixelRect(left, top, right - left, bottom - top);
        }
    }

    public void Invalidate() => _cached = null;

    public nint GetMonitorHandle(string monitorId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(monitorId);
        var descriptor = GetMonitors().FirstOrDefault(x => string.Equals(x.Id, monitorId, StringComparison.OrdinalIgnoreCase));
        if (descriptor is null)
        {
            return 0;
        }

        return MonitorFromPoint(new NativePoint { X = descriptor.Bounds.X + descriptor.Bounds.Width / 2, Y = descriptor.Bounds.Y + descriptor.Bounds.Height / 2 }, 2);
    }

    private static VirtualPixelRect ToRect(RectangleNative rect) => new(rect.Left, rect.Top, rect.Right - rect.Left, rect.Bottom - rect.Top);

    private delegate bool MonitorEnumProc(nint monitor, nint hdc, nint rect, nint data);

    [DllImport("user32.dll")]
    private static extern bool EnumDisplayMonitors(nint hdc, nint clipRect, MonitorEnumProc callback, nint data);

    [DllImport("user32.dll")]
    private static extern nint MonitorFromPoint(NativePoint point, uint flags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool GetMonitorInfo(nint monitor, ref MonitorInfo info);

    [DllImport("Shcore.dll")]
    private static extern int GetDpiForMonitor(nint monitor, int dpiType, out uint dpiX, out uint dpiY);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct MonitorInfo
    {
        public int CbSize;
        public RectangleNative Monitor;
        public RectangleNative Work;
        public uint Flags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string DeviceName;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RectangleNative
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X;
        public int Y;
    }
}

public sealed class CoordinateTransformService
{
    public static DipPoint PhysicalToDip(VirtualPixelPoint point, MonitorDescriptor monitor) => new(
        (point.X - monitor.Bounds.X) / monitor.ScaleX,
        (point.Y - monitor.Bounds.Y) / monitor.ScaleY);

    public static VirtualPixelPoint DipToPhysical(DipPoint point, MonitorDescriptor monitor) => new(
        monitor.Bounds.X + (int)Math.Round(point.X * monitor.ScaleX),
        monitor.Bounds.Y + (int)Math.Round(point.Y * monitor.ScaleY));

    public static DipRect PhysicalToDip(VirtualPixelRect rect, MonitorDescriptor monitor) => new(
        (rect.X - monitor.Bounds.X) / monitor.ScaleX,
        (rect.Y - monitor.Bounds.Y) / monitor.ScaleY,
        rect.Width / monitor.ScaleX,
        rect.Height / monitor.ScaleY);
}
