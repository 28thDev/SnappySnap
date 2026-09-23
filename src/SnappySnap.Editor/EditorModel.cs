using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SnappySnap.Core;

namespace SnappySnap.Editor;

public enum EditorTool
{
    Select,
    Arrow,
    Rectangle,
    Line,
    Freehand,
    Text,
    Highlight,
    Blur,
    Pixelate,
    StepMarker,
    Crop,
    Image
}

public abstract class EditorElement
{
    protected EditorElement(Guid? id = null) => Id = id ?? Guid.NewGuid();
    public Guid Id { get; }
    // A revision invalidates retained drawing/effect dependencies without copying geometry on every frame.
    internal long Revision { get; private set; }
    protected void Changed() => Revision++;
    protected void Set<T>(ref T field, T value) { if (EqualityComparer<T>.Default.Equals(field, value)) return; field = value; Changed(); }
    private Rect _bounds;
    private Color _color = Color.FromRgb(255, 59, 48), _fillColor = Colors.Transparent;
    private double _strokeWidth = 5, _opacity = 1;
    public Rect Bounds { get => _bounds; set => Set(ref _bounds, value); }
    public Color Color { get => _color; set => Set(ref _color, value); }
    public double StrokeWidth { get => _strokeWidth; set => Set(ref _strokeWidth, value); }
    public Color FillColor { get => _fillColor; set => Set(ref _fillColor, value); }
    public double Opacity { get => _opacity; set => Set(ref _opacity, value); }
    public bool IsSelected { get; set; }
}

public sealed class ArrowElement : EditorElement
{
    public ArrowElement(Point start, Point end)
    {
        _start = start; _end = end; _control = start + (end - start) / 2;
        UpdateBounds();
    }
    private Point _start, _end, _control;
    public Point Start { get => _start; set { Set(ref _start, value); UpdateBounds(); } }
    public Point End { get => _end; set { Set(ref _end, value); UpdateBounds(); } }
    public Point Control { get => _control; set { Set(ref _control, value); UpdateBounds(); } }
    public void UpdateBounds() => Bounds = ArrowGeometry.Curve(this).Bounds;
}

public sealed class RectangleElement : EditorElement
{
    public RectangleElement(Rect bounds) => Bounds = bounds;
}

public sealed class LineElement : EditorElement
{
    public LineElement(Point start, Point end) => (Start, End, Bounds) = (start, end, new Rect(start, end));
    private Point _start, _end;
    public Point Start { get => _start; set => Set(ref _start, value); }
    public Point End { get => _end; set => Set(ref _end, value); }
}

public sealed class FreehandElement : EditorElement
{
    public ObservableCollection<Point> Points { get; } = new();
    public FreehandElement(IEnumerable<Point> points)
    {
        Points.CollectionChanged += (_, _) => Changed();
        foreach (var point in points)
        {
            Points.Add(point);
        }
        Bounds = Points.Count == 0 ? Rect.Empty : new Rect(Points.Min(x => x.X), Points.Min(x => x.Y), Points.Max(x => x.X) - Points.Min(x => x.X), Points.Max(x => x.Y) - Points.Min(x => x.Y));
    }
}

public sealed class TextElement : EditorElement
{
    public TextElement(string text, Point position) : base() { Bounds = new Rect(position, new Size()); Text = text; UpdateTextBounds(); }
    private string _text = "";
    private double _fontSize = 20;
    public string Text { get => _text; set { Set(ref _text, value); UpdateTextBounds(); } }
    public double FontSize { get => _fontSize; set { Set(ref _fontSize, value); UpdateTextBounds(); } }
    private void UpdateTextBounds()
    {
        var layout = TextLayout.Create(Text, FontSize, Brushes.Black);
        Bounds = new Rect(Bounds.Location, new Size(Math.Max(1, layout.WidthIncludingTrailingWhitespace), Math.Max(1, layout.Height)));
    }
}

public static class TextLayout
{
    public static FormattedText Create(string text, double fontSize, Brush brush) => new(text,
        System.Globalization.CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, new Typeface("Segoe UI"), fontSize, brush, 1.0);
}

public sealed class ChangeTextCommand(TextElement element, string before, string after) : IEditorCommand
{
    public void Execute(EditorDocument document) => element.Text = after;
    public void Undo(EditorDocument document) => element.Text = before;
}

public sealed class HighlightElement : EditorElement
{
    public HighlightElement(Rect bounds) { Bounds = bounds; Opacity = 0.35; Color = Colors.Gold; }
}

public sealed class BlurElement : EditorElement
{
    public BlurElement(Rect bounds) => Bounds = bounds;
}

public sealed class PixelateElement : EditorElement
{
    public PixelateElement(Rect bounds) => Bounds = bounds;
}

public sealed class StepMarkerElement : EditorElement
{
    public StepMarkerElement(int number, Point center) : base() => (Number, Bounds) = (number,
        new Rect(center.X - EditorSettings.DefaultStepDiameter / 2, center.Y - EditorSettings.DefaultStepDiameter / 2,
            EditorSettings.DefaultStepDiameter, EditorSettings.DefaultStepDiameter));
    private int _number;
    public int Number { get => _number; set => Set(ref _number, value); }
}

public sealed class ImageElement : EditorElement
{
    public ImageElement(BitmapSource image, Rect bounds) => (Image, Bounds) = (image, bounds);
    public BitmapSource Image { get; }
}

public sealed class EditorDocument
{
    public EditorDocument(CapturedImage capturedImage)
    {
        BaseImage = EditorRenderer.ToBitmapSource(capturedImage);
        Width = capturedImage.Width;
        Height = capturedImage.Height;
    }

    public BitmapSource BaseImage { get; }
    public int Width { get; }
    public int Height { get; }
    public ObservableCollection<EditorElement> Elements { get; } = new();
    public Rect CropRect { get; set; }
    public bool HasCrop => !CropRect.IsEmpty && CropRect.Width > 0 && CropRect.Height > 0;
    public Rect VisibleBounds => HasCrop ? CropRect : new Rect(0, 0, Width, Height);

    public Rect ConstrainCrop(Rect selection)
    {
        selection.Intersect(VisibleBounds);
        if (selection.IsEmpty || selection.Width < 2 || selection.Height < 2) return Rect.Empty;
        // Pixel-aligned edges keep preview dimensions identical to the exported bitmap at any zoom.
        return new Rect(new Point(Math.Floor(selection.Left), Math.Floor(selection.Top)),
            new Point(Math.Ceiling(selection.Right), Math.Ceiling(selection.Bottom)));
    }
    public int NextStepNumber => Elements.OfType<StepMarkerElement>().Select(x => x.Number).DefaultIfEmpty(0).Max() + 1;
}

public interface IEditorCommand
{
    void Execute(EditorDocument document);
    void Undo(EditorDocument document);
}

public sealed class EditorCommandHistory
{
    private readonly Stack<IEditorCommand> _undo = new();
    private readonly Stack<IEditorCommand> _redo = new();

    public bool CanUndo => _undo.Count > 0;
    public bool CanRedo => _redo.Count > 0;

    public void Execute(IEditorCommand command, EditorDocument document)
    {
        command.Execute(document);
        _undo.Push(command);
        _redo.Clear();
    }

    public void Undo(EditorDocument document)
    {
        if (_undo.Count == 0) return;
        var command = _undo.Pop();
        command.Undo(document);
        _redo.Push(command);
    }

    public void Redo(EditorDocument document)
    {
        if (_redo.Count == 0) return;
        var command = _redo.Pop();
        command.Execute(document);
        _undo.Push(command);
    }
}

public sealed class AddElementCommand(EditorElement element) : IEditorCommand
{
    public void Execute(EditorDocument document) { if (!document.Elements.Contains(element)) document.Elements.Add(element); }
    public void Undo(EditorDocument document) => document.Elements.Remove(element);
}

public sealed class DeleteElementCommand(EditorElement element) : IEditorCommand
{
    private int _index;
    public void Execute(EditorDocument document) { _index = document.Elements.IndexOf(element); if (_index >= 0) document.Elements.RemoveAt(_index); }
    public void Undo(EditorDocument document) { if (_index >= 0) document.Elements.Insert(Math.Min(_index, document.Elements.Count), element); }
}

public sealed class MoveElementCommand(EditorElement element, Vector delta) : IEditorCommand
{
    public void Execute(EditorDocument document) => Move(delta);
    public void Undo(EditorDocument document) => Move(-delta);
    private void Move(Vector amount)
    {
        element.Bounds = new Rect(element.Bounds.Location + amount, element.Bounds.Size);
        switch (element)
        {
            case ArrowElement arrow:
                arrow.Start += amount; arrow.End += amount; arrow.Control += amount; break;
            case LineElement line:
                line.Start += amount; line.End += amount; break;
            case FreehandElement freehand:
                for (var index = 0; index < freehand.Points.Count; index++) freehand.Points[index] += amount;
                break;
        }
    }
}

public sealed record ElementGeometry(Rect Bounds, Point? Start, Point? End, IReadOnlyList<Point> Points, Point? Control = null);

public static class EditorGeometry
{
    public static ElementGeometry Capture(EditorElement element) => new(
        element.Bounds,
        element switch
        {
            ArrowElement arrow => arrow.Start,
            LineElement line => line.Start,
            _ => null
        },
        element switch
        {
            ArrowElement arrow => arrow.End,
            LineElement line => line.End,
            _ => null
        },
        element is FreehandElement freehand ? freehand.Points.ToArray() : Array.Empty<Point>(),
        element is ArrowElement curved ? curved.Control : null);

    public static void Apply(EditorElement element, ElementGeometry geometry)
    {
        element.Bounds = geometry.Bounds;
        switch (element)
        {
            case ArrowElement arrow when geometry.Start.HasValue && geometry.End.HasValue:
                arrow.Start = geometry.Start.Value;
                arrow.End = geometry.End.Value;
                arrow.Control = geometry.Control ?? throw new InvalidOperationException("Arrow geometry requires a control point.");
                break;
            case LineElement line when geometry.Start.HasValue && geometry.End.HasValue:
                line.Start = geometry.Start.Value;
                line.End = geometry.End.Value;
                line.Bounds = new Rect(line.Start, line.End);
                break;
            case FreehandElement freehand:
                freehand.Points.Clear();
                foreach (var point in geometry.Points) freehand.Points.Add(point);
                break;
        }
    }

    public static ElementGeometry Translate(ElementGeometry geometry, Vector delta) => new(
        new Rect(geometry.Bounds.Location + delta, geometry.Bounds.Size),
        geometry.Start.HasValue ? geometry.Start.Value + delta : null,
        geometry.End.HasValue ? geometry.End.Value + delta : null,
        geometry.Points.Select(point => point + delta).ToArray(),
        geometry.Control.HasValue ? geometry.Control.Value + delta : null);

    public static ElementGeometry Resize(ElementGeometry geometry, Rect target)
    {
        var source = geometry.Bounds;
        var scaleX = Math.Abs(source.Width) < 0.001 ? 1d : target.Width / source.Width;
        var scaleY = Math.Abs(source.Height) < 0.001 ? 1d : target.Height / source.Height;

        Point Map(Point point) => new(
            target.Left + (point.X - source.Left) * scaleX,
            target.Top + (point.Y - source.Top) * scaleY);

        return new ElementGeometry(
            target,
            geometry.Start.HasValue ? Map(geometry.Start.Value) : null,
            geometry.End.HasValue ? Map(geometry.End.Value) : null,
            geometry.Points.Select(Map).ToArray(),
            geometry.Control.HasValue ? Map(geometry.Control.Value) : null);
    }
}

public sealed class TransformElementCommand(EditorElement element, ElementGeometry before, ElementGeometry after) : IEditorCommand
{
    public void Execute(EditorDocument document) => EditorGeometry.Apply(element, after);
    public void Undo(EditorDocument document) => EditorGeometry.Apply(element, before);
}

public sealed class ResizeElementCommand(EditorElement element, ElementGeometry before, Rect after) : IEditorCommand
{
    private readonly ElementGeometry _after = EditorGeometry.Resize(before, after);

    public void Execute(EditorDocument document) => EditorGeometry.Apply(element, _after);
    public void Undo(EditorDocument document) => EditorGeometry.Apply(element, before);
}

public sealed class CropCommand(Rect oldCrop, Rect newCrop) : IEditorCommand
{
    public void Execute(EditorDocument document) => document.CropRect = newCrop;
    public void Undo(EditorDocument document) => document.CropRect = oldCrop;
}

public sealed record ElementStyle(Color Color, Color FillColor, double StrokeWidth, double Opacity, double FontSize, double StepDiameter = EditorSettings.DefaultStepDiameter)
{
    public static ElementStyle Read(EditorElement element) => new(element.Color, element.FillColor, element.StrokeWidth, element.Opacity,
        element is TextElement text ? text.FontSize : 20, element is StepMarkerElement ? element.Bounds.Width : EditorSettings.DefaultStepDiameter);
    public void Apply(EditorElement element)
    {
        element.Color = Color; element.FillColor = FillColor; element.StrokeWidth = StrokeWidth; element.Opacity = Opacity;
        if (element is TextElement text) text.FontSize = FontSize;
        if (element is StepMarkerElement marker && marker.Bounds.Width != StepDiameter)
            marker.Bounds = new Rect(marker.Bounds.X + (marker.Bounds.Width - StepDiameter) / 2,
                marker.Bounds.Y + (marker.Bounds.Height - StepDiameter) / 2, StepDiameter, StepDiameter);
    }
}

public sealed class StyleElementCommand(EditorElement element, ElementStyle before, ElementStyle after) : IEditorCommand
{
    public void Execute(EditorDocument document) => after.Apply(element);
    public void Undo(EditorDocument document) => before.Apply(element);
}
