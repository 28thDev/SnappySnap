using System.Runtime.InteropServices;
using SharpDX.Direct3D;
using SharpDX.Direct3D11;
using SharpDX.DXGI;
using SnappySnap.Core;
using Windows.Graphics.DirectX.Direct3D11;
using Windows.Media.Core;
using Windows.Media.Editing;
using Windows.Media.Playback;
using Windows.Storage;
using Device = SharpDX.Direct3D11.Device;

namespace SnappySnap.Infrastructure;

/// <summary>One native clock drives audio and video. The UI pulls only the latest frame.</summary>
public sealed class WindowsVideoPreviewSession : IVideoPreviewSession
{
    private readonly object _gate = new();
    private readonly object _frameGate = new();
    private readonly MediaPlayer _player = new() { IsVideoFrameServerEnabled = true, AutoPlay = false };
    private readonly Device _device;
    private Texture2D _target = null!, _staging = null!;
    private IDirect3DSurface _surface = null!;
    private MediaSource? _source;
    private MediaComposition? _composition;
    private PreviewFrame? _frame;
    private volatile bool _disposed;
    private bool _seeking, _wantsPlay;
    private TimeSpan? _nextSeek;
    private int _generation, _framePending, _frameWorker;
    private int _width, _height;
    public string? Error { get; private set; }
    public WindowsVideoPreviewSession()
    {
        _device = new Device(DriverType.Hardware, DeviceCreationFlags.BgraSupport);
        ResizeSurface(960, 540);
        _player.PlaybackSession.SeekCompleted += (_, _) =>
        {
            lock (_gate)
            {
                if (_disposed) return;
                if (_nextSeek is { } next && Math.Abs((_player.PlaybackSession.Position - next).TotalMilliseconds) >= 1)
                { _nextSeek = null; _player.PlaybackSession.Position = next; }
                else { _nextSeek = null; _seeking = false; if (_wantsPlay) _player.Play(); }
            }
        };
        _player.VideoFrameAvailable += OnFrame;
        _player.MediaFailed += (_, args) => Error = args.ErrorMessage;
        _player.MediaEnded += (_, _) => _wantsPlay = false;
    }
    private void ResizeSurface(int width, int height)
    {
        if (_width == width && _height == height) return;
        _surface?.Dispose(); _target?.Dispose(); _staging?.Dispose();
        var description = new Texture2DDescription { Width = width, Height = height, ArraySize = 1, MipLevels = 1, Format = Format.B8G8R8A8_UNorm, SampleDescription = new SampleDescription(1, 0), Usage = ResourceUsage.Default, BindFlags = BindFlags.RenderTarget | BindFlags.ShaderResource };
        _target = new Texture2D(_device, description);
        description.Usage = ResourceUsage.Staging; description.BindFlags = BindFlags.None; description.CpuAccessFlags = CpuAccessFlags.Read;
        _staging = new Texture2D(_device, description);
        using var surface = _target.QueryInterface<Surface>();
        Marshal.ThrowExceptionForHR(CreateDirect3D11SurfaceFromDXGISurface(surface.NativePointer, out var pointer));
        try { _surface = WinRT.MarshalInterface<IDirect3DSurface>.FromAbi(pointer); } finally { Marshal.Release(pointer); }
        _width = width; _height = height;
    }
    public async Task LoadAsync(string path, VideoEditTimeline timeline, CancellationToken cancellationToken)
    {
        int generation;
        lock (_gate) { ObjectDisposedException.ThrowIf(_disposed, this); generation = ++_generation; _wantsPlay = false; _seeking = false; _nextSeek = null; _player.Pause(); Interlocked.Exchange(ref _frame, null); }
        var composition = new MediaComposition();
        var file = await StorageFile.GetFileFromPathAsync(Path.GetFullPath(path)).AsTask(cancellationToken).ConfigureAwait(false);
        foreach (var range in VideoEditSession.KeepRanges(timeline))
        {
            var clip = await MediaClip.CreateFromFileAsync(file).AsTask(cancellationToken).ConfigureAwait(false);
            clip.TrimTimeFromStart = range.Start; clip.TrimTimeFromEnd = timeline.SourceDuration - range.End;
            composition.Clips.Add(clip);
        }
        if (composition.Clips.Count == 0) throw new InvalidOperationException("Keep at least one video fragment.");
        var properties = composition.Clips[0].GetVideoEncodingProperties();
        var size = WindowsMediaVideoEditingService.FitDimensions(properties.Width, properties.Height, 960, 540);
        var source = MediaSource.CreateFromMediaStreamSource(composition.GeneratePreviewMediaStreamSource(size.Width, size.Height));
        var opened = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void OnOpened(MediaPlayer sender, object args) => opened.TrySetResult();
        void OnFailed(MediaPlayer sender, MediaPlayerFailedEventArgs args) => opened.TrySetException(new InvalidOperationException(args.ErrorMessage));
        lock (_gate)
        {
            if (_disposed || generation != _generation || cancellationToken.IsCancellationRequested) { source.Dispose(); return; }
            lock (_frameGate)
            {
                _player.Source = null; _source?.Dispose(); _composition?.Clips.Clear();
                ResizeSurface(size.Width, size.Height);
                _source = source; _composition = composition; Interlocked.Exchange(ref _frame, null); Error = null;
                _player.MediaOpened += OnOpened; _player.MediaFailed += OnFailed; _player.Source = source;
            }
        }
        try { await opened.Task.WaitAsync(TimeSpan.FromSeconds(15), cancellationToken).ConfigureAwait(false); }
        finally { lock (_gate) { if (!_disposed) { _player.MediaOpened -= OnOpened; _player.MediaFailed -= OnFailed; } } }
    }
    private void OnFrame(MediaPlayer sender, object args)
    {
        Interlocked.Exchange(ref _framePending, 1);
        ScheduleFrameCopy();
    }
    private void ScheduleFrameCopy()
    {
        if (Interlocked.CompareExchange(ref _frameWorker, 1, 0) != 0) return;
        _ = Task.Run(() =>
        {
            try
            {
                while (Interlocked.Exchange(ref _framePending, 0) != 0)
                {
                    lock (_frameGate)
                    {
                        if (_disposed) return;
                        var generation = Volatile.Read(ref _generation);
                        try
                        {
                            _player.CopyFrameToVideoSurface(_surface);
                            _device.ImmediateContext.CopyResource(_target, _staging);
                            var mapped = _device.ImmediateContext.MapSubresource(_staging, 0, MapMode.Read, SharpDX.Direct3D11.MapFlags.None);
                            try
                            {
                                var pixels = new byte[_width * _height * 4];
                                for (var y = 0; y < _height; y++) Marshal.Copy(mapped.DataPointer + y * mapped.RowPitch, pixels, y * _width * 4, _width * 4);
                                if (generation == Volatile.Read(ref _generation)) Interlocked.Exchange(ref _frame, new PreviewFrame(_width, _height, pixels));
                            }
                            finally { _device.ImmediateContext.UnmapSubresource(_staging, 0); }
                        }
                        catch (Exception ex) { Error = "Preview frame failed: " + ex.Message; }
                    }
                }
            }
            finally
            {
                Interlocked.Exchange(ref _frameWorker, 0);
                if (!_disposed && Volatile.Read(ref _framePending) != 0) ScheduleFrameCopy();
            }
        });
    }
    public PreviewFrame? TakeFrame() => Interlocked.Exchange(ref _frame, null);
    public void Play() { lock (_gate) { if (_disposed) return; _wantsPlay = true; if (!_seeking) _player.Play(); } }
    public void Pause() { lock (_gate) { if (_disposed) return; _wantsPlay = false; _player.Pause(); } }
    public void Seek(TimeSpan position)
    {
        lock (_gate)
        {
            if (_disposed) return;
            if (_seeking) { _nextSeek = position; return; }
            if (Math.Abs((_player.PlaybackSession.Position - position).TotalMilliseconds) < 1) return;
            _player.Pause(); _seeking = true; _player.PlaybackSession.Position = position;
        }
    }
    public TimeSpan Position => _disposed ? TimeSpan.Zero : _player.PlaybackSession.Position;
    public bool IsPlaying => !_disposed && _wantsPlay && (_seeking || _player.PlaybackSession.PlaybackState is MediaPlaybackState.Playing or MediaPlaybackState.Buffering);
    public double Volume { get => _player.Volume; set => _player.Volume = Math.Clamp(value, 0, 1); }
    public bool IsMuted { get => _player.IsMuted; set => _player.IsMuted = value; }
    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return; _disposed = true; ++_generation;
            lock (_frameGate)
            {
                _player.VideoFrameAvailable -= OnFrame; _player.Pause(); _player.Source = null; _player.Dispose(); _source?.Dispose();
                _composition?.Clips.Clear(); _surface.Dispose(); _staging.Dispose(); _target.Dispose(); _device.Dispose(); Interlocked.Exchange(ref _frame, null);
            }
        }
    }
    [DllImport("d3d11.dll", ExactSpelling = true)] private static extern int CreateDirect3D11SurfaceFromDXGISurface(nint surface, out nint graphicsSurface);
}
