using SnappySnap.Core;

namespace SnappySnap.Application;

public sealed class RecordingSessionCoordinator : IAsyncDisposable
{
    private readonly IVideoCaptureBackendFactory _backendFactory;
    private readonly IHistoryRepository _historyRepository;
    private readonly IThumbnailService _thumbnailService;
    private readonly IRecoveryStore _recoveryStore;
    private readonly IAppLogger _logger;
    private readonly RecordingStateMachine _stateMachine = new();
    private readonly ActiveMediaTimer _timer;
    private readonly SemaphoreSlim _operationGate = new(1, 1);
    private IVideoCaptureBackend? _backend;
    private Guid _sessionId;
    private string? _finalPath;
    private string? _tempPath;
    private CapturePlan? _capturePlan;
    private bool _systemAudioEnabled;
    private bool _systemAudioAvailable;
    private bool _microphoneEnabled;
    private bool _microphoneAvailable;
    private string? _lastError;
    private bool _disposed;

    public RecordingSessionCoordinator(
        IVideoCaptureBackendFactory backendFactory,
        IHistoryRepository historyRepository,
        IThumbnailService thumbnailService,
        IRecoveryStore recoveryStore,
        IAppLogger logger,
        IMonotonicClock clock)
    {
        _backendFactory = backendFactory;
        _historyRepository = historyRepository;
        _thumbnailService = thumbnailService;
        _recoveryStore = recoveryStore;
        _logger = logger;
        _timer = new ActiveMediaTimer(clock);
    }

    public RecordingSnapshot Snapshot => new(_stateMachine.State, _timer.Elapsed, _systemAudioEnabled, _microphoneEnabled, _microphoneAvailable, _capturePlan, _finalPath, _lastError, _systemAudioAvailable);
    public event EventHandler<RecordingSnapshot>? StateChanged;

    public Task StartAsync(CapturePlan plan, string captureRoot, string tempDirectory, AppSettings settings, CancellationToken cancellationToken) =>
        Task.Run(() => StartCoreAsync(plan, captureRoot, tempDirectory, settings, cancellationToken), cancellationToken);

    private async Task StartCoreAsync(CapturePlan plan, string captureRoot, string tempDirectory, AppSettings settings, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentException.ThrowIfNullOrWhiteSpace(captureRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(tempDirectory);
        ObjectDisposedException.ThrowIf(_disposed, this);
        await _operationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_stateMachine.State != RecordingState.Idle)
            {
                return;
            }

            _stateMachine.Apply(RecordingCommand.StartRequested);
            Publish();
            _stateMachine.Apply(RecordingCommand.RegionSelected);
            _capturePlan = plan;
            _sessionId = Guid.NewGuid();
            Directory.CreateDirectory(tempDirectory);
            var createdAt = DateTimeOffset.UtcNow;
            _finalPath = MediaPathGenerator.Create(captureRoot, MediaType.Video, createdAt);
            _tempPath = Path.Combine(tempDirectory, $"recording-{_sessionId:N}.partial.mp4");
            _systemAudioEnabled = settings.Recording.SystemAudioDefault;
            _microphoneEnabled = settings.Recording.MicrophoneDefault;
            _microphoneAvailable = false;
            _lastError = null;
            _backend = _backendFactory.Create();
            _backend.Failed += OnBackendFailed;
            _backend.StateChanged += OnBackendStateChanged;
            var request = new VideoRecordingRequest(
                plan,
                _tempPath,
                _finalPath,
                QualityProfileCatalog.Resolve(settings.Recording.QualityProfile),
                settings.Recording.MousePointer,
                _systemAudioEnabled,
                _microphoneEnabled);
            await _recoveryStore.WriteAsync(new RecoverySessionRecord(_sessionId, RecoveryState.RecordingStarted, createdAt, createdAt, _tempPath, _finalPath, _backend.GetType().Name), cancellationToken).ConfigureAwait(false);
            await _backend.StartAsync(request, cancellationToken).ConfigureAwait(false);
            _systemAudioAvailable = _backend.State.SystemAudioAvailable;
            if (!_systemAudioAvailable) _systemAudioEnabled = false;
            _microphoneAvailable = _backend.State.MicrophoneAvailable;
            if (!_microphoneAvailable)
            {
                _microphoneEnabled = false;
            }
            _timer.Reset();
            _timer.Start();
            _stateMachine.Apply(RecordingCommand.BackendStarted);
            Publish();
        }
        catch (Exception ex)
        {
            _lastError = "Recording could not start. Check the local log for details.";
            _logger.Error("Recording start failed.", ex, new Dictionary<string, object?> { ["sessionId"] = _sessionId });
            if (_stateMachine.State == RecordingState.Starting)
            {
                _stateMachine.Apply(RecordingCommand.StartFailed);
            }
            Publish();
            await ReleaseFailedSessionAsync().ConfigureAwait(false);
            throw;
        }
        finally
        {
            _operationGate.Release();
        }
    }

    public Task PauseOrResumeAsync(CancellationToken cancellationToken)
    {
        var expectedState = _stateMachine.State;
        if (expectedState is not (RecordingState.Recording or RecordingState.Paused)) return Task.CompletedTask;
        return Task.Run(() => PauseOrResumeCoreAsync(expectedState, cancellationToken), cancellationToken);
    }

    private async Task PauseOrResumeCoreAsync(RecordingState expectedState, CancellationToken cancellationToken)
    {
        await _operationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            // A duplicate request for the old state must not become the inverse operation after waiting.
            if (_backend is null || _stateMachine.State != expectedState)
            {
                return;
            }

            if (_stateMachine.State == RecordingState.Recording)
            {
                _stateMachine.Apply(RecordingCommand.PauseRequested);
                Publish();
                await _backend.PauseAsync(cancellationToken).ConfigureAwait(false);
                _timer.Pause();
                _stateMachine.Apply(RecordingCommand.Paused);
                Publish();
            }
            else if (_stateMachine.State == RecordingState.Paused)
            {
                _stateMachine.Apply(RecordingCommand.ResumeRequested);
                Publish();
                await _backend.ResumeAsync(cancellationToken).ConfigureAwait(false);
                _timer.Resume();
                _stateMachine.Apply(RecordingCommand.Resumed);
                Publish();
            }
        }
        catch (Exception ex)
        {
            _lastError = "Recording pause/resume failed.";
            _logger.Error("Recording pause/resume failed.", ex, new Dictionary<string, object?> { ["sessionId"] = _sessionId });
            if (_stateMachine.State is RecordingState.Pausing or RecordingState.Resuming)
                _stateMachine.Apply(RecordingCommand.BackendFailed);
            _timer.Stop();
            Publish();
            await ReleaseFailedSessionAsync().ConfigureAwait(false);
            throw;
        }
        finally
        {
            _operationGate.Release();
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.Run(() => StopCoreAsync(cancellationToken), cancellationToken);

    private async Task StopCoreAsync(CancellationToken cancellationToken)
    {
        await _operationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_backend is null || _stateMachine.State is RecordingState.Idle or RecordingState.Completed or RecordingState.Error or RecordingState.RecoveryRequired or RecordingState.Finalizing)
            {
                return;
            }

            _stateMachine.Apply(RecordingCommand.StopRequested);
            Publish();
            var activeDuration = _timer.Stop();
            await _recoveryStore.MarkAsync(_sessionId, RecoveryState.Finalizing, null, cancellationToken).ConfigureAwait(false);
            var result = await _backend.StopAsync(cancellationToken).ConfigureAwait(false);
            var finalPath = _finalPath ?? throw new InvalidOperationException("Final recording path is missing.");
            var completedPath = result.CompletedPath;
            Directory.CreateDirectory(Path.GetDirectoryName(finalPath)!);
            if (!string.Equals(completedPath, finalPath, StringComparison.OrdinalIgnoreCase))
            {
                File.Move(completedPath, finalPath, overwrite: false);
            }

            var plan = _capturePlan ?? throw new InvalidOperationException("Capture plan is missing.");
            var item = await _historyRepository.AddAsync(new NewHistoryItem(MediaType.Video, finalPath, DateTimeOffset.UtcNow, result.WidthPx > 0 ? result.WidthPx : plan.OutputWidth, result.HeightPx > 0 ? result.HeightPx : plan.OutputHeight, result.Duration > TimeSpan.Zero ? result.Duration : activeDuration, null, plan.SelectedVirtualBounds), cancellationToken).ConfigureAwait(false);
            _ = GenerateThumbnailAsync(item);
            await _recoveryStore.MarkAsync(_sessionId, RecoveryState.Recovered, null, cancellationToken).ConfigureAwait(false);
            _stateMachine.Apply(RecordingCommand.Finalized);
            Publish();
            await ReleaseBackendAsync().ConfigureAwait(false);
            _stateMachine.Apply(RecordingCommand.Reset);
            _capturePlan = null;
            _tempPath = null;
            Publish();
        }
        catch (Exception ex)
        {
            _lastError = "Recording could not be finalized. The temporary file was retained for recovery.";
            _logger.Error("Recording finalization failed.", ex, new Dictionary<string, object?> { ["sessionId"] = _sessionId, ["tempPath"] = _tempPath, ["finalPath"] = _finalPath });
            if (_stateMachine.State == RecordingState.Finalizing)
            {
                _stateMachine.Apply(RecordingCommand.FinalizeFailed);
            }
            await TryMarkRecoveryFailureAsync(ex).ConfigureAwait(false);
            Publish();
            await ReleaseFailedSessionAsync().ConfigureAwait(false);
            throw;
        }
        finally
        {
            _operationGate.Release();
        }
    }

    public Task SetSystemAudioEnabledAsync(bool enabled, CancellationToken cancellationToken) => Task.Run(() => SetSystemAudioCoreAsync(enabled, cancellationToken), cancellationToken);

    private async Task SetSystemAudioCoreAsync(bool enabled, CancellationToken cancellationToken)
    {
        await _operationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_backend is null)
            {
                return;
            }
            if (!_systemAudioAvailable) { _systemAudioEnabled = false; Publish(); return; }
            await _backend.SetSystemAudioMutedAsync(!enabled, cancellationToken).ConfigureAwait(false);
            _systemAudioEnabled = enabled;
            Publish();
        }
        finally
        {
            _operationGate.Release();
        }
    }

    public Task SetMicrophoneEnabledAsync(bool enabled, CancellationToken cancellationToken) => Task.Run(() => SetMicrophoneCoreAsync(enabled, cancellationToken), cancellationToken);

    private async Task SetMicrophoneCoreAsync(bool enabled, CancellationToken cancellationToken)
    {
        await _operationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_backend is null)
            {
                return;
            }
            if (!_microphoneAvailable)
            {
                _microphoneEnabled = false;
                Publish();
                return;
            }
            await _backend.SetMicrophoneMutedAsync(!enabled, cancellationToken).ConfigureAwait(false);
            _microphoneEnabled = enabled;
            Publish();
        }
        finally
        {
            _operationGate.Release();
        }
    }

    private void OnBackendFailed(object? sender, RecordingBackendError error)
    {
        // Native recorder callbacks must return before disposal can join the recorder thread.
        _ = Task.Run(() => HandleBackendEventAsync(sender, async () =>
        {
            _lastError = error.Message;
            _logger.Error("Video backend reported an error.", error.Exception, new Dictionary<string, object?> { ["sessionId"] = _sessionId, ["message"] = error.Message });
            if (_stateMachine.State is RecordingState.Recording or RecordingState.Paused)
                _stateMachine.Apply(RecordingCommand.BackendFailed);
            _timer.Stop();
            Publish();
            await ReleaseFailedSessionAsync().ConfigureAwait(false);
        }));
    }

    private void OnBackendStateChanged(object? sender, RecordingBackendState state)
    {
        _ = Task.Run(() => HandleBackendEventAsync(sender, () =>
        {
            _microphoneAvailable = _backend!.State.MicrophoneAvailable;
            _systemAudioAvailable = _backend.State.SystemAudioAvailable;
            if (!_systemAudioAvailable) _systemAudioEnabled = false;
            if (!_microphoneAvailable) _microphoneEnabled = false;
            Publish();
            return Task.CompletedTask;
        }));
    }

    private async Task HandleBackendEventAsync(object? sender, Func<Task> action)
    {
        await _operationGate.WaitAsync().ConfigureAwait(false);
        try { if (!_disposed && ReferenceEquals(sender, _backend)) await action().ConfigureAwait(false); }
        catch (Exception ex) { _logger.Error("Could not handle recording backend event.", ex); }
        finally { _operationGate.Release(); }
    }

    private async Task ReleaseBackendAsync()
    {
        if (_backend is null) return;
        _backend.Failed -= OnBackendFailed;
        _backend.StateChanged -= OnBackendStateChanged;
        await _backend.DisposeAsync().ConfigureAwait(false);
        _backend = null;
    }

    private async Task ReleaseFailedSessionAsync()
    {
        _timer.Stop();
        try { await ReleaseBackendAsync().ConfigureAwait(false); }
        catch (Exception ex) { _logger.Error("Failed backend cleanup; recovery is still required.", ex); return; }
        if (_stateMachine.State is RecordingState.Error or RecordingState.RecoveryRequired)
        {
            _stateMachine.Apply(RecordingCommand.Reset);
            _capturePlan = null;
            Publish();
        }
    }

    private async Task GenerateThumbnailAsync(HistoryItem item)
    {
        try { await _thumbnailService.EnsureThumbnailAsync(item, CancellationToken.None).ConfigureAwait(false); }
        catch (Exception ex) { _logger.Error("Recording thumbnail failed; media remains saved.", ex); }
    }

    private async Task TryMarkRecoveryFailureAsync(Exception exception)
    {
        if (_sessionId == Guid.Empty)
        {
            return;
        }

        try
        {
            var state = _finalPath is not null && File.Exists(_finalPath) && (_tempPath is null || !File.Exists(_tempPath))
                ? RecoveryState.FinalizedButNotIndexed : RecoveryState.Finalizing;
            await _recoveryStore.MarkAsync(_sessionId, state, exception.Message, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception recoveryError)
        {
            _logger.Error("Could not update recovery state.", recoveryError, new Dictionary<string, object?> { ["sessionId"] = _sessionId });
        }
    }

    private void Publish() => StateChanged?.Invoke(this, Snapshot);

    public ValueTask DisposeAsync() => new(Task.Run(DisposeCoreAsync));

    private async Task DisposeCoreAsync()
    {
        await _operationGate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_disposed) return;
            _disposed = true;
            _timer.Stop();
            await ReleaseBackendAsync().ConfigureAwait(false);
        }
        finally { _operationGate.Release(); }
        // Queued native callbacks may still acquire this managed semaphore and observe _disposed.
    }
}
