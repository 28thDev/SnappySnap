using SnappySnap.Application;
using SnappySnap.Core;
using Xunit;
namespace SnappySnap.Application.Tests;
public sealed class UpdateCoordinatorTests
{
    private sealed class Service : IUpdateService
    {
        public int Checks;
        public bool Fail;
        public TaskCompletionSource<UpdateOffer?> Pending = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<UpdateOffer?> CheckAsync(CancellationToken ct) { Checks++; return Pending.Task.WaitAsync(ct); }
        public Task<DownloadedUpdate> DownloadAsync(UpdateOffer offer, IProgress<double>? progress, CancellationToken ct) => Task.FromResult(new DownloadedUpdate(offer.Release, "cache"));
        public Task VerifyAsync(DownloadedUpdate update, CancellationToken ct) => Fail ? Task.FromException(new IOException("damaged")) : Task.CompletedTask;
    }
    private sealed class Logger : IAppLogger
    {
        public void Info(string message, IReadOnlyDictionary<string, object?>? properties = null) { }
        public void Warn(string message, IReadOnlyDictionary<string, object?>? properties = null) { }
        public void Error(string message, Exception? exception = null, IReadOnlyDictionary<string, object?>? properties = null) { }
    }
    [Fact] public async Task DuplicateChecksDoNotStart()
    {
        var service = new Service(); var c = new UpdateCoordinator(service, new Logger());
        var first = c.CheckAsync(); await c.CheckAsync(); Assert.Equal(1, service.Checks);
        service.Pending.SetResult(null); await first;
        Assert.Equal(UpdateState.Idle, c.State); Assert.NotNull(c.LastCheck);
    }
    [Fact] public async Task ReadyPackageReverifiedAndVerificationFailurePreventsPreparation()
    {
        var service = new Service(); var c = new UpdateCoordinator(service, new Logger());
        service.Pending.SetResult(new UpdateOffer(new("0.4.1", DateTimeOffset.UtcNow, "notes", "setup", 1, "hash", "win-x64", 22000), [], []));
        await c.CheckAsync(); Assert.Equal(UpdateState.Available, c.State); Assert.Null(c.Download);
        await c.DownloadAsync(); Assert.Equal(UpdateState.Ready, c.State);
        var cached = c.Download; await c.CheckAsync(); Assert.Same(cached, c.Download); Assert.Equal(UpdateState.Ready, c.State);
        service.Pending = new(TaskCreationOptions.RunContinuationsAsynchronously);
        service.Pending.SetResult(null);
        await c.CheckAsync(); Assert.Same(cached, c.Download); Assert.Equal(UpdateState.Ready, c.State);
        Assert.True(await c.PrepareAsync()); c.PreparationCancelled(); Assert.Equal(UpdateState.Ready, c.State);
        service.Fail = true; Assert.False(await c.PrepareAsync()); Assert.Equal(UpdateState.Error, c.State); Assert.Null(c.Download); Assert.NotNull(c.Offer);
    }
}
