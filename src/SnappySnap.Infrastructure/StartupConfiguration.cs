using SnappySnap.Core;

namespace SnappySnap.Infrastructure;

/// <summary>Installer entry point using the same settings and startup registration as Settings.</summary>
public static class StartupConfiguration
{
    public static async Task ApplyAsync(string mode, ISettingsStore store, Action<bool> register,
        CancellationToken cancellationToken = default)
    {
        if (mode is not ("on" or "off" or "preserve"))
            throw new ArgumentException("Expected on, off or preserve.", nameof(mode));
        var settings = await store.LoadAsync(cancellationToken).ConfigureAwait(false);
        var previous = settings.General.StartWithWindows;
        settings.General.StartWithWindows = mode switch { "on" => true, "off" => false, _ => previous };
        var changed = settings.General.StartWithWindows != previous;
        if (changed) await store.SaveAsync(settings, cancellationToken).ConfigureAwait(false);
        try { register(settings.General.StartWithWindows); }
        catch
        {
            settings.General.StartWithWindows = previous;
            if (changed) await store.SaveAsync(settings, cancellationToken).ConfigureAwait(false);
            throw;
        }
    }
}
