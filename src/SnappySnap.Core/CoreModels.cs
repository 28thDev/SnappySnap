namespace SnappySnap.Core;

public readonly record struct VirtualPixelPoint(int X, int Y)
{
    public static VirtualPixelPoint operator +(VirtualPixelPoint left, VirtualPixelPoint right) => new(left.X + right.X, left.Y + right.Y);
    public static VirtualPixelPoint operator -(VirtualPixelPoint left, VirtualPixelPoint right) => new(left.X - right.X, left.Y - right.Y);
}

public readonly record struct VirtualPixelRect(int X, int Y, int Width, int Height)
{
    public int Left => X;
    public int Top => Y;
    public int Right => checked(X + Width);
    public int Bottom => checked(Y + Height);
    public bool IsEmpty => Width <= 0 || Height <= 0;

    public bool Contains(VirtualPixelPoint point) => point.X >= Left && point.X < Right && point.Y >= Top && point.Y < Bottom;

    public bool TryIntersect(VirtualPixelRect other, out VirtualPixelRect intersection)
    {
        var left = Math.Max(Left, other.Left);
        var top = Math.Max(Top, other.Top);
        var right = Math.Min(Right, other.Right);
        var bottom = Math.Min(Bottom, other.Bottom);
        if (right <= left || bottom <= top)
        {
            intersection = default;
            return false;
        }

        intersection = new VirtualPixelRect(left, top, right - left, bottom - top);
        return true;
    }

    public VirtualPixelRect Normalize() => new(
        Math.Min(Left, Right),
        Math.Min(Top, Bottom),
        Math.Abs(Width),
        Math.Abs(Height));
}

public readonly record struct DipPoint(double X, double Y);
public readonly record struct DipSize(double Width, double Height);
public readonly record struct DipRect(double X, double Y, double Width, double Height);

public sealed record MonitorDescriptor(
    string Id,
    VirtualPixelRect Bounds,
    VirtualPixelRect WorkArea,
    uint DpiX,
    uint DpiY,
    bool IsPrimary)
{
    public double ScaleX => DpiX == 0 ? 1d : DpiX / 96d;
    public double ScaleY => DpiY == 0 ? 1d : DpiY / 96d;
}

public sealed record CaptureSegment(
    string MonitorId,
    VirtualPixelRect SourceRectOnMonitorPx,
    VirtualPixelRect DestinationRectInOutputPx);

public sealed record CapturePlan(
    VirtualPixelRect SelectedVirtualBounds,
    int OutputWidth,
    int OutputHeight,
    IReadOnlyList<CaptureSegment> Segments)
{
    public void Validate()
    {
        if (SelectedVirtualBounds.IsEmpty || OutputWidth <= 0 || OutputHeight <= 0 || Segments.Count == 0)
        {
            throw new InvalidOperationException("Capture plan must contain a non-empty output and at least one segment.");
        }

        if (OutputWidth != SelectedVirtualBounds.Width || OutputHeight != SelectedVirtualBounds.Height)
        {
            throw new InvalidOperationException("Capture plan output dimensions must match the selected physical rectangle.");
        }
    }
}

public sealed class CapturePlanBuilder : ICapturePlanBuilder
{
    public CapturePlan Build(VirtualPixelRect selectedRegion, IReadOnlyList<MonitorDescriptor> monitors)
    {
        var normalized = selectedRegion.Normalize();
        if (normalized.IsEmpty)
        {
            throw new ArgumentException("A capture region must have a positive physical size.", nameof(selectedRegion));
        }

        ArgumentNullException.ThrowIfNull(monitors);
        var segments = new List<CaptureSegment>();
        foreach (var monitor in monitors)
        {
            if (!normalized.TryIntersect(monitor.Bounds, out var intersection))
            {
                continue;
            }

            var source = new VirtualPixelRect(
                intersection.X - monitor.Bounds.X,
                intersection.Y - monitor.Bounds.Y,
                intersection.Width,
                intersection.Height);
            var destination = new VirtualPixelRect(
                intersection.X - normalized.X,
                intersection.Y - normalized.Y,
                intersection.Width,
                intersection.Height);
            segments.Add(new CaptureSegment(monitor.Id, source, destination));
        }

        if (segments.Count == 0)
        {
            throw new ArgumentException("The capture region does not intersect an available monitor.", nameof(selectedRegion));
        }

        var plan = new CapturePlan(normalized, normalized.Width, normalized.Height, segments);
        plan.Validate();
        return plan;
    }
}

public sealed record CapturedImage
{
    public CapturedImage(int width, int height, byte[] bgra32)
    {
        if (width <= 0 || height <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(width), "Captured image dimensions must be positive.");
        }

        ArgumentNullException.ThrowIfNull(bgra32);
        if (bgra32.Length != checked(width * height * 4))
        {
            throw new ArgumentException("BGRA32 data length does not match image dimensions.", nameof(bgra32));
        }

        Width = width;
        Height = height;
        Bgra32 = bgra32;
    }

    public int Width { get; }
    public int Height { get; }
    public byte[] Bgra32 { get; }
}

public sealed class FrozenDesktopSnapshot
{
    public FrozenDesktopSnapshot(VirtualPixelRect virtualDesktopBounds, CapturedImage capturedImage)
    {
        if (virtualDesktopBounds.IsEmpty)
        {
            throw new ArgumentException("Frozen desktop bounds must be non-empty.", nameof(virtualDesktopBounds));
        }

        ArgumentNullException.ThrowIfNull(capturedImage);
        if (capturedImage.Width != virtualDesktopBounds.Width || capturedImage.Height != virtualDesktopBounds.Height)
        {
            throw new ArgumentException("Frozen desktop image dimensions must match its physical virtual-desktop bounds.", nameof(capturedImage));
        }

        VirtualDesktopBounds = virtualDesktopBounds;
        CapturedImage = capturedImage;
    }

    public VirtualPixelRect VirtualDesktopBounds { get; }
    public CapturedImage CapturedImage { get; }

    public CapturedImage Crop(VirtualPixelRect selectedVirtualBounds, CancellationToken cancellationToken = default)
    {
        var selected = selectedVirtualBounds.Normalize();
        if (selected.IsEmpty)
        {
            throw new ArgumentException("A frozen snapshot crop must have a positive physical size.", nameof(selectedVirtualBounds));
        }

        if (selected.Left < VirtualDesktopBounds.Left || selected.Top < VirtualDesktopBounds.Top ||
            selected.Right > VirtualDesktopBounds.Right || selected.Bottom > VirtualDesktopBounds.Bottom)
        {
            throw new ArgumentOutOfRangeException(nameof(selectedVirtualBounds), selectedVirtualBounds,
                "The selected region must be fully contained by the frozen virtual desktop bounds.");
        }

        var sourceX = selected.X - VirtualDesktopBounds.X;
        var sourceY = selected.Y - VirtualDesktopBounds.Y;
        var bytes = new byte[checked(selected.Width * selected.Height * 4)];
        var rowBytes = checked(selected.Width * 4);
        for (var y = 0; y < selected.Height; y++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var sourceOffset = checked(((sourceY + y) * CapturedImage.Width + sourceX) * 4);
            Buffer.BlockCopy(CapturedImage.Bgra32, sourceOffset, bytes, y * rowBytes, rowBytes);
        }

        return new CapturedImage(selected.Width, selected.Height, bytes);
    }
}

public enum MediaType
{
    Screenshot = 0,
    Video = 1
}

public enum ThumbnailState
{
    Pending = 0,
    Ready = 1,
    Failed = 2
}

public sealed record NewHistoryItem(
    MediaType MediaType,
    string FilePath,
    DateTimeOffset CreatedAtUtc,
    int WidthPx,
    int HeightPx,
    TimeSpan? Duration,
    string? ThumbnailPath,
    VirtualPixelRect SourceBounds,
    string? SourceWindowTitle = null,
    string? SourceProcessName = null);

public sealed record HistoryItem(
    Guid Id,
    MediaType MediaType,
    string FilePath,
    DateTimeOffset CreatedAtUtc,
    int WidthPx,
    int HeightPx,
    TimeSpan? Duration,
    long FileSizeBytes,
    string? ThumbnailPath,
    ThumbnailState ThumbnailState,
    VirtualPixelRect SourceBounds,
    string? SourceWindowTitle,
    string? SourceProcessName,
    bool IsPinned,
    DateTimeOffset? DeletedAtUtc);

public sealed record QualityProfile(
    string Key,
    string DisplayName,
    int FrameRate,
    int Quality,
    bool HardwarePreferred,
    bool FastStart,
    int AudioBitrateKbps = 128);

public sealed record VideoRecordingRequest(
    CapturePlan CapturePlan,
    string TempPath,
    string FinalPath,
    QualityProfile Quality,
    bool CaptureCursor,
    bool SystemAudioEnabled,
    bool MicrophoneEnabled);

public sealed record VideoRecordingResult(
    string CompletedPath,
    TimeSpan Duration,
    int WidthPx,
    int HeightPx,
    bool HardwareEncodingUsed,
    string BackendDiagnostics);

public sealed record RecordingBackendCapabilities(
    bool SupportsPauseResume,
    bool SupportsDynamicSystemAudioMute,
    bool SupportsDynamicMicrophoneMute,
    bool SupportsHardwareEncoding,
    bool SupportsMultiMonitor);

public sealed record RecordingBackendState(
    bool IsRecording,
    bool IsPaused,
    TimeSpan MediaDuration,
    bool SystemAudioMuted,
    bool MicrophoneMuted,
    string? Diagnostics = null,
    bool MicrophoneAvailable = true,
    bool SystemAudioAvailable = true);

public sealed record RecordingBackendError(string Message, Exception? Exception = null, bool IsRecoverable = true);

public sealed record RecoverySessionRecord(
    Guid SessionId,
    RecoveryState State,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset UpdatedAtUtc,
    string TempPath,
    string IntendedFinalPath,
    string BackendName,
    string? Error = null);

public enum RecoveryState
{
    RecordingStarted = 0,
    Finalizing = 1,
    FinalizedButNotIndexed = 2,
    Unrecoverable = 3,
    Recovered = 4
}

public enum RecordingState
{
    Idle,
    SelectingRegion,
    Starting,
    Recording,
    Pausing,
    Paused,
    Resuming,
    Finalizing,
    Completed,
    Error,
    RecoveryRequired
}

public enum RecordingCommand
{
    StartRequested,
    RegionSelected,
    BackendStarted,
    StartFailed,
    PauseRequested,
    Paused,
    ResumeRequested,
    Resumed,
    StopRequested,
    Finalized,
    FinalizeFailed,
    BackendFailed,
    Reset,
    Cancel
}

public enum ScreenshotState
{
    Idle,
    PreparingSelection,
    SelectingRegion,
    Capturing,
    Editing,
    Exporting,
    Completed,
    Error
}

public enum ScreenshotCommand
{
    StartRequested,
    SelectionPrepared,
    DirectRegionSelected,
    PreparationFailed,
    RegionSelected,
    Captured,
    CaptureFailed,
    Confirm,
    Exported,
    ExportFailed,
    Discard,
    Reset,
    OriginalSaved,
    EditorOpened
}

public sealed record RecordingSnapshot(
    RecordingState State,
    TimeSpan ActiveDuration,
    bool SystemAudioEnabled,
    bool MicrophoneEnabled,
    bool MicrophoneAvailable,
    CapturePlan? CapturePlan,
    string? FinalPath,
    string? Error,
    bool SystemAudioAvailable = true);

public sealed record ScreenshotSnapshot(ScreenshotState State, CapturePlan? CapturePlan, CapturedImage? Image, string? Error);
