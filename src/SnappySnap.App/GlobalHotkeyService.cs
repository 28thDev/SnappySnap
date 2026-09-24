using System.Runtime.InteropServices;
using Microsoft.Win32;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Input;
using SnappySnap.Core;
using SnappySnap.Localization;

namespace SnappySnap.App;

[Flags]
public enum HotkeyModifiers : uint
{
    None = 0,
    Alt = 1,
    Control = 2,
    Shift = 4,
    Windows = 8,
    NoRepeat = 0x4000
}

public static class HotkeyParser
{
    internal static string DisplayKey(Key key) => key == Key.PrintScreen ? "PrintScreen" : key.ToString();

    public static bool TryParse(string? display, out HotkeyModifiers modifiers, out Key key)
    {
        modifiers = HotkeyModifiers.None;
        key = Key.None;
        if (string.IsNullOrWhiteSpace(display)) return false;

        var tokens = display.Split('+', StringSplitOptions.TrimEntries);
        if (tokens.Length == 1 && tokens[0].Equals("PrtSc", StringComparison.OrdinalIgnoreCase))
            tokens[0] = "PrintScreen";
        if (tokens.Length < 2 && !tokens[0].Equals("PrintScreen", StringComparison.OrdinalIgnoreCase)) return false;
        foreach (var token in tokens[..^1])
        {
            switch (token.ToLowerInvariant())
            {
                case "ctrl":
                case "control":
                    modifiers |= HotkeyModifiers.Control;
                    break;
                case "alt":
                    modifiers |= HotkeyModifiers.Alt;
                    break;
                case "shift":
                    modifiers |= HotkeyModifiers.Shift;
                    break;
                case "win":
                case "windows":
                    modifiers |= HotkeyModifiers.Windows;
                    break;
                default:
                    return false;
            }
        }

        var keyToken = tokens[^1];
        if (keyToken.Equals("Space", StringComparison.OrdinalIgnoreCase))
        {
            key = Key.Space;
        }
        else if (keyToken.Length == 1 && char.IsLetter(keyToken[0]) && Enum.TryParse(keyToken.ToUpperInvariant(), out Key letter))
        {
            key = letter;
        }
        else if (keyToken.Length == 1 && char.IsDigit(keyToken[0]))
        {
            key = (Key)((int)Key.D0 + (keyToken[0] - '0'));
        }
        else if (keyToken.StartsWith('F') && int.TryParse(keyToken[1..], out var functionNumber) && functionNumber is >= 1 and <= 24)
        {
            key = (Key)((int)Key.F1 + functionNumber - 1);
        }
        else if (!Enum.TryParse(keyToken, true, out key))
        {
            return false;
        }

        return (modifiers != HotkeyModifiers.None || key == Key.PrintScreen) && key != Key.None && KeyInterop.VirtualKeyFromKey(key) != 0 && key is not (Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin);
    }
}

public sealed record HotkeyRegistration(int Id, string Shortcut, string? Error, int WindowsError = 0, bool Attempted = true)
{
    public bool Registered => Attempted && Error is null;
}

public sealed class GlobalHotkeyService : IDisposable
{
    private const int WmHotkey = 0x0312;
    private const string WindowsPrintScreenWarning = "Windows opens screen capture with Print Screen. Turn this off in Windows Settings > Accessibility > Keyboard, or choose another shortcut.";
    private readonly IAppLogger _logger;
    private readonly HwndSource _source;
    private readonly Dictionary<int, string> _registered = new();
    private IReadOnlyList<HotkeyRegistration> _results = Array.Empty<HotkeyRegistration>();
    public event EventHandler? RegistrationsChanged;
    public IReadOnlyList<HotkeyRegistration> Results { get => _results; private set { _results = value; RegistrationsChanged?.Invoke(this, EventArgs.Empty); } }
    public string Warning => string.Join("\n", Results.Select(r => (r.Shortcut, Error: AvailabilityError(r))).Where(x => x.Error is not null).Select(x => $"{x.Shortcut}: {L.T(x.Error!)}"));
    public static string? AvailabilityError(HotkeyRegistration registration)
    {
        if (registration.Error is not null) return registration.Error;
        return registration.Registered && HotkeyParser.TryParse(registration.Shortcut, out var modifiers, out var key)
            && modifiers == HotkeyModifiers.None && key == Key.PrintScreen && WindowsUsesPrintScreenForSnipping()
                ? WindowsPrintScreenWarning : null;
    }

    private static bool WindowsUsesPrintScreenForSnipping()
    {
        try
        {
            var value = Registry.GetValue(@"HKEY_CURRENT_USER\Control Panel\Keyboard", "PrintScreenKeyForSnippingEnabled", null);
            return value is int enabled ? enabled != 0 : OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22621);
        }
        catch (UnauthorizedAccessException) { return false; }
        catch (System.Security.SecurityException) { return false; }
    }
    public static Dictionary<int, string> Bindings(HotkeySettings settings)
    {
        var bindings = new Dictionary<int, string> { [1001] = settings.RegionScreenshot, [1002] = settings.RegionVideo, [1003] = settings.PauseResumeVideo, [1005] = settings.FullScreenshot };
        if (!string.IsNullOrWhiteSpace(settings.OpenShelf)) bindings.Add(1004, settings.OpenShelf);
        return bindings;
    }
    public static string Normalize(string display)
    {
        if (!HotkeyParser.TryParse(display, out var modifiers, out var key)) return display.Trim();
        return (modifiers.HasFlag(HotkeyModifiers.Control) ? "Ctrl+" : "") +
            (modifiers.HasFlag(HotkeyModifiers.Alt) ? "Alt+" : "") +
            (modifiers.HasFlag(HotkeyModifiers.Shift) ? "Shift+" : "") +
            (modifiers.HasFlag(HotkeyModifiers.Windows) ? "Win+" : "") + HotkeyParser.DisplayKey(key);
    }
    public IReadOnlyDictionary<int, string> Snapshot() => new Dictionary<int, string>(_registered);
    public IReadOnlyList<HotkeyRegistration> Apply(IReadOnlyDictionary<int, string> bindings, bool rollbackOnFailure = true)
    {
        var normalized = bindings.ToDictionary(x => x.Key, x => Normalize(x.Value));
        var duplicate = normalized.GroupBy(x => x.Value, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1).SelectMany(g => g.Select(x => x.Key)).ToHashSet();
        var invalid = normalized.Select(x => new HotkeyRegistration(x.Key, x.Value,
            duplicate.Contains(x.Key) ? "Duplicate shortcut" : !HotkeyParser.TryParse(x.Value, out _, out _) ? "Invalid shortcut" : null)).ToArray();
        if (invalid.Any(x => !x.Registered))
        {
            if (rollbackOnFailure) return invalid.Select(x => x with { Attempted = false }).ToArray();
            UnregisterAll();
            Results = invalid.Select(x => x.Registered ? Register(x.Id, x.Shortcut) : x).ToArray();
            return Results;
        }
        var previous = Snapshot();
        var previousResults = Results;
        UnregisterAll(); // Release the complete set so swapping two actions is valid.
        var results = normalized.Select(x => Register(x.Key, x.Value)).ToArray();
        if (rollbackOnFailure && results.Any(x => !x.Registered))
        {
            Restore(previous, previousResults);
        }
        else Results = results;
        return results;
    }
    public void Restore(IReadOnlyDictionary<int, string> bindings, IReadOnlyList<HotkeyRegistration> previousResults)
    {
        UnregisterAll();
        var restored = bindings.Select(x => Register(x.Key, x.Value)).ToArray();
        Results = restored.Concat(previousResults.Where(x => !bindings.ContainsKey(x.Id))).ToArray();
        if (restored.Any(x => !x.Registered)) _logger.Warn("Could not restore every previous hotkey: " + Warning);
    }
    public bool Probe(string shortcut)
    {
        if (!HotkeyParser.TryParse(shortcut, out var modifiers, out var key)) return false;
        if (modifiers == HotkeyModifiers.None && key == Key.PrintScreen && WindowsUsesPrintScreenForSnipping()) return false;
        const int probeId = 2000;
        if (!RegisterHotKey(_source.Handle, probeId, (uint)(modifiers | HotkeyModifiers.NoRepeat), (uint)KeyInterop.VirtualKeyFromKey(key))) return false;
        UnregisterHotKey(_source.Handle, probeId);
        return true;
    }
    private HotkeyRegistration Register(int id, string shortcut)
    {
        if (!HotkeyParser.TryParse(shortcut, out var modifiers, out var key)) return new(id, shortcut, "Invalid shortcut");
        if (TryRegister(id, modifiers, key)) { _registered[id] = shortcut; return new(id, shortcut, null); }
        var error = _lastError;
        return new(id, shortcut, error == 1409 ? "Already used by another application" : "Windows rejected this shortcut.", error);
    }
    private int _lastError;

    public GlobalHotkeyService(HiddenHostWindow host, IAppLogger logger)
    {
        _logger = logger;
        _source = HwndSource.FromHwnd(new WindowInteropHelper(host).Handle) ?? throw new InvalidOperationException("Hotkey host HWND is unavailable.");
        _source.AddHook(WndProc);
    }

    public event EventHandler<int>? HotkeyPressed;

    public void UnregisterAll()
    {
        foreach (var id in _registered.Keys) _ = UnregisterHotKey(_source.Handle, id);
        _registered.Clear();
    }

    private bool TryRegister(int id, HotkeyModifiers modifiers, Key key)
    {
        var virtualKey = KeyInterop.VirtualKeyFromKey(key);
        if (!RegisterHotKey(_source.Handle, id, (uint)(modifiers | HotkeyModifiers.NoRepeat), (uint)virtualKey))
        {
            _lastError = Marshal.GetLastWin32Error();
            _logger.Warn("RegisterHotKey failed.", new Dictionary<string, object?>
            {
                ["id"] = id,
                ["virtualKey"] = virtualKey,
                ["modifiers"] = (uint)modifiers,
                ["win32Error"] = _lastError
            });
            return false;
        }
        return true;
    }

    private nint WndProc(nint hwnd, int message, nint wParam, nint lParam, ref bool handled)
    {
        if (message == WmHotkey)
        {
            handled = true;
            HotkeyPressed?.Invoke(this, wParam.ToInt32());
        }
        return 0;
    }

    public void Dispose()
    {
        UnregisterAll();
        _source.RemoveHook(WndProc);

    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(nint hwnd, int id, uint modifiers, uint virtualKey);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(nint hwnd, int id);
}

public sealed class HiddenHostWindow : Window
{
    public HiddenHostWindow()
    {
        Width = 1;
        Height = 1;
        WindowStyle = WindowStyle.None;
        ShowInTaskbar = false;
        AllowsTransparency = true;
        Background = System.Windows.Media.Brushes.Transparent;
        Opacity = 0;
        ShowActivated = false;
    }
}
