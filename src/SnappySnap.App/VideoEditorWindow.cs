using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using SnappySnap.Core;
using SnappySnap.Infrastructure;
using SnappySnap.Presentation;
using SnappySnap.Localization;
using System.Diagnostics;
namespace SnappySnap.App;

public sealed class VideoEditorWindow : Window, IDisposable
{
    private readonly string _sourcePath;
    private readonly IAppLogger _logger;
    private readonly IVideoEditingService _editing = new WindowsMediaVideoEditingService();
    private readonly IVideoPreviewSession _preview = new WindowsVideoPreviewSession();
    private readonly Image _image = new() { Stretch = Stretch.Uniform };
    private readonly VideoTimeline _timeline = new() { IsEnabled = false };
    private readonly TextBlock _status, _summary, _time, _selectionLabel;
    private readonly TextBox _from, _to;
    private readonly Button _play, _export, _undo, _redo, _delete, _cancel, _keep, _showOutput;
    private readonly Expander _exactTime;
    private readonly ProgressBar _progress;
    private readonly DispatcherTimer _timer;
    private readonly CancellationTokenSource _lifetime = new();
    private CancellationTokenSource? _exportCancellation, _loadCancellation;
    private VideoEditSession? _edit;
    private WriteableBitmap? _bitmap;
    private bool _disposed, _scrubbing, _resume, _loading, _previewReady;
    private int _loadGeneration;
    private double? _pendingSeek;
    private string? _lastPreviewError;
    private bool? _displayedPlaying;
    private TimeRange[]? _savedSegments;
    private VideoEditResult? _pendingExport;
    private TimeRange[]? _pendingSegments;
    private string? _lastOutput;
    public bool IsExporting => _exportCancellation is not null;
    public Func<VideoEditResult, Task>? SavedAsync { get; set; }

    public VideoEditorWindow(string sourcePath, IAppLogger logger)
    {
        _sourcePath = Path.GetFullPath(sourcePath); _logger = logger;
        Title = "SnappySnap — Video Editor"; Width = 1260; Height = 850; MinWidth = 940; MinHeight = 620;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        var root = new DockPanel { Margin = new Thickness(18, 12, 18, 14) };
        var top = new DockPanel { Margin = new Thickness(0, 0, 0, 12) };
        var actions = new StackPanel { Orientation = Orientation.Horizontal };
        _undo = Ui.Button("Undo", "\uE7A7", (_, _) => Change(() => _edit!.Undo()), "GhostButton");
        _redo = Ui.Button("Redo", "\uE7A6", (_, _) => Change(() => _edit!.Redo()), "GhostButton");
        _export = Ui.Button("Export a copy", "\uE898", async (_, _) => await ExportAsync(), "PrimaryButton"); _export.IsEnabled = false;
        actions.Children.Add(_undo); actions.Children.Add(_redo); actions.Children.Add(_export);
        DockPanel.SetDock(actions, Dock.Right); top.Children.Add(actions);
        var filename = Ui.Text(Path.GetFileName(_sourcePath), 15); filename.TextWrapping = TextWrapping.NoWrap; filename.TextTrimming = TextTrimming.CharacterEllipsis; filename.ToolTip = _sourcePath; top.Children.Add(filename);
        DockPanel.SetDock(top, Dock.Top); root.Children.Add(top);
        var bottom = new DockPanel { Margin = new Thickness(0, 10, 0, 0) };
        _cancel = Ui.Button("Cancel export", "", (_, _) => _exportCancellation?.Cancel(), "GhostButton"); _cancel.Visibility = Visibility.Collapsed; DockPanel.SetDock(_cancel, Dock.Right); bottom.Children.Add(_cancel);
        _showOutput = Ui.Button("Show in folder", "\uE8B7", (_, _) =>
        {
            if (_lastOutput is not null) try { Process.Start(new ProcessStartInfo("explorer.exe", "/select,\"" + _lastOutput + "\"") { UseShellExecute = true }); }
            catch (Exception ex) { Fail("Could not open the folder", ex); }
        }, "GhostButton"); _showOutput.Visibility = Visibility.Collapsed; DockPanel.SetDock(_showOutput, Dock.Right); bottom.Children.Add(_showOutput);
        var notices = new StackPanel(); _status = Ui.Text("Loading video…", 12, "Muted"); notices.Children.Add(_status);
        System.Windows.Automation.AutomationProperties.SetLiveSetting(_status, System.Windows.Automation.AutomationLiveSetting.Polite);
        _progress = new ProgressBar { Maximum = 1, Height = 4, Margin = new Thickness(0, 6, 0, 0), Visibility = Visibility.Collapsed }; notices.Children.Add(_progress); bottom.Children.Add(notices);
        DockPanel.SetDock(bottom, Dock.Bottom); root.Children.Add(bottom);
        var timelinePanel = new DockPanel { Margin = new Thickness(0, 10, 0, 0) };
        var tools = new WrapPanel { Margin = new Thickness(0, 0, 0, 8) };
        _selectionLabel = Ui.Text("Select a fragment", 12, "Muted"); _selectionLabel.MinWidth = 130; _selectionLabel.Margin = new Thickness(0, 0, 16, 0); tools.Children.Add(_selectionLabel);
        _delete = Ui.Button("Delete fragment", "\uE74D", (_, _) => DeleteSelection()); tools.Children.Add(_delete);
        _keep = Ui.Button("Keep only selection", "", (_, _) => Change(() => _edit!.Trim(TimeSpan.FromSeconds(_timeline.SelectionStart), TimeSpan.FromSeconds(_timeline.SelectionEnd))), "GhostButton"); tools.Children.Add(_keep);
        DockPanel.SetDock(tools, Dock.Top); timelinePanel.Children.Add(tools);
        var exact = new WrapPanel(); var from = new StackPanel { Width = 180, Margin = new Thickness(0, 0, 12, 0) }; var to = new StackPanel { Width = 180, Margin = new Thickness(0, 0, 12, 0) };
        _from = TimeField(from, "Selection start (seconds)"); _to = TimeField(to, "Selection end (seconds)"); exact.Children.Add(from); exact.Children.Add(to);
        exact.Children.Add(Ui.Button("Set selection", "", (_, _) => SetSelection()));
        _exactTime = new Expander { Header = Ui.Text("Exact time…", 12), Content = exact, Margin = new Thickness(0, 0, 0, 6), IsEnabled = false };
        DockPanel.SetDock(_exactTime, Dock.Top); timelinePanel.Children.Add(_exactTime);
        var summaryRow = new DockPanel { Margin = new Thickness(0, 6, 0, 0) };
        var zoom = new Slider { Minimum = 1, Maximum = 10, Value = 1, Width = 110 }; Ui.Localize(zoom, ToolTipProperty, "Timeline zoom");
        Ui.Localize(zoom, System.Windows.Automation.AutomationProperties.NameProperty, "Timeline zoom"); DockPanel.SetDock(zoom, Dock.Right); summaryRow.Children.Add(zoom);
        _summary = Ui.Text("", 12, "Muted"); summaryRow.Children.Add(_summary); DockPanel.SetDock(summaryRow, Dock.Bottom); timelinePanel.Children.Add(summaryRow);
        var scroll = new ScrollViewer { Content = _timeline, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled };
        void ResizeTimeline() => _timeline.Width = Math.Max(1, scroll.ActualWidth - 4) * zoom.Value;
        zoom.ValueChanged += (_, _) => ResizeTimeline(); scroll.SizeChanged += (_, _) => ResizeTimeline(); timelinePanel.Children.Add(scroll);
        var trackCard = Ui.Card(timelinePanel, 10); DockPanel.SetDock(trackCard, Dock.Bottom); root.Children.Add(trackCard);
        var viewer = new DockPanel();
        var controls = new WrapPanel { Margin = new Thickness(0, 8, 0, 0) };
        _play = Ui.Button("Play", "\uE768", (_, _) => TogglePlayback()); _play.IsEnabled = false; controls.Children.Add(_play);
        _time = Ui.Text("00:00 / 00:00", 12, "Muted"); _time.Margin = new Thickness(12, 0, 12, 0); controls.Children.Add(_time);
        controls.Children.Add(Ui.Text("Preview audio", 12, "Muted"));
        var volume = new Slider { Minimum = 0, Maximum = 1, Value = 1, Width = 100, Margin = new Thickness(10, 0, 10, 0) }; Ui.Localize(volume, System.Windows.Automation.AutomationProperties.NameProperty, "Preview volume"); volume.ValueChanged += (_, _) => _preview.Volume = volume.Value; controls.Children.Add(volume);
        var mute = new CheckBox { Content = Ui.Text("Mute preview"), Margin = new Thickness(10, 0, 0, 0) }; Ui.Localize(mute, ToolTipProperty, "Preview volume does not affect export."); mute.Click += (_, _) => _preview.IsMuted = mute.IsChecked == true; controls.Children.Add(mute);
        DockPanel.SetDock(controls, Dock.Bottom); viewer.Children.Add(controls);
        viewer.Children.Add(new Border { Background = Brushes.Black, Child = _image, CornerRadius = new CornerRadius(6), ClipToBounds = true }); root.Children.Add(viewer);
        Ui.Shell(this, "", root);
        _timeline.SelectionChanged += UpdateSelection;
        _timeline.TrimChanged += (start, end) => Change(() => _edit!.Trim(TimeSpan.FromSeconds(start), TimeSpan.FromSeconds(end)));
        _timeline.SeekRequested += seconds => _pendingSeek = seconds;
        _timeline.ScrubStarted += () => { _scrubbing = true; _resume = _preview.IsPlaying; _preview.Pause(); };
        _timeline.ScrubCompleted += () => { FlushSeek(); _scrubbing = false; if (_resume && !_loading) _preview.Play(); };
        PreviewKeyDown += OnKeyDown;
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(33) }; _timer.Tick += (_, _) => Tick(); _timer.Start();
        L.Current.PropertyChanged += OnLanguageChanged;
        Loaded += async (_, _) => { Ui.FitInitialBounds(this); await LoadAsync(); }; Closed += (_, _) => Dispose();
        Closing += (_, e) =>
        {
            if (IsExporting) { e.Cancel = true; MessageBox.Show(L.T("Wait for export to finish, or cancel it before closing."), "SnappySnap"); }
            else if (_pendingExport is not null && MessageBox.Show(this, L.T("The video file is saved but missing from history. Close without retrying?"), "SnappySnap", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) e.Cancel = true;
            else if (_edit is not null && _savedSegments is not null && !_edit.Segments.SequenceEqual(_savedSegments) &&
                _pendingExport is null && MessageBox.Show(this, L.T("Discard your unsaved video edits?"), "SnappySnap", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) e.Cancel = true;
        };
    }
    private void OnLanguageChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e) => UpdateState();
    private static TextBox TimeField(Panel parent, string label)
    {
        parent.Children.Add(Ui.Text(label, 12, "Muted"));
        var box = new TextBox { Text = "0", Margin = new Thickness(0, 4, 0, 8) }; Ui.Localize(box, System.Windows.Automation.AutomationProperties.NameProperty, label); parent.Children.Add(box); return box;
    }
    private async Task LoadAsync()
    {
        try
        {
            var data = await Task.Run(() => _editing.LoadPreviewAsync(_sourcePath, _lifetime.Token), _lifetime.Token);
            if (_disposed) return;
            _edit = new VideoEditSession(data.Duration); _timeline.Edit = _edit;
            _savedSegments = _edit.Segments.ToArray();
            static BitmapImage Decode(byte[] bytes) { using var stream = new MemoryStream(bytes); var image = new BitmapImage(); image.BeginInit(); image.CacheOption = BitmapCacheOption.OnLoad; image.StreamSource = stream; image.EndInit(); image.Freeze(); return image; }
            _image.Source = Decode(data.Poster); _timeline.Frames = data.Thumbnails.Select(bytes => (ImageSource)Decode(bytes)).ToArray();
            await ReloadPreviewAsync();
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { Fail("Could not load video", ex); }
    }
    private async Task ReloadPreviewAsync()
    {
        if (_edit is null) return;
        var generation = ++_loadGeneration;
        _loadCancellation?.Cancel(); _loadCancellation?.Dispose(); _loadCancellation = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        var token = _loadCancellation.Token; var edit = _edit.ExportTimeline(); var position = Math.Min(_timeline.Position, _edit.Duration.TotalSeconds);
        _pendingSeek = null; _resume = false; _timeline.Position = position;
        _loading = true; _previewReady = false; UpdateState();
        try
        {
            await Task.Run(() => _preview.LoadAsync(_sourcePath, edit, token), token);
            if (_disposed || generation != _loadGeneration) return;
            _preview.Seek(TimeSpan.FromSeconds(position));
            _previewReady = true;
            Ui.Localize(_status, TextBlock.TextProperty, "Ready");
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { if (!_disposed && generation == _loadGeneration) Fail("Preview failed", ex); }
        finally { if (!_disposed && generation == _loadGeneration) { _loading = false; UpdateState(); } }
    }
    private void Change(Action change)
    {
        if (_edit is null || IsExporting || _pendingExport is not null) return;
        try { var before = _edit.Segments.ToArray(); change(); if (before.SequenceEqual(_edit.Segments)) return; _timeline.SelectionStart = _timeline.SelectionEnd = 0; UpdateSelection(); UpdateState(); _ = ReloadPreviewAsync(); }
        catch (Exception ex) { Fail("Could not apply this edit. Select a smaller fragment and try again.", ex); }
    }
    private void DeleteSelection() => Change(() => _edit!.Delete(TimeSpan.FromSeconds(_timeline.SelectionStart), TimeSpan.FromSeconds(_timeline.SelectionEnd)));
    private void SetSelection()
    {
        if (_edit is null || IsExporting || _pendingExport is not null) return;
        if (!double.TryParse(_from.Text, NumberStyles.Float, L.Culture, out var start) || !double.TryParse(_to.Text, NumberStyles.Float, L.Culture, out var end) || !double.IsFinite(start) || !double.IsFinite(end) || start < 0 || end <= start || end > _edit.Duration.TotalSeconds)
        { _status.Text = L.F("Enter a range within the video in seconds (for example {0:0.0}).", 1.5); return; }
        _timeline.SelectionStart = start; _timeline.SelectionEnd = end; UpdateSelection();
    }
    private void UpdateSelection()
    {
        _from.Text = _timeline.SelectionStart.ToString("0.###", L.Culture); _to.Text = _timeline.SelectionEnd.ToString("0.###", L.Culture);
        var selected = _timeline.SelectionEnd > _timeline.SelectionStart;
        _selectionLabel.Text = selected ? L.F("Selected: {0:0.###} s", _timeline.SelectionEnd - _timeline.SelectionStart) : L.T("Select a fragment");
        _keep.IsEnabled = _delete.IsEnabled = selected && !IsExporting && _pendingExport is null; _timeline.InvalidateVisual();
        _timeline.NotifySelectionChanged();
    }
    private void UpdateState()
    {
        _cancel.Visibility = IsExporting ? Visibility.Visible : Visibility.Collapsed;
        UpdateSelection();
        var editable = !IsExporting && _pendingExport is null;
        _undo.IsEnabled = _edit?.CanUndo == true && editable; _redo.IsEnabled = _edit?.CanRedo == true && editable;
        _play.IsEnabled = _edit is not null && !_loading && _previewReady && !IsExporting; _export.IsEnabled = _edit is not null && !_loading && !IsExporting; _timeline.IsEnabled = _edit is not null && editable; _exactTime.IsEnabled = _timeline.IsEnabled;
        _export.Content = Ui.Label("\uE898", _pendingExport is null ? "Export a copy" : "Retry adding to history");
        Ui.Localize(_export, System.Windows.Automation.AutomationProperties.NameProperty, _pendingExport is null ? "Export a copy" : "Retry adding to history");
        _showOutput.Visibility = _lastOutput is null ? Visibility.Collapsed : Visibility.Visible;
        if (_edit is not null) _summary.Text = L.F("Original {0}  ·  Result {1}  ·  Removed {2}", Format(_edit.SourceDuration), Format(_edit.Duration), Format(_edit.SourceDuration - _edit.Duration));
        _timeline.InvalidateVisual();
    }
    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (Keyboard.FocusedElement is TextBoxBase || _edit is null || IsExporting) return;
        if (e.Key == Key.Space) TogglePlayback();
        else if (e.Key == Key.Escape) { _timeline.SelectionStart = _timeline.SelectionEnd = 0; UpdateSelection(); }
        else if (e.Key == Key.Delete) DeleteSelection();
        else if (e.Key == Key.Z && Keyboard.Modifiers == ModifierKeys.Control) Change(() => _edit.Undo());
        else if (e.Key == Key.Y && Keyboard.Modifiers == ModifierKeys.Control) Change(() => _edit.Redo());
        else if (e.Key is Key.Left or Key.Right) _pendingSeek = Math.Clamp(_preview.Position.TotalSeconds + (e.Key == Key.Left ? -1 : 1) * (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) ? 1 : .1), 0, _edit.Duration.TotalSeconds);
        else return;
        e.Handled = true;
    }
    private void TogglePlayback() { if (_loading || !_previewReady || _edit is null) return; if (_preview.IsPlaying) _preview.Pause(); else _preview.Play(); }
    private void FlushSeek()
    {
        if (_loading || !_previewReady || _pendingSeek is not { } seconds || _edit is null) return;
        _pendingSeek = null; _preview.Seek(TimeSpan.FromSeconds(Math.Clamp(seconds, 0, _edit.Duration.TotalSeconds)));
    }
    private void Tick()
    {
        if (_disposed) return; FlushSeek();
        if (_preview.TakeFrame() is { } frame)
        {
            if (_bitmap is null || _bitmap.PixelWidth != frame.Width || _bitmap.PixelHeight != frame.Height) _bitmap = new WriteableBitmap(frame.Width, frame.Height, 96, 96, PixelFormats.Bgra32, null);
            _bitmap.WritePixels(new Int32Rect(0, 0, frame.Width, frame.Height), frame.Bgra32, frame.Width * 4, 0); _image.Source = _bitmap;
        }
        var position = _preview.Position;
        if (!_scrubbing && !_loading && _timeline.Position != position.TotalSeconds) { _timeline.Position = position.TotalSeconds; _timeline.InvalidateVisual(); }
        var time = $"{Format(position)} / {Format(_edit?.Duration ?? TimeSpan.Zero)}";
        if (_time.Text != time) _time.Text = time;
        var playing = _preview.IsPlaying;
        if (_displayedPlaying != playing)
        {
            _displayedPlaying = playing; _play.Content = Ui.Label(playing ? "\uE769" : "\uE768", playing ? "Pause" : "Play");
            Ui.Localize(_play, System.Windows.Automation.AutomationProperties.NameProperty, playing ? "Pause" : "Play");
        }
        if (_preview.Error is { } error) { Ui.Localize(_status, TextBlock.TextProperty, "Preview failed"); if (_lastPreviewError != error) { _logger.Warn("Video preview failed: " + error); _lastPreviewError = error; } }
    }
    private async Task ExportAsync()
    {
        if (_edit is null || IsExporting) return;
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token); _exportCancellation = cancellation;
        _preview.Pause(); _progress.Value = 0; _progress.Visibility = _pendingExport is null ? Visibility.Visible : Visibility.Collapsed; UpdateState();
        try
        {
            if (_pendingExport is null)
            {
                var output = Path.Combine(Path.GetDirectoryName(_sourcePath)!, Path.GetFileNameWithoutExtension(_sourcePath) + "-edited-" + Guid.NewGuid().ToString("N")[..8] + ".mp4");
                _pendingExport = await _editing.ExportAsync(_sourcePath, output, _edit.ExportTimeline(), new Progress<double>(p => { _progress.Value = p; _status.Text = L.F("Exporting… {0:P0}", p); }), cancellation.Token);
                _pendingSegments = _edit.Segments.ToArray(); _lastOutput = _pendingExport.OutputPath;
            }
            if (SavedAsync is not null) await SavedAsync(_pendingExport);
            _savedSegments = _pendingSegments;
            _status.Text = L.F("Saved {0} · {1}", Path.GetFileName(_pendingExport.OutputPath), FileSizeFormatter.Format(new FileInfo(_pendingExport.OutputPath).Length, L.Culture));
            _pendingExport = null; _pendingSegments = null;
        }
        catch (OperationCanceledException) when (_pendingExport is null) { Ui.Localize(_status, TextBlock.TextProperty, "Export cancelled. Your original is unchanged."); }
        catch (Exception ex)
        {
            if (_pendingExport is null) Fail("Export failed. Your original is unchanged.", ex);
            else { _logger.Error("Video saved, but history indexing failed.", ex); Ui.Localize(_status, TextBlock.TextProperty, "Video saved, but not added to history. Retry or show the file in its folder."); }
        }
        finally { _exportCancellation = null; if (!_disposed) { _progress.Visibility = Visibility.Collapsed; UpdateState(); } }
    }
    private void Fail(string message, Exception ex) { _logger.Error(message, ex); if (!_disposed) _status.Text = L.T(message); }
    public void Dispose()
    {
        if (_disposed) return;
        L.Current.PropertyChanged -= OnLanguageChanged;
        _disposed = true; _lifetime.Cancel(); _loadCancellation?.Cancel(); _timer.Stop();
        _ = Task.Run(() => { try { _preview.Dispose(); } catch (Exception ex) { _logger.Error("Preview cleanup failed.", ex); } });
        _loadCancellation?.Dispose(); _lifetime.Dispose();
    }
    private static string Format(TimeSpan value) => value.ToString(value.TotalHours >= 1 ? @"hh\:mm\:ss" : @"mm\:ss\.f", CultureInfo.InvariantCulture);
}
