using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SnappySnap.Core;
using SnappySnap.Editor;
using Xunit;

namespace SnappySnap.Editor.Tests;

public sealed class CropTests
{
    [Fact]
    public void RepeatedCropUndoRedoRestoresVisibleBoundsWithoutMovingAnnotations()
    {
        var doc = Document(); var history = new EditorCommandHistory();
        var arrow = new ArrowElement(new(15, 12), new(35, 25)); doc.Elements.Add(arrow);
        var first = new Rect(10, 8, 25, 20); var second = new Rect(15, 12, 10, 10);
        Assert.Equal(new Rect(0, 0, 40, 32), doc.VisibleBounds);
        history.Execute(new CropCommand(doc.CropRect, first), doc);
        history.Execute(new CropCommand(doc.CropRect, doc.ConstrainCrop(second)), doc);
        Assert.Equal(second, doc.VisibleBounds);
        history.Undo(doc); Assert.Equal(first, doc.VisibleBounds);
        history.Undo(doc); Assert.Equal(new Rect(0, 0, 40, 32), doc.VisibleBounds);
        history.Redo(doc); history.Redo(doc); Assert.Equal(second, doc.VisibleBounds);
        Assert.Equal(new Point(15, 12), arrow.Start); Assert.Equal(new Point(35, 25), arrow.End);
        Assert.Equal(40, doc.BaseImage.PixelWidth);
    }

    [Theory]
    [InlineData(-20, -20, 100, 100, 10, 8, 25, 20)]
    [InlineData(15.2, 12.8, 10.1, 8.1, 15, 12, 11, 9)]
    [InlineData(30, 20, 100, 100, 30, 20, 5, 8)]
    public void SelectionIsClippedToCurrentImageAndAlignedToPixels(double x, double y, double w, double h, double ex, double ey, double ew, double eh)
    {
        var doc = Document(); doc.CropRect = new Rect(10, 8, 25, 20);
        Assert.Equal(new Rect(ex, ey, ew, eh), doc.ConstrainCrop(new Rect(x, y, w, h)));
    }

    [Theory]
    [InlineData(12, 12, 1, 10)]
    [InlineData(12, 12, 10, 0)]
    [InlineData(100, 100, 10, 10)]
    public void TinyOrOutsideSelectionDoesNotProduceCrop(double x, double y, double w, double h)
    {
        Assert.Equal(Rect.Empty, Document().ConstrainCrop(new Rect(x, y, w, h)));
    }

    [Fact]
    public void PreviewAndExportUseSameOriginAndClipWithoutCropOutline()
    {
        var doc = Document();
        doc.Elements.Add(new RectangleElement(new Rect(8, 6, 20, 16)) { FillColor = Colors.Red });
        doc.CropRect = new Rect(10, 8, 25, 20);
        var preview = Pixels(EditorRenderer.CreateVisual(doc, includeSelectionAdorners: true));
        Assert.Equal(Pixels(EditorRenderer.CreateVisual(doc)), preview);
        // Render beyond the output extent to verify actual clipping rather than just a smaller target.
        Assert.Equal(255, preview[3]);
        Assert.Equal(0, preview[(30 * 40 + 30) * 4 + 3]);
    }

    private static EditorDocument Document() => new(new CapturedImage(40, 32, Enumerable.Repeat((byte)255, 40 * 32 * 4).ToArray()));
    private static byte[] Pixels(Visual visual)
    {
        var target = new RenderTargetBitmap(40, 32, 96, 96, PixelFormats.Pbgra32); target.Render(visual);
        var bytes = new byte[40 * 32 * 4]; target.CopyPixels(bytes, 40 * 4, 0); return bytes;
    }
}
