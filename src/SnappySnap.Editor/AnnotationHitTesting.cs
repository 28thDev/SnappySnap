using System.Windows;
namespace SnappySnap.Editor;
public static class AnnotationHitTesting
{
    public static bool Contains(EditorElement element, Point point, double zoom)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(zoom);
        var tolerance = Math.Max(8 / zoom, element.StrokeWidth / 2);
        return element switch
        {
            LineElement line => Distance(point, line.Start, line.End) <= tolerance,
            ArrowElement arrow => ArrowGeometry.Curve(arrow).StrokeContains(new System.Windows.Media.Pen(System.Windows.Media.Brushes.Black, tolerance * 2), point)
                || ArrowGeometry.Head(arrow).FillContains(point),
            FreehandElement freehand => freehand.Points.Zip(freehand.Points.Skip(1), (a, b) => Distance(point, a, b)).Any(d => d <= tolerance),
            _ => Inflated(element.Bounds, 8 / zoom).Contains(point)
        };
    }
    private static Rect Inflated(Rect rect, double tolerance) { rect.Inflate(tolerance, tolerance); return rect; }
    private static double Distance(Point point, Point a, Point b)
    {
        var line = b - a;
        if (line.LengthSquared < .000001) return (point - a).Length;
        var fraction = Math.Clamp(Vector.Multiply(point - a, line) / line.LengthSquared, 0, 1);
        return (point - (a + fraction * line)).Length;
    }
}
