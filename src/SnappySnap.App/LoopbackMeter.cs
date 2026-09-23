#if DEBUG
using System.Runtime.InteropServices;
namespace SnappySnap.App;

/// <summary>Test-only endpoint loopback meter. Retains timing/levels, never audio samples.</summary>
internal sealed class LoopbackMeter : IDisposable
{
    private readonly object _enumerator;
    private readonly IMMDevice _device;
    private readonly IAudioClient _client;
    private readonly IAudioCaptureClient _capture;
    private readonly int _channels, _sampleRate;
    public LoopbackMeter()
    {
        _enumerator = Activator.CreateInstance(Type.GetTypeFromCLSID(new Guid("BCDE0395-E52F-467C-8E3D-C4579291692E"))!)!;
        Marshal.ThrowExceptionForHR(((IMMDeviceEnumerator)_enumerator).GetDefaultAudioEndpoint(0, 1, out _device));
        var clientId = typeof(IAudioClient).GUID;
        Marshal.ThrowExceptionForHR(_device.Activate(ref clientId, 23, 0, out var audio)); _client = (IAudioClient)audio;
        Marshal.ThrowExceptionForHR(_client.GetMixFormat(out var format));
        try
        {
            _channels = Marshal.ReadInt16(format, 2); _sampleRate = Marshal.ReadInt32(format, 4);
            var tag = (ushort)Marshal.ReadInt16(format, 0); var bits = Marshal.ReadInt16(format, 14);
            var subFormat = tag == 0xfffe ? Marshal.PtrToStructure<Guid>(format + 24) : Guid.Empty;
            if (bits != 32 || !(tag == 3 || subFormat == new Guid("00000003-0000-0010-8000-00aa00389b71"))) throw new NotSupportedException("Loopback harness requires float32 mix format.");
            var session = Guid.Empty;
            Marshal.ThrowExceptionForHR(_client.Initialize(0, 0x20000, 2_000_000, 0, format, ref session));
        }
        finally { Marshal.FreeCoTaskMem(format); }
        var captureId = typeof(IAudioCaptureClient).GUID;
        Marshal.ThrowExceptionForHR(_client.GetService(ref captureId, out var capture)); _capture = (IAudioCaptureClient)capture;
        Marshal.ThrowExceptionForHR(_client.Start());
    }
    public IReadOnlyList<(double Time, double Level)> Read()
    {
        var result = new List<(double, double)>();
        Marshal.ThrowExceptionForHR(_capture.GetNextPacketSize(out var count));
        while (count > 0)
        {
            Marshal.ThrowExceptionForHR(_capture.GetBuffer(out var data, out var frames, out var flags, out _, out var qpc));
            try
            {
                var level = 0d;
                if ((flags & 2) == 0)
                {
                    var samples = new float[frames * _channels]; Marshal.Copy(data, samples, 0, samples.Length);
                    // Narrow-band amplitude isolates the fixture's 880 Hz pulse from unrelated low-frequency noise.
                    double real = 0, imaginary = 0;
                    for (var frame = 0; frame < frames; frame++)
                    {
                        var phase = 2 * Math.PI * 880 * frame / _sampleRate;
                        real += samples[frame * _channels] * Math.Cos(phase); imaginary += samples[frame * _channels] * Math.Sin(phase);
                    }
                    level = 2 * Math.Sqrt(real * real + imaginary * imaginary) / frames;
                }
                if ((flags & 4) == 0) result.Add((qpc / 10_000_000d, level));
            }
            finally { Marshal.ThrowExceptionForHR(_capture.ReleaseBuffer(frames)); }
            Marshal.ThrowExceptionForHR(_capture.GetNextPacketSize(out count));
        }
        return result;
    }
    public void Dispose()
    {
        _client.Stop(); Marshal.ReleaseComObject(_capture); Marshal.ReleaseComObject(_client); Marshal.ReleaseComObject(_device); Marshal.ReleaseComObject(_enumerator);
    }
    [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDeviceEnumerator
    {
        [PreserveSig] int EnumAudioEndpoints(int flow, uint mask, out nint collection);
        [PreserveSig] int GetDefaultAudioEndpoint(int flow, int role, out IMMDevice device);
    }
    [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDevice
    {
        [PreserveSig] int Activate(ref Guid iid, uint context, nint parameters, [MarshalAs(UnmanagedType.IUnknown)] out object instance);
    }
    [ComImport, Guid("1CB9AD4C-DBFA-4C32-B178-C2F568A703B2"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioClient
    {
        [PreserveSig] int Initialize(int mode, uint flags, long duration, long periodicity, nint format, ref Guid session);
        [PreserveSig] int GetBufferSize(out uint frames);
        [PreserveSig] int GetStreamLatency(out long latency);
        [PreserveSig] int GetCurrentPadding(out uint frames);
        [PreserveSig] int IsFormatSupported(int mode, nint format, out nint closest);
        [PreserveSig] int GetMixFormat(out nint format);
        [PreserveSig] int GetDevicePeriod(out long defaultPeriod, out long minimumPeriod);
        [PreserveSig] int Start();
        [PreserveSig] int Stop();
        [PreserveSig] int Reset();
        [PreserveSig] int SetEventHandle(nint handle);
        [PreserveSig] int GetService(ref Guid iid, [MarshalAs(UnmanagedType.IUnknown)] out object service);
    }
    [ComImport, Guid("C8ADBD64-E71E-48A0-A4DE-185C395CD317"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioCaptureClient
    {
        [PreserveSig] int GetBuffer(out nint data, out uint frames, out uint flags, out ulong devicePosition, out ulong qpcPosition);
        [PreserveSig] int ReleaseBuffer(uint frames);
        [PreserveSig] int GetNextPacketSize(out uint frames);
    }
}
#endif
