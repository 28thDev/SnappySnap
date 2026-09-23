namespace SnappySnap.Core;

public sealed record ShelfPreferences(double ThumbnailHeight = 164, ShelfPlacement? Compact = null, ShelfPlacement? History = null)
{
    public static ShelfPreferences Normalize(ShelfPreferences? value)
    {
        value ??= new();
        return value with
        {
            ThumbnailHeight = double.IsFinite(value.ThumbnailHeight) ? 104 + Math.Round((Math.Clamp(value.ThumbnailHeight, 104, 244) - 104) / 10) * 10 : 164,
            Compact = Valid(value.Compact) ? value.Compact : null,
            History = Valid(value.History) ? value.History : null
        };
    }

    private static bool Valid(ShelfPlacement? value) => value is not null && !string.IsNullOrWhiteSpace(value.MonitorId)
        && double.IsFinite(value.Bounds.X) && double.IsFinite(value.Bounds.Y)
        && double.IsFinite(value.Bounds.Width) && double.IsFinite(value.Bounds.Height)
        && value.Bounds.Width > 0 && value.Bounds.Height > 0;
}

// Bounds are DIPs relative to the named monitor's work-area origin, not virtual pixels.
public sealed record ShelfPlacement(string MonitorId, DipRect Bounds)
{
    public VirtualPixelRect Restore(MonitorDescriptor monitor, bool compact)
    {
        var area = monitor.WorkArea;
        var width = (int)Math.Round(Math.Clamp(Bounds.Width, Math.Min(compact ? 560 : 700, area.Width / monitor.ScaleX), area.Width / monitor.ScaleX) * monitor.ScaleX);
        var height = (int)Math.Round(Math.Clamp(Bounds.Height, Math.Min(compact ? 420 : 480, area.Height / monitor.ScaleY), area.Height / monitor.ScaleY) * monitor.ScaleY);
        var x = area.X + (int)Math.Round(Math.Clamp(Bounds.X, 0, (area.Width - width) / monitor.ScaleX) * monitor.ScaleX);
        var y = area.Y + (int)Math.Round(Math.Clamp(Bounds.Y, 0, (area.Height - height) / monitor.ScaleY) * monitor.ScaleY);
        return new(x, y, width, height);
    }
}
