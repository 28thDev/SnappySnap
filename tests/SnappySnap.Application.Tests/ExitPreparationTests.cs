using SnappySnap.Application;
using SnappySnap.Core;
using Xunit;
namespace SnappySnap.Application.Tests;
public sealed class ExitPreparationTests
{
    [Fact] public async Task BusyCaptureNeverClosesEditorsOrStartsHelper()
    {
        var policy = new ExitPreparation(); var calls = 0;
        Assert.Equal(ExitPreparationResult.Busy, await policy.PrepareAsync(() => true, () => { calls++; return true; }, () => { calls++; return Task.CompletedTask; }, _ => calls++));
        Assert.Equal(0, calls);
    }
    [Fact] public async Task EditorRefusalRestoresWorkAndDoesNotStartHelper()
    {
        var policy = new ExitPreparation(); var blocked = false; var started = false;
        Assert.Equal(ExitPreparationResult.Cancelled, await policy.PrepareAsync(() => false, () => false, () => { started = true; return Task.CompletedTask; }, value => blocked = value));
        Assert.False(blocked); Assert.False(started); Assert.False(policy.IsPreparing);
    }
    [Fact] public async Task HelperFailureRestoresWorkAndPropagatesError()
    {
        var policy = new ExitPreparation(); var blocked = false;
        await Assert.ThrowsAsync<IOException>(() => policy.PrepareAsync(() => false, () => true, () => throw new IOException("helper failed"), value => blocked = value));
        Assert.False(blocked); Assert.False(policy.IsPreparing);
    }
    [Fact] public async Task MustWaitForHelperReadinessWithNewWorkBlocked()
    {
        var policy = new ExitPreparation(); var blocked = false;
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var preparation = policy.PrepareAsync(() => false, () => true, () => ready.Task, value => blocked = value);
        Assert.True(blocked); Assert.False(preparation.IsCompleted);
        Assert.Equal(ExitPreparationResult.Busy, await policy.PrepareAsync(() => false, () => true, () => Task.CompletedTask, _ => { }));
        ready.SetResult(); Assert.Equal(ExitPreparationResult.Ready, await preparation); Assert.True(blocked);
        policy.Reset(value => blocked = value); Assert.False(blocked);
    }
}
