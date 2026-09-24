using SnappySnap.Localization;

namespace SnappySnap.Core;

public sealed class AppSettings
{
    public int SchemaVersion { get; set; } = 6;
    public ShelfPreferences Shelf { get; set; } = new();
    public EditorSettings Editor { get; set; } = new();
    public UpdateSettings Updates { get; set; } = new();
    public GeneralSettings General { get; set; } = new();
    public HotkeySettings Hotkeys { get; set; } = new();
    public ScreenshotSettings Screenshot { get; set; } = new();
    public RecordingSettings Recording { get; set; } = new();

    public static AppSettings Defaults() => new();
}

public sealed class GeneralSettings
{
    public bool StartWithWindows { get; set; } = true;
    public string Theme { get; set; } = "System";
    public string Language { get; set; } = L.DefaultLanguage;
    public string CaptureRoot { get; set; } = "%USERPROFILE%\\Pictures\\SnappySnap";
    public int ShelfRecentCount { get; set; } = 50;
}

public sealed class HotkeySettings
{
    public string RegionScreenshot { get; set; } = "Ctrl+E";
    public string FullScreenshot { get; set; } = "PrintScreen";
    public string RegionVideo { get; set; } = "Ctrl+Alt+E";
    public string PauseResumeVideo { get; set; } = "Ctrl+Alt+Space";
    public string? FastRegionScreenshot { get; set; }
    public string? OpenShelf { get; set; }
}

public sealed class ScreenshotSettings
{
    public string Format { get; set; } = "Png";
    public bool OpenEditor { get; set; } = true;
    public bool CopyToClipboard { get; set; } = true;
}

public sealed class RecordingSettings
{
    public string QualityProfile { get; set; } = "Compact";
    public int FrameRate { get; set; } = 30;
    public bool SystemAudioDefault { get; set; } = true;
    public bool MicrophoneDefault { get; set; }
    public bool MousePointer { get; set; } = true;
    public string LeftClickColor { get; set; } = "#FFFFC107";
    public string RightClickColor { get; set; } = "#FFFF6B35";
    public int ClickRippleRadiusPx { get; set; } = 26;
    public int ClickRippleDurationMs { get; set; } = 220;
}

public static class QualityProfileCatalog
{
    public static QualityProfile Compact { get; } = new("Compact", "Compact", 30, 45, true);
    public static QualityProfile Balanced { get; } = new("Balanced", "Balanced", 30, 65, true);
    public static QualityProfile High { get; } = new("High", "High", 30, 75, true);

    public static QualityProfile Resolve(string? key) => key?.ToLowerInvariant() switch
    {
        "balanced" => Balanced,
        "high" => High,
        _ => Compact
    };
}

public static class MediaPathGenerator
{
    public static string Create(string captureRoot, MediaType mediaType, DateTimeOffset timestampUtc, Func<string, bool>? exists = null, string imageFormat = "Png")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(captureRoot);
        exists ??= File.Exists;
        var monthFolder = Path.Combine(captureRoot, timestampUtc.ToLocalTime().ToString("yyyy-MM", System.Globalization.CultureInfo.InvariantCulture));
        var extension = mediaType == MediaType.Screenshot ? (imageFormat.Equals("Jpg", StringComparison.OrdinalIgnoreCase) ? ".jpg" : ".png") : ".mp4";
        var stem = timestampUtc.ToLocalTime().ToString("yyyy-MM-dd_HH-mm-ss", System.Globalization.CultureInfo.InvariantCulture);
        var candidate = Path.Combine(monthFolder, stem + extension);
        var counter = 2;
        while (exists(candidate))
        {
            candidate = Path.Combine(monthFolder, $"{stem}-{counter++}{extension}");
        }

        return candidate;
    }
}
