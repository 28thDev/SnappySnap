using SnappySnap.Core;
namespace SnappySnap.Application;

/// <summary>One preparation policy for tray exit and update: never dispose before editor consent and helper readiness.</summary>
public sealed class ExitPreparation
{
    public bool IsPreparing { get; private set; }
    public async Task<ExitPreparationResult> PrepareAsync(Func<bool> busy, Func<bool> closeEditors, Func<Task> prepareHost, Action<bool> blockNewWork)
    {
        if (IsPreparing || busy()) return ExitPreparationResult.Busy;
        IsPreparing = true; blockNewWork(true);
        try
        {
            if (!closeEditors()) { Reset(blockNewWork); return ExitPreparationResult.Cancelled; }
            await prepareHost();
            return ExitPreparationResult.Ready;
        }
        catch { Reset(blockNewWork); throw; }
    }
    public void Reset(Action<bool> blockNewWork) { IsPreparing = false; blockNewWork(false); }
}
