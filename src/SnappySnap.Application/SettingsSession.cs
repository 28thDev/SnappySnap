using System.Text.Json;
using SnappySnap.Core;

namespace SnappySnap.Application;

// Owns preference updates and serializes writes with the Settings window's older draft.
public sealed class SettingsSession(AppSettings current, ISettingsStore store) : ISettingsStore, IDisposable
{
    private readonly SemaphoreSlim _writes = new(1, 1);
    private readonly object _sync = new();
    private AppSettings _current = current;

    public void SetShelfThumbnailHeight(double height)
    {
        lock (_sync) _current.Shelf = ShelfPreferences.Normalize(_current.Shelf with { ThumbnailHeight = height });
    }

    public void SetShelfPlacement(bool compact, ShelfPlacement placement)
    {
        lock (_sync) _current.Shelf = compact ? _current.Shelf with { Compact = placement } : _current.Shelf with { History = placement };
    }

    public void SetStyle(string tool, AnnotationStyleSettings style)
    {
        lock (_sync)
        {
            if (!_current.Editor.Styles.ContainsKey(tool)) throw new ArgumentException("Unknown annotation tool.", nameof(tool));
            var editor = _current.Editor.Copy(); editor.Styles[tool] = style;
            _current.Editor = EditorSettings.Normalize(editor);
        }
    }

    public Task<AppSettings> LoadAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_sync) return Task.FromResult(Clone(_current));
    }

    public async Task SaveAsync(AppSettings settings, CancellationToken cancellationToken)
    {
        await _writes.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            AppSettings snapshot;
            lock (_sync) { snapshot = Clone(settings); snapshot.Editor = _current.Editor.Copy(); snapshot.Shelf = _current.Shelf; }
            await store.SaveAsync(snapshot, cancellationToken).ConfigureAwait(false);
            lock (_sync) { settings.Editor = _current.Editor.Copy(); settings.Shelf = _current.Shelf; _current = settings; }
        }
        finally { _writes.Release(); }
    }

    public async Task FlushPreferencesAsync(CancellationToken cancellationToken)
    {
        await _writes.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            AppSettings snapshot;
            lock (_sync) snapshot = Clone(_current);
            await store.SaveAsync(snapshot, cancellationToken).ConfigureAwait(false);
        }
        finally { _writes.Release(); }
    }

    private static AppSettings Clone(AppSettings value) => JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(value))!;

    public void Dispose() => _writes.Dispose();
}
