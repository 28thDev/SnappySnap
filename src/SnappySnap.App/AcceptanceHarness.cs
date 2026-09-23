#if PERFORMANCE_HARNESS
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using SnappySnap.Application;
using SnappySnap.Capture;
using SnappySnap.Core;
using SnappySnap.Editor;
using SnappySnap.History;
using SnappySnap.Infrastructure;
using SnappySnap.Presentation;

namespace SnappySnap.App;

internal static class AcceptanceHarness
{
    public static async Task RunAsync(string output, bool interactive)
    {
        Directory.CreateDirectory(output);
        var paths = new AppPaths(Path.Combine(output, "profile"), output); paths.EnsureDirectories();
        using var logger = new FileLogger(paths.LogsPath);
        await using var repository = new SqliteHistoryRepository(paths.DatabasePath, logger);
        var service = new ShelfService(repository, new ThumbnailService(paths.ThumbnailPath, repository, logger));
        var settings = AppSettings.Defaults(); settings.General.StartWithWindows = false; settings.General.CaptureRoot = output; settings.General.ShelfRecentCount = 500;
        var fixture = Fixture(); var media = Path.Combine(output, "fixture.png");
        await EditorRenderer.SavePngAsync(new EditorDocument(fixture), media, CancellationToken.None);
        var thumb = new TransformedBitmap(EditorRenderer.ToBitmapSource(fixture), new ScaleTransform(1d / 8, 1d / 8)); thumb.Freeze();
        var thumbnailPath = Path.Combine(output, "thumbnail.png");
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(thumb)); using (var stream = File.Create(thumbnailPath)) encoder.Save(stream);
        var state = new ShelfState { Loaded = true };
        for (var i = 0; i < 500; i++) state.Rows.Add(new ShelfRow(new HistoryItem(Guid.NewGuid(), MediaType.Screenshot, media, DateTimeOffset.UtcNow.AddMinutes(-i), 3840, 2160, null, new FileInfo(media).Length, thumbnailPath, ThumbnailState.Ready, new(0, 0, 3840, 2160), null, null, false, null)));
        var checks = new List<string>();
        foreach (var width in new[] { 560d, 740d, 1000d })
        {
            var shelf = new ShelfWindow(new ShelfContent(service, settings, logger, state), false) { MinWidth = 0, Width = width, Height = 900 };
            shelf.Show(); await Idle(); await Task.Delay(300);
            var items = PerformanceHarness.Descendants<ListViewItem>(shelf).ToArray();
            Require(items.Length is > 0 and < 30, "Shelf must realize only viewport rows.");
            Require(items.Any(item => (item.Content as ShelfRow)?.Thumbnail is not null), "Visible thumbnails must load.");
            checks.Add($"PASS shelf width={width}: realized={items.Length}/500; previews={items.Count(item => (item.Content as ShelfRow)?.Thumbnail is not null)}");
            var list = PerformanceHarness.Descendants<ListView>(shelf).Single();
            var second = (ListViewItem)list.ItemContainerGenerator.ContainerFromIndex(1);
            second.IsSelected = true; await Idle();
            Require(list.SelectedItems.Count == 1 && ReferenceEquals(list.SelectedItem, state.Rows[1]), "Context selection must target exactly the clicked capture.");
            list.SelectedIndex = 499; list.ScrollIntoView(list.SelectedItem); await Idle();
            Require(list.ItemContainerGenerator.ContainerFromIndex(499) is ListViewItem, "Last item must be reachable via ScrollIntoView.");
            Require(PerformanceHarness.Descendants<ListViewItem>(shelf).Count() < 30, "Scrolling must remain virtualized.");
            list.SelectedIndex = 0; list.ScrollIntoView(list.SelectedItem); await Idle();
            foreach (var container in PerformanceHarness.Descendants<ListViewItem>(list))
            {
                var index = list.ItemContainerGenerator.IndexFromContainer(container);
                Require(index >= 0 && ReferenceEquals(list.Items[index], container.Content), "Recycled container must retain the correct item.");
            }
            Require(list.SelectedIndex == 0, "Scroll/recycling must preserve selection.");
            checks.Add($"PASS width={width}: first selected index={list.SelectedIndex}; selected bounds={((ListViewItem)list.ItemContainerGenerator.ContainerFromIndex(0)).TransformToAncestor(list).Transform(new Point(0, 0))}");
            var rows = state.Rows.ToArray(); state.Rows.Clear(); foreach (var row in rows) state.Rows.Add(row); list.SelectedIndex = 0; await Idle();
            Require(PerformanceHarness.Descendants<ListViewItem>(list).Any(item => ReferenceEquals(item.Content, state.Rows[0])), "Refresh must show the first row.");
            PerformanceHarness.Snapshot(shelf, Path.Combine(output, $"shelf-{width}.png"));
            if (width == 1000) await Chrome(shelf, checks, "Shelf");
            shelf.Close();
        }
        var cold = new ShelfState { Loaded = true };
        foreach (var row in state.Rows) cold.Rows.Add(new ShelfRow(row.Item with { Id = Guid.NewGuid(), ThumbnailPath = null, ThumbnailState = ThumbnailState.Pending }));
        var opening = Stopwatch.StartNew();
        var coldShelf = new ShelfWindow(new ShelfContent(service, settings, logger, cold), false);
        coldShelf.Show(); await Idle();
        var coldOpenMs = opening.Elapsed.TotalMilliseconds;
        await Task.Delay(1000);
        var realizedCold = PerformanceHarness.Descendants<ListViewItem>(coldShelf).Count();
        var generatedCold = Directory.GetFiles(paths.ThumbnailPath).Length;
        Require(realizedCold < 30 && generatedCold is > 0 and < 30, "Cold history must only generate viewport thumbnails.");
        coldShelf.Close(); await Task.Delay(500);
        var closedCount = Directory.GetFiles(paths.ThumbnailPath).Length;
        await Task.Delay(500);
        Require(Directory.GetFiles(paths.ThumbnailPath).Length == closedCount, "Closed Shelf must stop generating thumbnails.");
        checks.Add($"PASS cold Shelf 500: open/drain={coldOpenMs:0.0} ms (includes fixed 80 ms settling); containers={realizedCold}; generated={generatedCold}; stable after close={closedCount}");
        var compact = new ShelfWindow(new ShelfContent(service, settings, logger, state), true);
        var foreground = GetForegroundWindow(); compact.Place(new Win32MonitorTopologyService().GetMonitors()[0]); compact.Show(); compact.Place(new Win32MonitorTopologyService().GetMonitors()[0]); await Idle();
        Require(GetForegroundWindow() == foreground, "Save Shelf must not activate.");
        PerformanceHarness.Snapshot(compact, Path.Combine(output, "shelf-compact.png")); checks.Add("PASS compact Shelf preserves foreground; shares the same 500 rows and selection."); compact.Close();
        var editor = new EditorWindow(fixture, logger); editor.Show(); await Idle();
        await Chrome(editor, checks, "Screenshot Editor"); editor.Close();
        var preferences = new SettingsWindow(settings, new JsonSettingsStore(paths, logger), new WindowsStartupRegistration(), logger);
        // Save would alter startup registration; this harness only opens/closes Settings chrome.
        preferences.Show(); await Idle(); await Chrome(preferences, checks, "Settings"); preferences.Close();
        var videoPath = Path.Combine(output, "video-fixture.mp4");
        if (File.Exists(videoPath))
        {
            var video = new VideoEditorWindow(videoPath, logger); video.Show(); await Idle();
            await Chrome(video, checks, "Video Editor"); video.Close();
            var edit = new VideoEditSession(TimeSpan.FromSeconds(10));
            edit.Delete(TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(3)); edit.Delete(TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(6));
            using var preview = new WindowsVideoPreviewSession { IsMuted = true };
            await Task.Run(() => preview.LoadAsync(videoPath, edit.ExportTimeline(), CancellationToken.None));
            foreach (var position in new[] { .4, 2.4, 6.4, 1.4, 5.4 })
            {
                preview.TakeFrame(); var seek = Stopwatch.StartNew(); preview.Seek(TimeSpan.FromSeconds(position));
                var expectedRed = 30 + (int)Math.Floor(edit.ToSource(TimeSpan.FromSeconds(position)).TotalSeconds) * 20;
                var matched = false;
                while (seek.ElapsedMilliseconds < 8000)
                {
                    var frame = preview.TakeFrame();
                    if (frame is not null && Math.Abs(frame.Bgra32[((frame.Height / 2) * frame.Width + frame.Width / 2) * 4 + 2] - expectedRed) < 12) { matched = true; break; }
                    await Task.Delay(10);
                }
                Require(matched, $"Edited seek {position} must produce the corresponding source-color frame.");
                checks.Add($"PASS native edited seek {position:0.0}s -> source {edit.ToSource(TimeSpan.FromSeconds(position)).TotalSeconds:0.0}s; correct frame after {seek.Elapsed.TotalMilliseconds:0.0} ms");
            }
            for (var i = 0; i < 30; i++) preview.Seek(TimeSpan.FromSeconds(i % 7 + .4));
            preview.Seek(TimeSpan.FromSeconds(3.4)); preview.Play(); await Task.Delay(600); preview.Pause();
            Require(preview.Position.TotalSeconds is >= 3.4 and < 4.6, "Rapid seeks must converge to the latest position and resume playback.");
            preview.Dispose(); await Task.Delay(150);
            Require(preview.TakeFrame() is null, "Disposed preview must not publish frames.");
            checks.Add("PASS native latest-seek coalescing, Play/Pause and no frames after disposal.");
        }
        checks.Add("NOT VERIFIED: negative/mixed-monitor topology, real 100/150% DPI, taskbar auto-hide, installed upgrade cycle.");
        await File.WriteAllLinesAsync(Path.Combine(output, "checks.txt"), checks);
        if (!interactive) return;
        var host = new Window { Title = "SnappySnap — Isolated acceptance", Width = 520, Height = 510 };
        var buttons = new StackPanel { Margin = new Thickness(20) };
        buttons.Children.Add(Ui.Text("Generated fixtures only. Working history and settings are not used.", 14));
        buttons.Children.Add(Ui.Button("Open screenshot (4K)", "", async (_, _) =>
        {
            var edit = new EditorWindow(fixture, logger) { Owner = host };
            if (await edit.ShowAsync())
            {
                var file = Path.Combine(output, "manual-" + Guid.NewGuid().ToString("N") + ".png");
                await EditorRenderer.SavePngAsync(edit.Document, file, CancellationToken.None);
                await File.AppendAllTextAsync(Path.Combine(output, "checks.txt"), "PASS manual screenshot saved: " + file + "\n");
            }
        }));
        buttons.Children.Add(Ui.Button("Open full Shelf", "", (_, _) => new ShelfWindow(new ShelfContent(service, settings, logger, state), false).Show()));
        buttons.Children.Add(Ui.Button("Open compact Shelf", "", (_, _) =>
        {
            // Only the interactive test exposes the tool window in the automation inventory.
            // Production/nonactivation acceptance above retains ShowInTaskbar=false.
            var window = new ShelfWindow(new ShelfContent(service, settings, logger, state), true) { ShowInTaskbar = true };
            window.Closed += async (_, _) => await File.AppendAllTextAsync(Path.Combine(output, "checks.txt"), "OBSERVED interactive compact Shelf closed.\n");
            window.Place(new Win32MonitorTopologyService().GetMonitors()[0]); window.Show(); window.Place(new Win32MonitorTopologyService().GetMonitors()[0]);
            var label = Ui.Text("Drop a Shelf capture here", 16);
            var receiver = new Window { Owner = window, Title = "SnappySnap — FileDrop receiver", Left = window.Left + 400, Top = window.Top + 450, Width = 380, Height = 150, Topmost = true, ShowActivated = false, AllowDrop = true, Content = Ui.Card(label) };
            receiver.DragOver += (_, e) => { e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None; e.Handled = true; };
            receiver.Drop += async (_, e) =>
            {
                var files = e.Data.GetData(DataFormats.FileDrop) as string[];
                Require(files is { Length: 1 } && files[0] == media, "Drag-out must deliver the selected capture as FileDrop.");
                label.Text = "PASS FileDrop: fixture.png";
                await File.AppendAllTextAsync(Path.Combine(output, "checks.txt"), "PASS real Shelf drag-out: selected fixture received by another HWND as FileDrop/Copy.\n");
            };
            receiver.Show();
        }));
        buttons.Children.Add(Ui.Button("Open video fixture", "", (_, _) => new VideoEditorWindow(Path.Combine(output, "video-fixture.mp4"), logger).Show()));
        buttons.Children.Add(Ui.Text("Settings: automatic chrome check completed without saving. Close this test panel when finished.", 12, "Muted"));
        var drop = Ui.Text("Drop a test Shelf capture here", 16); var target = Ui.Card(drop); target.AllowDrop = true; target.Margin = new Thickness(0, 12, 0, 0);
        target.DragOver += (_, e) => { e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None; e.Handled = true; };
        target.Drop += async (_, e) =>
        {
            var files = e.Data.GetData(DataFormats.FileDrop) as string[];
            Require(files is { Length: 1 } && files[0] == media, "Drag-out must deliver the selected capture as FileDrop.");
            drop.Text = "PASS FileDrop: fixture.png";
            await File.AppendAllTextAsync(Path.Combine(output, "checks.txt"), "PASS real Shelf drag-out: selected fixture received as FileDrop/Copy.\n");
        };
        buttons.Children.Add(target);
        Ui.Shell(host, "Acceptance fixtures", buttons); var closed = new TaskCompletionSource(); host.Closed += (_, _) => closed.TrySetResult(); host.Show(); await closed.Task;
    }
    private static async Task Chrome(Window window, List<string> checks, string name)
    {
        var handle = new WindowInteropHelper(window).Handle; GetWindowRect(handle, out var original);
        var dpi = VisualTreeHelper.GetDpi(window);
        var limits = new MinMaxInfo { MaxTrackSize = new NativePoint(10000, 10000) };
        SendMessage(handle, 0x0024, 0, ref limits);
        Require(limits.MinTrackSize.X >= window.MinWidth * dpi.DpiScaleX && limits.MinTrackSize.Y >= window.MinHeight * dpi.DpiScaleY, $"{name} native resize must retain WPF minimums.");
        var monitor = MonitorFromWindow(handle, 2); var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() }; Require(GetMonitorInfo(monitor, ref info), "Monitor info.");
        window.WindowState = WindowState.Maximized; await Idle(); GetWindowRect(handle, out var maximized);
        Require(maximized.Equals(info.Work), $"{name} maximized {maximized} differs from work area {info.Work}.");
        window.WindowState = WindowState.Normal; await Idle(); GetWindowRect(handle, out var restored);
        Require(restored.Equals(original), $"{name} restore lost previous geometry.");
        checks.Add($"PASS {name}: maximize HWND={maximized}; restore HWND={restored}; DPI={VisualTreeHelper.GetDpi(window).PixelsPerDip * 96}");
    }
    private static async Task Idle() { await System.Windows.Application.Current.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Render); await Task.Delay(80); }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static CapturedImage Fixture()
    {
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawRectangle(Ui.Brush("Background"), null, new Rect(0, 0, 3840, 2160));
            for (var row = 0; row < 16; row++)
            {
                dc.DrawRectangle(Ui.Brush("Surface"), new Pen(Ui.Brush("Border"), 2), new Rect(100, 80 + row * 122, 3640, 105));
                dc.DrawText(new FormattedText($"{row + 1:00}  SnappySnap fixture — small text / shapes / crop / effects      3840 × 2160", System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight, new Typeface("Segoe UI"), 38, Ui.Brush("Text"), 1), new Point(130, 100 + row * 122));
            }
        }
        var target = new RenderTargetBitmap(3840, 2160, 96, 96, PixelFormats.Pbgra32); target.Render(visual); target.Freeze(); return EditorRenderer.ToCapturedImage(target);
    }
    [StructLayout(LayoutKind.Sequential)] private record struct NativeRect(int Left, int Top, int Right, int Bottom);
    [StructLayout(LayoutKind.Sequential)] private record struct NativePoint(int X, int Y);
    [StructLayout(LayoutKind.Sequential)] private struct MinMaxInfo { public NativePoint Reserved, MaxSize, MaxPosition, MinTrackSize, MaxTrackSize; }
    [DllImport("user32.dll")] private static extern nint SendMessage(nint hwnd, int message, nint wParam, ref MinMaxInfo limits);
    [StructLayout(LayoutKind.Sequential)] private struct MonitorInfo { public int Size; public NativeRect Monitor, Work; public uint Flags; }
    [DllImport("user32.dll")] private static extern bool GetWindowRect(nint hwnd, out NativeRect rect);
    [DllImport("user32.dll")] private static extern nint GetForegroundWindow();
    [DllImport("user32.dll")] private static extern nint MonitorFromWindow(nint hwnd, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool GetMonitorInfo(nint monitor, ref MonitorInfo info);
}
#endif
