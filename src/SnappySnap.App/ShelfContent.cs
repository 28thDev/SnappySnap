using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Automation;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SnappySnap.Application;
using SnappySnap.Core;
using SnappySnap.Editor;
using SnappySnap.Presentation;
using SnappySnap.Localization;
using WpfContextMenu = System.Windows.Controls.ContextMenu;
using WpfListView = System.Windows.Controls.ListView;

namespace SnappySnap.App;

public sealed class ShelfState : INotifyPropertyChanged
{
    public ObservableCollection<ShelfRow> Rows { get; } = new();
    private ShelfRow? _selected;
    public ShelfRow? Selected { get => _selected; set { _selected = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Selected))); } }
    public bool Loaded { get; set; }
    public int LoadVersion { get; set; }
    public int ActiveSaves { get; set; }
    public bool IsSaving => ActiveSaves > 0;
    public bool IsEditing { get; set; }
    public bool IsDragging { get; set; }
    public event PropertyChangedEventHandler? PropertyChanged;
    public Action<EditorWindow>? ConfigureEditor { get; init; }
    public Action<string>? Notify { get; init; }
    public event EventHandler? Saved;
    public void NotifySaved() => Saved?.Invoke(this, EventArgs.Empty);
    public event EventHandler? Deleted;
    public void NotifyDeleted() => Deleted?.Invoke(this, EventArgs.Empty);
}

public sealed partial class ShelfContent : UserControl, IDisposable
{
    private readonly ShelfService _service;
    private AppSettings _settings;
    private int _recentCount;
    public void UpdateSettings(AppSettings settings)
    {
        var reload = _recentCount != settings.General.ShelfRecentCount;
        _settings = settings; _recentCount = settings.General.ShelfRecentCount;
        SetCurrentValue(ThumbnailHeightProperty, settings.Shelf.ThumbnailHeight);
        foreach (var row in _rows) row.RefreshLocale();
        UpdateSelectionActions();
        if (reload && IsLoaded) _ = LoadAsync();
    }
    public static readonly DependencyProperty ThumbnailHeightProperty = DependencyProperty.Register(nameof(ThumbnailHeight), typeof(double), typeof(ShelfContent), new PropertyMetadata(164d));
    public double ThumbnailHeight { get => (double)GetValue(ThumbnailHeightProperty); set => SetValue(ThumbnailHeightProperty, value); }
    public event Action<double>? ThumbnailHeightChanged;
    private readonly IAppLogger _logger;
    private readonly ShelfState _state;
    private ObservableCollection<ShelfRow> _rows => _state.Rows;
    private readonly WpfListView _list;
    private CancellationTokenSource? _visibleLifetime;
    private readonly Button _copy, _editButton;
    private readonly Border _recoveryNotice;
    private string? _recoveryFolder, _logsFolder;
    private readonly Button _older;
    private readonly Button _expand;
    private bool _loadingOlder;
    private bool _captureInProgress;
    private bool _disposed;

    public ShelfContent(ShelfService service, AppSettings settings, IAppLogger logger, ShelfState? state = null)
    {
        _state = state ?? new ShelfState();
        _service = service; _settings = settings; _recentCount = settings.General.ShelfRecentCount; _logger = logger;
        ThumbnailHeight = settings.Shelf.ThumbnailHeight;
        var body = new Grid { Margin = new Thickness(16) };
        foreach (var height in new[] { GridLength.Auto, GridLength.Auto, GridLength.Auto, GridLength.Auto, GridLength.Auto, new GridLength(1, GridUnitType.Star) })
            body.RowDefinitions.Add(new RowDefinition { Height = height });
        var actions = new WrapPanel { Margin = new Thickness(0, 0, 0, 12) };
        _older = Ui.Button("Load older", "\uE70D", async (_, _) => await LoadOlderAsync());
        actions.Children.Add(_older);
        _count = Ui.Text("Loading…", 12, "Muted"); _count.Margin = new Thickness(10, 0, 14, 0); actions.Children.Add(_count);
        _message = Ui.Text("", 13, "Muted"); _message.Visibility = Visibility.Collapsed; _message.Margin = new Thickness(0, 0, 0, 12); Grid.SetRow(_message, 3); body.Children.Add(_message);
        _list = new WpfListView { ClipToBounds = true, SelectionMode = SelectionMode.Extended, ItemsSource = _rows, ItemTemplate = (DataTemplate)System.Windows.Application.LoadComponent(new Uri("/SnappySnap;component/ShelfCard.xaml", UriKind.Relative)) };
        Ui.Localize(_list, AutomationProperties.NameProperty, "Captures");
        Ui.Localize(_list, AutomationProperties.HelpTextProperty, "Ctrl+click toggles a capture; Shift+click selects a range; Ctrl+A selects all. Drag empty space to select an area, or drag selected captures to another app.");
        _list.SetBinding(System.Windows.Controls.Primitives.Selector.SelectedItemProperty, new Binding(nameof(ShelfState.Selected)) { Source = _state, Mode = BindingMode.TwoWay });
        var cardStyle = new Style(typeof(ListViewItem), (Style)FindResource(typeof(ListViewItem)));
        cardStyle.Setters.Add(new Setter(UIElement.ClipToBoundsProperty, true)); _list.ItemContainerStyle = cardStyle;
        ScrollViewer.SetCanContentScroll(_list, true);
        VirtualizingPanel.SetIsVirtualizing(_list, true);
        VirtualizingPanel.SetVirtualizationMode(_list, VirtualizationMode.Recycling);
        var factory = new FrameworkElementFactory(typeof(ShelfGridPanel));
        factory.SetBinding(ShelfGridPanel.ThumbnailHeightProperty, new Binding(nameof(ThumbnailHeight)) { Source = this });
        _list.ItemsPanel = new ItemsPanelTemplate(factory);
        _list.ItemContainerGenerator.StatusChanged += (_, _) => LoadVisibleThumbnails();
        _list.AddHandler(ScrollViewer.ScrollChangedEvent, new ScrollChangedEventHandler((_, _) => LoadVisibleThumbnails()));
        _list.MouseDoubleClick += OnDoubleClick;
        ConfigureSelection();
        _list.KeyDown += async (_, e) => { if (e.Key == Key.Enter) EditSelected(); else if (e.Key == Key.Delete) await DeleteSelectedAsync(); else if (e.Key == Key.C && Keyboard.Modifiers == ModifierKeys.Control) CopySelected(); else if (e.Key == Key.C && Keyboard.Modifiers == (ModifierKeys.Control | ModifierKeys.Shift)) CopyPathSelected(); };
        _list.ContextMenu = CreateContextMenu();
        var listHost = new Grid { ClipToBounds = true }; listHost.Children.Add(_list); listHost.Children.Add(_selectionCanvas);
        Grid.SetRow(listHost, 5); body.Children.Add(listHost);
        _copy = Ui.Button("Copy", "\uE8C8", (_, _) => CopySelected()); _editButton = Ui.Button("Edit", "\uE70F", (_, _) => EditSelected());
        actions.Children.Add(_copy); actions.Children.Add(_editButton);
        actions.Children.Add(Ui.Button("Settings", "\uE713", (_, _) => SettingsRequested?.Invoke(this, EventArgs.Empty), "GhostButton"));
        _list.SelectionChanged += (_, _) => UpdateSelectionActions();
        _expand = Ui.Button("History", "\uE740", (_, _) => ExpandRequested?.Invoke(this, EventArgs.Empty)); actions.Children.Add(_expand);
        var sizeControl = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 4) };
        sizeControl.Children.Add(Ui.Text("Thumbnails", 12, "Muted"));
        var size = new Slider { Minimum = 104, Maximum = 244, TickFrequency = 10, SmallChange = 10, LargeChange = 20, IsSnapToTickEnabled = true, Width = 130, Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        Ui.Localize(size, AutomationProperties.NameProperty, "Thumbnail size");
        size.SetBinding(Slider.ValueProperty, new Binding(nameof(ThumbnailHeight)) { Source = this, Mode = BindingMode.TwoWay });
        size.ValueChanged += (_, _) => { if (size.Value != _settings.Shelf.ThumbnailHeight) ThumbnailHeightChanged?.Invoke(size.Value); };
        sizeControl.Children.Add(size); actions.Children.Add(sizeControl);
        foreach (FrameworkElement action in actions.Children) action.Margin = new Thickness(action.Margin.Left, 4, action.Margin.Right, 4);
        body.Children.Add(actions);
        _warning = Ui.Text("", 12, "Danger"); _warning.TextWrapping = TextWrapping.Wrap; _warning.Visibility = Visibility.Collapsed; _warning.Margin = new Thickness(0, 0, 0, 12);
        Grid.SetRow(_warning, 1); body.Children.Add(_warning);
        _updateNotice = Ui.Button("", "\uE895", (_, _) => SettingsRequested?.Invoke(this, EventArgs.Empty));
        _updateNotice.Visibility = Visibility.Collapsed; _updateNotice.Margin = new Thickness(0, 0, 0, 12); Grid.SetRow(_updateNotice, 2); body.Children.Add(_updateNotice);
        var recovery = new WrapPanel(); recovery.Children.Add(Ui.Text("A recording needs recovery.", 12, "Warning"));
        recovery.Children.Add(Ui.Button("Open recovery folder", "\uE8B7", (_, _) => OpenRecoveryLocation(_recoveryFolder), "GhostButton"));
        recovery.Children.Add(Ui.Button("Open logs", "", (_, _) => OpenRecoveryLocation(_logsFolder), "GhostButton"));
        _recoveryNotice = Ui.Card(recovery, 10); _recoveryNotice.Visibility = Visibility.Collapsed; _recoveryNotice.Margin = new Thickness(0, 0, 0, 10); Grid.SetRow(_recoveryNotice, 4); body.Children.Add(_recoveryNotice);
        Content = body;
        Loaded += async (_, _) => { if (_disposed) return; _visibleLifetime = new CancellationTokenSource(); if (!_state.Loaded) await LoadAsync(); else { ShowLatest(); LoadVisibleThumbnails(); } UpdateSelectionActions(); };
        Unloaded += (_, _) => ReleaseVisibleLifetime();
    }

    private void ReleaseVisibleLifetime()
    {
        _visibleLifetime?.Cancel();
        _visibleLifetime?.Dispose();
        _visibleLifetime = null;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        ReleaseVisibleLifetime();
    }

    private readonly Button _updateNotice;
    public void SetUpdateNotice(string message) { _updateNotice.Content = message; _updateNotice.Visibility = message.Length == 0 ? Visibility.Collapsed : Visibility.Visible; }
    private readonly TextBlock _count;
    private readonly TextBlock _message;
    private void UpdateSelectionActions()
    {
        _copy.IsEnabled = _editButton.IsEnabled = _list.SelectedItems.Count == 1 && !IsSaving;
        _count.Text = _list.SelectedItems.Count > 1 ? L.F("Selected: {0}", _list.SelectedItems.Count) : L.F("Captures: {0}", _rows.Count);
    }
    public void SetRecoveryNotice(string? folder, string logs)
    {
        _recoveryFolder = folder; _logsFolder = logs;
        _recoveryNotice.Visibility = folder is null ? Visibility.Collapsed : Visibility.Visible;
    }
    private void OpenRecoveryLocation(string? folder)
    {
        if (folder is null) return;
        try { Process.Start(new ProcessStartInfo(folder) { UseShellExecute = true }); }
        catch (Exception ex) { ShowFailure("Could not open the folder", ex); }
    }
    public void SetCompact(bool compact)
    {
        _older.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
        _expand.Visibility = compact ? Visibility.Visible : Visibility.Collapsed;
    }
    private readonly TextBlock _warning;
    public void SetWarning(string warning) { _warning.Text = warning; _warning.Visibility = warning.Length == 0 ? Visibility.Collapsed : Visibility.Visible; }
    public event EventHandler? ExpandRequested;
    public event EventHandler? DismissRequested;
    public event EventHandler? ScreenshotRequested;
    public bool IsInteracting => IsSaving || _captureInProgress || _state.IsDragging || _marqueeSelection is not null || _state.IsEditing || _list.ContextMenu?.IsOpen == true;
    public event EventHandler? SettingsRequested;
    public bool IsSaving => _state.IsSaving;
    private void ChangeActiveSaves(int delta)
    {
        _state.ActiveSaves += delta;
        IsEnabled = !IsSaving;
        UpdateSelectionActions();
    }
    internal void SetCaptureInProgress(bool value) => _captureInProgress = value;
    public async Task LoadAsync()
    {
        EndSelectionGesture();
        var generation = ++_state.LoadVersion;
        try
        {
            var token = _visibleLifetime?.Token ?? CancellationToken.None;
            var items = await Task.Run(() => _service.LoadRecentAsync(_settings.General.ShelfRecentCount, token), token).ConfigureAwait(true);
            if (generation != _state.LoadVersion || token.IsCancellationRequested) return;
            _rows.Clear();
            foreach (var item in items) _rows.Add(new ShelfRow(item));
            ShowLatest();
            _message.Text = items.Count == 0 ? L.F("No captures yet. {0}: screenshot. {1}: recording.", _settings.Hotkeys.RegionScreenshot, _settings.Hotkeys.RegionVideo) : "";
            _message.Visibility = items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            _state.Loaded = true;
            _older.IsEnabled = true;
            LoadVisibleThumbnails(); UpdateSelectionActions();
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { _logger.Error("Shelf loading failed.", ex); Ui.Localize(_message, TextBlock.TextProperty, "Could not load captures. Try Refresh."); _message.Visibility = Visibility.Visible; }
    }

    private void ShowLatest()
    {
        _list.SelectedIndex = _rows.Count - 1;
        var generation = _state.LoadVersion;
        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, new Action(() =>
        {
            if (!IsLoaded || generation != _state.LoadVersion) return;
            _list.UpdateLayout();
            FindVisual<ScrollViewer>(_list)?.ScrollToBottom();
        }));
    }

    internal static T? FindVisual<T>(DependencyObject parent) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T result) return result;
            if (FindVisual<T>(child) is { } nested) return nested;
        }
        return null;
    }

    private void LoadVisibleThumbnails()
    {
        if (_visibleLifetime is null) return;
        foreach (var item in RealizedItems(_list))
            if (item.Content is ShelfRow row) _ = row.EnsureThumbnailAsync(_service, _logger, _visibleLifetime.Token);
    }

    private async Task LoadOlderAsync()
    {
        if (_loadingOlder) return;
        _loadingOlder = true; _older.IsEnabled = false;
        var generation = _state.LoadVersion;
        var loaded = _rows.Select(row => row.Item).ToArray();
        var token = _visibleLifetime?.Token ?? CancellationToken.None;
        try
        {
            var items = await Task.Run(() => _service.LoadOlderAsync(loaded, 100, token), token);
            if (generation != _state.LoadVersion || token.IsCancellationRequested) return;
            var panel = FindVisual<ShelfGridPanel>(_list);
            var anchor = panel?.GetViewportAnchor() ?? (Index: 0, Top: 0d);
            for (var i = 0; i < items.Count; i++) _rows.Insert(i, new ShelfRow(items[i]));
            _list.UpdateLayout();
            panel?.RestoreViewportAnchor(anchor.Index + items.Count, anchor.Top);
            _older.IsEnabled = items.Count == 100;
            if (items.Count == 0) { Ui.Localize(_message, TextBlock.TextProperty, "All captures are loaded."); _message.Visibility = Visibility.Visible; }
            LoadVisibleThumbnails(); UpdateSelectionActions();
        }
        catch (OperationCanceledException) { _older.IsEnabled = true; }
        catch (Exception ex) { ShowFailure("Could not load older captures. Try again.", ex); _older.IsEnabled = true; }
        finally { _loadingOlder = false; }
    }

    private void ShowFailure(string message, Exception ex)
    {
        _logger.Error(message, ex);
        _message.Text = L.T(message);
        _message.Visibility = Visibility.Visible;
    }
    private static IEnumerable<ListViewItem> RealizedItems(DependencyObject parent)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is ListViewItem item) yield return item;
            else foreach (var nested in RealizedItems(child)) yield return nested;
        }
    }

    private WpfContextMenu CreateContextMenu()
    {
        var menu = new WpfContextMenu();
        AddMenuItem(menu, "Open", (_, _) => OpenSelected());
        AddMenuItem(menu, "Copy", (_, _) => CopySelected());
        AddMenuItem(menu, "Copy path", (_, _) => CopyPathSelected());
        AddMenuItem(menu, "Open folder", (_, _) => OpenFolderSelected());
        AddMenuItem(menu, "Edit / preview", (_, _) => EditSelected());
        menu.Items.Add(new Separator());
        var delete = AddMenuItem(menu, "Delete", async (_, _) => await DeleteSelectedAsync());
        menu.Items.Add(new Separator());
        var refresh = AddMenuItem(menu, "Refresh", async (_, _) => await LoadAsync());
        var screenshot = AddMenuItem(menu, "Screenshot Shelf", (_, _) => ScreenshotRequested?.Invoke(this, EventArgs.Empty));
        menu.Opened += (_, _) =>
        {
            var count = _list.SelectedItems.Count;
            foreach (var item in menu.Items.OfType<MenuItem>()) item.IsEnabled = !IsSaving && (item == refresh || item == screenshot || (item == delete ? count > 0 : count == 1));
            delete.Header = count > 1 ? L.F("Delete selected ({0})", count) : L.T("Delete");
        };
        return menu;
    }

    private static MenuItem AddMenuItem(ItemsControl menu, string text, RoutedEventHandler handler)
    {
        var glyph = text switch { "Open" => "\uE8A7", "Copy" => "\uE8C8", "Copy path" => "\uE71B", "Open folder" => "\uE8B7", "Edit / preview" => "\uE70F", "Delete" => "\uE74D", "Refresh" => "\uE72C", "Screenshot Shelf" => "\uE722", _ => throw new ArgumentOutOfRangeException(nameof(text)) };
        var shortcut = text switch { "Copy" => "Ctrl+C", "Copy path" => "Ctrl+Shift+C", "Delete" => "Del", _ => "" };
        var item = new MenuItem { Icon = Ui.Icon(glyph, 15), InputGestureText = shortcut }; Ui.Localize(item, HeaderedItemsControl.HeaderProperty, text); if (text == "Delete") item.Foreground = Ui.Brush("Danger"); item.Click += handler; menu.Items.Add(item);
        return item;
    }

    private ShelfRow? SelectedRow => _list.SelectedItems.Count == 1 ? _list.SelectedItem as ShelfRow : null;
    private void OnDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (Keyboard.Modifiers == ModifierKeys.None && ItemsControl.ContainerFromElement(_list, e.OriginalSource as DependencyObject) is ListViewItem)
        {
            EditSelected();
            e.Handled = true;
        }
    }

    private void OpenSelected()
    {
        var row = SelectedRow; if (row is null) return;
        try { Process.Start(new ProcessStartInfo(row.Item.FilePath) { UseShellExecute = true }); DismissRequested?.Invoke(this, EventArgs.Empty); } catch (Exception ex) { ShowFailure("Could not open capture. Check whether the file was moved or deleted.", ex); }
    }

    private async void CopySelected()
    {
        var row = SelectedRow; if (row is null) return;
        try
        {
            if (row.Item.MediaType == MediaType.Screenshot)
            {
                var image = await Task.Run(() => EditorRenderer.LoadImage(row.Item.FilePath)); Clipboard.SetImage(image);
            }
            else Clipboard.SetDataObject(new DataObject(DataFormats.FileDrop, new[] { row.Item.FilePath }));
        }
        catch (Exception ex) { ShowFailure("Could not copy capture. Check the file and try again when the clipboard is available.", ex); }
    }

    private void CopyPathSelected()
    {
        try { if (SelectedRow is { } row) Clipboard.SetText(row.Item.FilePath); }
        catch (Exception ex) { ShowFailure("Clipboard is busy. Try Copy path again.", ex); }
    }
    private void OpenFolderSelected()
    {
        if (SelectedRow is not { } row) return;
        try { Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{row.Item.FilePath}\"") { UseShellExecute = true }); } catch (Exception ex) { ShowFailure("Could not open capture folder. Check the file path.", ex); }
    }

    private async void EditSelected()
    {
        if (SelectedRow is not { } row || IsSaving || _state.IsEditing) return;
        if (!File.Exists(row.Item.FilePath)) { ShowFailure("Could not open capture. Check whether the file was moved or deleted.", new FileNotFoundException("Capture file is missing.", row.Item.FilePath)); return; }
        DismissRequested?.Invoke(this, EventArgs.Empty);
        if (row.Item.MediaType == MediaType.Video)
        {
            try
            {
                var editor = new VideoEditorWindow(row.Item.FilePath, _logger);
                editor.SavedAsync = async result =>
                {
                    await Task.Run(() => _service.AddVideoAsync(result.OutputPath, row.Item.WidthPx, row.Item.HeightPx, result.Duration, CancellationToken.None));
                    await LoadAsync(); _state.NotifySaved();
                };
                editor.Show();
            }
            catch (Exception ex) { _logger.Error("Could not open video editor.", ex); MessageBox.Show(L.T("Could not open video editor. Check the file and try again."), "SnappySnap"); }
            return;
        }
        try
        {
            _state.IsEditing = true;
            var captured = await Task.Run(() => EditorRenderer.ToCapturedImage(EditorRenderer.LoadImage(row.Item.FilePath)));
            var document = new EditorWindow(captured, _logger, _settings.Screenshot.Format, _settings.Editor);
            _state.ConfigureEditor?.Invoke(document);
            string? saveNotice = null;
            var save = new ScreenshotSaveSession(document.Document, _service,
                () => Environment.ExpandEnvironmentVariables(_settings.General.CaptureRoot), row.Item.SourceBounds,
                bitmap => { if (_settings.Screenshot.CopyToClipboard) Clipboard.SetImage(bitmap); },
                message => { saveNotice = message; _state.Notify?.Invoke(message); }, _logger, row.Item.FilePath);
            document.SaveRequestedAsync = async request =>
            {
                ChangeActiveSaves(1);
                try
                {
                    await save.SaveAsync(request);
                    await LoadAsync();
                    if (saveNotice is not null) { _message.Text = saveNotice; _message.Visibility = Visibility.Visible; }
                    _state.NotifySaved();
                }
                finally { ChangeActiveSaves(-1); }
            };
            document.Show();
        }
        catch (Exception ex) { _logger.Error("Could not edit shelf screenshot.", ex); MessageBox.Show(L.T("Could not save the edited screenshot. Check the folder and try again."), "SnappySnap"); }
        finally { _state.IsEditing = false; }
    }

    private async Task DeleteSelectedAsync()
    {
        if (IsSaving || _state.IsEditing) return;
        var rows = _list.SelectedItems.Cast<object>().OfType<ShelfRow>().ToArray();
        if (rows.Length == 0) return;
        _state.IsEditing = true;
        var prompt = rows.Length == 1
            ? L.F("Delete {0}?\n\nThe file will be permanently deleted from disk and removed from Shelf.", rows[0].Title)
            : L.F("Delete {0} selected captures?\n\nThe files will be permanently deleted from disk and removed from Shelf.", rows.Length);
        try { if (MessageBox.Show(Window.GetWindow(this), prompt, "SnappySnap", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return; }
        finally { _state.IsEditing = false; }
        ChangeActiveSaves(1);
        try
        {
            var ids = rows.Select(row => row.Item.Id).ToArray();
            await Task.Run(() => _service.DeleteManyAsync(ids, CancellationToken.None));
            await LoadAsync();
            _state.NotifyDeleted();
        }
        catch (Exception ex)
        {
            await LoadAsync();
            ShowFailure(rows.Length == 1
                ? "Could not delete capture. Close apps using the file and try again."
                : "Could not delete all selected captures. Close apps using the files and try again.", ex);
        }
        finally { ChangeActiveSaves(-1); }
    }
}

public sealed class ShelfRow : INotifyPropertyChanged
{
    private string? _thumbnailPath;
    public ShelfRow(HistoryItem item) { Item = item; _thumbnailPath = item.ThumbnailPath; }
    public HistoryItem Item { get; }
    public string Title => Path.GetFileName(Item.FilePath);
    public string Timestamp => Item.CreatedAtUtc.ToLocalTime().ToString("g", L.Culture);
    public string MediaLabel => L.T(Item.MediaType == MediaType.Video ? "Video recording" : "Screenshot");
    public string Metadata => Item.CreatedAtUtc.ToLocalTime().ToString(Item.CreatedAtUtc.LocalDateTime.Date == DateTime.Today ? "HH:mm" : "d MMM · HH:mm", L.Culture) + " · " + FileSizeFormatter.Format(Item.FileSizeBytes, L.Culture);
    public string AccessibleName => $"{Title}, {MediaLabel}, {Timestamp}, {Details}";
    public string FileInfo => $"{Item.FilePath}\n{Timestamp}\n{Details}";
    public void RefreshLocale() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));
    public string Badge => Item.MediaType == MediaType.Video ? Item.Duration?.ToString(@"mm\:ss", System.Globalization.CultureInfo.InvariantCulture) ?? "MP4" : Path.GetExtension(Item.FilePath).TrimStart('.').ToUpperInvariant();
    public Visibility VideoVisibility => Item.MediaType == MediaType.Video ? Visibility.Visible : Visibility.Collapsed;
    public string Details => $"{Item.WidthPx} × {Item.HeightPx}   •   {FileSizeFormatter.Format(Item.FileSizeBytes, L.Culture)}";
    public ImageSource? Thumbnail { get; private set; }
    public event PropertyChangedEventHandler? PropertyChanged;
    private static readonly SemaphoreSlim ThumbnailWorkers = new(2);
    private Task? _loading;
    private bool _failed;
    public Task EnsureThumbnailAsync(ShelfService service, IAppLogger logger, CancellationToken token)
    {
        if (Thumbnail is not null || _failed || token.IsCancellationRequested) return Task.CompletedTask;
        if (_loading is { } pending) return RetryAfterCancellationAsync(pending, service, logger, token);
        return _loading = LoadThumbnailAsync(service, logger, token);
    }
    private async Task RetryAfterCancellationAsync(Task pending, ShelfService service, IAppLogger logger, CancellationToken token)
    {
        await pending;
        if (!token.IsCancellationRequested && Thumbnail is null && !_failed) await EnsureThumbnailAsync(service, logger, token);
    }
    private async Task LoadThumbnailAsync(ShelfService service, IAppLogger logger, CancellationToken token)
    {
        try
        {
            var image = await Task.Run(async () =>
            {
                await ThumbnailWorkers.WaitAsync(token).ConfigureAwait(false);
                try
                {
                    token.ThrowIfCancellationRequested();
                    var path = _thumbnailPath;
                    if (path is null || !File.Exists(path)) path = (await service.EnsureThumbnailAsync(Item, token).ConfigureAwait(false)).ThumbnailPath;
                    token.ThrowIfCancellationRequested();
                    return path is null ? null : EditorRenderer.LoadImage(path);
                }
                finally { ThumbnailWorkers.Release(); }
            }, token);
            if (!token.IsCancellationRequested) { Thumbnail = image; _failed = image is null; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Thumbnail))); }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { _failed = true; logger.Error("Shelf thumbnail failed.", ex, new Dictionary<string, object?> { ["captureId"] = Item.Id }); }
        finally { _loading = null; }
    }
}

