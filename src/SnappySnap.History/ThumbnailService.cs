using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.WindowsRuntime;
using SnappySnap.Core;
using Windows.Storage;
using Windows.Storage.FileProperties;

namespace SnappySnap.History;

public sealed class ThumbnailService : IThumbnailService
{
    private readonly string _cacheDirectory;
    private readonly IHistoryRepository _repository;
    private readonly IAppLogger _logger;

    public ThumbnailService(string cacheDirectory, IHistoryRepository repository, IAppLogger logger)
    {
        _cacheDirectory = cacheDirectory;
        _repository = repository;
        _logger = logger;
        Directory.CreateDirectory(cacheDirectory);
    }

    public async Task<ThumbnailResult> EnsureThumbnailAsync(HistoryItem item, CancellationToken cancellationToken)
    {
        var path = item.ThumbnailPath ?? Path.Combine(_cacheDirectory, $"{item.Id:N}-{item.CreatedAtUtc.UtcTicks}.jpg");
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!File.Exists(path))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                if (item.MediaType == MediaType.Screenshot)
                {
                    // A thumbnail reader must not prevent atomic replacement by Save As.
                    using var input = new FileStream(item.FilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                    using var source = new Bitmap(input);
                    using var thumbnail = CreateThumbnail(source, 320, 220);
                    thumbnail.Save(path, ImageFormat.Jpeg);
                }
                else
                {
                    await CreateVideoThumbnailAsync(item, path, cancellationToken).ConfigureAwait(false);
                }
            }

            await _repository.UpdateThumbnailAsync(item.Id, item.CreatedAtUtc, path, ThumbnailState.Ready, cancellationToken).ConfigureAwait(false);
            return new ThumbnailResult(path, ThumbnailState.Ready);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or ExternalException)
        {
            _logger.Error("Thumbnail generation failed.", ex, new Dictionary<string, object?> { ["captureId"] = item.Id, ["filePath"] = item.FilePath });
            await _repository.UpdateThumbnailAsync(item.Id, item.CreatedAtUtc, null, ThumbnailState.Failed, cancellationToken).ConfigureAwait(false);
            return new ThumbnailResult(null, ThumbnailState.Failed, ex.Message);
        }
    }

    private static Bitmap CreateThumbnail(Bitmap source, int maxWidth, int maxHeight)
    {
        var scale = Math.Min((double)maxWidth / source.Width, (double)maxHeight / source.Height);
        scale = Math.Min(scale, 1d);
        var width = Math.Max(1, (int)Math.Round(source.Width * scale));
        var height = Math.Max(1, (int)Math.Round(source.Height * scale));
        var target = new Bitmap(width, height, PixelFormat.Format24bppRgb);
        using var graphics = Graphics.FromImage(target);
        graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
        graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
        graphics.DrawImage(source, new Rectangle(0, 0, width, height));
        return target;
    }

    private static Bitmap CreateVideoPlaceholder(HistoryItem item, int width, int height)
    {
        var bitmap = new Bitmap(width, height, PixelFormat.Format24bppRgb);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.Clear(Color.FromArgb(31, 36, 46));
        using var brush = new SolidBrush(Color.FromArgb(104, 211, 145));
        var triangle = new[] { new Point(width / 2 - 18, height / 2 - 24), new Point(width / 2 - 18, height / 2 + 24), new Point(width / 2 + 26, height / 2) };
        graphics.FillPolygon(brush, triangle);
        using var textBrush = new SolidBrush(Color.WhiteSmoke);
        using var font = new Font("Segoe UI", 9, FontStyle.Regular);
        var text = item.Duration.HasValue ? item.Duration.Value.ToString(@"mm\:ss", System.Globalization.CultureInfo.InvariantCulture) : "Video";
        graphics.DrawString(text, font, textBrush, 8, 8);
        return bitmap;
    }

    private async Task CreateVideoThumbnailAsync(HistoryItem item, string path, CancellationToken cancellationToken)
    {
        try
        {
            var source = await StorageFile.GetFileFromPathAsync(item.FilePath).AsTask(cancellationToken).ConfigureAwait(false);
            using var thumbnail = await source.GetThumbnailAsync(ThumbnailMode.VideosView, 320, ThumbnailOptions.ResizeThumbnail).AsTask(cancellationToken).ConfigureAwait(false);
            if (thumbnail is null)
            {
                throw new InvalidOperationException("Windows did not provide a video thumbnail.");
            }

            using var stream = thumbnail.AsStreamForRead();
            using var sourceImage = Image.FromStream(stream);
            using var bitmap = new Bitmap(sourceImage);
            using var resized = CreateThumbnail(bitmap, 320, 220);
            resized.Save(path, ImageFormat.Jpeg);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or ExternalException or InvalidOperationException)
        {
            _logger.Warn("Windows video thumbnail was unavailable; using a local placeholder.", new Dictionary<string, object?> { ["captureId"] = item.Id, ["error"] = ex.Message });
            using var placeholder = CreateVideoPlaceholder(item, 320, 180);
            placeholder.Save(path, ImageFormat.Jpeg);
        }
    }
}
