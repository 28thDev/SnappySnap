using SnappySnap.Core;

namespace SnappySnap.Infrastructure;

public sealed class AppPaths
{
    public AppPaths(string? localRoot = null, string? defaultPicturesRoot = null)
    {
        LocalRoot = localRoot ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SnappySnap");
        DefaultPicturesRoot = defaultPicturesRoot ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "SnappySnap");
    }

    public string LocalRoot { get; }
    public string DefaultPicturesRoot { get; }
    public string SettingsPath => Path.Combine(LocalRoot, "settings.json");
    public string UpdateCheckStatePath => Path.Combine(LocalRoot, "update-check-state.json");
    public string DatabasePath => Path.Combine(LocalRoot, "snappysnap.db");
    public string LogsPath => Path.Combine(LocalRoot, "Logs");
    public string TempPath => Path.Combine(LocalRoot, "Temp");
    public string RecoveryPath => Path.Combine(LocalRoot, "Recovery");
    public string ThumbnailPath => Path.Combine(LocalRoot, "Cache", "Thumbnails");

    public string ExpandCaptureRoot(string? configuredRoot)
    {
        var value = string.IsNullOrWhiteSpace(configuredRoot) ? DefaultPicturesRoot : configuredRoot;
        value = Environment.ExpandEnvironmentVariables(value);
        return Path.GetFullPath(value.Replace("%USERPROFILE%", Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), StringComparison.OrdinalIgnoreCase));
    }

    public void EnsureDirectories()
    {
        Directory.CreateDirectory(LocalRoot);
        Directory.CreateDirectory(LogsPath);
        Directory.CreateDirectory(TempPath);
        Directory.CreateDirectory(RecoveryPath);
        Directory.CreateDirectory(ThumbnailPath);
    }
}

public sealed class AppVersion
{
    public static string Current => typeof(AppVersion).Assembly.GetName().Version!.ToString(3);
}
