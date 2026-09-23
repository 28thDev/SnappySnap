using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SnappySnap.Core;
using SnappySnap.Editor;
using Xunit;

namespace SnappySnap.Editor.Tests;

public sealed class RetainedDrawingTests
{
    [Fact]
    public void CachedPreviewMatchesFreshRenderingAfterEditsUndoReorderAndRemoval()
    {
        var doc = Document(); var history = new EditorCommandHistory();
        var rect = new RectangleElement(new Rect(5, 8, 18, 12)) { FillColor = Colors.Red };
        var freehand = new FreehandElement([new(2, 2), new(30, 25)]);
        doc.Elements.Add(rect); doc.Elements.Add(new BlurElement(new Rect(4, 4, 20, 19)));
        doc.Elements.Add(new PixelateElement(new Rect(12, 13, 23, 14))); doc.Elements.Add(freehand);
        var cache = new EditorDrawingCache(doc);
        byte[] Current() => Pixels(cache.Update(), doc.Width, doc.Height);
        void Match() => Assert.Equal(Pixels(new EditorDrawingCache(doc).Update(), doc.Width, doc.Height), Current());
        var original = Current();
        history.Execute(new MoveElementCommand(rect, new Vector(8, 3)), doc); Match(); Assert.NotEqual(original, Current());
        history.Undo(doc); Match(); Assert.Equal(original, Current());
        history.Redo(doc); Match();
        history.Execute(new StyleElementCommand(rect, ElementStyle.Read(rect), ElementStyle.Read(rect) with { FillColor = Colors.Blue, Opacity = .6 }), doc); Match();
        freehand.Points.Add(new Point(34, 3)); Match();
        doc.Elements.Move(0, 2); Match();
        history.Execute(new DeleteElementCommand(doc.Elements[1]), doc); Match();
        history.Undo(doc); Match();
        doc.Elements.Clear(); Match();
    }

    [Fact]
    public void BlurMatchesExactClippedBoxAverageIncludingAlphaAndEdgePixels()
    {
        var doc = Document();
        var bounds = new Rect(3, 4, 17, 13);
        doc.Elements.Add(new BlurElement(bounds));
        var actual = Pixels(new EditorDrawingCache(doc).Update(), doc.Width, doc.Height);
        var source = new byte[doc.Width * doc.Height * 4]; doc.BaseImage.CopyPixels(source, doc.Width * 4, 0);
        for (var y = 4; y < 17; y++) for (var x = 3; x < 20; x++) for (var c = 0; c < 4; c++)
        {
            var sum = 0; var count = 0;
            for (var sy = Math.Max(4, y - 5); sy <= Math.Min(16, y + 5); sy++)
                for (var sx = Math.Max(3, x - 5); sx <= Math.Min(19, x + 5); sx++) { sum += source[(sy * doc.Width + sx) * 4 + c]; count++; }
            Assert.Equal((byte)(sum / count), actual[(y * doc.Width + x) * 4 + c]);
        }
    }

    [Fact]
    public void PixelateUsesClippedTenPixelBlocksAndPreservesOutsidePixels()
    {
        var doc = Document(); doc.Elements.Add(new PixelateElement(new Rect(3, 4, 17, 13)));
        var pixels = Pixels(new EditorDrawingCache(doc).Update(), doc.Width, doc.Height);
        byte[] At(int x, int y) => pixels.Skip((y * doc.Width + x) * 4).Take(4).ToArray();
        Assert.Equal(new byte[] { 37, 59, 39, 255 }, At(3, 4));
        Assert.Equal(new byte[] { 80, 105, 78, 255 }, At(19, 16));
        Assert.Equal(new byte[] { 10, 28, 14, 255 }, At(2, 4));
    }

    [Fact]
    public void EffectsSamplePrecedingInsertedImageAndDoNotDoubleAlpha()
    {
        var doc = new EditorDocument(new CapturedImage(16, 16, new byte[16 * 16 * 4]));
        var image = BitmapSource.Create(1, 1, 96, 96, PixelFormats.Bgra32, null, new byte[] { 0, 0, 255, 128 }, 4); image.Freeze();
        doc.Elements.Add(new ImageElement(image, new Rect(0, 0, 16, 16)));
        var original = Pixels(new EditorDrawingCache(doc).Update(), 16, 16);
        doc.Elements.Add(new BlurElement(new Rect(4, 4, 8, 8)));
        doc.Elements.Add(new PixelateElement(new Rect(6, 6, 8, 8)));
        Assert.Equal(original, Pixels(new EditorDrawingCache(doc).Update(), 16, 16));
    }

    [Fact]
    public void SelectionAndCropDoNotChangeCachedContentAndExportUsesCrop()
    {
        var doc = Document(); var rect = new RectangleElement(new Rect(8, 8, 20, 15)); doc.Elements.Add(rect);
        var cache = new EditorDrawingCache(doc); var initial = Pixels(cache.Update(), doc.Width, doc.Height);
        rect.IsSelected = true; doc.CropRect = new Rect(5, 5, 20, 20);
        Assert.Equal(initial, Pixels(cache.Update(), doc.Width, doc.Height));
        var exported = Pixels(EditorRenderer.CreateVisual(doc).Drawing, 20, 20);
        for (var y = 0; y < 20; y++) for (var x = 0; x < 20; x++) for (var c = 0; c < 4; c++)
            Assert.Equal(initial[((y + 5) * doc.Width + x + 5) * 4 + c], exported[(y * 20 + x) * 4 + c]);
    }

    private static EditorDocument Document()
    {
        const int width = 40, height = 32; var bytes = new byte[width * height * 4];
        for (var y = 0; y < height; y++) for (var x = 0; x < width; x++) { var i = (y * width + x) * 4; bytes[i] = (byte)(x * 5); bytes[i + 1] = (byte)(y * 7); bytes[i + 2] = (byte)(x * 3 + y * 2); bytes[i + 3] = 255; }
        return new EditorDocument(new CapturedImage(width, height, bytes));
    }
    private static byte[] Pixels(Drawing drawing, int width, int height)
    {
        var visual = new DrawingVisual(); using (var dc = visual.RenderOpen()) dc.DrawDrawing(drawing);
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32); bitmap.Render(visual);
        var bytes = new byte[width * height * 4]; bitmap.CopyPixels(bytes, width * 4, 0); return bytes;
    }
}
