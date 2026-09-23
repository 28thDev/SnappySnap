namespace SnappySnap.Core;

public interface IAppLogger
{
    void Info(string message, IReadOnlyDictionary<string, object?>? properties = null);
    void Warn(string message, IReadOnlyDictionary<string, object?>? properties = null);
    void Error(string message, Exception? exception = null, IReadOnlyDictionary<string, object?>? properties = null);
}

public interface IMonitorTopologyService
{
    IReadOnlyList<MonitorDescriptor> GetMonitors();
    VirtualPixelRect VirtualDesktopBounds { get; }
}

public interface ICapturePlanBuilder
{
    CapturePlan Build(VirtualPixelRect selectedRegion, IReadOnlyList<MonitorDescriptor> monitors);
}

public interface IScreenshotCaptureService
{
    Task<CapturedImage> CaptureAsync(CapturePlan plan, CancellationToken cancellationToken);
}

public interface IScreenshotCaptureDiagnostics
{
    string BackendName { get; }
}

public interface IVideoCaptureBackend : IAsyncDisposable
{
    RecordingBackendCapabilities Capabilities { get; }
    RecordingBackendState State { get; }
    event EventHandler<RecordingBackendState>? StateChanged;
    event EventHandler<RecordingBackendError>? Failed;
    Task StartAsync(VideoRecordingRequest request, CancellationToken cancellationToken);
    Task PauseAsync(CancellationToken cancellationToken);
    Task ResumeAsync(CancellationToken cancellationToken);
    Task SetSystemAudioMutedAsync(bool muted, CancellationToken cancellationToken);
    Task SetMicrophoneMutedAsync(bool muted, CancellationToken cancellationToken);
    Task<VideoRecordingResult> StopAsync(CancellationToken cancellationToken);
}

public interface IVideoCaptureBackendFactory
{
    IVideoCaptureBackend Create();
}

public interface IMouseClickSource : IDisposable
{
    event EventHandler<MouseClickEvent>? Clicked;
    void Start();
    void Stop();
}

public enum MouseButton
{
    Left,
    Right
}

public sealed record MouseClickEvent(DateTimeOffset TimestampUtc, VirtualPixelPoint Position, MouseButton Button);

public interface ICaptureExclusionService
{
    bool TryExcludeWindow(nint hwnd);
    bool TryRestoreWindow(nint hwnd);
}

public interface IHistoryRepository
{
    Task<HistoryItem> AddAsync(NewHistoryItem item, CancellationToken cancellationToken);
    Task<HistoryItem> UpdateScreenshotAsync(Guid id, NewHistoryItem item, CancellationToken cancellationToken);
    Task<IReadOnlyList<HistoryItem>> GetRecentAsync(int count, CancellationToken cancellationToken);
    IAsyncEnumerable<HistoryItem> EnumerateOlderAsync(DateTimeOffset? before, CancellationToken cancellationToken);
    Task DeleteAsync(Guid id, CancellationToken cancellationToken);
    Task<HistoryItem?> GetByIdAsync(Guid id, CancellationToken cancellationToken);
    Task<HistoryItem?> FindByPathAsync(string filePath, CancellationToken cancellationToken);
    Task UpdateThumbnailAsync(Guid id, DateTimeOffset createdAtUtc, string? thumbnailPath, ThumbnailState state, CancellationToken cancellationToken);
}

public interface IThumbnailService
{
    Task<ThumbnailResult> EnsureThumbnailAsync(HistoryItem item, CancellationToken cancellationToken);
}

public sealed record ThumbnailResult(string? ThumbnailPath, ThumbnailState State, string? Error = null);

public interface ISettingsStore
{
    Task<AppSettings> LoadAsync(CancellationToken cancellationToken);
    Task SaveAsync(AppSettings settings, CancellationToken cancellationToken);
}

public interface IRecoveryStore
{
    Task WriteAsync(RecoverySessionRecord record, CancellationToken cancellationToken);
    Task<IReadOnlyList<RecoverySessionRecord>> ScanAsync(CancellationToken cancellationToken);
    Task MarkAsync(Guid sessionId, RecoveryState state, string? error, CancellationToken cancellationToken);
}

public interface IMonotonicClock
{
    long Timestamp { get; }
    long Frequency { get; }
}

public sealed class SystemMonotonicClock : IMonotonicClock
{
    public long Timestamp => System.Diagnostics.Stopwatch.GetTimestamp();
    public long Frequency => System.Diagnostics.Stopwatch.Frequency;
}

public interface IVideoEditingService
{
    Task<VideoPreviewData> LoadPreviewAsync(string sourcePath, CancellationToken cancellationToken);
    Task<VideoEditResult> ExportAsync(string sourcePath, string destinationPath, VideoEditTimeline timeline, IProgress<double>? progress, CancellationToken cancellationToken);
}

public sealed record VideoEditTimeline(TimeSpan SourceDuration, TimeSpan TrimStart, TimeSpan TrimEnd, IReadOnlyList<TimeRange> RemovedRanges);
public readonly record struct TimeRange(TimeSpan Start, TimeSpan End)
{
    public TimeSpan Duration => End - Start;
}
public sealed record VideoEditResult(string OutputPath, TimeSpan Duration, string BackendDiagnostics);


public sealed record VideoPreviewData(TimeSpan Duration, IReadOnlyList<byte[]> Thumbnails, byte[] Poster);
