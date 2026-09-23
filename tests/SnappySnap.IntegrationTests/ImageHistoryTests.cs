using System.Drawing;
using System.Drawing.Imaging;
using SnappySnap.Core;
using SnappySnap.History;
using SnappySnap.Infrastructure;
using Xunit;
namespace SnappySnap.IntegrationTests;
public sealed class ImageHistoryTests
{
    [Theory] [InlineData("png")] [InlineData("jpg")]
    public async Task BothImageFormatsAreIndexedAndProduceReadableThumbnails(string extension)
    {
        var root = Path.Combine(Path.GetTempPath(), "SnappySnapImageTests", Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        try
        {
            using var logger = new FileLogger(Path.Combine(root, "logs"));
            await using var repository = new SqliteHistoryRepository(Path.Combine(root, "history.db"), logger);
            var file = Path.Combine(root, "capture." + extension);
            using (var image = new Bitmap(40, 30)) { using var drawing = Graphics.FromImage(image); drawing.Clear(Color.White); image.Save(file, extension == "jpg" ? ImageFormat.Jpeg : ImageFormat.Png); }
            var item = await repository.AddAsync(new NewHistoryItem(MediaType.Screenshot, file, DateTimeOffset.UtcNow, 40, 30, null, null, new VirtualPixelRect(0, 0, 40, 30)), CancellationToken.None);
            var thumbnail = await new ThumbnailService(Path.Combine(root, "thumbs"), repository, logger).EnsureThumbnailAsync(item, CancellationToken.None);
            Assert.Equal(new FileInfo(file).Length, item.FileSizeBytes);
            Assert.Equal(file, (await repository.GetRecentAsync(10, CancellationToken.None)).Single().FilePath);
            Assert.Equal(ThumbnailState.Ready, thumbnail.State);
            using var decoded = new Bitmap(thumbnail.ThumbnailPath!); Assert.Equal(40, decoded.Width); Assert.Equal(30, decoded.Height);
        }
        finally { Directory.Delete(root, recursive: true); }
    }
}
