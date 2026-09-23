using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SnappySnap.Core;

namespace SnappySnap.Editor;

public static class EditorRenderer
{
    public static BitmapSource ToBitmapSource(CapturedImage image)
    {
        var source = BitmapSource.Create(image.Width, image.Height, 96, 96, PixelFormats.Bgra32, null, image.Bgra32, image.Width * 4);
        source.Freeze();
        return source;
    }

    public static CapturedImage ToCapturedImage(BitmapSource source)
    {
        var converted = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
        converted.Freeze();
        var bytes = new byte[checked(converted.PixelWidth * converted.PixelHeight * 4)];
        converted.CopyPixels(bytes, converted.PixelWidth * 4, 0);
        return new CapturedImage(converted.PixelWidth, converted.PixelHeight, bytes);
    }

    public static DrawingVisual CreateVisual(EditorDocument document, bool includeSelectionAdorners = false, double zoom = 1)
    {
        var drawing = new EditorDrawingCache(document).Update();
        var visual = new DrawingVisual();
        using var context = visual.RenderOpen();
        var crop = document.VisibleBounds;
        context.PushClip(new RectangleGeometry(new Rect(crop.Size)));
        context.PushTransform(new TranslateTransform(-crop.X, -crop.Y));
        context.DrawDrawing(drawing);
        context.Pop();

        if (includeSelectionAdorners)
        {
            foreach (var element in document.Elements.Where(x => x.IsSelected))
            {
                DrawSelectionAdorners(context, element, crop, zoom);
            }
        }

        return visual;
    }

    public static Task SavePngAsync(EditorDocument document, string path, CancellationToken cancellationToken) => SaveAsync(document, path, "Png", cancellationToken);
    public static Task<BitmapSource> RenderAsync(EditorDocument document, bool whiteBackground, CancellationToken cancellationToken) => Task.Run<BitmapSource>(() =>
    {
        var crop = document.VisibleBounds;
        var visual = CreateVisual(document);
        var target = new RenderTargetBitmap((int)Math.Ceiling(crop.Width), (int)Math.Ceiling(crop.Height), 96, 96, PixelFormats.Pbgra32);
        if (whiteBackground)
        {
            var white = new DrawingVisual();
            using (var background = white.RenderOpen()) background.DrawRectangle(Brushes.White, null, new Rect(0, 0, crop.Width, crop.Height));
            target.Render(white);
        }
        target.Render(visual);
        target.Freeze();
        return target;
    }, cancellationToken);

    public static async Task SaveAsync(EditorDocument document, string path, string format, CancellationToken cancellationToken)
    {
        var image = await RenderAsync(document, format.Equals("Jpg", StringComparison.OrdinalIgnoreCase), cancellationToken).ConfigureAwait(false);
        await SaveBitmapAsync(image, path, format, cancellationToken).ConfigureAwait(false);
    }

    public static Task SaveBitmapAsync(BitmapSource image, string path, string format, CancellationToken cancellationToken, bool overwriteExisting = true) => Task.Run(async () =>
    {
        var jpeg = format.Equals("Jpg", StringComparison.OrdinalIgnoreCase);
        if (!jpeg) image = WithoutUnusedAlpha(image, cancellationToken);
        BitmapEncoder encoder = jpeg ? new JpegBitmapEncoder { QualityLevel = 90 } : new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(image));
        path = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                encoder.Save(stream);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
            }
            cancellationToken.ThrowIfCancellationRequested();
            if (overwriteExisting && File.Exists(path)) File.Replace(temporary, path, null);
            else File.Move(temporary, path);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }, cancellationToken);

    private static BitmapSource WithoutUnusedAlpha(BitmapSource image, CancellationToken cancellationToken)
    {
        // Desktop captures are opaque. Keep alpha for actual transparent editor content.
        if (image.Format != PixelFormats.Bgra32 && image.Format != PixelFormats.Pbgra32) return image;
        var stride = checked(image.PixelWidth * 4);
        var row = new byte[stride];
        for (var y = 0; y < image.PixelHeight; y++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            image.CopyPixels(new Int32Rect(0, y, image.PixelWidth, 1), row, stride, 0);
            for (var x = 3; x < stride; x += 4) if (row[x] != 255) return image;
        }
        var opaque = new FormatConvertedBitmap(image, PixelFormats.Bgr24, null, 0); opaque.Freeze(); return opaque;
    }

    public static BitmapSource LoadImage(string path)
    {
        using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        var image = new BitmapImage();
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.StreamSource = input;
        image.EndInit();
        image.Freeze();
        return image;
    }

    internal static void DrawElement(DrawingContext context, EditorElement element)
    {
        var pen = new Pen(new SolidColorBrush(element.Color), element.StrokeWidth);
        pen.Brush.Freeze();
        switch (element)
        {
            case ArrowElement arrow:
                context.DrawGeometry(pen.Brush, null, ArrowGeometry.Outline(arrow));
                break;
            case RectangleElement rectangle:
                context.DrawRectangle(new SolidColorBrush(rectangle.FillColor), pen, rectangle.Bounds);
                break;
            case LineElement line:
                context.DrawLine(pen, line.Start, line.End);
                break;
            case FreehandElement freehand when freehand.Points.Count > 1:
                var geometry = new StreamGeometry();
                using (var g = geometry.Open())
                {
                    g.BeginFigure(freehand.Points[0], false, false);
                    g.PolyLineTo(freehand.Points.Skip(1).ToArray(), true, true);
                }
                geometry.Freeze();
                context.DrawGeometry(null, pen, geometry);
                break;
            case TextElement text:
                var formatted = TextLayout.Create(text.Text, text.FontSize, new SolidColorBrush(text.Color));
                context.DrawText(formatted, text.Bounds.Location);
                break;
            case HighlightElement highlight:
                var highlightBrush = new SolidColorBrush(Color.FromRgb(highlight.Color.R, highlight.Color.G, highlight.Color.B));
                highlightBrush.Freeze();
                context.DrawRectangle(highlightBrush, null, highlight.Bounds);
                break;
            case BlurElement:
            case PixelateElement:
                break;
            case StepMarkerElement marker:
                var markerBrush = new SolidColorBrush(marker.Color);
                markerBrush.Freeze();
                var diameter = Math.Min(marker.Bounds.Width, marker.Bounds.Height);
                context.DrawEllipse(Brushes.White, new Pen(markerBrush, diameter / 12), new Point(marker.Bounds.X + marker.Bounds.Width / 2, marker.Bounds.Y + marker.Bounds.Height / 2), marker.Bounds.Width / 2, marker.Bounds.Height / 2);
                var markerText = new FormattedText(marker.Number.ToString(System.Globalization.CultureInfo.InvariantCulture), System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight, new Typeface("Segoe UI Semibold"), diameter / 2, markerBrush, 1.0);
                if (markerText.Width > diameter * .75) markerText.SetFontSize(diameter / 2 * diameter * .75 / markerText.Width);
                context.DrawText(markerText, new Point(marker.Bounds.X + (marker.Bounds.Width - markerText.Width) / 2, marker.Bounds.Y + (marker.Bounds.Height - markerText.Height) / 2));
                break;
            case ImageElement image:
                context.DrawImage(image.Image, image.Bounds);
                break;
        }
    }

    internal static void DrawSelectionAdorners(DrawingContext context, EditorElement element, Rect crop, double zoom)
    {
        if (element is ArrowElement arrow)
        {
            foreach (var point in new[] { arrow.Start, ArrowGeometry.Middle(arrow), arrow.End })
                context.DrawEllipse(Brushes.White, new Pen(Brushes.MediumSpringGreen, 1.5 / zoom),
                    point - new Vector(crop.X, crop.Y), 4 / zoom, 4 / zoom);
            return;
        }
        var bounds = new Rect(element.Bounds.X - crop.X, element.Bounds.Y - crop.Y, element.Bounds.Width, element.Bounds.Height);
        var pen = new Pen(Brushes.MediumSpringGreen, 2 / zoom) { DashStyle = DashStyles.Dash };
        pen.Freeze();
        context.DrawRectangle(null, pen, bounds);
        foreach (var point in new[] { new Point(bounds.Left, bounds.Top), new Point(bounds.Right, bounds.Top), new Point(bounds.Left, bounds.Bottom), new Point(bounds.Right, bounds.Bottom) })
        {
            context.DrawRectangle(Brushes.White, new Pen(Brushes.MediumSpringGreen, 1 / zoom), new Rect(point.X - 5 / zoom, point.Y - 5 / zoom, 10 / zoom, 10 / zoom));
        }
    }

}

public sealed class EditorSurface : FrameworkElement
{
    private readonly EditorDocument _document;
    private readonly EditorDrawingCache _drawing;
    public double Zoom { get; set; } = 1;
    public EditorElement? EditingElement { get; set; }

    public EditorSurface(EditorDocument document)
    {
        _document = document;
        _drawing = new EditorDrawingCache(document);
        Focusable = true;
        SnapsToDevicePixels = true;
    }

    protected override Size MeasureOverride(Size availableSize) => new(_document.Width, _document.Height);
    protected override Size ArrangeOverride(Size finalSize) => new(_document.Width, _document.Height);
    protected override void OnRender(DrawingContext drawingContext)
    {
        drawingContext.DrawDrawing(_drawing.Update(EditingElement));
        foreach (var element in _document.Elements)
            if (element.IsSelected && element != EditingElement) EditorRenderer.DrawSelectionAdorners(drawingContext, element, new Rect(0, 0, _document.Width, _document.Height), Zoom);
    }
}
