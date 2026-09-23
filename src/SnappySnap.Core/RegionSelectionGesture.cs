namespace SnappySnap.Core;

public readonly record struct SelectionInputSettings(uint DoubleClickMilliseconds, int DoubleClickWidth, int DoubleClickHeight, int DragWidth, int DragHeight);
public enum SelectionGestureKind { None, Region, Monitor }
public readonly record struct SelectionGestureResult(SelectionGestureKind Kind, VirtualPixelRect Region);

/// <summary>Physical-pixel gesture recognition; the Windows overlay supplies system thresholds.</summary>
public sealed class RegionSelectionGesture
{
    private VirtualPixelPoint _start, _previous;
    private uint _downTime, _previousTime;
    private bool _hasPrevious, _secondClick, _active;
    private SelectionInputSettings _settings;
    public bool HasDragged { get; private set; }

    public void Begin(VirtualPixelPoint point, uint timestamp, SelectionInputSettings settings)
    {
        _settings = settings;
        _secondClick = _hasPrevious && unchecked(timestamp - _previousTime) <= settings.DoubleClickMilliseconds
            && point.X >= _previous.X - settings.DoubleClickWidth / 2 && point.X < _previous.X + (settings.DoubleClickWidth + 1) / 2
            && point.Y >= _previous.Y - settings.DoubleClickHeight / 2 && point.Y < _previous.Y + (settings.DoubleClickHeight + 1) / 2;
        _hasPrevious = false;
        _start = point; _downTime = timestamp; HasDragged = false; _active = true;
    }

    public void Move(VirtualPixelPoint point)
    {
        if (_active && (Math.Abs(point.X - _start.X) >= _settings.DragWidth || Math.Abs(point.Y - _start.Y) >= _settings.DragHeight))
            HasDragged = true;
    }

    public SelectionGestureResult End(VirtualPixelPoint point, IReadOnlyList<MonitorDescriptor> monitors, bool allowMonitor)
    {
        if (!_active) return default;
        Move(point); _active = false;
        if (HasDragged)
        {
            var region = new VirtualPixelRect(Math.Min(_start.X, point.X), Math.Min(_start.Y, point.Y), Math.Abs(point.X - _start.X), Math.Abs(point.Y - _start.Y));
            return region.Width >= 4 && region.Height >= 4 ? new(SelectionGestureKind.Region, region) : default;
        }
        if (_secondClick && allowMonitor)
        {
            var monitor = monitors.FirstOrDefault(m => m.Bounds.Contains(point));
            return monitor is null ? default : new(SelectionGestureKind.Monitor, monitor.Bounds);
        }
        _previous = _start; _previousTime = _downTime; _hasPrevious = true;
        return default;
    }
}
