using SnappySnap.Core;
using SnappySnap.Infrastructure;
using Xunit;
namespace SnappySnap.IntegrationTests;
public sealed class VideoExportSafetyTests
{
    [Fact]
    public async Task ExportCannotOverwriteSourceEvenIfCallerPassesItsPathAsDestination()
    {
        var path = Path.Combine(Path.GetTempPath(), "SnappySnap-original-" + Guid.NewGuid() + ".mp4");
        var original = new byte[] { 1, 2, 3, 4 }; await File.WriteAllBytesAsync(path, original);
        try
        {
            await Assert.ThrowsAsync<ArgumentException>(() => new WindowsMediaVideoEditingService().ExportAsync(path, path,
                new VideoEditSession(TimeSpan.FromSeconds(3)).ExportTimeline(), null, CancellationToken.None));
            Assert.Equal(original, await File.ReadAllBytesAsync(path));
        }
        finally { File.Delete(path); }
    }
}
