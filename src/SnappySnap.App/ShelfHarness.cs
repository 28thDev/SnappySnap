#if DEBUG
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Input;
using System.Windows.Threading;
using SnappySnap.Application;
using SnappySnap.Capture;
using SnappySnap.Core;
using SnappySnap.History;
using SnappySnap.Infrastructure;

namespace SnappySnap.App;

internal static partial class VisualHarness
{
    public static async Task RunShelfAsync(string output, bool restoreOnly = false)
    {
        SnappySnap.Localization.L.SetLanguage("en"); SnappySnap.Presentation.Ui.ApplyTheme("Dark");
        Directory.CreateDirectory(output);
        var paths = new AppPaths(Path.Combine(output, "profile"), output); paths.EnsureDirectories();
        using var logger = new FileLogger(paths.LogsPath);
        await using var repository = new SqliteHistoryRepository(paths.DatabasePath, logger);
        var service = new ShelfService(repository, new ThumbnailService(paths.ThumbnailPath, repository, logger));
        var store = new JsonSettingsStore(paths, logger);
        var settings = await store.LoadAsync(default);
        var session = new SettingsSession(settings, store);
        var checks = new List<string>();
        if (restoreOnly)
        {
            Require(settings.Shelf.ThumbnailHeight == 124 && settings.General.ShelfRecentCount == 17, "Preferences survive a process restart");
            var monitors = new Win32MonitorTopologyService().GetMonitors();
            foreach (var compact in new[] { true, false })
            {
                var placement = (compact ? settings.Shelf.Compact : settings.Shelf.History) ?? throw new InvalidOperationException("Missing persisted placement");
                var restartMonitor = monitors.First(m => m.Id == placement.MonitorId);
                var expected = placement.Restore(restartMonitor, compact);
                var content = new ShelfContent(service, settings, logger);
                var window = new ShelfWindow(content, compact, placement) { ShowActivated = false, Topmost = false };
                var foreground = GetForegroundWindow();
                try
                {
                    window.Show(); await content.LoadAsync(); await Idle(); CheckLatest(content);
                    Require(GetForegroundWindow() == foreground, "Restart does not activate Shelf");
                    Require(GetShelfBounds(new WindowInteropHelper(window).Handle, out var actual) && actual.Left == expected.X && actual.Top == expected.Y && actual.Right - actual.Left == expected.Width && actual.Bottom - actual.Top == expected.Height, "Native bounds survive process restart");
                    Require(window.TryGetCaptureBounds(out var captureBounds) && captureBounds.Width > 0 && captureBounds.Height > 0, "Shelf exposes physical capture bounds");
                    Require(content.ThumbnailHeight == 124, "Thumbnail size applied after restart");
                }
                finally { window.Close(); }
            }
            await File.WriteAllTextAsync(Path.Combine(output, "restart-results.txt"), "PASS separate process: both native window placements, thumbnail height 124, limit 17, latest selection/bottom scroll and foreground preservation restored from the isolated profile.");
            return;
        }
        var source = Path.Combine(output, "fixture.png"); SaveBitmap(Fixture(), source);
        // FileDrop transports paths without decoding media. This fixture intentionally tests that boundary only.
        var videoPath = Path.Combine(output, "file-drop-fixture.mp4"); await File.WriteAllTextAsync(videoPath, "FileDrop fixture, not playable video.");
        var mixedState = new ShelfState();
        mixedState.Rows.Add(new ShelfRow(new HistoryItem(Guid.NewGuid(), MediaType.Screenshot, source, DateTimeOffset.UtcNow, 960, 600, null, new FileInfo(source).Length, source, ThumbnailState.Ready, new(0, 0, 960, 600), null, null, false, null)));
        mixedState.Rows.Add(new ShelfRow(mixedState.Rows[0].Item with { Id = Guid.NewGuid(), MediaType = MediaType.Video, FilePath = videoPath }));
        var mixedContent = new ShelfContent(service, settings, logger, mixedState);
        var mixedList = (ListView)typeof(ShelfContent).GetField("_list", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(mixedContent)!; mixedList.SelectAll();
        Require(mixedContent.CreateSelectedFileDrop()?.GetData(DataFormats.FileDrop) is string[] mixed && mixed.SequenceEqual(new[] { source, videoPath }), "Mixed PNG/MP4 file-drop paths");
        File.Delete(videoPath);
        Require(mixedContent.CreateSelectedFileDrop() is null, "Missing selected file blocks entire drag rather than silently omitting it");
        mixedList.UnselectAll(); Require(mixedContent.CreateSelectedFileDrop() is null, "Empty selection has no drag data");
        checks.Add("PASS mixed PNG/MP4 FileDrop paths, missing-file feedback and empty selection; file transport fixture does not test media decoding or external OLE delivery.");
        var time = DateTimeOffset.UtcNow.AddMinutes(-10);
        for (var i = 0; i < 525; i++)
        {
            var path = Path.Combine(output, $"capture-{i:000}-long-name-with-additional-context-for-layout.png"); File.Copy(source, path, true);
            await repository.AddAsync(new(MediaType.Screenshot, path, time.AddSeconds(i / 5), 960, 600, null, source, new(0, 0, 960, 600)), default);
        }
        async Task Idle() => await System.Windows.Application.Current.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
        void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
        void CheckLatest(ShelfContent content)
        {
            var list = Descendants<ListView>(content).Single();
            var rows = list.Items.Cast<ShelfRow>().ToArray();
            Require(rows.Length == settings.General.ShelfRecentCount, "Recent count");
            Require(rows.Select(r => r.Item.Id).SequenceEqual(rows.OrderBy(r => r.Item.CreatedAtUtc).ThenBy(r => r.Item.Id.ToString(), StringComparer.Ordinal).Select(r => r.Item.Id)), "Chronological order");
            Require(ReferenceEquals(list.SelectedItem, rows[^1]), "Latest selection");
            var scroll = Descendants<ScrollViewer>(list).Single();
            Require(Math.Abs(scroll.VerticalOffset - scroll.ScrollableHeight) < 1, $"Bottom scroll: {scroll.VerticalOffset} vs {scroll.ScrollableHeight}");
        }

        foreach (var compact in new[] { true, false })
        foreach (var small in new[] { true, false })
        foreach (var height in new[] { 104d, 164d, 244d })
        {
            session.SetShelfThumbnailHeight(height);
            var content = new ShelfContent(service, settings, logger);
            var window = new ShelfWindow(content, compact);
            var width = compact ? (small ? 560 : 900) : (small ? 700 : 1160);
            var windowHeight = compact ? (small ? 420 : 740) : (small ? 480 : 900);
            await Snapshot(window, output, $"shelf-{(compact ? "compact" : "history")}-{width}-{height}", arrange: w => { w.Width = width; w.Height = windowHeight; }, exercise: async _ =>
            {
                content.SetWarning("A shortcut is occupied. Choose another shortcut in Settings.");
                content.SetUpdateNotice("Update available · Settings");
                await content.LoadAsync(); await Idle(); CheckLatest(content);
                var list = Descendants<ListView>(content).Single();
                Require(Descendants<ListViewItem>(list).Count() < 50, "Virtualization must realize only viewport rows");
                Require(list.ClipToBounds && list.ActualHeight > 0, "Clipped positive list viewport");
                var moreMenu = list.ContextMenu!;
                var screenshotButton = moreMenu.Items.OfType<MenuItem>().SingleOrDefault(item => Equals(item.Header, "Screenshot Shelf"));
                Require(screenshotButton is not null, "Shelf exposes a self-screenshot action");
                if (compact && small && height == 104)
                {
                    var requested = false;
                    EventHandler onRequested = (_, _) => requested = true;
                    content.ScreenshotRequested += onRequested;
                    screenshotButton!.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
                    content.ScreenshotRequested -= onRequested;
                    Require(requested, "Screenshot Shelf raises the capture request");
                }
                var listTop = list.TranslatePoint(new System.Windows.Point(), content).Y;
                var settingsButton = Descendants<Button>(content).Single(b => Name(b) == "Settings");
                var settingsRequested = false;
                EventHandler onSettings = (_, _) => settingsRequested = true;
                content.SettingsRequested += onSettings;
                settingsButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
                content.SettingsRequested -= onSettings;
                Require(settingsRequested, "Settings opens with one toolbar click");
                var actions = Descendants<Button>(content).Where(b => Name(b) is "Copy" or "Edit" or "Settings" or "History");
                foreach (var button in actions) Require(button.TranslatePoint(new System.Windows.Point(0, button.ActualHeight), content).Y <= listTop, "Toolbar overlaps list");
                var slider = Descendants<Slider>(content).Single();
                Require(AutomationProperties.GetName(slider) == "Thumbnail size" && slider.Focusable && slider.SmallChange == 10, "Accessible keyboard slider");
                var scroll = Descendants<ScrollViewer>(list).Single(); scroll.ScrollToTop(); await Idle();
                CheckShelfSelection(content, window, Path.Combine(output, $"selection-{(compact ? "compact" : "history")}-{width}-{height}.png"));
                content.EndSelectionGesture();
                CaptureContent(window, Path.Combine(output, $"shelf-top-{(compact ? "compact" : "history")}-{width}-{height}.png"));
                scroll.ScrollToBottom(); await Idle();
            });
        }
        checks.Add("PASS 12 WPF layouts: compact 560x420/900x740, history 700x480/1160x900; preview heights 104/164/244; top/bottom scroll, warnings, update notices, long filenames, latest selection, clipping and viewport virtualization.");
        checks.Add("PASS 12 selection layouts: accessible multi-selection, deferred selected-card click, ordered multi-file FileDrop, normal/reverse/Ctrl/Shift marquee, Escape restoration, offscreen selection after scroll, capture-loss cleanup. Physical Ctrl/Shift clicks and external OLE drop remain manual acceptance.");

        var compactContent = new ShelfContent(service, settings, logger);
        var historyContent = new ShelfContent(service, settings, logger);
        var compactWindow = new ShelfWindow(compactContent, true) { ShowActivated = false, Topmost = false, Left = -15000, Top = -15000 };
        compactWindow.Loaded += (_, _) =>
        {
            ((GlobalMouseClickSource?)typeof(ShelfWindow).GetField("_outsideClicks", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(compactWindow))?.Stop();
            compactWindow.Left = -15000; compactWindow.Top = -15000;
        };
        var historyWindow = new ShelfWindow(historyContent, false) { ShowActivated = false, Left = -15000, Top = -15000 };
        try
        {
            compactWindow.Show(); historyWindow.Show();
            await compactContent.LoadAsync(); await historyContent.LoadAsync(); await Idle();
            var historyList = Descendants<ListView>(historyContent).Single();
            var scroll = Descendants<ScrollViewer>(historyList).Single(); scroll.ScrollToVerticalOffset(85); await Idle();
            var panel = Descendants<ShelfGridPanel>(historyList).Single(); var anchor = panel.GetViewportAnchor();
            var anchorId = ((ShelfRow)historyList.Items[anchor.Index]).Item.Id;
            await (Task)typeof(ShelfContent).GetMethod("LoadOlderAsync", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(historyContent, null)!;
            await Idle();
            var after = panel.GetViewportAnchor();
            // A page can change the column of the anchored item; its row and pixel offset must survive.
            var anchoredIndex = historyList.Items.Cast<ShelfRow>().ToList().FindIndex(r => r.Item.Id == anchorId);
            Require(Math.Abs(after.Top - anchor.Top) < 1 && anchoredIndex >= after.Index && anchoredIndex < after.Index + Math.Max(1, (int)(panel.ViewportWidth / panel.CardMinimumWidth)), "Older-page viewport anchor");
            Require(historyList.Items.Count == 150, "Older history page");
            Require(Descendants<ListView>(compactContent).Single().Items.Count == 50, "Compact collection isolated from history");

            compactContent.ThumbnailHeightChanged += height => { session.SetShelfThumbnailHeight(height); compactContent.UpdateSettings(settings); historyContent.UpdateSettings(settings); };
            var sizeSlider = Descendants<Slider>(compactContent).Single(); sizeSlider.SetCurrentValue(Slider.ValueProperty, 114d); await Idle();
            Require(compactContent.ThumbnailHeight == 114 && historyContent.ThumbnailHeight == 114 && settings.Shelf.ThumbnailHeight == 114, "Live slider shared preference");
            sizeSlider.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(sizeSlider) ?? throw new InvalidOperationException("Shelf slider has no presentation source."), 0, Key.Right) { RoutedEvent = Keyboard.KeyDownEvent }); await Idle();
            Require(sizeSlider.Value == 124 && historyContent.ThumbnailHeight == 124, "Slider Right key increases size by one step");
            sizeSlider.SetCurrentValue(Slider.ValueProperty, 104d); await Idle();
            scroll.ScrollToBottom(); await Idle();
            sizeSlider.SetCurrentValue(Slider.ValueProperty, 244d); await Idle();
            Require(Math.Abs(scroll.VerticalOffset - scroll.ScrollableHeight) < 1, "Growing thumbnails must keep the latest row visible");
            historyWindow.Width = 700; await Idle();
            Require(Math.Abs(scroll.VerticalOffset - scroll.ScrollableHeight) < 1, "Changing columns must preserve bottom scroll");
            historyWindow.Height = 580; await Idle();
            Require(Math.Abs(scroll.VerticalOffset - scroll.ScrollableHeight) < 1, "Resizing height must preserve bottom scroll");
            scroll.ScrollToVerticalOffset(panel.RowHeight * 4 + 35); await Idle();
            var reading = panel.GetViewportAnchor();
            var readingId = ((ShelfRow)historyList.Items[reading.Index]).Item.Id;
            sizeSlider.SetCurrentValue(Slider.ValueProperty, 104d); await Idle();
            var reflowed = panel.GetViewportAnchor();
            var columns = Math.Max(1, (int)(panel.ViewportWidth / panel.CardMinimumWidth));
            Require(historyList.Items.Cast<ShelfRow>().Skip(reflowed.Index).Take(columns).Any(row => row.Item.Id == readingId), "Resizing thumbnails must retain the visible history row");
            Require(Descendants<Button>(historyContent).Single(b => Name(b) == "History").Visibility == Visibility.Collapsed, "Expanded history must not offer History again");
            sizeSlider.SetCurrentValue(Slider.ValueProperty, 124d); await Idle();
            checks.Add("PASS review regression: preview resize/column/viewport changes retain bottom or current history row; Expand only appears in compact mode.");
            settings.General.ShelfRecentCount = 17; compactContent.UpdateSettings(settings); historyContent.UpdateSettings(settings);
            await compactContent.LoadAsync(); await historyContent.LoadAsync(); await Idle();
            CheckLatest(compactContent); CheckLatest(historyContent);
            Require((await repository.GetRecentAsync(1000, default)).Count == 525, "Display limit must not delete files");
            var savedPath = Path.Combine(output, "saved-capture.png"); File.Copy(source, savedPath, true);
            var saved = await service.AddScreenshotAsync(savedPath, 960, 600, default);
            await compactContent.LoadAsync(); await historyContent.LoadAsync(); await Idle();
            CheckLatest(compactContent); CheckLatest(historyContent);
            Require(((ShelfRow)Descendants<ListView>(compactContent).Single().SelectedItem).Item.Id == saved.Id && ((ShelfRow)historyList.SelectedItem).Item.Id == saved.Id, "New saved capture selected in both views");
            await service.DeleteAsync(saved.Id, default); await compactContent.LoadAsync(); await historyContent.LoadAsync(); await Idle();
            CheckLatest(compactContent); CheckLatest(historyContent);
            Require(!historyList.Items.Cast<ShelfRow>().Any(row => row.Item.Id == saved.Id) && !Descendants<ListView>(compactContent).Single().Items.Cast<ShelfRow>().Any(row => row.Item.Id == saved.Id), "Deleted capture absent in both views");
            var batchAPath = Path.Combine(output, "batch-delete-a.png"); var batchBPath = Path.Combine(output, "batch-delete-b.png");
            File.Copy(source, batchAPath, true); File.Copy(source, batchBPath, true);
            var batchA = await service.AddScreenshotAsync(batchAPath, 960, 600, default);
            var batchB = await service.AddScreenshotAsync(batchBPath, 960, 600, default);
            await service.DeleteManyAsync(new[] { batchA.Id, batchB.Id }, default);
            Require(!File.Exists(batchAPath) && !File.Exists(batchBPath), "Batch delete physically removes every selected media file");
            checks.Add("PASS older-page anchor, isolated compact limit, live shared slider and routed Right key, live limit=17 without file deletion; multi-selection Delete is enabled and batch media deletion removes files from disk.");
        }
        finally { compactWindow.Close(); historyWindow.Close(); }

        // Real HWND move/resize + persistence; no injected desktop input or production profile.
        var monitor = new Win32MonitorTopologyService().GetMonitors().First(m => m.IsPrimary);
        foreach (var compact in new[] { true, false })
        {
            var initial = new ShelfPlacement(monitor.Id, new DipRect(20, 25, compact ? 650 : 850, compact ? 500 : 650));
            var content = new ShelfContent(service, settings, logger);
            var window = new ShelfWindow(content, compact, initial) { ShowActivated = false, Topmost = false };
            window.PlacementChanged += value => session.SetShelfPlacement(compact, value);
            var foreground = GetForegroundWindow();
            window.Show(); await Idle();
            Require(GetForegroundWindow() == foreground, "Non-activating open");
            var moved = new ShelfPlacement(monitor.Id, new DipRect(65, 55, compact ? 710 : 920, compact ? 530 : 700));
            var pixels = moved.Restore(monitor, compact);
            var hwnd = new WindowInteropHelper(window).Handle;
            // Simulate minimums reduced on a smaller work area before moving to this display.
            window.MinWidth = 200; window.MinHeight = 160;
            SetShelfBounds(hwnd, 0, pixels.X, pixels.Y, pixels.Width, pixels.Height, 0x0010 | 0x0004);
            SendShelfMessage(hwnd, 0x0232, 0, 0); await Idle();
            Require(window.MinWidth == Math.Min(compact ? 560 : 700, monitor.WorkArea.Width / monitor.ScaleX) && window.MinHeight == Math.Min(compact ? 420 : 480, monitor.WorkArea.Height / monitor.ScaleY), "Moving restores minimums for the current work area");
            var saved = compact ? settings.Shelf.Compact : settings.Shelf.History;
            Require(saved is not null && Math.Abs(saved.Bounds.Width - pixels.Width / monitor.ScaleX) < 1, "Completed resize saves normal geometry");
            window.WindowState = WindowState.Maximized; await Idle(); window.Close();
            Require(saved == (compact ? settings.Shelf.Compact : settings.Shelf.History), "Maximized close retains normal geometry");
            await session.FlushPreferencesAsync(default);
            var reloaded = await new JsonSettingsStore(paths, logger).LoadAsync(default);
            var restored = compact ? reloaded.Shelf.Compact : reloaded.Shelf.History;
            var reopened = new ShelfWindow(new ShelfContent(service, reloaded, logger), compact, restored) { ShowActivated = false, Topmost = false };
            reopened.Show(); await Idle();
            Require(Math.Abs(reopened.ActualWidth - saved!.Bounds.Width) < 1 && Math.Abs(reopened.ActualHeight - saved.Bounds.Height) < 1, "Reopened size");
            Require(GetShelfBounds(new WindowInteropHelper(reopened).Handle, out var actual) && Math.Abs(actual.Left - pixels.X) <= 1 && Math.Abs(actual.Top - pixels.Y) <= 1, "Reopened position");
            reopened.Close();
        }
        checks.Add($"PASS native HWND move/resize completion, normal bounds after maximized close, JSON reload and reopen for both modes; foreground preserved. Monitor {monitor.Bounds.Width}x{monitor.Bounds.Height}, DPI {monitor.DpiX}.");

        var emptyPaths = new AppPaths(Path.Combine(output, "empty-profile"), output); emptyPaths.EnsureDirectories();
        await using var emptyRepository = new SqliteHistoryRepository(emptyPaths.DatabasePath, logger);
        var emptyService = new ShelfService(emptyRepository, new ThumbnailService(emptyPaths.ThumbnailPath, emptyRepository, logger));
        await Snapshot(new ShelfWindow(emptyService, settings, logger), output, "shelf-empty", exercise: async w => { await ((ShelfWindow)w).Shelf.LoadAsync(); await Idle(); Require(Descendants<ListView>(w).Single().Items.Count == 0, "Empty shelf"); });
        checks.Add("PASS empty state. NOT VERIFIED: physical mouse resize/keyboard, real tray/hotkey capture flow, multi-monitor mixed DPI and monitor disconnect. Native geometry checks are programmatic, not physical input acceptance.");
        await File.WriteAllLinesAsync(Path.Combine(output, "results.txt"), checks);
    }

    private static void CheckShelfSelection(ShelfContent content, Window window, string screenshot)
    {
        void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
        var list = Descendants<ListView>(content).Single();
        var panel = Descendants<ShelfGridPanel>(list).Single();
        var rows = list.Items.Cast<ShelfRow>().ToArray();
        var first = (ListViewItem)list.ItemContainerGenerator.ContainerFromIndex(0);
        var bounds = panel.GetItemBounds(0, first.Margin);
        var start = new System.Windows.Point(bounds.Right + 2, Math.Min(bounds.Bottom, panel.ViewportHeight) - 2);
        var end = new System.Windows.Point(bounds.Left + 2, bounds.Top + 2);
        Require(list.SelectionMode == SelectionMode.Extended, "Native Ctrl/Shift selection mode");
        var peer = new System.Windows.Automation.Peers.ListViewAutomationPeer(list);
        Require(((System.Windows.Automation.Provider.ISelectionProvider)peer).CanSelectMultiple, "UI Automation exposes multiple selection");
        list.UnselectAll(); list.SelectedItems.Add(rows[3]); list.SelectedItems.Add(rows[0]);
        var menu = list.ContextMenu ?? throw new InvalidOperationException("Shelf context menu is missing.");
        menu.RaiseEvent(new RoutedEventArgs(ContextMenu.OpenedEvent));
        var delete = menu.Items.OfType<MenuItem>().Single(item => item.InputGestureText == "Del");
        Require(delete.IsEnabled && delete.Header is string header && header.Contains('2'), "Context Delete stays enabled for a multi-selection");
        var savingProperty = typeof(ShelfContent).GetProperty(nameof(ShelfContent.IsSaving))!;
        savingProperty.SetValue(content, true);
        try
        {
            menu.RaiseEvent(new RoutedEventArgs(ContextMenu.OpenedEvent));
            Require(!list.IsEnabled && !delete.IsEnabled, "In-flight file changes disable Shelf input and repeat deletion");
            Require(((Task)typeof(ShelfContent).GetMethod("DeleteSelectedAsync", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(content, null)!).IsCompletedSuccessfully,
                "A repeat delete returns without a second confirmation or file operation");
        }
        finally { savingProperty.SetValue(content, false); }
        menu.RaiseEvent(new RoutedEventArgs(ContextMenu.OpenedEvent));
        Require(list.IsEnabled && delete.IsEnabled, "Shelf input returns after file changes finish");
        var data = content.CreateSelectedFileDrop();
        Require(data?.GetData(DataFormats.FileDrop) is string[] paths && paths.SequenceEqual(new[] { rows[0].Item.FilePath, rows[3].Item.FilePath }), "FileDrop includes selection in shelf order");

        var down = new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, System.Windows.Input.MouseButton.Left) { RoutedEvent = UIElement.PreviewMouseLeftButtonDownEvent, Source = first };
        typeof(MouseButtonEventArgs).GetProperty(nameof(MouseButtonEventArgs.ClickCount))!.SetValue(down, 1);
        list.RaiseEvent(down);
        Require(list.SelectedItems.Count == 2, "Pressed selected card retains drag group");
        list.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, System.Windows.Input.MouseButton.Left) { RoutedEvent = UIElement.PreviewMouseLeftButtonUpEvent, Source = first });
        Require(list.SelectedItems.Count == 1 && list.SelectedItem == rows[0], "Click without drag collapses group");

        content.BeginMarquee(start, panel, ModifierKeys.None); content.UpdateMarquee(end, panel);
        Require(list.SelectedItems.Count == 1 && list.SelectedItems.Contains(rows[0]), "Reverse marquee selects first card only");
        content.EndSelectionGesture();
        content.BeginMarquee(end, panel, ModifierKeys.Control); content.UpdateMarquee(start, panel);
        Require(list.SelectedItems.Count == 0, "Ctrl marquee toggles initial selection");
        content.CancelSelectionGesture(); Require(list.SelectedItems.Contains(rows[0]), "Cancel restores selection");
        list.SelectedItems.Add(rows[^1]);
        content.BeginMarquee(end, panel, ModifierKeys.Shift); content.UpdateMarquee(start, panel);
        Require(list.SelectedItems.Count == 2 && list.SelectedItems.Contains(rows[^1]), "Shift marquee keeps selection outside area");
        content.EndSelectionGesture();
        content.BeginMarquee(start, panel, ModifierKeys.None); content.UpdateMarquee(end, panel);
        list.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(list) ?? throw new InvalidOperationException("Shelf list has no presentation source."), 0, Key.Escape) { RoutedEvent = Keyboard.PreviewKeyDownEvent });
        Require(window.IsVisible && list.SelectedItems.Count == 2, "Escape restores selection without closing compact shelf");

        content.BeginMarquee(new System.Windows.Point(0, 0), panel, ModifierKeys.None);
        panel.SetVerticalOffset(panel.RowHeight * 5); list.UpdateLayout();
        content.UpdateMarquee(new System.Windows.Point(panel.ViewportWidth, panel.ViewportHeight / 2), panel);
        Require(list.SelectedItems.Contains(rows[0]) && list.SelectedItems.Count > 5, "Marquee includes virtualized rows across scroll");
        Require(Descendants<ListViewItem>(list).Count() < rows.Length, "Marquee preserves virtualization");
        content.EndSelectionGesture(); panel.SetVerticalOffset(0); list.UpdateLayout();
        content.BeginMarquee(new System.Windows.Point(0, 0), panel, ModifierKeys.None);
        content.UpdateMarquee(new System.Windows.Point(panel.ViewportWidth - 12, Math.Min(panel.RowHeight * 1.5, panel.ViewportHeight - 5)), panel);
        CaptureContent(window, screenshot);
        list.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice, 0) { RoutedEvent = Mouse.LostMouseCaptureEvent });
        Require(!content.IsInteracting, "Capture loss terminates marquee");
    }

    [StructLayout(LayoutKind.Sequential)] private struct ShelfNativeRect { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll", EntryPoint = "GetWindowRect")] private static extern bool GetShelfBounds(nint hwnd, out ShelfNativeRect rect);
    [DllImport("user32.dll", EntryPoint = "SetWindowPos")] private static extern bool SetShelfBounds(nint hwnd, nint after, int x, int y, int width, int height, uint flags);
    [DllImport("user32.dll", EntryPoint = "SendMessageW")] private static extern nint SendShelfMessage(nint hwnd, uint message, nint wParam, nint lParam);
}
#endif
