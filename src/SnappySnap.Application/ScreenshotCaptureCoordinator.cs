using SnappySnap.Core;

namespace SnappySnap.Application;

public sealed class ScreenshotCaptureCoordinator
{
    private readonly IScreenshotCaptureService _captureService;
    private readonly ScreenshotStateMachine _stateMachine = new();
    private CapturePlan? _capturePlan;
    private CapturedImage? _image;
    private FrozenDesktopSnapshot? _frozenSnapshot;
    private string? _error;

    public ScreenshotCaptureCoordinator(IScreenshotCaptureService captureService) => _captureService = captureService;

    public ScreenshotSnapshot Snapshot => new(_stateMachine.State, _capturePlan, _image, _error);
    public bool HasFrozenSnapshot => _frozenSnapshot is not null;
    public string CaptureBackendName => (_captureService as IScreenshotCaptureDiagnostics)?.BackendName ?? _captureService.GetType().Name;
    public event EventHandler<ScreenshotSnapshot>? StateChanged;

    public void Begin()
    {
        if (_stateMachine.State != ScreenshotState.Idle)
        {
            throw new InvalidOperationException($"Cannot begin screenshot from {_stateMachine.State}.");
        }
        _stateMachine.Apply(ScreenshotCommand.StartRequested);
        _capturePlan = null;
        _image = null;
        _frozenSnapshot = null;
        _error = null;
        Publish();
    }

    public async Task<FrozenDesktopSnapshot> PrepareSelectionAsync(CapturePlan fullDesktopPlan, CancellationToken cancellationToken)
    {
        if (_stateMachine.State != ScreenshotState.PreparingSelection)
        {
            throw new InvalidOperationException($"Cannot prepare screenshot selection from {_stateMachine.State}.");
        }

        fullDesktopPlan.Validate();
        try
        {
            var captured = await Task.Run(() => _captureService.CaptureAsync(fullDesktopPlan, cancellationToken), cancellationToken).ConfigureAwait(false);
            if (_stateMachine.State != ScreenshotState.PreparingSelection)
            {
                throw new OperationCanceledException("Screenshot selection preparation was discarded.", cancellationToken);
            }

            _frozenSnapshot = new FrozenDesktopSnapshot(fullDesktopPlan.SelectedVirtualBounds, captured);
            _stateMachine.Apply(ScreenshotCommand.SelectionPrepared);
            Publish();
            return _frozenSnapshot;
        }
        catch (Exception)
        {
            _frozenSnapshot = null;
            if (_stateMachine.State == ScreenshotState.PreparingSelection)
            {
                _error = "Screenshot selection preparation failed.";
                _stateMachine.Apply(ScreenshotCommand.PreparationFailed);
                Publish();
            }
            throw;
        }
    }

    public async Task<CapturedImage> CaptureAsync(CapturePlan plan, CancellationToken cancellationToken)
    {
        if (_stateMachine.State != ScreenshotState.SelectingRegion)
        {
            throw new InvalidOperationException($"Cannot capture screenshot from {_stateMachine.State}.");
        }

        plan.Validate();
        var frozenSnapshot = _frozenSnapshot ?? throw new InvalidOperationException("Cannot capture a screenshot without a prepared desktop snapshot.");

        _capturePlan = plan;
        _stateMachine.Apply(ScreenshotCommand.RegionSelected);
        Publish();
        try
        {
            _image = await Task.Run(() => frozenSnapshot.Crop(plan.SelectedVirtualBounds, cancellationToken), cancellationToken).ConfigureAwait(false);
            _frozenSnapshot = null;
            _stateMachine.Apply(ScreenshotCommand.Captured);
            Publish();
            return _image;
        }
        catch (Exception)
        {
            _frozenSnapshot = null;
            _error = "Screenshot capture failed.";
            _stateMachine.Apply(ScreenshotCommand.CaptureFailed);
            Publish();
            throw;
        }
    }

    public async Task<CapturedImage> CaptureDirectAsync(CapturePlan plan, CancellationToken cancellationToken)
    {
        if (_stateMachine.State != ScreenshotState.PreparingSelection)
        {
            throw new InvalidOperationException($"Cannot capture directly from {_stateMachine.State}.");
        }

        try
        {
            plan.Validate();
            _capturePlan = plan;
            _stateMachine.Apply(ScreenshotCommand.DirectRegionSelected);
            Publish();
            _image = await Task.Run(() => _captureService.CaptureAsync(plan, cancellationToken), cancellationToken).ConfigureAwait(false);
            _stateMachine.Apply(ScreenshotCommand.Captured);
            Publish();
            return _image;
        }
        catch (Exception)
        {
            _error = "Screenshot capture failed.";
            _stateMachine.Apply(_stateMachine.State == ScreenshotState.Capturing
                ? ScreenshotCommand.CaptureFailed : ScreenshotCommand.PreparationFailed);
            Publish();
            throw;
        }
    }

    public void BeginExport()
    {
        if (_stateMachine.State != ScreenshotState.Editing)
        {
            throw new InvalidOperationException($"Cannot export screenshot from {_stateMachine.State}.");
        }
        _stateMachine.Apply(ScreenshotCommand.Confirm);
        _error = null;
        Publish();
    }

    public void ConfirmExported()
    {
        _stateMachine.Apply(ScreenshotCommand.Exported);
        Publish();
        Reset();
    }

    public void ConfirmOriginalSaved()
    {
        _stateMachine.Apply(ScreenshotCommand.OriginalSaved);
        Publish();
    }

    public void FailExport(Exception exception)
    {
        _error = exception.Message;
        _stateMachine.Apply(ScreenshotCommand.ExportFailed);
        Publish();
    }

    public void Cancel()
    {
        if (_stateMachine.State is ScreenshotState.PreparingSelection or ScreenshotState.SelectingRegion or ScreenshotState.Editing)
        {
            _stateMachine.Apply(ScreenshotCommand.Discard);
            Reset();
        }
        else if (_stateMachine.State == ScreenshotState.Error) Reset();
    }

    private void Reset()
    {
        if (_stateMachine.State is ScreenshotState.Completed or ScreenshotState.Error)
        {
            _stateMachine.Apply(ScreenshotCommand.Reset);
        }
        _capturePlan = null;
        _image = null;
        _frozenSnapshot = null;
        _error = null;
        Publish();
    }

    private void Publish() => StateChanged?.Invoke(this, Snapshot);
}
