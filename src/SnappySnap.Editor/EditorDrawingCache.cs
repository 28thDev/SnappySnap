using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace SnappySnap.Editor;

/// <summary>Retains vector drawings; only region effects rasterize the preceding content.</summary>
public sealed class EditorDrawingCache(EditorDocument document)
{
    private sealed record Entry(EditorElement Element, long Revision, Drawing Drawing);
    private readonly List<Entry> _entries = new();
    private DrawingGroup? _result;
    private EditorElement? _hidden;

    public DrawingGroup Update(EditorElement? hidden = null)
    {
        if (_hidden != hidden) { _hidden = hidden; _entries.Clear(); _result = null; }
        if (_result is not null && _entries.Count == document.Elements.Count &&
            _entries.Select((entry, i) => ReferenceEquals(entry.Element, document.Elements[i]) && entry.Revision == entry.Element.Revision).All(match => match)) return _result;
        var changed = _result is null || _entries.Count != document.Elements.Count;
        var upstreamChanged = false;
        var group = new DrawingGroup();
        group.Children.Add(new ImageDrawing(document.BaseImage, new Rect(0, 0, document.Width, document.Height)));
        for (var i = 0; i < document.Elements.Count; i++)
        {
            var element = document.Elements[i];
            var effect = element is BlurElement or PixelateElement;
            var entry = i < _entries.Count ? _entries[i] : null;
            var dirty = entry is null || !ReferenceEquals(entry.Element, element) || entry.Revision != element.Revision;
            if (dirty || (effect && upstreamChanged))
            {
                Drawing drawing;
                if (element == hidden) { drawing = new DrawingGroup(); drawing.Freeze(); }
                else if (effect) drawing = EffectPatch(group, element.Bounds, element is BlurElement);
                else
                {
                    var visual = new DrawingVisual();
                    using (var dc = visual.RenderOpen()) { dc.PushOpacity(element.Opacity); EditorRenderer.DrawElement(dc, element); dc.Pop(); }
                    drawing = visual.Drawing; drawing.Freeze();
                }
                entry = new Entry(element, element.Revision, drawing);
                if (i < _entries.Count) _entries[i] = entry; else _entries.Add(entry);
                changed = upstreamChanged = true;
            }
            if (effect && entry!.Drawing is ImageDrawing patch)
            {
                // Replace the region, including its alpha, rather than blending it over itself.
                var outside = new GeometryGroup { FillRule = FillRule.EvenOdd };
                outside.Children.Add(new RectangleGeometry(new Rect(0, 0, document.Width, document.Height)));
                outside.Children.Add(new RectangleGeometry(patch.Rect)); outside.Freeze();
                group.ClipGeometry = outside; group.Freeze();
                var next = new DrawingGroup(); next.Children.Add(group); next.Children.Add(patch); group = next;
            }
            else group.Children.Add(entry!.Drawing);
        }
        if (_entries.Count > document.Elements.Count) _entries.RemoveRange(document.Elements.Count, _entries.Count - document.Elements.Count);
        if (!changed) return _result!;
        group.Freeze(); return _result = group;
    }

    private Drawing EffectPatch(Drawing input, Rect bounds, bool blur)
    {
        var x = Math.Clamp((int)Math.Floor(bounds.Left), 0, document.Width);
        var y = Math.Clamp((int)Math.Floor(bounds.Top), 0, document.Height);
        var right = Math.Clamp((int)Math.Ceiling(bounds.Right), 0, document.Width);
        var bottom = Math.Clamp((int)Math.Ceiling(bounds.Bottom), 0, document.Height);
        if (right <= x || bottom <= y) { var empty = new DrawingGroup(); empty.Freeze(); return empty; }
        var width = right - x; var height = bottom - y;
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen()) { dc.PushTransform(new TranslateTransform(-x, -y)); dc.DrawDrawing(input); dc.Pop(); }
        var raster = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32); raster.Render(visual);
        var converted = new FormatConvertedBitmap(raster, PixelFormats.Bgra32, null, 0);
        var pixels = new byte[width * height * 4]; converted.CopyPixels(pixels, width * 4, 0);
        var result = blur ? Blur(pixels, width, height) : Pixelate(pixels, width, height);
        var bitmap = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, result, width * 4); bitmap.Freeze();
        var patch = new ImageDrawing(bitmap, new Rect(x, y, width, height)); patch.Freeze(); return patch;
    }

    // Exact 11x11 box average with edge clipping; integer sums avoid intermediate rounding.
    internal static byte[] Blur(byte[] source, int width, int height)
    {
        var horizontal = new int[source.Length];
        for (var y = 0; y < height; y++) for (var c = 0; c < 4; c++)
        {
            var sum = 0;
            for (var x = 0; x < Math.Min(width, 6); x++) sum += source[(y * width + x) * 4 + c];
            for (var x = 0; x < width; x++)
            {
                horizontal[(y * width + x) * 4 + c] = sum;
                if (x >= 5) sum -= source[(y * width + x - 5) * 4 + c];
                if (x + 6 < width) sum += source[(y * width + x + 6) * 4 + c];
            }
        }
        var result = new byte[source.Length];
        for (var x = 0; x < width; x++) for (var c = 0; c < 4; c++)
        {
            var sum = 0; var columns = Math.Min(width - 1, x + 5) - Math.Max(0, x - 5) + 1;
            for (var y = 0; y < Math.Min(height, 6); y++) sum += horizontal[(y * width + x) * 4 + c];
            for (var y = 0; y < height; y++)
            {
                var rows = Math.Min(height - 1, y + 5) - Math.Max(0, y - 5) + 1;
                result[(y * width + x) * 4 + c] = (byte)(sum / (columns * rows));
                if (y >= 5) sum -= horizontal[( (y - 5) * width + x) * 4 + c];
                if (y + 6 < height) sum += horizontal[((y + 6) * width + x) * 4 + c];
            }
        }
        return result;
    }

    private static byte[] Pixelate(byte[] source, int width, int height)
    {
        var result = new byte[source.Length];
        for (var top = 0; top < height; top += 10) for (var left = 0; left < width; left += 10)
        {
            var bottom = Math.Min(height, top + 10); var right = Math.Min(width, left + 10); var count = (bottom - top) * (right - left);
            for (var c = 0; c < 4; c++)
            {
                var sum = 0;
                for (var y = top; y < bottom; y++) for (var x = left; x < right; x++) sum += source[(y * width + x) * 4 + c];
                var value = (byte)(sum / count);
                for (var y = top; y < bottom; y++) for (var x = left; x < right; x++) result[(y * width + x) * 4 + c] = value;
            }
        }
        return result;
    }
}
