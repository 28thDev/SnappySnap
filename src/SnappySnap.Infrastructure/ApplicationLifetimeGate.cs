using System.Security.Principal;

namespace SnappySnap.Infrastructure;

/// <summary>Names are shared with installer/SnappySnap.iss; SID isolates per-user installations.</summary>
public sealed class ApplicationLifetimeGate : IDisposable
{
    private readonly Mutex _running;
    private ApplicationLifetimeGate(Mutex running) => _running = running;

    public static string UserSuffix => WindowsIdentity.GetCurrent().User!.Value;
    public static string RunningName => @"Global\SnappySnap.Running." + UserSuffix;
    public static string HandoffName => @"Global\SnappySnap.UpdateHandoff." + UserSuffix;
    public static string MaintenanceName => @"Global\SnappySnap.Maintenance." + UserSuffix;

    public static ApplicationLifetimeGate? TryEnter(bool configuration, out string? error)
        => TryEnter(configuration, out error, RunningName, MaintenanceName, HandoffName);

    internal static ApplicationLifetimeGate? TryEnter(bool configuration, out string? error, string runningName, string maintenanceName, string handoffName)
    {
        error = null;
        // Create before checking maintenance: Setup checks this marker after creating its own.
        var running = new Mutex(false, runningName, out var created);
        if (!created)
        {
            running.Dispose();
            error = "SnappySnap is already running. Open it from the system tray.";
            return null;
        }
        if (!configuration && (Mutex.TryOpenExisting(maintenanceName, out var maintenance) || Mutex.TryOpenExisting(handoffName, out maintenance)))
        {
            maintenance.Dispose(); running.Dispose();
            error = "SnappySnap is being installed or removed. Please wait until Setup finishes.";
            return null;
        }
        return new ApplicationLifetimeGate(running);
    }

    public void Dispose() => _running.Dispose();
}
