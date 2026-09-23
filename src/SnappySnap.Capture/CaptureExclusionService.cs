using System.ComponentModel;
using System.Runtime.InteropServices;
using SnappySnap.Core;

namespace SnappySnap.Capture;

public sealed class WindowCaptureExclusionService : ICaptureExclusionService
{
    private const uint WdaNone = 0;
    private const uint WdaExcludeFromCapture = 0x11;
    private readonly IAppLogger _logger;

    public WindowCaptureExclusionService(IAppLogger logger) => _logger = logger;

    public bool TryExcludeWindow(nint hwnd) => TrySet(hwnd, WdaExcludeFromCapture);
    public bool TryRestoreWindow(nint hwnd) => TrySet(hwnd, WdaNone);

    private bool TrySet(nint hwnd, uint affinity)
    {
        if (hwnd == 0)
        {
            return false;
        }

        var success = SetWindowDisplayAffinity(hwnd, affinity);
        if (!success)
        {
            _logger.Warn("Could not set capture exclusion for a SnappySnap window.", new Dictionary<string, object?>
            {
                ["hwnd"] = hwnd.ToInt64(),
                ["affinity"] = affinity,
                ["win32Error"] = Marshal.GetLastWin32Error()
            });
        }
        return success;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowDisplayAffinity(nint hwnd, uint affinity);
}
