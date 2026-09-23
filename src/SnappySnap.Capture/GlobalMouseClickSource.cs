using System.Runtime.InteropServices;
using System.ComponentModel;
using SnappySnap.Core;

namespace SnappySnap.Capture;

public sealed class GlobalMouseClickSource : IMouseClickSource
{
    private const int WhMouseLl = 14;
    private const int WmLButtonDown = 0x0201;
    private const int WmRButtonDown = 0x0204;
    private readonly LowLevelMouseProc _callback;
    private nint _hook;
    private bool _started;

    public GlobalMouseClickSource() => _callback = OnMouse;

    public event EventHandler<MouseClickEvent>? Clicked;

    public void Start()
    {
        if (_started)
        {
            return;
        }

        using var process = System.Diagnostics.Process.GetCurrentProcess();
        using var module = process.MainModule;
        _hook = SetWindowsHookEx(WhMouseLl, _callback, GetModuleHandle(module?.ModuleName), 0);
        if (_hook == 0)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not install the mouse click hook.");
        }
        _started = true;
    }

    public void Stop()
    {
        if (!_started)
        {
            return;
        }

        _ = UnhookWindowsHookEx(_hook);
        _hook = 0;
        _started = false;
    }

    private nint OnMouse(int code, nint wParam, nint lParam)
    {
        if (code >= 0 && (wParam == WmLButtonDown || wParam == WmRButtonDown))
        {
            var data = Marshal.PtrToStructure<MouseHookStruct>(lParam);
            var button = wParam == WmLButtonDown ? MouseButton.Left : MouseButton.Right;
            Clicked?.Invoke(this, new MouseClickEvent(DateTimeOffset.UtcNow, new VirtualPixelPoint(data.Point.X, data.Point.Y), button));
        }

        return CallNextHookEx(_hook, code, wParam, lParam);
    }

    public void Dispose() => Stop();

    private delegate nint LowLevelMouseProc(int code, nint wParam, nint lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MouseHookStruct
    {
        public NativePoint Point;
        public uint MouseData;
        public uint Flags;
        public uint Time;
        public nint ExtraInfo;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern nint SetWindowsHookEx(int idHook, LowLevelMouseProc callback, nint moduleHandle, uint threadId);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(nint hook);

    [DllImport("user32.dll")]
    private static extern nint CallNextHookEx(nint hook, int code, nint wParam, nint lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern nint GetModuleHandle(string? moduleName);
}
