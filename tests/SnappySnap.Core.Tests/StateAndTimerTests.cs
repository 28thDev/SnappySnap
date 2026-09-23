using SnappySnap.Core;
using Xunit;

namespace SnappySnap.Core.Tests;

public sealed class StateAndTimerTests
{
    [Fact]
    public void Recording_state_machine_rejects_stop_before_recording()
    {
        var machine = new RecordingStateMachine();

        Assert.Throws<InvalidOperationException>(() => machine.Apply(RecordingCommand.StopRequested));
        Assert.Equal(RecordingState.Idle, machine.State);
    }

    [Fact]
    public void Recording_state_machine_supports_pause_stop_and_reset()
    {
        var machine = new RecordingStateMachine();

        machine.Apply(RecordingCommand.StartRequested);
        machine.Apply(RecordingCommand.RegionSelected);
        machine.Apply(RecordingCommand.BackendStarted);
        machine.Apply(RecordingCommand.PauseRequested);
        machine.Apply(RecordingCommand.Paused);
        machine.Apply(RecordingCommand.StopRequested);
        machine.Apply(RecordingCommand.Finalized);
        machine.Apply(RecordingCommand.Reset);

        Assert.Equal(RecordingState.Idle, machine.State);
    }

    [Fact]
    public void Active_timer_excludes_paused_interval()
    {
        var clock = new FakeClock();
        var timer = new ActiveMediaTimer(clock);

        timer.Start();
        clock.Advance(TimeSpan.FromSeconds(10));
        timer.Pause();
        clock.Advance(TimeSpan.FromSeconds(20));
        timer.Resume();
        clock.Advance(TimeSpan.FromSeconds(10));

        Assert.Equal(TimeSpan.FromSeconds(20), timer.Elapsed);
    }

    [Fact]
    public void Path_generator_adds_collision_suffix_without_overwriting()
    {
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var now = new DateTimeOffset(2026, 9, 20, 12, 34, 56, TimeSpan.FromHours(3));

        var first = MediaPathGenerator.Create("C:\\captures", MediaType.Screenshot, now, p => !paths.Add(p));
        var second = MediaPathGenerator.Create("C:\\captures", MediaType.Screenshot, now, p => !paths.Add(p));
        var expectedStem = now.ToLocalTime().ToString("yyyy-MM-dd_HH-mm-ss", System.Globalization.CultureInfo.InvariantCulture);

        Assert.EndsWith($"{expectedStem}.png", first);
        Assert.EndsWith($"{expectedStem}-2.png", second);
        Assert.NotEqual(first, second);
    }

    private sealed class FakeClock : IMonotonicClock
    {
        public long Timestamp { get; private set; }
        public long Frequency { get; } = TimeSpan.TicksPerSecond;

        public void Advance(TimeSpan duration) => Timestamp += duration.Ticks;
    }
}
