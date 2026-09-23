namespace SnappySnap.Core;

public sealed record AnnotationStyleSettings(string? Color = null, string? FillColor = null,
    double? StrokeWidth = null, double? Opacity = null, double? FontSize = null, double? StepDiameter = null);

public sealed class EditorSettings
{
    public const double DefaultArrowStrokeWidth = 10;
    public const double MaximumArrowStrokeWidth = 24;
    public const double DefaultStepDiameter = 64;
    public const double MinimumStepDiameter = 24;
    public const double MaximumStepDiameter = 240;
    public Dictionary<string, AnnotationStyleSettings> Styles { get; set; } = Defaults();

    public static Dictionary<string, AnnotationStyleSettings> Defaults() => new(StringComparer.Ordinal)
    {
        ["Arrow"] = new("#FFFF3B30", StrokeWidth: DefaultArrowStrokeWidth, Opacity: 1),
        ["Rectangle"] = new("#FFFF3B30", "#00FFFFFF", 5, 1),
        ["Line"] = new("#FFFF3B30", StrokeWidth: 5, Opacity: 1),
        ["Freehand"] = new("#FFFF3B30", StrokeWidth: 5, Opacity: 1),
        ["Text"] = new("#FFFF3B30", Opacity: 1, FontSize: 20),
        ["Highlight"] = new("#FFFFD700", Opacity: .35),
        ["StepMarker"] = new("#FFFF3B30", Opacity: 1, StepDiameter: DefaultStepDiameter)
    };

    public EditorSettings Copy() => new() { Styles = new(Styles, StringComparer.Ordinal) };

    public static EditorSettings Normalize(EditorSettings? settings)
    {
        var styles = Defaults();
        foreach (var (key, fallback) in styles.ToArray())
        {
            if (settings?.Styles is null || !settings.Styles.TryGetValue(key, out var value) || value is null) continue;
            styles[key] = new(
                Color(value.Color, fallback.Color), Color(value.FillColor, fallback.FillColor),
                Number(value.StrokeWidth, fallback.StrokeWidth, 1, key == "Arrow" ? MaximumArrowStrokeWidth : 12),
                Number(value.Opacity, fallback.Opacity, .1, 1), Number(value.FontSize, fallback.FontSize, 12, 48),
                Number(value.StepDiameter, fallback.StepDiameter, MinimumStepDiameter, MaximumStepDiameter));
        }
        return new() { Styles = styles };
    }

    private static string? Color(string? value, string? fallback) => fallback is null ? null
        : value is { Length: 9 } && value[0] == '#' && value.AsSpan(1).ToString().All(Uri.IsHexDigit) ? value : fallback;
    private static double? Number(double? value, double? fallback, double min, double max) => fallback is null ? null
        : value is { } number && double.IsFinite(number) && number >= min && number <= max ? number : fallback;
}
