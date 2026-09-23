using System.Windows;
using System.Windows.Media;
using SnappySnap.Core;
using SnappySnap.Editor;
using Xunit;

namespace SnappySnap.Editor.Tests;

public sealed class StepMarkerTests
{
    [Fact]
    public void SizeChangesPreserveCenterAndSupportUndoRedo()
    {
        var document = new EditorDocument(new CapturedImage(400, 400, new byte[400 * 400 * 4]));
        var step = new StepMarkerElement(1, new Point(200, 200)); document.Elements.Add(step);
        var original = step.Bounds;
        var style = ElementStyle.Read(step);
        var history = new EditorCommandHistory();
        history.Execute(new StyleElementCommand(step, style, style with { StepDiameter = 160 }), document);
        Assert.Equal(new Rect(120, 120, 160, 160), step.Bounds);
        history.Undo(document); Assert.Equal(original, step.Bounds);
        history.Redo(document); Assert.Equal(new Rect(120, 120, 160, 160), step.Bounds);
        var resized = step.Bounds;
        (ElementStyle.Read(step) with { Color = Colors.Blue }).Apply(step);
        Assert.Equal(resized, step.Bounds);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(12)]
    [InlineData(123)]
    public void CircleNumberAndOutlineScaleTogetherInExportDrawing(int number)
    {
        var document = new EditorDocument(new CapturedImage(400, 400, new byte[400 * 400 * 4]));
        var step = new StepMarkerElement(number, new Point(200, 200)); document.Elements.Add(step);
        var small = Flatten(EditorRenderer.CreateVisual(document).Drawing).ToArray();
        var circle = small.OfType<GeometryDrawing>().Single(x => x.Geometry is EllipseGeometry);
        var glyph = small.OfType<GlyphRunDrawing>().Single();
        (ElementStyle.Read(step) with { StepDiameter = 128 }).Apply(step);
        var large = Flatten(EditorRenderer.CreateVisual(document).Drawing).ToArray();
        var largeCircle = large.OfType<GeometryDrawing>().Single(x => x.Geometry is EllipseGeometry);
        var largeGlyph = large.OfType<GlyphRunDrawing>().Single();
        Assert.Equal(circle.Geometry.Bounds.Width * 2, largeCircle.Geometry.Bounds.Width, 4);
        Assert.Equal(circle.Pen.Thickness * 2, largeCircle.Pen.Thickness, 4);
        // WPF rounds fitted multi-digit glyph metrics at each font size.
        Assert.InRange(Math.Abs(glyph.GlyphRun.FontRenderingEmSize * 2 - largeGlyph.GlyphRun.FontRenderingEmSize), 0, .01);
        Assert.True(largeGlyph.Bounds.Width <= step.Bounds.Width * .8);
    }

    [Fact]
    public void StepSizePreferenceIsIndependentOfOtherTools()
    {
        var styles = new EditorToolStyles();
        var arrow = styles.Get(EditorTool.Arrow);
        var preference = styles.Set(EditorTool.StepMarker, styles.Get(EditorTool.StepMarker) with { StepDiameter = 144 });
        Assert.Equal(144, preference.StepDiameter);
        Assert.Null(preference.StrokeWidth); Assert.Null(preference.FontSize);
        var next = new StepMarkerElement(2, new Point(200, 200)); styles.Get(EditorTool.StepMarker).Apply(next);
        Assert.Equal(144, next.Bounds.Width); Assert.Equal(arrow, styles.Get(EditorTool.Arrow));
    }

    private static IEnumerable<Drawing> Flatten(Drawing drawing)
    {
        yield return drawing;
        if (drawing is DrawingGroup group)
            foreach (var child in group.Children)
                foreach (var nested in Flatten(child)) yield return nested;
    }
}
