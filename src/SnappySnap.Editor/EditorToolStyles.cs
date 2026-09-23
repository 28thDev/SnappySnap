using System.Globalization;
using System.Windows.Media;
using SnappySnap.Core;

namespace SnappySnap.Editor;

public sealed class EditorToolStyles(EditorSettings? settings = null)
{
    private readonly EditorSettings _settings = EditorSettings.Normalize(settings);
    public ElementStyle Get(EditorTool tool)
    {
        if (!_settings.Styles.TryGetValue(tool.ToString(), out var style))
            return new(Colors.Red, Colors.Transparent, 5, 1, 20);
        return new(Parse(style.Color, Colors.Red), Parse(style.FillColor, Colors.Transparent), style.StrokeWidth ?? 5, style.Opacity ?? 1, style.FontSize ?? 20, style.StepDiameter ?? EditorSettings.DefaultStepDiameter);
    }
    public AnnotationStyleSettings Set(EditorTool tool, ElementStyle value)
    {
        if (!_settings.Styles.TryGetValue(tool.ToString(), out var previous)) throw new ArgumentException("Tool has no persistent style.", nameof(tool));
        var style = new AnnotationStyleSettings(previous.Color is null ? null : value.Color.ToString(CultureInfo.InvariantCulture),
            previous.FillColor is null ? null : value.FillColor.ToString(CultureInfo.InvariantCulture), previous.StrokeWidth is null ? null : value.StrokeWidth,
            previous.Opacity is null ? null : value.Opacity, previous.FontSize is null ? null : value.FontSize,
            previous.StepDiameter is null ? null : value.StepDiameter);
        _settings.Styles[tool.ToString()] = style;
        return style;
    }
    public static EditorTool? ToolOf(EditorElement element) => element switch
    {
        ArrowElement => EditorTool.Arrow, RectangleElement => EditorTool.Rectangle, LineElement => EditorTool.Line,
        FreehandElement => EditorTool.Freehand, TextElement => EditorTool.Text, HighlightElement => EditorTool.Highlight,
        StepMarkerElement => EditorTool.StepMarker, _ => null
    };
    private static Color Parse(string? value, Color fallback) => value is null ? fallback : (Color)ColorConverter.ConvertFromString(value);
}
