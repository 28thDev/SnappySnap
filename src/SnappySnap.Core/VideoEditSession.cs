namespace SnappySnap.Core;

/// <summary>Non-destructive, single-source edit. All UI operations use edited time.</summary>
public sealed class VideoEditSession
{
    private TimeRange[] _segments;
    private readonly Stack<TimeRange[]> _undo = new(), _redo = new();
    public TimeSpan SourceDuration { get; }
    public IReadOnlyList<TimeRange> Segments => Array.AsReadOnly(_segments);
    public TimeSpan Duration => TimeSpan.FromTicks(_segments.Sum(r => r.Duration.Ticks));
    public bool CanUndo => _undo.Count > 0;
    public bool CanRedo => _redo.Count > 0;
    public VideoEditSession(TimeSpan duration)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(duration, TimeSpan.Zero);
        SourceDuration = duration; _segments = [new(TimeSpan.Zero, duration)];
    }
    public TimeSpan ToSource(TimeSpan edited)
    {
        var ticks = Math.Clamp(edited.Ticks, 0, Duration.Ticks);
        foreach (var range in _segments)
        {
            if (ticks < range.Duration.Ticks) return range.Start + TimeSpan.FromTicks(ticks);
            ticks -= range.Duration.Ticks;
        }
        return _segments[^1].End;
    }
    public TimeSpan ToEdited(TimeSpan source)
    {
        var cursor = TimeSpan.Zero;
        foreach (var range in _segments)
        {
            if (source < range.Start) return cursor;
            if (source <= range.End) return cursor + source - range.Start;
            cursor += range.Duration;
        }
        return cursor;
    }
    public void Delete(TimeSpan start, TimeSpan end)
    {
        if (start < TimeSpan.Zero || end > Duration || end <= start) throw new ArgumentException("Select a range inside the edited video.");
        if (Duration - (end - start) < TimeSpan.FromMilliseconds(50)) throw new InvalidOperationException("Keep at least a short fragment of the recording.");
        var kept = new List<TimeRange>(); var cursor = TimeSpan.Zero;
        foreach (var range in _segments)
        {
            var segmentEnd = cursor + range.Duration;
            if (start >= segmentEnd || end <= cursor) kept.Add(range);
            else
            {
                if (start > cursor) kept.Add(new(range.Start, range.Start + start - cursor));
                if (end < segmentEnd) kept.Add(new(range.Start + end - cursor, range.End));
            }
            cursor = segmentEnd;
        }
        Commit(kept.ToArray());
    }
    public void Trim(TimeSpan start, TimeSpan end)
    {
        if (start < TimeSpan.Zero || end > Duration || end - start < TimeSpan.FromMilliseconds(50)) throw new ArgumentException("Keep a non-empty range inside the edited video.");
        var kept = new List<TimeRange>(); var cursor = TimeSpan.Zero;
        foreach (var range in _segments)
        {
            var a = start > cursor ? start : cursor; var b = end < cursor + range.Duration ? end : cursor + range.Duration;
            if (b > a) kept.Add(new(range.Start + a - cursor, range.Start + b - cursor));
            cursor += range.Duration;
        }
        Commit(kept.ToArray());
    }
    private void Commit(TimeRange[] segments) { if (_segments.SequenceEqual(segments)) return; _undo.Push(_segments); _segments = segments; _redo.Clear(); }
    public void Undo() { if (!CanUndo) return; _redo.Push(_segments); _segments = _undo.Pop(); }
    public void Redo() { if (!CanRedo) return; _undo.Push(_segments); _segments = _redo.Pop(); }
    public VideoEditTimeline ExportTimeline()
    {
        var removed = new List<TimeRange>();
        for (var i = 1; i < _segments.Length; i++) if (_segments[i].Start > _segments[i-1].End) removed.Add(new(_segments[i-1].End, _segments[i].Start));
        return new(SourceDuration, _segments[0].Start, SourceDuration - _segments[^1].End, removed);
    }
    public static IReadOnlyList<TimeRange> KeepRanges(VideoEditTimeline timeline)
    {
        var cursor = timeline.TrimStart; var end = timeline.SourceDuration - timeline.TrimEnd; var kept = new List<TimeRange>();
        if (cursor < TimeSpan.Zero || end <= cursor || end > timeline.SourceDuration) throw new ArgumentException("Invalid trim range.");
        foreach (var range in timeline.RemovedRanges.OrderBy(r => r.Start))
        {
            if (range.End <= range.Start || range.Start < TimeSpan.Zero || range.End > timeline.SourceDuration) throw new ArgumentException("Invalid removed range.");
            var start = range.Start > cursor ? range.Start : cursor; var stop = range.End < end ? range.End : end;
            if (stop <= start) continue;
            if (start > cursor) kept.Add(new(cursor, start < end ? start : end));
            if (stop > cursor) cursor = stop;
            if (cursor >= end) break;
        }
        if (cursor < end) kept.Add(new(cursor, end));
        return kept;
    }
}

public sealed record PreviewFrame(int Width, int Height, byte[] Bgra32);
public interface IVideoPreviewSession : IDisposable
{
    Task LoadAsync(string path, VideoEditTimeline timeline, CancellationToken cancellationToken);
    void Play();
    void Pause();
    void Seek(TimeSpan position);
    TimeSpan Position { get; }
    bool IsPlaying { get; }
    double Volume { get; set; }
    bool IsMuted { get; set; }
    PreviewFrame? TakeFrame();
    string? Error { get; }
}
