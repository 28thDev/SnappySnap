using System.Windows;
using SnappySnap.Core;
using SnappySnap.Editor;
using Xunit;

namespace SnappySnap.Editor.Tests;

public sealed class EditorModelTests
{
    [Fact]
    public void Transform_command_moves_and_resizes_geometry_with_undo_redo()
    {
        var document = new EditorDocument(new CapturedImage(640, 480, new byte[640 * 480 * 4]));
        var rectangle = new RectangleElement(new Rect(10, 20, 100, 60));
        var history = new EditorCommandHistory();
        history.Execute(new AddElementCommand(rectangle), document);
        var before = EditorGeometry.Capture(rectangle);
        var after = EditorGeometry.Resize(EditorGeometry.Translate(before, new Vector(30, 40)), new Rect(40, 60, 200, 120));

        history.Execute(new TransformElementCommand(rectangle, before, after), document);
        Assert.Equal(new Rect(40, 60, 200, 120), rectangle.Bounds);
        history.Undo(document);
        Assert.Equal(new Rect(10, 20, 100, 60), rectangle.Bounds);
        history.Redo(document);
        Assert.Equal(new Rect(40, 60, 200, 120), rectangle.Bounds);
    }

    [Fact]
    public void Fifty_mixed_commands_undo_and_redo_without_document_corruption()
    {
        var document = new EditorDocument(new CapturedImage(800, 600, new byte[800 * 600 * 4]));
        var history = new EditorCommandHistory();
        var elements = new List<RectangleElement>();
        for (var index = 0; index < 50; index++)
        {
            var element = new RectangleElement(new Rect(index, index, 20, 20));
            elements.Add(element);
            history.Execute(new AddElementCommand(element), document);
            var before = EditorGeometry.Capture(element);
            var after = EditorGeometry.Translate(before, new Vector(5, -2));
            history.Execute(new TransformElementCommand(element, before, after), document);
        }

        for (var index = 0; index < 100; index++) history.Undo(document);
        Assert.Empty(document.Elements);
        for (var index = 0; index < 100; index++) history.Redo(document);
        Assert.Equal(50, document.Elements.Count);
        Assert.All(elements, element => Assert.Equal(new Size(20, 20), element.Bounds.Size));
    }

    [Fact]
    public void Crop_command_is_reversible()
    {
        var document = new EditorDocument(new CapturedImage(400, 300, new byte[400 * 300 * 4]));
        var history = new EditorCommandHistory();
        history.Execute(new CropCommand(Rect.Empty, new Rect(20, 30, 120, 80)), document);
        Assert.True(document.HasCrop);
        history.Undo(document);
        Assert.False(document.HasCrop);
        history.Redo(document);
        Assert.Equal(new Rect(20, 30, 120, 80), document.CropRect);
    }
    [Fact]
    public void PropertyEditsUndoAndRedoTogetherAndTextBoundsFollowFontSize()
    {
        var document = new EditorDocument(new CapturedImage(100, 100, new byte[40000]));
        var text = new TextElement("Label", new Point(10, 20));
        var history = new EditorCommandHistory();
        history.Execute(new AddElementCommand(text), document);
        var before = ElementStyle.Read(text);
        var after = before with { Color = System.Windows.Media.Colors.MediumSpringGreen, FillColor = System.Windows.Media.Colors.Gold, StrokeWidth = 7, Opacity = .6, FontSize = 32 };
        var bounds = text.Bounds;
        history.Execute(new StyleElementCommand(text, before, after), document);
        Assert.Equal(after, ElementStyle.Read(text));
        Assert.Equal(bounds.Location, text.Bounds.Location);
        Assert.True(text.Bounds.Height > bounds.Height);
        history.Undo(document);
        Assert.Equal(before, ElementStyle.Read(text));
        Assert.Equal(bounds, text.Bounds);
        history.Redo(document);
        Assert.Equal(after, ElementStyle.Read(text));
    }
    [Fact]
    public async Task ExportRendersStyledAnnotationAndCropOffTheCallingThread()
    {
        var document = new EditorDocument(new CapturedImage(80, 60, new byte[80 * 60 * 4]));
        document.Elements.Add(new RectangleElement(new Rect(5, 5, 40, 30)) { FillColor = System.Windows.Media.Colors.Red, Color = System.Windows.Media.Colors.Red });
        document.CropRect = new Rect(10, 10, 20, 15);
        var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "snappysnap-export-" + Guid.NewGuid().ToString("N") + ".png");
        try
        {
            await EditorRenderer.SavePngAsync(document, path, CancellationToken.None);
            var result = EditorRenderer.LoadImage(path);
            Assert.Equal(20, result.PixelWidth); Assert.Equal(15, result.PixelHeight);
            var pixels = EditorRenderer.ToCapturedImage(result).Bgra32;
            Assert.Equal(255, pixels[2]); Assert.Equal(255, pixels[3]);
        }
        finally { System.IO.File.Delete(path); }
    }
}
