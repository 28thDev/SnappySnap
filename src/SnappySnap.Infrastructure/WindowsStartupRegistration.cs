using Microsoft.Win32;

namespace SnappySnap.Infrastructure;

public sealed class WindowsStartupRegistration
{
    private const string RunKey = "Software\\Microsoft\\Windows\\CurrentVersion\\Run";
    private const string ValueName = "SnappySnap";

    public bool IsEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: false);
        return key?.GetValue(ValueName) is string value && !string.IsNullOrWhiteSpace(value);
    }

    public void SetEnabled(bool enabled, string executablePath)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKey, writable: true) ?? throw new InvalidOperationException("Could not open Windows startup settings.");
        if (enabled)
        {
            key.SetValue(ValueName, $"\"{executablePath}\" --tray");
        }
        else
        {
            key.DeleteValue(ValueName, throwOnMissingValue: false);
        }
    }
}
