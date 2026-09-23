using SnappySnap.Core;
using Xunit;

namespace SnappySnap.Application.Tests;

public sealed partial class RecordingSessionCoordinatorTests
{
    [Fact]
    public async Task Missing_system_audio_stays_unavailable_when_toggled_and_does_not_block_video()
    {
        var root = Path.Combine(Path.GetTempPath(), "SnappySnapTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var backend = new FakeBackend(systemAudioAvailable: false); var history = new FakeHistory();
            await using var coordinator = new SnappySnap.Application.RecordingSessionCoordinator(new FakeBackendFactory(backend), history, new FakeThumbnail(), new FakeRecovery(), new FakeLogger(), new SystemMonotonicClock());
            var plan = new CapturePlan(new(0, 0, 20, 20), 20, 20, [new("display", new(0, 0, 20, 20), new(0, 0, 20, 20))]);
            await coordinator.StartAsync(plan, Path.Combine(root, "captures"), Path.Combine(root, "temp"), AppSettings.Defaults(), default);
            Assert.False(coordinator.Snapshot.SystemAudioAvailable); Assert.False(coordinator.Snapshot.SystemAudioEnabled);
            await coordinator.SetSystemAudioEnabledAsync(true, default);
            Assert.False(coordinator.Snapshot.SystemAudioAvailable); Assert.False(coordinator.Snapshot.SystemAudioEnabled);
            Assert.Equal(RecordingState.Recording, coordinator.Snapshot.State);
            await coordinator.StopAsync(default); Assert.Single(history.Items);
        }
        finally { Directory.Delete(root, recursive: true); }
    }
}
