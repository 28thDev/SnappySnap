using System.Globalization;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;
using SnappySnap.Core;
using SnappySnap.Infrastructure;
using SnappySnap.Presentation;
using SnappySnap.Localization;

namespace SnappySnap.App;

public sealed class SettingsWindow : Window
{
    private static readonly string[] ImageFormats = ["Png", "Jpg"];
    private static readonly string[] RippleDurations = ["150", "220", "300"];
    private static readonly string[] RippleRadii = ["18", "26", "34"];

    private bool _saving;
    public bool IsSaving => _saving;
    private readonly bool _originalStartup;
    private readonly string _originalTheme;
    private bool _saved;
    internal string PreviewTheme => (string)_theme.SelectedValue;
    private readonly GlobalHotkeyService? _hotkeys;
    private readonly SnappySnap.Application.UpdateCoordinator? _updates;
    private readonly Dictionary<string, StackPanel> _sections = new();
    private readonly Dictionary<string, ToggleButton> _navigation = new();
    private readonly ScrollViewer _page = new() { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
    private readonly Dictionary<int, TextBlock> _hotkeyErrors = new();
    private readonly AppSettings _settings;
    private readonly ISettingsStore _store;
    private readonly WindowsStartupRegistration _startup;
    private readonly IAppLogger _logger;
    private readonly CheckBox _start, _systemAudio, _microphone, _clipboard, _openEditor;
    private readonly TextBox _screenshotHotkey, _fullScreenshotHotkey, _videoHotkey, _pauseHotkey, _shelfHotkey, _folder, _recent;
    private readonly ComboBox _duration, _radius, _format, _language, _theme;
    private readonly TextBlock _status;
    private readonly List<ToggleButton> _qualityButtons = new();
    private string _quality;
    private string _leftColor, _rightColor;

    public SettingsWindow(AppSettings settings, ISettingsStore store, WindowsStartupRegistration startup, IAppLogger logger, GlobalHotkeyService? hotkeysService = null, SnappySnap.Application.UpdateCoordinator? updates = null, Func<Task>? installUpdate = null, Func<Task>? checkUpdate = null)
    {
        _settings = JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(settings))!;
        _originalStartup = settings.General.StartWithWindows;
        _originalTheme = settings.General.Theme;
        _hotkeys = hotkeysService; _updates = updates; _store = store; _startup = startup; _logger = logger;
        _quality = settings.Recording.QualityProfile; _leftColor = settings.Recording.LeftClickColor; _rightColor = settings.Recording.RightClickColor;
        Title = "SnappySnap — Settings"; Width = 1040; Height = 780; MinWidth = 780; MinHeight = 600; WindowStartupLocation = WindowStartupLocation.CenterScreen;
        var content = new DockPanel { Margin = new Thickness(28, 22, 28, 18) };
        var bottom = new DockPanel { Margin = new Thickness(0, 16, 0, 0) };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal };
        buttons.Children.Add(Ui.Button("Cancel", "", (_, _) => Close()));
        buttons.Children.Add(Ui.Button("Save changes", "\uE74E", async (_, _) => await SaveAsync(), "PrimaryButton"));
        DockPanel.SetDock(buttons, Dock.Right); bottom.Children.Add(buttons);
        _status = Ui.Text("Save changes to keep your preferences.", 12, "Muted"); AutomationProperties.SetLiveSetting(_status, AutomationLiveSetting.Polite); bottom.Children.Add(_status);
        DockPanel.SetDock(bottom, Dock.Bottom); content.Children.Add(bottom); content.Children.Add(_page);
        var navigation = new StackPanel { Margin = new Thickness(0, 8, 0, 0) };
        StackPanel Section(string title, string glyph)
        {
            var panel = new StackPanel { Margin = new Thickness(0, 0, 12, 0) };
            var heading = Ui.Text(title, 25); heading.Margin = new Thickness(0, 0, 0, 24); panel.Children.Add(heading);
            _sections.Add(title, panel);
            var button = new ToggleButton { Content = Ui.Label(glyph, title), HorizontalContentAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 0, 0, 8), Padding = new Thickness(10, 12, 10, 12) };
            Ui.Localize(button, AutomationProperties.NameProperty, title);
            button.Click += (_, _) => SelectSection(title); _navigation.Add(title, button); navigation.Children.Add(button); return panel;
        }
        var general = Section("General", "\uE713");
        _language = Options(general, "Interface language", L.SupportedLanguages.Select(language => (language.Code, language.DisplayName)).ToArray(), settings.General.Language);
        _theme = Options(general, "Appearance", new[] { ("System", "System"), ("Light", "Light"), ("Dark", "Dark") }, settings.General.Theme);
        _theme.SelectionChanged += (_, _) => Ui.ApplyTheme(PreviewTheme);
        general.Children.Add(new Separator { Margin = new Thickness(0, 12, 0, 18) });
        _start = Toggle(general, "Start with Windows", settings.General.StartWithWindows);
        general.Children.Add(Ui.Text("Capture folder"));
        var folderRow = new DockPanel { Margin = new Thickness(0, 6, 0, RowGap) };
        var browse = Ui.Button("Browse…", "\uE8B7", (_, _) =>
        {
            var dialog = new OpenFolderDialog { Title = L.T("Choose a capture folder") };
            if (dialog.ShowDialog(this) == true) _folder!.Text = dialog.FolderName;
        }); browse.Margin = new Thickness(10, 0, 0, 0); DockPanel.SetDock(browse, Dock.Right); folderRow.Children.Add(browse);
        _folder = new TextBox { Text = settings.General.CaptureRoot }; Ui.Localize(_folder, AutomationProperties.NameProperty, "Capture folder"); folderRow.Children.Add(_folder); general.Children.Add(folderRow);
        _recent = Field(general, "Recent captures in Shelf", settings.General.ShelfRecentCount.ToString(CultureInfo.InvariantCulture));
        Hint(general, "1–500. Files on disk are not deleted.");

        var hotkeys = Section("Hotkeys", "\uE765");
        _screenshotHotkey = Shortcut(hotkeys, "Screenshot region", settings.Hotkeys.RegionScreenshot);
        _fullScreenshotHotkey = Shortcut(hotkeys, "Screenshot full screen", settings.Hotkeys.FullScreenshot);
        _videoHotkey = Shortcut(hotkeys, "Start / stop recording", settings.Hotkeys.RegionVideo);
        _pauseHotkey = Shortcut(hotkeys, "Pause / resume recording", settings.Hotkeys.PauseResumeVideo);
        _shelfHotkey = Shortcut(hotkeys, "Open Shelf", settings.Hotkeys.OpenShelf ?? "", optional: true);
        foreach (var entry in new[] { (1001, _screenshotHotkey, "Ctrl+Shift+F9"), (1005, _fullScreenshotHotkey, ""), (1002, _videoHotkey, "Ctrl+Shift+F10"), (1003, _pauseHotkey, ""), (1004, _shelfHotkey, "") })
        {
            var registration = _hotkeys?.Results.FirstOrDefault(r => r.Id == entry.Item1);
            var error = Ui.Text(registration is null ? "" : GlobalHotkeyService.AvailabilityError(registration) ?? "", 12, "Danger");
            error.Margin = new Thickness(0, -6, 0, RowGap);
            var row = hotkeys.Children.IndexOf((UIElement)entry.Item2.Parent);
            _hotkeyErrors[entry.Item1] = error; hotkeys.Children.Insert(row + 1, error);
            Button? alternative = null;
            if (entry.Item3.Length > 0)
            {
                // The alternative belongs to its own field and is offered only while that field has a problem.
                var candidate = entry.Item3;
                alternative = Ui.Button(L.F("Try {0}", candidate), "", (_, _) =>
                {
                    if (_hotkeys?.Probe(candidate) == true) { entry.Item2.Text = candidate; Ui.Localize(error, TextBlock.TextProperty, "Available now; checked again when saving."); }
                    else Ui.Localize(error, TextBlock.TextProperty, "This shortcut is also occupied. Enter another combination.");
                });
                alternative.HorizontalAlignment = HorizontalAlignment.Left; alternative.Margin = new Thickness(0, -4, 0, RowGap);
                hotkeys.Children.Insert(row + 2, alternative);
            }
            void ShowProblem() => error.Visibility = (alternative ?? (UIElement)error).Visibility = error.Text.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
            // The descriptor holds its component strongly; detach so a closed window can be collected.
            var text = System.ComponentModel.DependencyPropertyDescriptor.FromProperty(TextBlock.TextProperty, typeof(TextBlock));
            EventHandler changed = (_, _) => ShowProblem();
            text.AddValueChanged(error, changed); Closed += (_, _) => text.RemoveValueChanged(error, changed);
            ShowProblem();
        }
        Hint(hotkeys, "Focus a field and press a key combination. Print Screen also works alone. Leave Open Shelf empty to disable its shortcut.", 4);

        var screenshot = Section("Screenshots", "\uEB9F");
        _format = Choice(screenshot, "Default image format", ImageFormats, settings.Screenshot.Format, "");
        _openEditor = Toggle(screenshot, "Open editor after capture", settings.Screenshot.OpenEditor);
        Hint(screenshot, "When off, new screenshots go straight to Shelf and are copied if the option below is on. Edit them later from Shelf.");
        _clipboard = Toggle(screenshot, "Copy saved image to clipboard", settings.Screenshot.CopyToClipboard);
        Hint(screenshot, "Copy keeps the editor open. Save replaces the current image; Save as new creates a copy.");

        var recording = Section("Recording", "\uE714");
        recording.Children.Add(Ui.Text("Video quality", 13));
        var qualities = new WrapPanel { Margin = new Thickness(0, 8, 0, 4) };
        foreach (var name in new[] { "Compact", "Balanced", "High" })
        {
            var button = new ToggleButton { Content = Ui.Text(name), Tag = name, IsChecked = _quality == name, Margin = new Thickness(0, 0, 8, 8), Padding = new Thickness(16, 8, 16, 8) };
            Ui.Localize(button, AutomationProperties.NameProperty, name);
            button.Click += (_, _) => { _quality = name; foreach (var other in _qualityButtons) other.IsChecked = Equals(other.Tag, name); };
            _qualityButtons.Add(button); qualities.Children.Add(button);
        }
        recording.Children.Add(qualities); Hint(recording, "MP4 / H.264 · 30 fps");
        _systemAudio = Toggle(recording, "Record system audio by default", settings.Recording.SystemAudioDefault);
        _microphone = Toggle(recording, "Record microphone by default", settings.Recording.MicrophoneDefault);
        var ripple = new StackPanel { Margin = new Thickness(0, 10, 0, 0) };
        AddColors(ripple, "Left click", _leftColor, color => _leftColor = color);
        AddColors(ripple, "Right click", _rightColor, color => _rightColor = color);
        _duration = Choice(ripple, "Duration", RippleDurations, settings.Recording.ClickRippleDurationMs.ToString(CultureInfo.InvariantCulture), "ms");
        _radius = Choice(ripple, "Radius", RippleRadii, settings.Recording.ClickRippleRadiusPx.ToString(CultureInfo.InvariantCulture), "px");
        recording.Children.Add(new Expander { Header = Ui.Text("Click highlighting"), Content = ripple, Margin = new Thickness(0, 8, 0, 0) });
        var updateSection = Section("Updates", "\uE895");
        if (updates is not null && installUpdate is not null && checkUpdate is not null)
            updateSection.Children.Add(new UpdatePanel(_settings.Updates, updates, installUpdate, checkUpdate));
        else updateSection.Children.Add(Ui.Text("No update source configured.", 13, "Muted"));
        Ui.Shell(this, "", content, navigation); SelectSection("General");
        Loaded += (_, _) => Ui.FitInitialBounds(this);
        Closing += (_, e) => { if (_saving) e.Cancel = true; };
        Closed += (_, _) => { if (!_saved) Ui.ApplyTheme(_originalTheme); };
    }

    internal void SelectSection(string section)
    {
        _page.Content = _sections[section]; _page.ScrollToTop();
        foreach (var pair in _navigation) pair.Value.IsChecked = pair.Key == section;
    }

    private static ComboBox Options(Panel parent, string label, (string Value, string Label)[] values, string selected)
    {
        var row = new Grid { Margin = new Thickness(0, 0, 0, RowGap) };
        row.ColumnDefinitions.Add(new ColumnDefinition());
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var box = new ComboBox { Width = 180, SelectedValuePath = nameof(ComboBoxItem.Tag) };
        foreach (var value in values)
        {
            var item = new ComboBoxItem { Tag = value.Value };
            Ui.Localize(item, ContentControl.ContentProperty, value.Label); box.Items.Add(item);
        }
        box.SelectedValue = selected; Ui.Localize(box, AutomationProperties.NameProperty, label);
        Grid.SetColumn(box, 1); row.Children.Add(box); row.Children.Add(Ui.Text(label)); parent.Children.Add(row); return box;
    }

    public event EventHandler<AppSettings>? SettingsSaved;
    // One rhythm for every settings row: 13 px label, RowGap below, and a muted hint tucked under its row.
    private const double RowGap = 12;
    private static void Hint(Panel parent, string text, double top = -6)
    {
        var hint = Ui.Text(text, 12, "Muted"); hint.Margin = new Thickness(0, top, 0, RowGap + 4); parent.Children.Add(hint);
    }
    private static CheckBox Toggle(Panel parent, string label, bool value) { var b = new CheckBox { Content = Ui.Text(label), IsChecked = value, Margin = new Thickness(0, 0, 0, RowGap) }; Ui.Localize(b, AutomationProperties.NameProperty, label); parent.Children.Add(b); return b; }
    private static TextBox Field(Panel parent, string label, string value)
    {
        var row = new Grid { Margin = new Thickness(0, 0, 0, RowGap) }; row.ColumnDefinitions.Add(new ColumnDefinition()); row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) }); row.Children.Add(Ui.Text(label));
        var b = new TextBox { Text = value }; Ui.Localize(b, AutomationProperties.NameProperty, label); Grid.SetColumn(b, 1); row.Children.Add(b); parent.Children.Add(row); return b;
    }
    private static TextBox Shortcut(Panel parent, string label, string value, bool optional = false)
    {
        var box = Field(parent, label, value);
        if (label != "Screenshot full screen") Ui.Localize(box, ToolTipProperty, "Press Ctrl/Alt/Shift and a key");
        box.PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Tab) return;
            e.Handled = true; var key = e.Key == Key.System ? e.SystemKey : e.Key; var modifiers = Keyboard.Modifiers;
            if (optional && modifiers == ModifierKeys.None && key is Key.Back or Key.Delete) { box.Clear(); return; }
            if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin || (modifiers == ModifierKeys.None && key != Key.PrintScreen)) return;
            box.Text = (modifiers.HasFlag(ModifierKeys.Control) ? "Ctrl+" : "") + (modifiers.HasFlag(ModifierKeys.Alt) ? "Alt+" : "") + (modifiers.HasFlag(ModifierKeys.Shift) ? "Shift+" : "") + (modifiers.HasFlag(ModifierKeys.Windows) ? "Win+" : "") + HotkeyParser.DisplayKey(key);
        }; return box;
    }
    private static ComboBox Choice(Panel p, string label, string[] values, string selected, string unit)
    {
        var row = new DockPanel { Margin = new Thickness(0, 0, 0, RowGap) }; var b = new ComboBox { ItemsSource = values.Contains(selected) ? values : values.Append(selected).ToArray(), SelectedItem = selected, Width = 100, ToolTip = L.T(unit) }; Ui.Localize(b, AutomationProperties.NameProperty, label); DockPanel.SetDock(b, Dock.Right); row.Children.Add(b); row.Children.Add(Ui.Text(unit.Length == 0 ? L.T(label) : L.T(label) + " (" + L.T(unit) + ")")); p.Children.Add(row); return b;
    }
    private static void AddColors(Panel parent, string label, string current, Action<string> changed)
    {
        var row = new DockPanel { Margin = new Thickness(0, 6, 0, 6) }; var swatches = new StackPanel { Orientation = Orientation.Horizontal }; DockPanel.SetDock(swatches, Dock.Right); row.Children.Add(swatches); row.Children.Add(Ui.Text(label));
        var buttons = new List<ToggleButton>();
        foreach (var color in new[] { "#FFFFC107", "#FFFF6B35", "#FF69E6A3", "#FF80B5FF" })
        {
            var b = new ToggleButton { Content = new System.Windows.Shapes.Ellipse { Width = 15, Height = 15, Fill = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color)) }, Padding = new Thickness(4), MinHeight = 26, IsChecked = current.Equals(color, StringComparison.OrdinalIgnoreCase), Tag = color, ToolTip = L.T(label) + " " + color }; Ui.Localize(b, AutomationProperties.NameProperty, label + " {0}", color); buttons.Add(b);
            b.Click += (_, _) => { changed(color); foreach (var other in buttons) other.IsChecked = Equals(other.Tag, color); }; swatches.Children.Add(b);
        }
        parent.Children.Add(row);
    }
    private async Task SaveAsync()
    {
        if (_saving) return; _saving = true;
        try
        {
            var fields = new[] { (1001, _screenshotHotkey.Text), (1005, _fullScreenshotHotkey.Text), (1002, _videoHotkey.Text), (1003, _pauseHotkey.Text), (1004, _shelfHotkey.Text) };
            var keys = fields.Select(field => GlobalHotkeyService.Normalize(field.Item2)).ToArray();
            var invalidKeys = false;
            for (var i = 0; i < keys.Length; i++)
            {
                var error = fields[i].Item1 == 1004 && keys[i].Length == 0 ? "" : !HotkeyParser.TryParse(keys[i], out _, out _) ? "Use a modifier and a non-modifier key." : keys.Count(k => k.Equals(keys[i], StringComparison.OrdinalIgnoreCase)) > 1 ? "This shortcut is assigned twice." : "";
                _hotkeyErrors[fields[i].Item1].Text = L.T(error); invalidKeys |= error.Length > 0;
            }
            if (invalidKeys) { SelectSection("Hotkeys"); Ui.Localize(_status, TextBlock.TextProperty, "Resolve the marked shortcut errors."); return; }
            if (!int.TryParse(_recent.Text, out var recent) || recent < 1 || recent > 500) { SelectSection("General"); Ui.Localize(_status, TextBlock.TextProperty, "Recent captures must be between 1 and 500."); return; }
            var folder = Environment.ExpandEnvironmentVariables(_folder.Text.Trim());
            if (!Path.IsPathFullyQualified(folder)) { SelectSection("General"); Ui.Localize(_status, TextBlock.TextProperty, "Choose an absolute capture folder path."); return; }
            _ = Path.GetFullPath(folder);
            _settings.General.Language = (string)_language.SelectedValue; _settings.General.Theme = (string)_theme.SelectedValue;
            _settings.General.StartWithWindows = _start.IsChecked == true; _settings.General.CaptureRoot = folder; _settings.General.ShelfRecentCount = recent;
            _settings.Hotkeys.RegionScreenshot = keys[0]; _settings.Hotkeys.FullScreenshot = keys[1]; _settings.Hotkeys.RegionVideo = keys[2]; _settings.Hotkeys.PauseResumeVideo = keys[3]; _settings.Hotkeys.OpenShelf = keys[4].Length == 0 ? null : keys[4];
            _settings.Screenshot.Format = (string)_format.SelectedItem;
            _settings.Screenshot.OpenEditor = _openEditor.IsChecked == true;
            _settings.Screenshot.CopyToClipboard = _clipboard.IsChecked == true;
            _settings.Recording.QualityProfile = _quality; _settings.Recording.SystemAudioDefault = _systemAudio.IsChecked == true; _settings.Recording.MicrophoneDefault = _microphone.IsChecked == true;
            _settings.Recording.LeftClickColor = _leftColor; _settings.Recording.RightClickColor = _rightColor;
            _settings.Recording.ClickRippleDurationMs = int.Parse((string)_duration.SelectedItem, CultureInfo.InvariantCulture); _settings.Recording.ClickRippleRadiusPx = int.Parse((string)_radius.SelectedItem, CultureInfo.InvariantCulture);
            var previous = _hotkeys?.Snapshot();
            var previousResults = _hotkeys?.Results;
            var results = _hotkeys?.Apply(GlobalHotkeyService.Bindings(_settings.Hotkeys));
            if (results is not null)
            {
                foreach (var result in results) _hotkeyErrors[result.Id].Text = L.T(GlobalHotkeyService.AvailabilityError(result) ?? "");
                if (results.Any(r => !r.Registered)) { SelectSection("Hotkeys"); Ui.Localize(_status, TextBlock.TextProperty, "Shortcuts were not changed. Resolve the marked conflicts."); return; }
            }
            try
            {
                IsEnabled = false;
                Ui.Localize(_status, TextBlock.TextProperty, "Saving…");
                if (_settings.General.StartWithWindows != _originalStartup && !string.IsNullOrWhiteSpace(Environment.ProcessPath)) _startup.SetEnabled(_settings.General.StartWithWindows, Environment.ProcessPath);
                await _store.SaveAsync(_settings, CancellationToken.None);
            }
            catch
            {
                if (previous is not null) _hotkeys!.Restore(previous, previousResults!);
                if (_settings.General.StartWithWindows != _originalStartup && !string.IsNullOrWhiteSpace(Environment.ProcessPath)) _startup.SetEnabled(_originalStartup, Environment.ProcessPath);
                throw;
            }
            _saved = true; SettingsSaved?.Invoke(this, _settings); _saving = false; Close();
        }
        catch (Exception ex) { _logger.Error("Settings save failed.", ex); Ui.Localize(_status, TextBlock.TextProperty, "Could not save settings. Check the folder and try again."); }
        finally { _saving = false; IsEnabled = true; }
    }
}
