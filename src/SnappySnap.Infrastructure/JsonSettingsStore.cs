using System.Text.Json;
using System.Text.Json.Serialization;
using SnappySnap.Core;
using SnappySnap.Localization;

namespace SnappySnap.Infrastructure;

public sealed class JsonSettingsStore : ISettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        AllowTrailingCommas = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new AnnotationStyleConverter() }
    };

    private readonly AppPaths _paths;
    private readonly IAppLogger _logger;

    public JsonSettingsStore(AppPaths paths, IAppLogger logger)
    {
        _paths = paths;
        _logger = logger;
    }

    public async Task<AppSettings> LoadAsync(CancellationToken cancellationToken)
    {
        _paths.EnsureDirectories();
        if (!File.Exists(_paths.SettingsPath))
        {
            var defaults = AppSettings.Defaults();
            await SaveAsync(defaults, cancellationToken).ConfigureAwait(false);
            return defaults;
        }

        try
        {
            AppSettings settings;
            await using (var stream = File.OpenRead(_paths.SettingsPath))
                settings = await JsonSerializer.DeserializeAsync<AppSettings>(stream, JsonOptions, cancellationToken).ConfigureAwait(false) ?? AppSettings.Defaults();
            var originalVersion = settings.SchemaVersion;
            var migrated = Migrate(settings);
            if (migrated.SchemaVersion != originalVersion)
            {
                await SaveAsync(migrated, cancellationToken).ConfigureAwait(false);
            }
            return Normalize(migrated);
        }
        catch (JsonException ex)
        {
            var backupPath = _paths.SettingsPath + $".corrupt-{DateTime.UtcNow:yyyyMMdd-HHmmssfff}";
            // If preservation fails, propagate the IO error rather than overwrite the user's profile.
            File.Move(_paths.SettingsPath, backupPath, overwrite: false);

            _logger.Error("Settings were invalid; defaults were restored.", ex, new Dictionary<string, object?> { ["settingsPath"] = _paths.SettingsPath });
            var defaults = AppSettings.Defaults();
            await SaveAsync(defaults, cancellationToken).ConfigureAwait(false);
            return defaults;
        }
    }

    public async Task SaveAsync(AppSettings settings, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);
        _paths.EnsureDirectories();
        settings = Normalize(Migrate(settings));
        var tempPath = _paths.SettingsPath + ".tmp";
        await using (var stream = File.Create(tempPath))
        {
            await JsonSerializer.SerializeAsync(stream, settings, JsonOptions, cancellationToken).ConfigureAwait(false);
            await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
        }

        if (File.Exists(_paths.SettingsPath))
        {
            File.Replace(tempPath, _paths.SettingsPath, null);
        }
        else
        {
            File.Move(tempPath, _paths.SettingsPath);
        }
    }

    private static AppSettings Migrate(AppSettings settings)
    {
        if (settings.SchemaVersion < 3)
        {
            if (settings.General?.ShelfRecentCount == 20) settings.General.ShelfRecentCount = 50;
            settings.SchemaVersion = 3;
        }
        if (settings.SchemaVersion < 4) settings.SchemaVersion = 4;
        if (settings.SchemaVersion < 5)
        {
            if (settings.Editor?.Styles is not null
                && settings.Editor.Styles.TryGetValue("Arrow", out var arrow)
                && arrow?.StrokeWidth == 5)
                settings.Editor.Styles["Arrow"] = arrow with { StrokeWidth = EditorSettings.DefaultArrowStrokeWidth };
            settings.SchemaVersion = 5;
        }
        return settings;
    }

    private static AppSettings Normalize(AppSettings settings)
    {
        settings.Editor = EditorSettings.Normalize(settings.Editor);
        settings.Shelf = ShelfPreferences.Normalize(settings.Shelf);
        settings.Updates ??= new UpdateSettings();
        settings.General ??= new GeneralSettings();
        if (!L.IsSupported(settings.General.Language)) settings.General.Language = L.DefaultLanguage;
        if (settings.General.Theme is not ("System" or "Light" or "Dark")) settings.General.Theme = "System";
        settings.Hotkeys ??= new HotkeySettings();
        settings.Screenshot ??= new ScreenshotSettings();
        settings.Recording ??= new RecordingSettings();
        settings.General.ShelfRecentCount = Math.Clamp(settings.General.ShelfRecentCount, 1, 500);
        settings.Recording.FrameRate = Math.Clamp(settings.Recording.FrameRate, 1, 60);
        settings.Recording.ClickRippleRadiusPx = Math.Clamp(settings.Recording.ClickRippleRadiusPx, 4, 200);
        settings.Recording.ClickRippleDurationMs = Math.Clamp(settings.Recording.ClickRippleDurationMs, 80, 1000);
        return settings;
    }
}

// Recover individual invalid preference values without discarding unrelated application settings.
internal sealed class AnnotationStyleConverter : JsonConverter<AnnotationStyleSettings>
{
    public override AnnotationStyleSettings Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options)
    {
        using var document = JsonDocument.ParseValue(ref reader);
        var root = document.RootElement;
        string? Text(string name) => root.ValueKind == JsonValueKind.Object && root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
        double? Number(string name) => root.ValueKind == JsonValueKind.Object && root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out var number) ? number : null;
        return new(Text("color"), Text("fillColor"), Number("strokeWidth"), Number("opacity"), Number("fontSize"), Number("stepDiameter"));
    }
    public override void Write(Utf8JsonWriter writer, AnnotationStyleSettings value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        if (value.Color is not null) writer.WriteString("color", value.Color);
        if (value.FillColor is not null) writer.WriteString("fillColor", value.FillColor);
        if (value.StrokeWidth.HasValue) writer.WriteNumber("strokeWidth", value.StrokeWidth.Value);
        if (value.Opacity.HasValue) writer.WriteNumber("opacity", value.Opacity.Value);
        if (value.FontSize.HasValue) writer.WriteNumber("fontSize", value.FontSize.Value);
        if (value.StepDiameter.HasValue) writer.WriteNumber("stepDiameter", value.StepDiameter.Value);
        writer.WriteEndObject();
    }
}
