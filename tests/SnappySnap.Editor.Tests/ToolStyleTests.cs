using System.Windows;
using System.Windows.Media;
using SnappySnap.Core;
using SnappySnap.Editor;
using Xunit;

namespace SnappySnap.Editor.Tests;

public sealed class ToolStyleTests
{
    [Fact]
    public void Styles_are_independent_and_keep_only_applicable_properties()
    {
        var styles = new EditorToolStyles();
        var before = styles.Get(EditorTool.Text);
        var arrow = styles.Get(EditorTool.Arrow) with { Color = Colors.Blue, StrokeWidth = 9, FontSize = 40 };
        var preference = styles.Set(EditorTool.Arrow, arrow);
        Assert.Null(preference.FontSize); Assert.Null(preference.FillColor);
        Assert.Equal(9, styles.Get(EditorTool.Arrow).StrokeWidth);
        Assert.Equal(before, styles.Get(EditorTool.Text));
        Assert.Equal(Colors.Gold, styles.Get(EditorTool.Highlight).Color);
        Assert.Equal(.35, styles.Get(EditorTool.Highlight).Opacity);
    }

    [Fact]
    public void Undo_document_style_does_not_undo_tool_preference()
    {
        var styles = new EditorToolStyles();
        var document = new EditorDocument(new CapturedImage(100, 100, new byte[40000]));
        var arrow = new ArrowElement(new(5, 5), new(50, 50)); document.Elements.Add(arrow);
        var before = ElementStyle.Read(arrow); var after = before with { StrokeWidth = 11 };
        var history = new EditorCommandHistory(); history.Execute(new StyleElementCommand(arrow, before, after), document);
        styles.Set(EditorTool.Arrow, after); history.Undo(document);
        Assert.Equal(before, ElementStyle.Read(arrow)); Assert.Equal(11, styles.Get(EditorTool.Arrow).StrokeWidth);
    }
}
