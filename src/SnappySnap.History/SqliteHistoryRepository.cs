using Microsoft.Data.Sqlite;
using SnappySnap.Core;

namespace SnappySnap.History;

public sealed class SqliteHistoryRepository : IHistoryRepository, IAsyncDisposable
{
    private readonly string _databasePath;
    private readonly IAppLogger _logger;
    private readonly SemaphoreSlim _initializeGate = new(1, 1);
    private bool _initialized;

    public SqliteHistoryRepository(string databasePath, IAppLogger logger)
    {
        _databasePath = databasePath;
        _logger = logger;
    }

    public async Task<HistoryItem> AddAsync(NewHistoryItem item, CancellationToken cancellationToken)
    {
        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);
        var id = Guid.NewGuid();
        var fileSize = new FileInfo(item.FilePath).Length;
        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        using var transaction = connection.BeginTransaction();
        // A new capture may legitimately reuse a deleted timestamp/path. Retired metadata must not block it.
        await using (var retired = connection.CreateCommand())
        {
            retired.Transaction = transaction;
            retired.CommandText = "DELETE FROM captures WHERE file_path = $path AND deleted_at_utc IS NOT NULL;";
            retired.Parameters.AddWithValue("$path", item.FilePath);
            await retired.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO captures
            (id, media_type, file_path, created_at_utc, width_px, height_px, duration_ms, file_size_bytes,
             thumbnail_path, thumbnail_state, source_x_px, source_y_px, source_width_px, source_height_px,
             source_window_title, source_process_name, is_pinned, deleted_at_utc)
            VALUES ($id, $mediaType, $filePath, $createdAt, $width, $height, $duration, $fileSize,
                    $thumbnailPath, $thumbnailState, $sourceX, $sourceY, $sourceWidth, $sourceHeight,
                    $sourceTitle, $sourceProcess, 0, NULL);
            """;
        AddCommonParameters(command, id, item, fileSize);
        command.Parameters.AddWithValue("$thumbnailState", (int)(item.ThumbnailPath is null ? ThumbnailState.Pending : ThumbnailState.Ready));
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        transaction.Commit();
        return await GetByIdAsync(id, cancellationToken).ConfigureAwait(false) ?? throw new InvalidOperationException("History row was not readable after insert.");
    }

    public async Task<HistoryItem> UpdateScreenshotAsync(Guid id, NewHistoryItem item, CancellationToken cancellationToken)
    {
        if (item.MediaType != MediaType.Screenshot) throw new ArgumentException("Expected a screenshot.", nameof(item));
        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);
        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE captures SET file_path=$filePath, created_at_utc=$createdAt,
                width_px=$width, height_px=$height, file_size_bytes=$fileSize,
                thumbnail_path=NULL, thumbnail_state=0,
                source_x_px=$sourceX, source_y_px=$sourceY, source_width_px=$sourceWidth, source_height_px=$sourceHeight
            WHERE id=$id AND media_type=$mediaType AND deleted_at_utc IS NULL;
            """;
        AddCommonParameters(command, id, item, new FileInfo(item.FilePath).Length);
        if (await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) != 1)
            throw new InvalidOperationException("The screenshot is no longer in history.");
        return await GetByIdAsync(id, cancellationToken).ConfigureAwait(false) ?? throw new InvalidOperationException("Updated screenshot was not readable.");
    }

    public async Task<IReadOnlyList<HistoryItem>> GetRecentAsync(int count, CancellationToken cancellationToken)
    {
        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);
        count = Math.Clamp(count, 1, 5000);
        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, media_type, file_path, created_at_utc, width_px, height_px, duration_ms, file_size_bytes,
                   thumbnail_path, thumbnail_state, source_x_px, source_y_px, source_width_px, source_height_px,
                   source_window_title, source_process_name, is_pinned, deleted_at_utc
            FROM captures
            WHERE deleted_at_utc IS NULL
            ORDER BY created_at_utc DESC, id DESC
            LIMIT $count;
            """;
        command.Parameters.AddWithValue("$count", count);
        return await ReadItemsAsync(command, cancellationToken).ConfigureAwait(false);
    }

    public async IAsyncEnumerable<HistoryItem> EnumerateOlderAsync(DateTimeOffset? before, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);
        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, media_type, file_path, created_at_utc, width_px, height_px, duration_ms, file_size_bytes,
                   thumbnail_path, thumbnail_state, source_x_px, source_y_px, source_width_px, source_height_px,
                   source_window_title, source_process_name, is_pinned, deleted_at_utc
            FROM captures
            WHERE deleted_at_utc IS NULL AND ($before IS NULL OR created_at_utc < $before)
            ORDER BY created_at_utc DESC, id DESC;
            """;
        command.Parameters.AddWithValue("$before", before?.UtcDateTime.ToString("O") ?? (object)DBNull.Value);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            yield return ReadItem(reader);
        }
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);
        var item = await GetByIdAsync(id, cancellationToken).ConfigureAwait(false);
        if (item is null || item.DeletedAtUtc.HasValue)
        {
            return;
        }

        cancellationToken.ThrowIfCancellationRequested();
        // File.Exists hides access errors. Delete directly so permission/sharing failures retain metadata.
        try { File.Delete(item.FilePath); }
        catch (DirectoryNotFoundException) { /* A moved/deleted parent leaves only a stale history row. */ }

        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "UPDATE captures SET deleted_at_utc = $deletedAt WHERE id = $id AND deleted_at_utc IS NULL;";
        command.Parameters.AddWithValue("$deletedAt", DateTimeOffset.UtcNow.UtcDateTime.ToString("O"));
        command.Parameters.AddWithValue("$id", id.ToString("D"));
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<HistoryItem?> GetByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);
        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, media_type, file_path, created_at_utc, width_px, height_px, duration_ms, file_size_bytes,
                   thumbnail_path, thumbnail_state, source_x_px, source_y_px, source_width_px, source_height_px,
                   source_window_title, source_process_name, is_pinned, deleted_at_utc
            FROM captures WHERE id = $id;
            """;
        command.Parameters.AddWithValue("$id", id.ToString("D"));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        return await reader.ReadAsync(cancellationToken).ConfigureAwait(false) ? ReadItem(reader) : null;
    }

    public async Task<HistoryItem?> FindByPathAsync(string filePath, CancellationToken cancellationToken)
    {
        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);
        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, media_type, file_path, created_at_utc, width_px, height_px, duration_ms, file_size_bytes,
                   thumbnail_path, thumbnail_state, source_x_px, source_y_px, source_width_px, source_height_px,
                   source_window_title, source_process_name, is_pinned, deleted_at_utc
            FROM captures WHERE file_path = $filePath COLLATE WINDOWS_PATH LIMIT 1;
            """;
        command.Parameters.AddWithValue("$filePath", filePath);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        return await reader.ReadAsync(cancellationToken).ConfigureAwait(false) ? ReadItem(reader) : null;
    }

    public async Task UpdateThumbnailAsync(Guid id, DateTimeOffset createdAtUtc, string? thumbnailPath, ThumbnailState state, CancellationToken cancellationToken)
    {
        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);
        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "UPDATE captures SET thumbnail_path = $path, thumbnail_state = $state WHERE id = $id AND created_at_utc = $createdAt AND deleted_at_utc IS NULL;";
        command.Parameters.AddWithValue("$createdAt", createdAtUtc.UtcDateTime.ToString("O"));
        command.Parameters.AddWithValue("$path", (object?)thumbnailPath ?? DBNull.Value);
        command.Parameters.AddWithValue("$state", (int)state);
        command.Parameters.AddWithValue("$id", id.ToString("D"));
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task EnsureInitializedAsync(CancellationToken cancellationToken)
    {
        if (_initialized)
        {
            return;
        }

        await _initializeGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_initialized)
            {
                return;
            }

            var directory = Path.GetDirectoryName(_databasePath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
            await using var command = connection.CreateCommand();
            command.CommandText = """
                PRAGMA journal_mode=WAL;
                CREATE TABLE IF NOT EXISTS schema_migrations (version INTEGER PRIMARY KEY, applied_at_utc TEXT NOT NULL);
                CREATE TABLE IF NOT EXISTS captures (
                    id TEXT PRIMARY KEY,
                    media_type INTEGER NOT NULL,
                    file_path TEXT NOT NULL UNIQUE,
                    created_at_utc TEXT NOT NULL,
                    width_px INTEGER NOT NULL,
                    height_px INTEGER NOT NULL,
                    duration_ms INTEGER NULL,
                    file_size_bytes INTEGER NOT NULL,
                    thumbnail_path TEXT NULL,
                    thumbnail_state INTEGER NOT NULL DEFAULT 0,
                    source_x_px INTEGER NOT NULL,
                    source_y_px INTEGER NOT NULL,
                    source_width_px INTEGER NOT NULL,
                    source_height_px INTEGER NOT NULL,
                    source_window_title TEXT NULL,
                    source_process_name TEXT NULL,
                    is_pinned INTEGER NOT NULL DEFAULT 0,
                    deleted_at_utc TEXT NULL
                );
                CREATE INDEX IF NOT EXISTS ix_captures_created_at ON captures(created_at_utc DESC);
                """;
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            _initialized = true;
        }
        finally
        {
            _initializeGate.Release();
        }
    }

    private async Task<SqliteConnection> OpenAsync(CancellationToken cancellationToken)
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = _databasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared
        }.ToString());
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        connection.CreateCollation("WINDOWS_PATH", StringComparer.OrdinalIgnoreCase.Compare);
        return connection;
    }

    private static void AddCommonParameters(SqliteCommand command, Guid id, NewHistoryItem item, long fileSize)
    {
        command.Parameters.AddWithValue("$id", id.ToString("D"));
        command.Parameters.AddWithValue("$mediaType", (int)item.MediaType);
        command.Parameters.AddWithValue("$filePath", item.FilePath);
        command.Parameters.AddWithValue("$createdAt", item.CreatedAtUtc.UtcDateTime.ToString("O"));
        command.Parameters.AddWithValue("$width", item.WidthPx);
        command.Parameters.AddWithValue("$height", item.HeightPx);
        command.Parameters.AddWithValue("$duration", item.Duration.HasValue ? (object)(long)item.Duration.Value.TotalMilliseconds : DBNull.Value);
        command.Parameters.AddWithValue("$fileSize", fileSize);
        command.Parameters.AddWithValue("$thumbnailPath", (object?)item.ThumbnailPath ?? DBNull.Value);
        command.Parameters.AddWithValue("$sourceX", item.SourceBounds.X);
        command.Parameters.AddWithValue("$sourceY", item.SourceBounds.Y);
        command.Parameters.AddWithValue("$sourceWidth", item.SourceBounds.Width);
        command.Parameters.AddWithValue("$sourceHeight", item.SourceBounds.Height);
        command.Parameters.AddWithValue("$sourceTitle", (object?)item.SourceWindowTitle ?? DBNull.Value);
        command.Parameters.AddWithValue("$sourceProcess", (object?)item.SourceProcessName ?? DBNull.Value);
    }

    private static async Task<IReadOnlyList<HistoryItem>> ReadItemsAsync(SqliteCommand command, CancellationToken cancellationToken)
    {
        var items = new List<HistoryItem>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            items.Add(ReadItem(reader));
        }
        return items;
    }

    private static HistoryItem ReadItem(SqliteDataReader reader)
    {
        var deletedText = reader.IsDBNull(17) ? null : reader.GetString(17);
        return new HistoryItem(
            Guid.Parse(reader.GetString(0)),
            (MediaType)reader.GetInt32(1),
            reader.GetString(2),
            DateTimeOffset.Parse(reader.GetString(3), System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.AssumeUniversal),
            reader.GetInt32(4),
            reader.GetInt32(5),
            reader.IsDBNull(6) ? null : TimeSpan.FromMilliseconds(reader.GetInt64(6)),
            reader.GetInt64(7),
            reader.IsDBNull(8) ? null : reader.GetString(8),
            (ThumbnailState)reader.GetInt32(9),
            new VirtualPixelRect(reader.GetInt32(10), reader.GetInt32(11), reader.GetInt32(12), reader.GetInt32(13)),
            reader.IsDBNull(14) ? null : reader.GetString(14),
            reader.IsDBNull(15) ? null : reader.GetString(15),
            reader.GetInt64(16) != 0,
            deletedText is null ? null : DateTimeOffset.Parse(deletedText, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.AssumeUniversal));
    }

    public ValueTask DisposeAsync()
    {
        SqliteConnection.ClearAllPools();
        _initializeGate.Dispose();
        return ValueTask.CompletedTask;
    }
}
