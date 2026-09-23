using SnappySnap.Core;
using Xunit;
using R = SnappySnap.Core.RecordingState;
using C = SnappySnap.Core.RecordingCommand;
using S = SnappySnap.Core.ScreenshotState;
using A = SnappySnap.Core.ScreenshotCommand;

namespace SnappySnap.Core.Tests;

public sealed class StateTransitionMatrixTests
{
    [Fact]
    public void Recording_accepts_documented_transitions_and_rejects_every_other_state_command_pair()
    {
        var transitions = new Dictionary<(R, C), R>
        {
            [(R.Idle, C.StartRequested)] = R.SelectingRegion,
            [(R.SelectingRegion, C.RegionSelected)] = R.Starting,
            [(R.SelectingRegion, C.Cancel)] = R.Idle,
            [(R.Starting, C.BackendStarted)] = R.Recording,
            [(R.Starting, C.StartFailed)] = R.Error,
            [(R.Recording, C.PauseRequested)] = R.Pausing,
            [(R.Pausing, C.Paused)] = R.Paused,
            [(R.Paused, C.ResumeRequested)] = R.Resuming,
            [(R.Resuming, C.Resumed)] = R.Recording,
            [(R.Recording, C.StopRequested)] = R.Finalizing,
            [(R.Paused, C.StopRequested)] = R.Finalizing,
            [(R.Finalizing, C.Finalized)] = R.Completed,
            [(R.Finalizing, C.FinalizeFailed)] = R.RecoveryRequired,
            [(R.Recording, C.BackendFailed)] = R.RecoveryRequired,
            [(R.Paused, C.BackendFailed)] = R.RecoveryRequired,
            [(R.Pausing, C.BackendFailed)] = R.RecoveryRequired,
            [(R.Resuming, C.BackendFailed)] = R.RecoveryRequired,
            [(R.Error, C.Reset)] = R.Idle,
            [(R.RecoveryRequired, C.Reset)] = R.Idle,
            [(R.Completed, C.Reset)] = R.Idle
        };
        var recording = new[] { C.StartRequested, C.RegionSelected, C.BackendStarted };
        var paths = new Dictionary<R, C[]>
        {
            [R.Idle] = [], [R.SelectingRegion] = [C.StartRequested],
            [R.Starting] = [C.StartRequested, C.RegionSelected], [R.Recording] = recording,
            [R.Pausing] = [..recording, C.PauseRequested], [R.Paused] = [..recording, C.PauseRequested, C.Paused],
            [R.Resuming] = [..recording, C.PauseRequested, C.Paused, C.ResumeRequested],
            [R.Finalizing] = [..recording, C.StopRequested], [R.Completed] = [..recording, C.StopRequested, C.Finalized],
            [R.Error] = [C.StartRequested, C.RegionSelected, C.StartFailed], [R.RecoveryRequired] = [..recording, C.BackendFailed]
        };
        foreach (var state in Enum.GetValues<R>())
        foreach (var command in Enum.GetValues<C>())
        {
            var machine = new RecordingStateMachine();
            foreach (var step in paths[state]) machine.Apply(step);
            if (transitions.TryGetValue((state, command), out var expected)) Assert.Equal(expected, machine.Apply(command));
            else { Assert.Throws<InvalidOperationException>(() => machine.Apply(command)); Assert.Equal(state, machine.State); }
        }
    }

    [Fact]
    public void Screenshot_accepts_documented_transitions_and_rejects_every_other_state_command_pair()
    {
        var transitions = new Dictionary<(S, A), S>
        {
            [(S.Idle, A.StartRequested)] = S.PreparingSelection,
            [(S.PreparingSelection, A.SelectionPrepared)] = S.SelectingRegion,
            [(S.PreparingSelection, A.DirectRegionSelected)] = S.Capturing,
            [(S.PreparingSelection, A.PreparationFailed)] = S.Error,
            [(S.PreparingSelection, A.Discard)] = S.Idle,
            [(S.SelectingRegion, A.RegionSelected)] = S.Capturing,
            [(S.SelectingRegion, A.Discard)] = S.Idle,
            [(S.Capturing, A.Captured)] = S.Editing,
            [(S.Capturing, A.CaptureFailed)] = S.Error,
            [(S.Editing, A.Confirm)] = S.Exporting,
            [(S.Editing, A.Discard)] = S.Idle,
            [(S.Exporting, A.Exported)] = S.Completed,
            [(S.Exporting, A.OriginalSaved)] = S.Editing,
            [(S.Exporting, A.ExportFailed)] = S.Editing,
            [(S.Error, A.Reset)] = S.Idle,
            [(S.Completed, A.Reset)] = S.Idle
        };
        var editing = new[] { A.StartRequested, A.SelectionPrepared, A.RegionSelected, A.Captured };
        var paths = new Dictionary<S, A[]>
        {
            [S.Idle] = [], [S.PreparingSelection] = [A.StartRequested],
            [S.SelectingRegion] = [A.StartRequested, A.SelectionPrepared],
            [S.Capturing] = [A.StartRequested, A.SelectionPrepared, A.RegionSelected],
            [S.Editing] = editing, [S.Exporting] = [..editing, A.Confirm], [S.Completed] = [..editing, A.Confirm, A.Exported],
            [S.Error] = [A.StartRequested, A.PreparationFailed]
        };
        foreach (var state in Enum.GetValues<S>())
        foreach (var command in Enum.GetValues<A>())
        {
            var machine = new ScreenshotStateMachine();
            foreach (var step in paths[state]) machine.Apply(step);
            if (transitions.TryGetValue((state, command), out var expected)) Assert.Equal(expected, machine.Apply(command));
            else { Assert.Throws<InvalidOperationException>(() => machine.Apply(command)); Assert.Equal(state, machine.State); }
        }
    }

    [Fact]
    public void Timer_duplicate_events_and_reset_do_not_accumulate_paused_time()
    {
        var clock = new Clock(); var timer = new ActiveMediaTimer(clock);
        timer.Start(); clock.Timestamp = 3_000; timer.Start();
        timer.Pause(); clock.Timestamp = 50_000; timer.Pause();
        Assert.Equal(TimeSpan.FromSeconds(3), timer.Stop());
        timer.Resume(); clock.Timestamp = 52_000;
        Assert.Equal(TimeSpan.FromSeconds(5), timer.Stop());
        timer.Reset(); Assert.Equal(TimeSpan.Zero, timer.Elapsed);
        timer.Start(); clock.Timestamp = 53_000; Assert.Equal(TimeSpan.FromSeconds(1), timer.Elapsed);
    }
    private sealed class Clock : IMonotonicClock { public long Timestamp { get; set; } public long Frequency => 1000; }
}
