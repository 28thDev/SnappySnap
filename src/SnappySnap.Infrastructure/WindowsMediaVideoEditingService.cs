using System.Runtime.InteropServices.WindowsRuntime;
using SnappySnap.Core;
using Windows.Media.Editing;
using Windows.Media.MediaProperties;
using Windows.Storage;

namespace SnappySnap.Infrastructure;

/// <summary>
/// Uses the Windows Media Foundation-backed WinRT composition API for local MP4
/// editing. Each kept range is a separate MediaClip, so middle removals do not
/// require a keyframe-only copy operation and the original file is never opened
/// for writing.
/// </summary>
public sealed class WindowsMediaVideoEditingService : IVideoEditingService
{
    public async Task<VideoPreviewData> LoadPreviewAsync(string sourcePath, CancellationToken cancellationToken)
    {
        var file = await StorageFile.GetFileFromPathAsync(Path.GetFullPath(sourcePath)).AsTask(cancellationToken).ConfigureAwait(false);
        var clip = await MediaClip.CreateFromFileAsync(file).AsTask(cancellationToken).ConfigureAwait(false);
        var composition = new MediaComposition(); composition.Clips.Add(clip);
        var properties = clip.GetVideoEncodingProperties();
        async Task<byte[]> Frame(TimeSpan time, int width, int height)
        {
            var size = FitDimensions(properties.Width, properties.Height, width, height);
            using var thumbnail = await composition.GetThumbnailAsync(time, size.Width, size.Height, VideoFramePrecision.NearestFrame).AsTask(cancellationToken).ConfigureAwait(false);
            using var stream = thumbnail.AsStreamForRead(); using var buffer = new MemoryStream();
            await stream.CopyToAsync(buffer, cancellationToken).ConfigureAwait(false); return buffer.ToArray();
        }
        var thumbnails = new List<byte[]>();
        for (var i = 0; i < 10; i++) thumbnails.Add(await Frame(TimeSpan.FromSeconds(clip.OriginalDuration.TotalSeconds * i / 10), 160, 90).ConfigureAwait(false));
        return new VideoPreviewData(clip.OriginalDuration, thumbnails, await Frame(TimeSpan.Zero, 960, 540).ConfigureAwait(false));
    }
    internal static (int Width, int Height) FitDimensions(uint width, uint height, int maximumWidth, int maximumHeight)
    {
        if (width == 0 || height == 0) throw new InvalidOperationException("The source has no video dimensions.");
        var scale = Math.Min(1, Math.Min(maximumWidth / (double)width, maximumHeight / (double)height));
        return (Math.Max(2, (int)(width * scale) / 2 * 2), Math.Max(2, (int)(height * scale) / 2 * 2));
    }
    public async Task<VideoEditResult> ExportAsync(
        string sourcePath,
        string destinationPath,
        VideoEditTimeline timeline,
        IProgress<double>? progress,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationPath);
        var keepRanges = VideoEditSession.KeepRanges(timeline);
        if (keepRanges.Count == 0) throw new InvalidOperationException("The edit removes the entire video.");
        if (string.Equals(Path.GetFullPath(sourcePath), Path.GetFullPath(destinationPath), StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Export must create a new file; the original recording is retained.", nameof(destinationPath));
        if (!File.Exists(sourcePath))
        {
            throw new FileNotFoundException("The source video was not found.", sourcePath);
        }

        var source = await StorageFile.GetFileFromPathAsync(Path.GetFullPath(sourcePath)).AsTask(cancellationToken).ConfigureAwait(false);
        var destinationDirectory = Path.GetDirectoryName(Path.GetFullPath(destinationPath))
            ?? throw new InvalidOperationException("The destination directory is missing.");
        Directory.CreateDirectory(destinationDirectory);
        var folder = await StorageFolder.GetFolderFromPathAsync(destinationDirectory).AsTask(cancellationToken).ConfigureAwait(false);
        var destinationName = Path.GetFileName(destinationPath);
        var destination = await folder.CreateFileAsync(destinationName, CreationCollisionOption.FailIfExists).AsTask(cancellationToken).ConfigureAwait(false);

        var composition = new MediaComposition();
        try
        {
            foreach (var range in keepRanges)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var clip = await MediaClip.CreateFromFileAsync(source).AsTask(cancellationToken).ConfigureAwait(false);
                clip.TrimTimeFromStart = range.Start;
                clip.TrimTimeFromEnd = timeline.SourceDuration - range.End;
                composition.Clips.Add(clip);
            }

            // Use the file's concrete profile; the composition's automatic profile can
            // silently raise AAC from 128 to 192 kbit/s even when its reported bitrate is 128.
            var encoding = await MediaEncodingProfile.CreateFromFileAsync(source).AsTask(cancellationToken).ConfigureAwait(false);
            var operation = composition.RenderToFileAsync(destination, MediaTrimmingPreference.Precise, encoding);
            operation.Progress += (_, value) => progress?.Report(Math.Clamp(value / 100d, 0d, 1d));
            var failure = await operation.AsTask(cancellationToken).ConfigureAwait(false);
            if (failure != Windows.Media.Transcoding.TranscodeFailureReason.None) throw new InvalidOperationException("Video render failed: " + failure);
            progress?.Report(1d);
            return new VideoEditResult(
                destinationPath,
                keepRanges.Aggregate(TimeSpan.Zero, (sum, range) => sum + range.Duration),
                "Windows MediaComposition / Media Foundation precise render");
        }
        catch
        {
            TryDelete(destinationPath);
            throw;
        }
        finally
        {
            composition.Clips.Clear();
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch
        {
            // The original remains safe; a failed cleanup is reported by the caller's log boundary.
        }
    }
}
