using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX;
using Windows.Graphics.DirectX.Direct3D11;
using Windows.Graphics.Imaging;
using SnappySnap.Core;
using SharpDX.Direct3D;
using SharpDX.Direct3D11;
using SharpDX.DXGI;
using Direct3DDevice = Windows.Graphics.DirectX.Direct3D11.IDirect3DDevice;
using SharpDxDevice = SharpDX.Direct3D11.Device;
using WinRT;

namespace SnappySnap.Capture;

public sealed class GdiScreenshotCaptureService : IScreenshotCaptureService, IScreenshotCaptureDiagnostics
{
    private readonly IMonitorTopologyService _topology;

    public GdiScreenshotCaptureService(IMonitorTopologyService topology) => _topology = topology;

    public string BackendName => "GDI";

    public Task<CapturedImage> CaptureAsync(CapturePlan plan, CancellationToken cancellationToken)
    {
        plan.Validate();
        return Task.Run(() => Capture(plan, _topology.GetMonitors(), cancellationToken), cancellationToken);
    }

    private static CapturedImage Capture(CapturePlan plan, IReadOnlyList<MonitorDescriptor> monitors, CancellationToken cancellationToken)
    {
        using var output = new Bitmap(plan.OutputWidth, plan.OutputHeight, PixelFormat.Format32bppArgb);
        using (var graphics = Graphics.FromImage(output))
        {
            graphics.Clear(Color.Transparent);
            foreach (var segment in plan.Segments)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var monitor = monitors.FirstOrDefault(x => string.Equals(x.Id, segment.MonitorId, StringComparison.OrdinalIgnoreCase))
                    ?? throw new InvalidOperationException($"Monitor '{segment.MonitorId}' is no longer available.");
                graphics.CopyFromScreen(
                    segment.SourceRectOnMonitorPx.X + monitor.Bounds.X,
                    segment.SourceRectOnMonitorPx.Y + monitor.Bounds.Y,
                    segment.DestinationRectInOutputPx.X,
                    segment.DestinationRectInOutputPx.Y,
                    new Size(segment.SourceRectOnMonitorPx.Width, segment.SourceRectOnMonitorPx.Height),
                    CopyPixelOperation.SourceCopy);
            }
        }

        var rectangle = new Rectangle(0, 0, output.Width, output.Height);
        var data = output.LockBits(rectangle, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            var bytes = new byte[checked(output.Width * output.Height * 4)];
            for (var y = 0; y < output.Height; y++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                Marshal.Copy(data.Scan0 + y * data.Stride, bytes, y * output.Width * 4, output.Width * 4);
            }
            return new CapturedImage(output.Width, output.Height, bytes);
        }
        finally
        {
            output.UnlockBits(data);
        }
    }
}

public sealed class WindowsGraphicsCaptureScreenshotService : IScreenshotCaptureService, IScreenshotCaptureDiagnostics
{
    private readonly GdiScreenshotCaptureService _fallback;
    private readonly DesktopDuplicationScreenshotService _desktopDuplication;
    private readonly IAppLogger _logger;
    private readonly IMonitorTopologyService _topology;
    private string _backendName = "Unknown";

    public WindowsGraphicsCaptureScreenshotService(IAppLogger logger, IMonitorTopologyService topology)
    {
        _logger = logger;
        _topology = topology;
        _fallback = new GdiScreenshotCaptureService(topology);
        _desktopDuplication = new DesktopDuplicationScreenshotService(topology);
    }

    public string BackendName => _backendName;

    public async Task<CapturedImage> CaptureAsync(CapturePlan plan, CancellationToken cancellationToken)
    {
        plan.Validate();
        if (!GraphicsCaptureSession.IsSupported())
        {
            _backendName = "GDI";
            _logger.Warn("Windows Graphics Capture is not supported; using the GDI screenshot fallback.", new Dictionary<string, object?>
            {
                ["segments"] = plan.Segments.Count,
                ["widthPx"] = plan.OutputWidth,
                ["heightPx"] = plan.OutputHeight
            });
            return await _fallback.CaptureAsync(plan, cancellationToken).ConfigureAwait(false);
        }

        try
        {
            var monitors = _topology.GetMonitors();
            var output = new byte[checked(plan.OutputWidth * plan.OutputHeight * 4)];
            using var device = CreateD3DDevice();
            var direct3DDevice = CreateDirect3DDevice(device);
            foreach (var segment in plan.Segments)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var monitor = monitors.FirstOrDefault(x => string.Equals(x.Id, segment.MonitorId, StringComparison.OrdinalIgnoreCase))
                    ?? throw new InvalidOperationException($"Monitor '{segment.MonitorId}' is no longer available.");
                if (_topology is not Win32MonitorTopologyService win32Topology)
                {
                    throw new InvalidOperationException("Windows Graphics Capture requires the Win32 monitor topology implementation.");
                }

                var handle = win32Topology.GetMonitorHandle(monitor.Id);
                if (handle == 0)
                {
                    throw new InvalidOperationException($"Could not resolve the native handle for monitor '{monitor.Id}'.");
                }

                _logger.Info("Creating a Windows Graphics Capture monitor item.", new Dictionary<string, object?>
                {
                    ["monitorId"] = monitor.Id,
                    ["monitorHandle"] = handle.ToInt64(),
                    ["bounds"] = $"{monitor.Bounds.X},{monitor.Bounds.Y},{monitor.Bounds.Width},{monitor.Bounds.Height}"
                });
                var frame = await CaptureMonitorAsync(direct3DDevice, handle, cancellationToken).ConfigureAwait(false);
                CopySegment(frame, monitor, segment, output, plan.OutputWidth);
            }

            _logger.Info("Screenshot captured with Windows Graphics Capture.", new Dictionary<string, object?>
            {
                ["segments"] = plan.Segments.Count,
                ["widthPx"] = plan.OutputWidth,
                ["heightPx"] = plan.OutputHeight
            });
            _backendName = "Windows Graphics Capture";
            return new CapturedImage(plan.OutputWidth, plan.OutputHeight, output);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            try
            {
                var duplicated = await _desktopDuplication.CaptureAsync(plan, cancellationToken).ConfigureAwait(false);
                _logger.Warn("Windows Graphics Capture monitor interop failed; captured with D3D11 Desktop Duplication.", new Dictionary<string, object?>
                {
                    ["wgcErrorType"] = ex.GetType().FullName,
                    ["wgcError"] = ex.Message,
                    ["segments"] = plan.Segments.Count,
                    ["widthPx"] = plan.OutputWidth,
                    ["heightPx"] = plan.OutputHeight
                });
                _backendName = "D3D11 Desktop Duplication";
                return duplicated;
            }
            catch (Exception duplicationException) when (duplicationException is not OperationCanceledException)
            {
                _logger.Warn("Windows Graphics Capture and D3D11 Desktop Duplication failed; using the GDI screenshot fallback.", new Dictionary<string, object?>
                {
                    ["wgcErrorType"] = ex.GetType().FullName,
                    ["wgcError"] = ex.Message,
                    ["duplicationErrorType"] = duplicationException.GetType().FullName,
                    ["duplicationError"] = duplicationException.Message,
                    ["duplicationStack"] = duplicationException.ToString(),
                    ["segments"] = plan.Segments.Count
                });
                _backendName = "GDI";
                return await _fallback.CaptureAsync(plan, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private static SharpDxDevice CreateD3DDevice() => new(DriverType.Hardware, DeviceCreationFlags.BgraSupport);

    private static Direct3DDevice CreateDirect3DDevice(SharpDxDevice device)
    {
        using var dxgiDevice = device.QueryInterface<SharpDX.DXGI.Device>();
        var hresult = CreateDirect3D11DeviceFromDXGIDevice(dxgiDevice.NativePointer, out var devicePointer);
        Marshal.ThrowExceptionForHR(hresult);
        try
        {
            return MarshalInterface<Direct3DDevice>.FromAbi(devicePointer);
        }
        finally
        {
            Marshal.Release(devicePointer);
        }
    }

    private static async Task<FramePixels> CaptureMonitorAsync(Direct3DDevice device, nint monitor, CancellationToken cancellationToken)
    {
        var item = CreateItemForMonitor(monitor);
        using var framePool = Direct3D11CaptureFramePool.CreateFreeThreaded(
            device,
            DirectXPixelFormat.B8G8R8A8UIntNormalized,
            2,
            item.Size);
        using var session = framePool.CreateCaptureSession(item);
        session.IsCursorCaptureEnabled = false;
        var completion = new TaskCompletionSource<SoftwareBitmap>(TaskCreationOptions.RunContinuationsAsynchronously);
        Windows.Foundation.TypedEventHandler<Direct3D11CaptureFramePool, object>? handler = null;
        handler = (pool, _) =>
        {
            using var frame = pool.TryGetNextFrame();
            if (frame is null)
            {
                return;
            }

            _ = ConvertFrameAsync(frame.Surface, completion);
        };

        framePool.FrameArrived += handler;
        try
        {
            session.StartCapture();
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(5));
            var bitmap = await completion.Task.WaitAsync(timeout.Token).ConfigureAwait(false);
            return ReadPixels(bitmap);
        }
        finally
        {
            framePool.FrameArrived -= handler;
            if (!completion.Task.IsCompleted)
            {
                completion.TrySetCanceled(cancellationToken);
            }
        }
    }

    private static async Task ConvertFrameAsync(IDirect3DSurface surface, TaskCompletionSource<SoftwareBitmap> completion)
    {
        try
        {
            var bitmap = await SoftwareBitmap.CreateCopyFromSurfaceAsync(surface).AsTask().ConfigureAwait(false);
            completion.TrySetResult(bitmap);
        }
        catch (Exception ex)
        {
            completion.TrySetException(ex);
        }
    }

    private static FramePixels ReadPixels(SoftwareBitmap source)
    {
        using var bitmap = source.BitmapPixelFormat == BitmapPixelFormat.Bgra8
            ? source
            : SoftwareBitmap.Convert(source, BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied);
        using var buffer = bitmap.LockBuffer(BitmapBufferAccessMode.Read);
        using var reference = buffer.CreateReference();
        if (reference is not IMemoryBufferByteAccess access)
        {
            throw new InvalidOperationException("The WGC bitmap did not expose a readable byte buffer.");
        }

        access.GetBuffer(out var pointer, out _);
        var description = buffer.GetPlaneDescription(0);
        var bytes = new byte[checked(description.Width * description.Height * 4)];
        for (var y = 0; y < description.Height; y++)
        {
            Marshal.Copy(pointer + description.StartIndex + y * description.Stride, bytes, y * description.Width * 4, description.Width * 4);
        }

        return new FramePixels(description.Width, description.Height, bytes);
    }

    private static void CopySegment(FramePixels frame, MonitorDescriptor monitor, CaptureSegment segment, byte[] output, int outputWidth)
    {
        if (frame.Width < segment.SourceRectOnMonitorPx.Right || frame.Height < segment.SourceRectOnMonitorPx.Bottom)
        {
            throw new InvalidOperationException($"WGC frame {frame.Width}x{frame.Height} is smaller than monitor '{monitor.Id}' segment {segment.SourceRectOnMonitorPx.Width}x{segment.SourceRectOnMonitorPx.Height}.");
        }

        for (var y = 0; y < segment.SourceRectOnMonitorPx.Height; y++)
        {
            var sourceOffset = ((segment.SourceRectOnMonitorPx.Y + y) * frame.Width + segment.SourceRectOnMonitorPx.X) * 4;
            var destinationOffset = ((segment.DestinationRectInOutputPx.Y + y) * outputWidth + segment.DestinationRectInOutputPx.X) * 4;
            System.Buffer.BlockCopy(frame.Bytes, sourceOffset, output, destinationOffset, segment.SourceRectOnMonitorPx.Width * 4);
        }
    }

    private static GraphicsCaptureItem CreateItemForMonitor(nint monitor)
    {
        var iid = new Guid("79C3F95B-31F7-4EC2-A464-632EF5D30760");
        var interop = GraphicsCaptureItem.As<IGraphicsCaptureItemInterop>();
        var itemPointer = interop.CreateForMonitor(monitor, iid);
        if (itemPointer == 0)
        {
            throw new InvalidOperationException("Windows Graphics Capture returned no monitor item.");
        }

        try
        {
            return GraphicsCaptureItem.FromAbi(itemPointer);
        }
        finally
        {
            Marshal.Release(itemPointer);
        }
    }

    [DllImport("d3d11.dll", ExactSpelling = true)]
    private static extern int CreateDirect3D11DeviceFromDXGIDevice(nint dxgiDevice, out nint graphicsDevice);

    [ComImport]
    [Guid("3628E81B-3CAC-4C60-B7F4-23CE0E0C3356")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [ComVisible(true)]
    private interface IGraphicsCaptureItemInterop
    {
        nint CreateForMonitor(nint monitor, in Guid iid);
    }

    [ComImport]
    [Guid("5B0D3235-4DBA-4D44-865E-8F1D0E4FD04D")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMemoryBufferByteAccess
    {
        void GetBuffer(out nint buffer, out uint capacity);
    }

    private sealed record FramePixels(int Width, int Height, byte[] Bytes);
}
