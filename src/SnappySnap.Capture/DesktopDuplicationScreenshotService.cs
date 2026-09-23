using System.IO;
using System.Runtime.InteropServices;
using SharpDX;
using SharpDX.Direct3D;
using SharpDX.Direct3D11;
using SharpDX.DXGI;
using SnappySnap.Core;
using SharpDxDevice = SharpDX.Direct3D11.Device;

namespace SnappySnap.Capture;

/// <summary>
/// D3D11 Desktop Duplication fallback for systems where the WinRT monitor
/// interop rejects CreateForMonitor. It still captures physical monitor pixels
/// on the GPU and keeps the public screenshot contract independent of either
/// native implementation.
/// </summary>
internal sealed class DesktopDuplicationScreenshotService
{
    private readonly IMonitorTopologyService _topology;

    public DesktopDuplicationScreenshotService(IMonitorTopologyService topology) => _topology = topology;

    public Task<CapturedImage> CaptureAsync(CapturePlan plan, CancellationToken cancellationToken)
    {
        plan.Validate();
        return Task.Run(() => Capture(plan, cancellationToken), cancellationToken);
    }

    private CapturedImage Capture(CapturePlan plan, CancellationToken cancellationToken)
    {
        var monitors = _topology.GetMonitors();
        var output = new byte[checked(plan.OutputWidth * plan.OutputHeight * 4)];
        foreach (var segment in plan.Segments)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var monitor = monitors.FirstOrDefault(x => string.Equals(x.Id, segment.MonitorId, StringComparison.OrdinalIgnoreCase))
                ?? throw new InvalidOperationException($"Monitor '{segment.MonitorId}' is no longer available.");
            var frame = CaptureMonitor(monitor.Id, cancellationToken);
            if (frame.Width < segment.SourceRectOnMonitorPx.Right || frame.Height < segment.SourceRectOnMonitorPx.Bottom)
            {
                throw new InvalidOperationException($"Desktop Duplication frame {frame.Width}x{frame.Height} is smaller than monitor '{monitor.Id}' segment.");
            }

            for (var y = 0; y < segment.SourceRectOnMonitorPx.Height; y++)
            {
                var sourceOffset = ((segment.SourceRectOnMonitorPx.Y + y) * frame.Width + segment.SourceRectOnMonitorPx.X) * 4;
                var destinationOffset = ((segment.DestinationRectInOutputPx.Y + y) * plan.OutputWidth + segment.DestinationRectInOutputPx.X) * 4;
                System.Buffer.BlockCopy(frame.Bytes, sourceOffset, output, destinationOffset, segment.SourceRectOnMonitorPx.Width * 4);
            }
        }

        return new CapturedImage(plan.OutputWidth, plan.OutputHeight, output);
    }

    private static DesktopFrame CaptureMonitor(string monitorId, CancellationToken cancellationToken)
    {
        using var factory = new Factory1();
        using var adapter = factory.Adapters
            .Select(adapter => adapter)
            .FirstOrDefault(candidate => candidate.Outputs.Any(output => string.Equals(output.Description.DeviceName, monitorId, StringComparison.OrdinalIgnoreCase)))
            ?? throw new InvalidOperationException($"No DXGI adapter exposes monitor '{monitorId}'.");
        using var output = adapter.Outputs.First(candidate => string.Equals(candidate.Description.DeviceName, monitorId, StringComparison.OrdinalIgnoreCase));
        using var output1 = output.QueryInterface<Output1>();
        using var device = new SharpDxDevice(adapter, DeviceCreationFlags.BgraSupport);
        using var duplication = output1.DuplicateOutput(device);
        var bounds = output.Description.DesktopBounds;
        var width = bounds.Right - bounds.Left;
        var height = bounds.Bottom - bounds.Top;
        using var staging = new Texture2D(device, new Texture2DDescription
        {
            Width = width,
            Height = height,
            MipLevels = 1,
            ArraySize = 1,
            Format = Format.B8G8R8A8_UNorm,
            SampleDescription = new SampleDescription(1, 0),
            Usage = ResourceUsage.Staging,
            BindFlags = BindFlags.None,
            CpuAccessFlags = CpuAccessFlags.Read,
            OptionFlags = ResourceOptionFlags.None
        });

        for (var attempt = 0; attempt < 10; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            SharpDX.DXGI.Resource? resource = null;
            try
            {
                var result = duplication.TryAcquireNextFrame(500, out _, out resource);
                if (result == SharpDX.DXGI.ResultCode.WaitTimeout)
                {
                    continue;
                }
                result.CheckError();
                using var source = resource.QueryInterface<Texture2D>();
                device.ImmediateContext.CopyResource(source, staging);
                var mapped = device.ImmediateContext.MapSubresource(staging, 0, MapMode.Read, SharpDX.Direct3D11.MapFlags.None);
                try
                {
                    var bytes = new byte[checked(width * height * 4)];
                    for (var y = 0; y < height; y++)
                    {
                        Marshal.Copy(mapped.DataPointer + y * mapped.RowPitch, bytes, y * width * 4, width * 4);
                    }

                    // Some DXGI sessions deliver an all-transparent first frame without an API error.
                    // A physical monitor frame cannot be transparent; let the existing GDI fallback capture it.
                    var hasVisiblePixel = false;
                    for (var alpha = 3; alpha < bytes.Length; alpha += 4)
                    {
                        if (bytes[alpha] == 0) continue;
                        hasVisiblePixel = true;
                        break;
                    }
                    if (!hasVisiblePixel)
                        throw new InvalidDataException($"Desktop Duplication returned a transparent frame for monitor '{monitorId}'.");

                    return new DesktopFrame(width, height, bytes);
                }
                finally
                {
                    device.ImmediateContext.UnmapSubresource(staging, 0);
                }
            }
            finally
            {
                resource?.Dispose();
                try { duplication.ReleaseFrame(); } catch (SharpDXException) { }
            }
        }

        throw new TimeoutException($"Desktop Duplication did not deliver a frame for monitor '{monitorId}'.");
    }

    private sealed record DesktopFrame(int Width, int Height, byte[] Bytes);
}
