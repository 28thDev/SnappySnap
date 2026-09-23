using SnappySnap.Core;

namespace SnappySnap.Application;

public sealed class ShelfService
{
    private readonly IHistoryRepository _repository;
    private readonly IThumbnailService _thumbnails;

    public ShelfService(IHistoryRepository repository, IThumbnailService thumbnails)
    {
        _repository = repository;
        _thumbnails = thumbnails;
    }

    public async Task<IReadOnlyList<HistoryItem>> LoadRecentAsync(int count, CancellationToken cancellationToken) =>
        Chronological(await _repository.GetRecentAsync(count, cancellationToken).ConfigureAwait(false));

    private static HistoryItem[] Chronological(IEnumerable<HistoryItem> items) =>
        items.OrderBy(item => item.CreatedAtUtc).ThenBy(item => item.Id.ToString(), StringComparer.Ordinal).ToArray();

    public async Task<IReadOnlyList<HistoryItem>> LoadOlderAsync(IReadOnlyList<HistoryItem> loaded, int count, CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(count);
        var known = loaded.Select(item => item.Id).ToHashSet();
        // Include the boundary timestamp, then exclude known IDs: equal timestamps must not skip captures.
        DateTimeOffset? before = loaded.Count == 0 ? null : loaded.Min(item => item.CreatedAtUtc).AddTicks(1);
        var page = new List<HistoryItem>();
        await foreach (var item in _repository.EnumerateOlderAsync(before, cancellationToken).ConfigureAwait(false))
        {
            if (known.Contains(item.Id)) continue;
            page.Add(item);
            if (page.Count == count) break;
        }
        return Chronological(page);
    }

    public async Task EnsureThumbnailsAsync(IEnumerable<HistoryItem> items, CancellationToken cancellationToken)
    {
        foreach (var item in items)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await _thumbnails.EnsureThumbnailAsync(item, cancellationToken).ConfigureAwait(false);
        }
    }

    public Task<ThumbnailResult> EnsureThumbnailAsync(HistoryItem item, CancellationToken cancellationToken) => _thumbnails.EnsureThumbnailAsync(item, cancellationToken);

    public Task<HistoryItem> AddScreenshotAsync(string path, int width, int height, CancellationToken cancellationToken) =>
        _repository.AddAsync(new NewHistoryItem(MediaType.Screenshot, path, DateTimeOffset.UtcNow, width, height, null, null, new VirtualPixelRect(0, 0, width, height)), cancellationToken);

    public async Task<HistoryItem> IndexScreenshotAsync(string path, int width, int height, VirtualPixelRect sourceBounds, CancellationToken cancellationToken)
    {
        path = Path.GetFullPath(path);
        var existing = await _repository.FindByPathAsync(path, cancellationToken).ConfigureAwait(false);
        var created = DateTimeOffset.UtcNow;
        if (existing is not null && created <= existing.CreatedAtUtc) created = existing.CreatedAtUtc.AddTicks(1);
        var item = new NewHistoryItem(MediaType.Screenshot, path, created, width, height, null, null, sourceBounds);
        return existing is { DeletedAtUtc: null }
            ? await _repository.UpdateScreenshotAsync(existing.Id, item, cancellationToken).ConfigureAwait(false)
            : await _repository.AddAsync(item, cancellationToken).ConfigureAwait(false);
    }

    public async Task<HistoryItem> AddVideoAsync(string path, int width, int height, TimeSpan duration, CancellationToken cancellationToken)
    {
        path = Path.GetFullPath(path);
        // Export retries can arrive after indexing succeeded but its UI callback failed.
        var existing = await _repository.FindByPathAsync(path, cancellationToken).ConfigureAwait(false);
        if (existing is { DeletedAtUtc: null }) return existing;
        return await _repository.AddAsync(new NewHistoryItem(MediaType.Video, path, DateTimeOffset.UtcNow, width, height, duration, null, new VirtualPixelRect(0, 0, width, height)), cancellationToken).ConfigureAwait(false);
    }

    public Task DeleteAsync(Guid id, CancellationToken cancellationToken) => _repository.DeleteAsync(id, cancellationToken);

    public async Task DeleteManyAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken)
    {
        foreach (var id in ids.Distinct())
        {
            cancellationToken.ThrowIfCancellationRequested();
            await _repository.DeleteAsync(id, cancellationToken).ConfigureAwait(false);
        }
    }
}
