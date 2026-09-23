using SnappySnap.Core;
using SnappySnap.Updates;
using Xunit;

namespace SnappySnap.IntegrationTests;

public sealed class UpdateScheduleTests : IDisposable
{
    private sealed class Clock(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;
        public override DateTimeOffset GetUtcNow() => Now;
    }
    private sealed class Logger : IAppLogger
    {
        public void Info(string message, IReadOnlyDictionary<string, object?>? properties = null) { }
        public void Warn(string message, IReadOnlyDictionary<string, object?>? properties = null) { }
        public void Error(string message, Exception? exception = null, IReadOnlyDictionary<string, object?>? properties = null) { }
    }
    private readonly string _root = Path.Combine(Path.GetTempPath(), "SnappySnap-schedule-test-" + Guid.NewGuid().ToString("N"));
    private readonly Clock _clock = new(new DateTimeOffset(2026, 9, 22, 0, 0, 0, TimeSpan.Zero));
    private string PathName => Path.Combine(_root, "schedule.json");
    private UpdateCheckSchedule Schedule() => new(PathName, new Logger(), _clock);

    [Fact] public async Task SuccessfulCheckPersistsAcrossRestart()
    {
        using (var schedule = Schedule())
        {
            await schedule.LoadAsync(default);
            Assert.True(await schedule.BeginAutomaticAsync(default));
            await schedule.RecordAsync(new(UpdateCheckFailure.None), default);
        }
        using (var restarted = Schedule())
        {
            await restarted.LoadAsync(default);
            Assert.False(await restarted.BeginAutomaticAsync(default));
            _clock.Now = _clock.Now.AddDays(1);
            Assert.True(await restarted.BeginAutomaticAsync(default));
        }
    }

    [Fact] public async Task NetworkFailuresBackOffOneSixThenTwentyFourHours()
    {
        using var schedule = Schedule(); await schedule.LoadAsync(default);
        foreach (var hours in new[] { 1, 6, 24 })
        {
            await schedule.RecordAsync(new(UpdateCheckFailure.Transient), default);
            _clock.Now = _clock.Now.AddHours(hours).AddSeconds(-1);
            Assert.False(await schedule.BeginAutomaticAsync(default));
            _clock.Now = _clock.Now.AddSeconds(1);
            Assert.True(await schedule.BeginAutomaticAsync(default));
        }
    }

    [Fact] public async Task RateLimitRespectsServerTime()
    {
        using var schedule = Schedule(); await schedule.LoadAsync(default);
        var retry = _clock.Now.AddHours(5);
        await schedule.RecordAsync(new(UpdateCheckFailure.RateLimited, retry), default);
        _clock.Now = retry.AddSeconds(-1);
        Assert.False(await schedule.BeginAutomaticAsync(default));
        _clock.Now = retry;
        Assert.True(await schedule.BeginAutomaticAsync(default));
    }

    [Fact] public async Task CorruptStateDelaysAutomaticRetry()
    {
        Directory.CreateDirectory(_root);
        await File.WriteAllTextAsync(PathName, "invalid-json");
        using var schedule = Schedule(); await schedule.LoadAsync(default);
        Assert.False(await schedule.BeginAutomaticAsync(default));
    }

    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }
}
