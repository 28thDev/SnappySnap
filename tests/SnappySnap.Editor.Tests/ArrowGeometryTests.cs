using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SnappySnap.Core;
using SnappySnap.Editor;
using Xunit;

namespace SnappySnap.Editor.Tests;

public sealed class ArrowGeometryTests
{
    [Theory]
    [InlineData(100, 0)]
    [InlineData(0, 100)]
    [InlineData(-100, -80)]
    [InlineData(6, 4)]
    public void NewArrowIsStraightWithAProportionalFilledHead(double x, double y)
    {
        var arrow = new ArrowElement(new Point(20, 20), new Point(20 + x, 20 + y));
        Assert.Equal(new Point(20 + x / 2, 20 + y / 2), arrow.Control);
        Assert.Equal(arrow.Control, ArrowGeometry.Middle(arrow));
        var direction = new Vector(x, y); direction.Normalize();
        Assert.True((ArrowGeometry.EndDirection(arrow) - direction).Length < .00001);
        Assert.True(ArrowGeometry.Head(arrow).GetArea() > 0);
        Assert.True(ArrowGeometry.Outline(arrow).GetArea() > 0);
    }

    [Fact]
    public void HeadScalesWithStrokeButIsBoundedByArrowLength()
    {
        var arrow = new ArrowElement(new(0, 0), new(200, 0)) { StrokeWidth = 1 };
        var thin = ArrowGeometry.Head(arrow).Bounds;
        arrow.StrokeWidth = 12;
        var thick = ArrowGeometry.Head(arrow).Bounds;
        Assert.True(thick.Height > thin.Height);
        Assert.True(thick.Width < 50);
        arrow.End = new Point(8, 0); arrow.Control = new Point(4, 0);
        Assert.True(ArrowGeometry.Head(arrow).Bounds.Width <= 3.61);
    }

    [Theory]
    [InlineData(50, -70)]
    [InlineData(50, 100)]
    [InlineData(-100, 20)]
    [InlineData(160, 20)]
    public void MiddleHandleFollowsPointerAndHeadFollowsTerminalTangent(double x, double y)
    {
        var arrow = new ArrowElement(new(10, 10), new(110, 60));
        var middle = new Point(x, y);
        arrow.Control = ArrowGeometry.ControlForMiddle(arrow.Start, arrow.End, middle);
        Assert.Equal(middle, ArrowGeometry.Middle(arrow));
        var tangent = arrow.End - arrow.Control; tangent.Normalize();
        Assert.True((tangent - ArrowGeometry.EndDirection(arrow)).Length < .00001);
        Assert.True(arrow.Bounds.Contains(middle));
        Assert.True(AnnotationHitTesting.Contains(arrow, middle, 1));
        var head = ArrowGeometry.Head(arrow);
        Assert.True(head.FillContains(arrow.End - tangent * 2));
    }

    [Fact]
    public void CurvedHitTestDoesNotSelectTheEmptyChord()
    {
        var arrow = new ArrowElement(new(10, 100), new(210, 100)) { Control = new(110, -80) };
        Assert.True(AnnotationHitTesting.Contains(arrow, new(110, 10), 1));
        Assert.False(AnnotationHitTesting.Contains(arrow, new(110, 100), 1));
    }

    [Fact]
    public void CreateBendMoveEndpointStyleAndDeleteRoundTripThroughHistory()
    {
        var document = new EditorDocument(new CapturedImage(400, 300, new byte[480000]));
        var history = new EditorCommandHistory();
        var arrow = new ArrowElement(new(20, 80), new(220, 80));
        var straight = EditorGeometry.Capture(arrow);
        history.Execute(new AddElementCommand(arrow), document);
        history.Execute(new TransformElementCommand(arrow, straight, straight with { Control = new Point(120, -40) }), document);
        var bent = EditorGeometry.Capture(arrow);
        history.Execute(new MoveElementCommand(arrow, new Vector(30, 50)), document);
        Assert.Equal(new Point(150, 10), arrow.Control);
        var moved = EditorGeometry.Capture(arrow);
        history.Execute(new TransformElementCommand(arrow, moved, moved with { Start = new Point(40, 120), End = new Point(300, 180) }), document);
        history.Execute(new StyleElementCommand(arrow, ElementStyle.Read(arrow), ElementStyle.Read(arrow) with { StrokeWidth = 10, Opacity = .5 }), document);
        history.Execute(new DeleteElementCommand(arrow), document);
        history.Undo(document); history.Undo(document); history.Undo(document);
        Assert.Equal(moved.Control, arrow.Control); Assert.Equal(moved.End, arrow.End);
        history.Undo(document); Assert.Equal(bent.Control, arrow.Control);
        history.Undo(document); Assert.Equal(straight.Control, arrow.Control);
        history.Undo(document); Assert.Empty(document.Elements);
        for (var i = 0; i < 5; i++) history.Redo(document);
        Assert.Equal(new Point(150, 10), arrow.Control); Assert.Equal(new Point(300, 180), arrow.End);
        Assert.Equal(10, arrow.StrokeWidth); Assert.Equal(.5, arrow.Opacity);
    }

    [Fact]
    public async Task ExportAndRetainedPreviewIncludeCurvatureAndInvalidateAfterUndo()
    {
        var document = new EditorDocument(new CapturedImage(240, 140, new byte[240 * 140 * 4]));
        var arrow = new ArrowElement(new(20, 100), new(220, 100)) { Color = Colors.Red, StrokeWidth = 6 };
        document.Elements.Add(arrow);
        var cache = new EditorDrawingCache(document); cache.Update();
        var before = EditorGeometry.Capture(arrow);
        var history = new EditorCommandHistory();
        history.Execute(new TransformElementCommand(arrow, before, before with { Control = new Point(120, -60) }), document);
        byte[] Preview()
        {
            var visual = new DrawingVisual();
            using (var dc = visual.RenderOpen()) dc.DrawDrawing(cache.Update());
            var bitmap = new RenderTargetBitmap(240, 140, 96, 96, PixelFormats.Pbgra32); bitmap.Render(visual);
            var result = new byte[240 * 140 * 4]; bitmap.CopyPixels(result, 240 * 4, 0); return result;
        }
        Assert.True(Preview()[(20 * 240 + 120) * 4 + 2] > 200);
        var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), Guid.NewGuid() + ".png");
        try
        {
            await EditorRenderer.SavePngAsync(document, path, CancellationToken.None);
            var pixels = EditorRenderer.ToCapturedImage(EditorRenderer.LoadImage(path)).Bgra32;
            Assert.True(pixels[(20 * 240 + 120) * 4 + 2] > 200);
            Assert.Equal(0, pixels[(100 * 240 + 120) * 4 + 3]);
            history.Undo(document);
            Assert.Equal(0, Preview()[(20 * 240 + 120) * 4 + 3]);
            await EditorRenderer.SavePngAsync(document, path, CancellationToken.None);
            pixels = EditorRenderer.ToCapturedImage(EditorRenderer.LoadImage(path)).Bgra32;
            Assert.Equal(0, pixels[(20 * 240 + 120) * 4 + 3]);
            Assert.True(pixels[(100 * 240 + 120) * 4 + 2] > 200);
        }
        finally { System.IO.File.Delete(path); }
    }
}
