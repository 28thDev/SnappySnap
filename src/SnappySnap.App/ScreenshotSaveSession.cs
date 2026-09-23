using System.Windows.Media.Imaging;
using SnappySnap.Application;
using SnappySnap.Core;
using SnappySnap.Editor;
using SnappySnap.Localization;

namespace SnappySnap.App;

// One editor session, shared by capture and Shelf. Failed indexing retains the chosen destination for retry.
internal sealed class ScreenshotSaveSession(
    EditorDocument document, ShelfService shelf, Func<string> captureRoot, VirtualPixelRect sourceBounds,
    Action<BitmapSource> copy, Action<string> notify, IAppLogger logger, string? originalPath = null)
{
    private string? _currentPath = originalPath is null ? null : Path.GetFullPath(originalPath);
    private bool _currentPathOwned = originalPath is not null;
    private EditorSaveRequest? _pendingRequest;
    private string? _pendingPath;
    private bool _pendingMayOverwrite;

    public Task SaveOriginalAsync(string format) => SaveCoreAsync(new(format), copyToClipboard: false);
    public Task SaveAsync(EditorSaveRequest request) => SaveCoreAsync(request, copyToClipboard: true);

    private async Task SaveCoreAsync(EditorSaveRequest request, bool copyToClipboard)
    {
        if (_pendingRequest != request || _pendingPath is null)
        {
            var replacingCurrent = request.Mode == EditorSaveMode.Replace && _currentPathOwned;
            var path = request.Mode switch
            {
                EditorSaveMode.Replace => _currentPath ?? await NewPathAsync(request.Format),
                EditorSaveMode.NewCopy => await NewPathAsync(request.Format),
                EditorSaveMode.File => Path.GetFullPath(request.DestinationPath ?? throw new InvalidOperationException("A file destination is required.")),
                _ => throw new InvalidOperationException("Unknown screenshot save mode.")
            };
            _pendingRequest = request;
            _pendingPath = path;
            _pendingMayOverwrite = replacingCurrent || request.Mode == EditorSaveMode.File;
            if (request.Mode == EditorSaveMode.Replace) _currentPath = path;
        }
        var destination = _pendingPath;
        // Replacement always follows the existing file's format, not the new-copy selector.
        var format = Path.GetExtension(destination).Equals(".png", StringComparison.OrdinalIgnoreCase) ? "Png" : "Jpg";
        var image = await EditorRenderer.RenderAsync(document, format == "Jpg", CancellationToken.None);

        try
        {
            await EditorRenderer.SaveBitmapAsync(image, destination, format, CancellationToken.None, _pendingMayOverwrite);
            if (request.Mode == EditorSaveMode.Replace) _currentPathOwned = true;
            _pendingMayOverwrite = true; // A history retry can rewrite the file created by this save.
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            if (!_pendingMayOverwrite && File.Exists(destination))
            {
                // Another writer claimed a generated name. Retry must choose a different one.
                if (request.Mode == EditorSaveMode.Replace)
                {
                    _currentPath = null;
                    _currentPathOwned = false;
                }
                _pendingRequest = null;
                _pendingPath = null;
            }
            throw new IOException(L.T("Could not write the image. Retry or choose another location with Save to file…"), ex);
        }

        HistoryItem item;
        try { item = await Task.Run(() => shelf.IndexScreenshotAsync(destination, image.PixelWidth, image.PixelHeight, sourceBounds, CancellationToken.None)); }
        catch (Exception ex) { throw new IOException(L.F("File saved at {0}. Could not add it to Shelf. Retry to finish.", destination), ex); }
        _currentPath = destination;
        _currentPathOwned = true;
        _pendingRequest = null;
        _pendingPath = null;
        _pendingMayOverwrite = false;
        _ = GenerateThumbnailAsync(item);
        if (!copyToClipboard) return;
        try { copy(image); }
        catch (Exception ex)
        {
            logger.Error("Image saved but clipboard was unavailable.", ex);
            notify(L.T("File saved. Clipboard is busy; copy the image from Shelf."));
        }
    }

    private Task<string> NewPathAsync(string format) => Task.Run(() =>
        MediaPathGenerator.Create(captureRoot(), MediaType.Screenshot, DateTimeOffset.UtcNow, imageFormat: format));

    private async Task GenerateThumbnailAsync(HistoryItem item)
    {
        try { await Task.Run(() => shelf.EnsureThumbnailAsync(item, CancellationToken.None)); }
        catch (Exception ex) { logger.Error("Saved screenshot thumbnail is unavailable.", ex); }
    }
}
