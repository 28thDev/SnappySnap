using System.Text.Json;
using SnappySnap.Core;

namespace SnappySnap.Infrastructure;

public sealed class JsonRecoveryStore : IRecoveryStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private readonly AppPaths _paths;

    public JsonRecoveryStore(AppPaths paths) => _paths = paths;

    public async Task WriteAsync(RecoverySessionRecord record, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _paths.EnsureDirectories();
        var path = GetPath(record.SessionId);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                await JsonSerializer.SerializeAsync(stream, record, JsonOptions, cancellationToken).ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
            }
            cancellationToken.ThrowIfCancellationRequested();
            File.Move(temporary, path, overwrite: true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    public async Task<IReadOnlyList<RecoverySessionRecord>> ScanAsync(CancellationToken cancellationToken)
    {
        _paths.EnsureDirectories();
        var results = new List<RecoverySessionRecord>();
        foreach (var path in Directory.EnumerateFiles(_paths.RecoveryPath, "*.json"))
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                await using var stream = File.OpenRead(path);
                var item = await JsonSerializer.DeserializeAsync<RecoverySessionRecord>(stream, JsonOptions, cancellationToken).ConfigureAwait(false);
                if (item is not null)
                {
                    results.Add(item);
                }
            }
            catch (JsonException)
            {
                results.Add(new RecoverySessionRecord(Guid.Empty, RecoveryState.Unrecoverable, File.GetCreationTimeUtc(path), DateTimeOffset.UtcNow, path, path, "unknown", "Invalid recovery record."));
            }
        }

        return results.OrderByDescending(x => x.UpdatedAtUtc).ToArray();
    }

    public async Task MarkAsync(Guid sessionId, RecoveryState state, string? error, CancellationToken cancellationToken)
    {
        var path = GetPath(sessionId);
        if (!File.Exists(path))
        {
            return;
        }

        RecoverySessionRecord? current;
        await using (var stream = File.OpenRead(path))
        {
            current = await JsonSerializer.DeserializeAsync<RecoverySessionRecord>(stream, JsonOptions, cancellationToken).ConfigureAwait(false);
        }
        if (current is null)
        {
            return;
        }

        await WriteAsync(current with { State = state, Error = error, UpdatedAtUtc = DateTimeOffset.UtcNow }, cancellationToken).ConfigureAwait(false);
        if (state == RecoveryState.Recovered)
        {
            File.Delete(path);
        }
    }

    private string GetPath(Guid sessionId) => Path.Combine(_paths.RecoveryPath, $"{sessionId:N}.json");
}
