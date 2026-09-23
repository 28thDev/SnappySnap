namespace SnappySnap.Core;

public sealed class RecordingStateMachine
{
    public RecordingState State { get; private set; } = RecordingState.Idle;

    public RecordingState Apply(RecordingCommand command)
    {
        var next = (State, command) switch
        {
            (RecordingState.Idle, RecordingCommand.StartRequested) => RecordingState.SelectingRegion,
            (RecordingState.SelectingRegion, RecordingCommand.RegionSelected) => RecordingState.Starting,
            (RecordingState.SelectingRegion, RecordingCommand.Cancel) => RecordingState.Idle,
            (RecordingState.Starting, RecordingCommand.BackendStarted) => RecordingState.Recording,
            (RecordingState.Starting, RecordingCommand.StartFailed) => RecordingState.Error,
            (RecordingState.Recording, RecordingCommand.PauseRequested) => RecordingState.Pausing,
            (RecordingState.Recording, RecordingCommand.StopRequested) => RecordingState.Finalizing,
            (RecordingState.Recording, RecordingCommand.BackendFailed) => RecordingState.RecoveryRequired,
            (RecordingState.Pausing, RecordingCommand.Paused) => RecordingState.Paused,
            (RecordingState.Pausing, RecordingCommand.BackendFailed) => RecordingState.RecoveryRequired,
            (RecordingState.Paused, RecordingCommand.ResumeRequested) => RecordingState.Resuming,
            (RecordingState.Paused, RecordingCommand.StopRequested) => RecordingState.Finalizing,
            (RecordingState.Paused, RecordingCommand.BackendFailed) => RecordingState.RecoveryRequired,
            (RecordingState.Resuming, RecordingCommand.Resumed) => RecordingState.Recording,
            (RecordingState.Resuming, RecordingCommand.BackendFailed) => RecordingState.RecoveryRequired,
            (RecordingState.Finalizing, RecordingCommand.Finalized) => RecordingState.Completed,
            (RecordingState.Finalizing, RecordingCommand.FinalizeFailed) => RecordingState.RecoveryRequired,
            (RecordingState.Error, RecordingCommand.Reset) => RecordingState.Idle,
            (RecordingState.RecoveryRequired, RecordingCommand.Reset) => RecordingState.Idle,
            (RecordingState.Completed, RecordingCommand.Reset) => RecordingState.Idle,
            _ => throw new InvalidOperationException($"Invalid recording transition: {State} + {command}.")
        };

        State = next;
        return next;
    }
}

public sealed class ScreenshotStateMachine
{
    public ScreenshotState State { get; private set; } = ScreenshotState.Idle;

    public ScreenshotState Apply(ScreenshotCommand command)
    {
        var next = (State, command) switch
        {
            (ScreenshotState.Idle, ScreenshotCommand.StartRequested) => ScreenshotState.PreparingSelection,
            (ScreenshotState.PreparingSelection, ScreenshotCommand.SelectionPrepared) => ScreenshotState.SelectingRegion,
            (ScreenshotState.PreparingSelection, ScreenshotCommand.DirectRegionSelected) => ScreenshotState.Capturing,
            (ScreenshotState.PreparingSelection, ScreenshotCommand.PreparationFailed) => ScreenshotState.Error,
            (ScreenshotState.PreparingSelection, ScreenshotCommand.Discard) => ScreenshotState.Idle,
            (ScreenshotState.SelectingRegion, ScreenshotCommand.RegionSelected) => ScreenshotState.Capturing,
            (ScreenshotState.SelectingRegion, ScreenshotCommand.Discard) => ScreenshotState.Idle,
            (ScreenshotState.Capturing, ScreenshotCommand.Captured) => ScreenshotState.Editing,
            (ScreenshotState.Capturing, ScreenshotCommand.CaptureFailed) => ScreenshotState.Error,
            (ScreenshotState.Editing, ScreenshotCommand.Confirm) => ScreenshotState.Exporting,
            (ScreenshotState.Editing, ScreenshotCommand.Discard) => ScreenshotState.Idle,
            (ScreenshotState.Exporting, ScreenshotCommand.Exported) => ScreenshotState.Completed,
            (ScreenshotState.Exporting, ScreenshotCommand.OriginalSaved) => ScreenshotState.Editing,
            (ScreenshotState.Exporting, ScreenshotCommand.ExportFailed) => ScreenshotState.Editing,
            (ScreenshotState.Error, ScreenshotCommand.Reset) => ScreenshotState.Idle,
            (ScreenshotState.Completed, ScreenshotCommand.Reset) => ScreenshotState.Idle,
            _ => throw new InvalidOperationException($"Invalid screenshot transition: {State} + {command}.")
        };

        State = next;
        return next;
    }
}

public sealed class ActiveMediaTimer
{
    private readonly IMonotonicClock _clock;
    private long _recordingStartedAt;
    private long _accumulated;
    private bool _recording;

    public ActiveMediaTimer(IMonotonicClock clock) => _clock = clock;

    public TimeSpan Elapsed
    {
        get
        {
            var ticks = _accumulated + (_recording ? _clock.Timestamp - _recordingStartedAt : 0);
            return TimeSpan.FromSeconds((double)ticks / _clock.Frequency);
        }
    }

    public void Start()
    {
        if (_recording)
        {
            return;
        }

        _recordingStartedAt = _clock.Timestamp;
        _recording = true;
    }

    public void Pause()
    {
        if (!_recording)
        {
            return;
        }

        _accumulated += _clock.Timestamp - _recordingStartedAt;
        _recording = false;
    }

    public void Resume() => Start();

    public TimeSpan Stop()
    {
        Pause();
        return Elapsed;
    }

    public void Reset()
    {
        _recordingStartedAt = 0;
        _accumulated = 0;
        _recording = false;
    }
}
