using System.Windows;
using System.Windows.Media;

namespace SnappySnap.Editor;

/// <summary>Shared quadratic geometry for preview, export and pointer hit testing.</summary>
public static class ArrowGeometry
{
    public static Point Middle(ArrowElement arrow) => At(arrow, .5);

    public static Point At(ArrowElement arrow, double t)
    {
        var u = 1 - t;
        return new Point(u * u * arrow.Start.X + 2 * u * t * arrow.Control.X + t * t * arrow.End.X,
            u * u * arrow.Start.Y + 2 * u * t * arrow.Control.Y + t * t * arrow.End.Y);
    }

    // The visible handle lies on the curve, so it follows the pointer exactly.
    public static Point ControlForMiddle(Point start, Point end, Point middle) =>
        new(2 * middle.X - (start.X + end.X) / 2, 2 * middle.Y - (start.Y + end.Y) / 2);

    public static Vector EndDirection(ArrowElement arrow)
    {
        var tangent = arrow.End - arrow.Control;
        if (tangent.LengthSquared < .000001) tangent = arrow.End - arrow.Start;
        if (tangent.LengthSquared < .000001) return new Vector(1, 0);
        tangent.Normalize();
        return tangent;
    }

    public static StreamGeometry Curve(ArrowElement arrow)
    {
        var geometry = new StreamGeometry();
        using (var context = geometry.Open())
        {
            context.BeginFigure(arrow.Start, false, false);
            context.QuadraticBezierTo(arrow.Control, arrow.End, true, true);
        }
        geometry.Freeze();
        return geometry;
    }

    public static StreamGeometry Head(ArrowElement arrow)
    {
        var direction = EndDirection(arrow);
        var normal = new Vector(-direction.Y, direction.X);
        // Bound the head by available curve length as well as the stroke. Short arrows stay legible.
        var length = (arrow.Control - arrow.Start).Length + (arrow.End - arrow.Control).Length;
        var headLength = Math.Min(8 + arrow.StrokeWidth * 2.5, length * .45);
        var halfWidth = headLength * .48;
        var geometry = new StreamGeometry();
        using (var context = geometry.Open())
        {
            context.BeginFigure(arrow.End, true, true);
            context.LineTo(arrow.End - direction * headLength + normal * halfWidth, true, false);
            context.LineTo(arrow.End - direction * (headLength * .72), true, false);
            context.LineTo(arrow.End - direction * headLength - normal * halfWidth, true, false);
        }
        geometry.Freeze();
        return geometry;
    }

    public static Geometry Outline(ArrowElement arrow)
    {
        var length = (arrow.Control - arrow.Start).Length + (arrow.End - arrow.Control).Length;
        var headLength = Math.Min(8 + arrow.StrokeWidth * 2.5, length * .45);
        // Stop the shaft inside the filled head, preserving a sharp tip even at thick widths.
        var low = 0d; var high = 1d;
        for (var i = 0; i < 16; i++)
        {
            var mid = (low + high) / 2;
            if ((arrow.End - At(arrow, mid)).Length > headLength * .55) low = mid; else high = mid;
        }
        var t = (low + high) / 2;
        var shaftCurve = new StreamGeometry();
        using (var context = shaftCurve.Open())
        {
            context.BeginFigure(arrow.Start, false, false);
            context.QuadraticBezierTo(arrow.Start + (arrow.Control - arrow.Start) * t, At(arrow, t), true, true);
        }
        var shaft = shaftCurve.GetWidenedPathGeometry(new Pen(Brushes.Black, Math.Min(arrow.StrokeWidth, length * .65))
        { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Flat, LineJoin = PenLineJoin.Round });
        var outline = new CombinedGeometry(GeometryCombineMode.Union, shaft, Head(arrow));
        outline.Freeze();
        return outline;
    }
}
