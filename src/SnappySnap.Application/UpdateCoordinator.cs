using SnappySnap.Core;
using SnappySnap.Localization;
using System.Security.Cryptography;
using System.Text.Json;
namespace SnappySnap.Application;

public enum UpdateState { Idle, Checking, Available, Downloading, Ready, Preparing, Error }
public sealed class UpdateCoordinator(IUpdateService service, IAppLogger logger) : IDisposable
{
    public UpdateState State { get; private set; } = UpdateState.Idle;
    private string _message = "Ready to check for updates";
    private object?[] _messageArgs = Array.Empty<object?>();
    public string Message => L.F(_message, _messageArgs);
    public DateTimeOffset? LastCheck { get; private set; }
    public UpdateOffer? Offer { get; private set; }
    public DownloadedUpdate? Download { get; private set; }
    public double Progress { get; private set; }
    public bool Busy => State is UpdateState.Checking or UpdateState.Downloading or UpdateState.Preparing;
    public event EventHandler? Changed;
    private CancellationTokenSource? _operation;
    private void Set(UpdateState state, string message, params object?[] args) { State = state; _message = message; _messageArgs = args; Changed?.Invoke(this, EventArgs.Empty); }
    public async Task<UpdateCheckOutcome> CheckAsync()
    {
        if (Busy) return new(UpdateCheckFailure.Skipped);
        _operation = new(); Set(UpdateState.Checking, "Checking for updates…");
        try
        {
            var checkedOffer = await service.CheckAsync(_operation.Token); LastCheck = DateTimeOffset.Now;
            if (checkedOffer is not null || Download is null) Offer = checkedOffer;
            if (Download?.Release != Offer?.Release) Download = null;
            Set(Download is not null ? UpdateState.Ready : Offer is null ? UpdateState.Idle : UpdateState.Available,
                Download is not null ? "Update verified and ready to install" : Offer is null ? "Up to date" : "Version {0} is available", Offer?.Release.Version);
            return new(UpdateCheckFailure.None);
        }
        catch (OperationCanceledException) { Set(UpdateState.Idle, "Check cancelled"); return new(UpdateCheckFailure.Cancelled); }
        catch (UpdateSourceException ex) { Fail(ex); return new(ex.Failure, ex.RetryAfterUtc); }
        catch (Exception ex) when (ex is CryptographicException or JsonException or InvalidDataException)
        { Fail(ex); return new(UpdateCheckFailure.InvalidRelease); }
        catch (Exception ex) { Fail(ex); return new(UpdateCheckFailure.Transient); }
        finally { _operation.Dispose(); _operation = null; }
    }
    public async Task DownloadAsync()
    {
        if (Busy || Offer is null) return;
        _operation = new(); Progress = 0; Set(UpdateState.Downloading, "Downloading and verifying the installer…");
        try
        {
            Download = await service.DownloadAsync(Offer, new Progress<double>(value => { Progress = value; Changed?.Invoke(this, EventArgs.Empty); }), _operation.Token);
            Set(UpdateState.Ready, "Update verified and ready to install");
        }
        catch (OperationCanceledException) { Set(UpdateState.Available, "Download cancelled"); }
        catch (Exception ex) { Fail(ex); }
        finally { _operation.Dispose(); _operation = null; }
    }
    public async Task<bool> PrepareAsync()
    {
        if (Busy || Download is null) return false;
        Set(UpdateState.Preparing, "Verifying before installation…");
        try { await service.VerifyAsync(Download, CancellationToken.None); return true; }
        catch (Exception ex) { Download = null; Fail(ex); return false; }
    }
    public void RestoreCached(VerifiedCachedUpdate update)
    {
        if (Busy || Download is not null) return;
        Offer = update.Offer; Download = update.Download;
        Set(UpdateState.Ready, "Update verified and ready to install");
    }
    public void PreparationCancelled() => Set(UpdateState.Ready, "Installation cancelled. The verified package is retained.");
    public void Fail(Exception ex) { logger.Error("Update failed.", ex); Set(UpdateState.Error, "Update failed. Check the connection and local log, then try again."); }
    public void Cancel() => _operation?.Cancel();
    public void Dispose() { Cancel(); (service as IDisposable)?.Dispose(); }
}
