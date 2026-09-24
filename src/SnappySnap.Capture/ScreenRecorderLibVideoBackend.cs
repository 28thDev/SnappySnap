using System.Diagnostics;
using System.IO;
using ScreenRecorderLib;
using SnappySnap.Core;

namespace SnappySnap.Capture;

public sealed class ScreenRecorderLibVideoBackend : IVideoCaptureBackend
{
    private readonly IAppLogger _logger;
    private readonly ActiveMediaTimer _timer = new(new SystemMonotonicClock());
    private Recorder? _recorder;
    private DynamicOptionsBuilder? _dynamicOptions;
    private AudioSourceBase? _systemAudio;
    private AudioSourceBase? _microphone;
    private float _systemAudioVolume = 1f;
    private float _microphoneVolume = 1f;
    private bool _microphoneAvailable;
    private VideoRecordingRequest? _request;
    private TaskCompletionSource<bool>? _started;
    private TaskCompletionSource<string>? _completed;
    private RecordingBackendState _state = new(false, false, TimeSpan.Zero, false, true, MicrophoneAvailable: false, SystemAudioAvailable: false);
    private bool _disposed;

    public ScreenRecorderLibVideoBackend(IAppLogger logger) => _logger = logger;
#if DEBUG
    // Deterministic encoder measurements without a visible window or desktop capture.
    public string? FixtureVideoPath { private get; init; }
#endif

    public RecordingBackendCapabilities Capabilities { get; } = new(true, true, true, true, true);
    public RecordingBackendState State => _state;
    public event EventHandler<RecordingBackendState>? StateChanged;
    public event EventHandler<RecordingBackendError>? Failed;

    public async Task StartAsync(VideoRecordingRequest request, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(request);
        request.CapturePlan.Validate();
        if ((request.CapturePlan.OutputWidth & 1) != 0 || (request.CapturePlan.OutputHeight & 1) != 0)
        {
            throw new ArgumentException("H.264 recording requires even output dimensions.", nameof(request));
        }
        if (_recorder is not null)
        {
            throw new InvalidOperationException("The video backend is already active.");
        }

        Directory.CreateDirectory(Path.GetDirectoryName(request.TempPath)!);
        _request = request;
        _started = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        _completed = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var options = BuildOptions(request);
#if DEBUG
        if (FixtureVideoPath is not null)
        {
            options.SourceOptions.RecordingSources = new List<RecordingSourceBase> { new VideoRecordingSource(FixtureVideoPath) };
        }
#endif
        _logger.Info("Starting ScreenRecorderLib video backend.", new Dictionary<string, object?>
        {
            ["qualityProfile"] = request.Quality.Key,
            ["hardwareEncodingRequested"] = request.Quality.HardwarePreferred,
            ["fastStart"] = request.Quality.FastStart,
            ["frameRate"] = request.Quality.FrameRate,
            ["segments"] = request.CapturePlan.Segments.Count
        });
        _recorder = Recorder.CreateRecorder(options);
        _dynamicOptions = _recorder.GetDynamicOptionsBuilder();
        _recorder.OnStatusChanged += OnStatusChanged;
        _recorder.OnRecordingFailed += OnRecordingFailed;
        _recorder.OnRecordingComplete += OnRecordingComplete;
        try
        {
            _recorder.Record(request.TempPath);
            await _started.Task.WaitAsync(TimeSpan.FromSeconds(30), cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            DetachAndDisposeRecorder();
            throw;
        }
    }

    public Task PauseAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (_recorder is null)
        {
            throw new InvalidOperationException("The video backend is not active.");
        }
        _recorder.Pause();
        _timer.Pause();
        PublishState(isPaused: true);
        return Task.CompletedTask;
    }

    public Task ResumeAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (_recorder is null)
        {
            throw new InvalidOperationException("The video backend is not active.");
        }
        _recorder.Resume();
        _timer.Resume();
        PublishState(isPaused: false);
        return Task.CompletedTask;
    }

    public Task SetSystemAudioMutedAsync(bool muted, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (_systemAudio is null || _dynamicOptions is null)
        {
            return Task.CompletedTask;
        }
        if (!muted && _systemAudioVolume <= 0)
        {
            _systemAudioVolume = 1f;
        }
        _systemAudio.Volume = muted ? 0f : _systemAudioVolume;
        _dynamicOptions.SetUpdatedAudioSource(_systemAudio).Apply();
        PublishState(systemAudioMuted: muted);
        return Task.CompletedTask;
    }

    public Task SetMicrophoneMutedAsync(bool muted, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (_microphone is null || _dynamicOptions is null)
        {
            return Task.CompletedTask;
        }
        if (!muted && _microphoneVolume <= 0)
        {
            _microphoneVolume = 1f;
        }
        _microphone.Volume = muted ? 0f : _microphoneVolume;
        _dynamicOptions.SetUpdatedAudioSource(_microphone).Apply();
        PublishState(microphoneMuted: muted);
        return Task.CompletedTask;
    }

    public async Task<VideoRecordingResult> StopAsync(CancellationToken cancellationToken)
    {
        if (_recorder is null || _request is null || _completed is null)
        {
            throw new InvalidOperationException("The video backend is not active.");
        }

        _timer.Stop();
        _logger.Info("Requesting ScreenRecorderLib stop.");
        _recorder.Stop();
        var path = await _completed.Task.WaitAsync(TimeSpan.FromMinutes(2), cancellationToken).ConfigureAwait(false);
        var duration = _timer.Elapsed;
        var result = new VideoRecordingResult(path, duration, _request.CapturePlan.OutputWidth, _request.CapturePlan.OutputHeight, false, $"ScreenRecorderLib 7.0.1; hardwareRequested={_request.Quality.HardwarePreferred}; hardwareOutcome=not-exposed-by-recorder-api; recorderApi=WindowsGraphicsCapture; sourceSegments={_request.CapturePlan.Segments.Count}");
        _logger.Info("ScreenRecorderLib video backend finalized.", new Dictionary<string, object?>
        {
            ["path"] = path,
            ["durationSeconds"] = duration.TotalSeconds,
            ["hardwareRequested"] = _request.Quality.HardwarePreferred,
            ["hardwareOutcome"] = "not-exposed-by-recorder-api"
        });
        DetachAndDisposeRecorder();
        return result;
    }

    private RecorderOptions BuildOptions(VideoRecordingRequest request)
    {
        var sources = request.CapturePlan.Segments.Select(segment =>
        {
            var source = new DisplayRecordingSource(segment.MonitorId)
            {
                RecorderApi = RecorderApi.WindowsGraphicsCapture,
                IsBorderRequired = false,
                IsCursorCaptureEnabled = request.CaptureCursor,
                SourceRect = new ScreenRect(segment.SourceRectOnMonitorPx.X, segment.SourceRectOnMonitorPx.Y, segment.SourceRectOnMonitorPx.Width, segment.SourceRectOnMonitorPx.Height),
                Position = new ScreenPoint(segment.DestinationRectInOutputPx.X, segment.DestinationRectInOutputPx.Y),
                OutputSize = new ScreenSize(segment.DestinationRectInOutputPx.Width, segment.DestinationRectInOutputPx.Height)
            };
            return (RecordingSourceBase)source;
        }).ToList();

        var audioSources = new List<AudioSourceBase>();
        try
        {
            _systemAudio = LoopbackAudioSource.Default;
            if (_systemAudio is not null)
            {
                _systemAudioVolume = request.SystemAudioEnabled ? 1f : 0f;
                _systemAudio.Volume = _systemAudioVolume;
                audioSources.Add(_systemAudio);
            }
        }
        catch (Exception ex)
        {
            _systemAudio = null;
            _logger.Warn("System audio source is unavailable; continuing without it.", new Dictionary<string, object?> { ["error"] = ex.Message });
        }

        try
        {
            _microphone = null;
            _microphoneAvailable = false;
            _microphone = CaptureAudioSource.Default;
            if (_microphone is not null)
            {
                _microphoneAvailable = true;
                _microphoneVolume = request.MicrophoneEnabled ? 1f : 0f;
                _microphone.Volume = _microphoneVolume;
                audioSources.Add(_microphone);
            }
            else
            {
                _logger.Warn("Microphone source is unavailable; continuing without microphone audio.");
            }
        }
        catch (Exception ex)
        {
            _microphone = null;
            _microphoneAvailable = false;
            _logger.Warn("Microphone source is unavailable; continuing without it.", new Dictionary<string, object?> { ["error"] = ex.Message });
        }

        return new RecorderOptions
        {
            LogOptions = new LogOptions
            {
                IsLogEnabled = true,
                LogSeverityLevel = LogLevel.Debug,
                LogFilePath = request.TempPath + ".native.log"
            },
            SourceOptions = new SourceOptions { RecordingSources = sources },
            OutputOptions = new OutputOptions
            {
                RecorderMode = RecorderMode.Video,
                OutputFrameSize = new ScreenSize(request.CapturePlan.OutputWidth, request.CapturePlan.OutputHeight),
                Stretch = StretchMode.None
            },
            VideoEncoderOptions = new VideoEncoderOptions
            {
                Encoder = new H264VideoEncoder { BitrateMode = H264BitrateControlMode.Quality, EncoderProfile = H264Profile.Main },
                Framerate = request.Quality.FrameRate,
                Quality = request.Quality.Quality,
                IsHardwareEncodingEnabled = request.Quality.HardwarePreferred,
                IsMp4FastStartEnabled = request.Quality.FastStart,
                IsFixedFramerate = true
            },
            AudioOptions = new AudioOptions
            {
                IsAudioEnabled = audioSources.Count > 0,
                AudioSources = audioSources,
                Bitrate = request.Quality.AudioBitrateKbps >= 160 ? AudioBitrate.bitrate_160kbps : AudioBitrate.bitrate_128kbps,
                Channels = AudioChannels.Stereo
            },
            MouseOptions = new MouseOptions { MouseClickDetectionMode = MouseDetectionMode.Polling }
        };
    }

    private void OnStatusChanged(object? sender, RecordingStatusEventArgs args)
    {
        _logger.Info("ScreenRecorderLib status changed.", new Dictionary<string, object?> { ["status"] = args.Status.ToString() });
        switch (args.Status)
        {
            case RecorderStatus.Recording:
                if (!_state.IsRecording)
                {
                    _timer.Reset();
                    _timer.Start();
                }
                PublishState(isRecording: true, isPaused: false);
                _started?.TrySetResult(true);
                break;
            case RecorderStatus.Paused:
                PublishState(isRecording: true, isPaused: true);
                break;
            case RecorderStatus.Finishing:
                PublishState(isRecording: true, isPaused: false, diagnostics: "Finishing");
                break;
            case RecorderStatus.Idle:
                PublishState(isRecording: false, isPaused: false);
                break;
        }
    }

    private void OnRecordingFailed(object? sender, RecordingFailedEventArgs args)
    {
        _logger.Error("ScreenRecorderLib reported a recording failure.", new InvalidOperationException(args.Error));
        var exception = new InvalidOperationException(args.Error);
        _started?.TrySetException(exception);
        _completed?.TrySetException(exception);
        Failed?.Invoke(this, new RecordingBackendError("ScreenRecorderLib failed to record the selected region.", exception));
    }

    private void OnRecordingComplete(object? sender, RecordingCompleteEventArgs args)
    {
        _logger.Info("ScreenRecorderLib reported recording completion.", new Dictionary<string, object?> { ["path"] = args.FilePath });
        _completed?.TrySetResult(args.FilePath);
    }

    private void PublishState(bool? isRecording = null, bool? isPaused = null, bool? systemAudioMuted = null, bool? microphoneMuted = null, string? diagnostics = null, bool? microphoneAvailable = null)
    {
        _state = new RecordingBackendState(
            isRecording ?? _state.IsRecording,
            isPaused ?? _state.IsPaused,
            _timer.Elapsed,
            systemAudioMuted ?? _state.SystemAudioMuted,
            microphoneMuted ?? _state.MicrophoneMuted,
            diagnostics ?? _state.Diagnostics,
            microphoneAvailable ?? _microphoneAvailable,
            _systemAudio is not null);
        StateChanged?.Invoke(this, _state);
    }

    private void DetachAndDisposeRecorder()
    {
        if (_recorder is null)
        {
            return;
        }
        _recorder.OnStatusChanged -= OnStatusChanged;
        _recorder.OnRecordingFailed -= OnRecordingFailed;
        _recorder.OnRecordingComplete -= OnRecordingComplete;
        _recorder.Dispose();
        _recorder = null;
        _dynamicOptions = null;
        _systemAudio = null;
        _microphone = null;
        PublishState(isRecording: false, isPaused: false);
    }

    public ValueTask DisposeAsync()
    {
        if (!_disposed)
        {
            _disposed = true;
            DetachAndDisposeRecorder();
        }
        return ValueTask.CompletedTask;
    }
}

public sealed class ScreenRecorderLibVideoBackendFactory : IVideoCaptureBackendFactory
{
    private readonly IAppLogger _logger;
    public ScreenRecorderLibVideoBackendFactory(IAppLogger logger) => _logger = logger;
    public IVideoCaptureBackend Create() => new ScreenRecorderLibVideoBackend(_logger);
}
