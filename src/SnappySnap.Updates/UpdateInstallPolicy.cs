namespace SnappySnap.Updates;

public static class UpdateInstallPolicy
{
    public static void RequireDownloadSpace(long availableBytes, long packageBytes)
    {
        if (availableBytes < packageBytes + 16L * 1024 * 1024) throw new IOException("Not enough free space to download the update.");
    }
    public static string InstalledExecutable => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "SnappySnap", "SnappySnap.exe");
    public static string CacheRoot => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SnappySnap", "Updates");
    public static void RequireInstalledExecutable(string? path)
    {
        if (!string.Equals(path is null ? null : Path.GetFullPath(path), InstalledExecutable, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("This is a folder build. Install SnappySnap with Setup before using automatic installation.");
    }
    public static void RequireSuccessfulSetup(int code, string actualVersion, string expectedVersion)
    {
        if (code != 0) throw new InvalidOperationException(code switch
        {
            31 => "Setup could not preserve the Windows startup setting. Run the retained Setup again after checking its log.",
            32 => "Windows restart is required. Restart Windows manually, then check the installation.",
            _ => $"Setup did not complete successfully (exit code {code}). Run the retained Setup to repair the installation."
        });
        if (actualVersion.Trim() != expectedVersion + ".0") throw new InvalidDataException("Setup finished, but the installed application version does not match. Run the retained Setup to repair it.");
    }
}
