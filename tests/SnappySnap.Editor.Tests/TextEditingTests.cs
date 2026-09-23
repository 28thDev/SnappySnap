using System.Windows;
using System.Windows.Media;
using SnappySnap.Core;
using SnappySnap.Editor;
using Xunit;

namespace SnappySnap.Editor.Tests;

public sealed class TextEditingTests
{
    [Fact]
    public void Text_change_is_one_undoable_command_and_recalculates_multiline_bounds()
    {
        var document = new EditorDocument(new CapturedImage(800, 600, new byte[800 * 600 * 4]));
        var text = new TextElement("До", new(35, 40)); document.Elements.Add(text);
        var bounds = text.Bounds;
        var history = new EditorCommandHistory();
        history.Execute(new ChangeTextCommand(text, text.Text, "После\nSecond line"), document);
        Assert.True(text.Bounds.Height > bounds.Height);
        Assert.Equal(bounds.Location, text.Bounds.Location);
        var expected = TextLayout.Create(text.Text, text.FontSize, Brushes.Black);
        Assert.Equal(expected.WidthIncludingTrailingWhitespace, text.Bounds.Width);
        history.Undo(document);
        Assert.Equal("До", text.Text); Assert.Equal(bounds, text.Bounds); Assert.False(history.CanUndo);
        history.Redo(document); Assert.Equal("После\nSecond line", text.Text);
    }

    [Fact]
    public void Hidden_text_is_preview_only_and_export_keeps_it()
    {
        var document = new EditorDocument(new CapturedImage(200, 100, new byte[200 * 100 * 4]));
        var text = new TextElement("Visible", new(20, 20)); document.Elements.Add(text);
        var cache = new EditorDrawingCache(document);
        var visible = cache.Update(); var hidden = cache.Update(text);
        Assert.NotSame(visible, hidden);
        Assert.Same(hidden, cache.Update(text));
        Assert.Equal("Visible", text.Text); Assert.Single(document.Elements);
        Assert.NotSame(hidden, cache.Update());
        Assert.False(EditorRenderer.CreateVisual(document).Drawing.Bounds.IsEmpty);
    }

    [Fact]
    public void Deleting_cleared_text_can_restore_its_position_in_document()
    {
        var document = new EditorDocument(new CapturedImage(200, 100, new byte[80000]));
        var text = new TextElement("Keep", new(20, 20));
        document.Elements.Add(text); document.Elements.Add(new RectangleElement(new Rect(1, 1, 10, 10)));
        var history = new EditorCommandHistory(); history.Execute(new DeleteElementCommand(text), document);
        Assert.Single(document.Elements); history.Undo(document); Assert.Same(text, document.Elements[0]);
        history.Redo(document); Assert.Single(document.Elements);
    }
}
