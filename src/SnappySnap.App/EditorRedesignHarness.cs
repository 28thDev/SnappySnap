#if DEBUG
using System.Reflection;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using SnappySnap.Capture;
using SnappySnap.Core;
using SnappySnap.Editor;
using SnappySnap.Infrastructure;

namespace SnappySnap.App;

/// <summary>Off-screen WPF rendering and handler-level regression checks. No injected desktop input.</summary>
internal static class EditorRedesignHarness
{
    public static async Task RunAsync(string output)
    {
        SnappySnap.Localization.L.SetLanguage("en"); SnappySnap.Presentation.Ui.ApplyTheme("Dark");
        Directory.CreateDirectory(output);
        var paths = new AppPaths(Path.Combine(output, "profile"), output); paths.EnsureDirectories();
        using var logger = new FileLogger(paths.LogsPath);
        var checks = new List<string>();
        var image = EditorRenderer.ToCapturedImage(VisualHarness.Fixture());
        var editor = new EditorWindow(image, logger) { Left = -15000, Top = -15000, ShowActivated = false, WindowStartupLocation = WindowStartupLocation.Manual };
        editor.Show();
        try
        {
            await Idle();
            Require(editor.CurrentTool == EditorTool.Arrow, "New session defaults to Arrow");
            Snapshot("initial");
            Gesture(new(250, 160), new(570, 235));
            Gesture(new(430, 430), new(790, 410));
            Require(editor.Document.Elements.OfType<ArrowElement>().Count() == 2 && editor.CurrentTool == EditorTool.Arrow, "Repeated arrows retain tool");
            checks.Add("PASS handler-level default Arrow and repeated creation");
            Click("Select");
            var arrow = editor.Document.Elements.OfType<ArrowElement>().First();
            Gesture(ArrowGeometry.Middle(arrow), ArrowGeometry.Middle(arrow));
            var neutral = arrow.Control;
            var middle = ArrowGeometry.Middle(arrow);
            Gesture(middle, middle + new Vector(0, -75));
            var bent = arrow.Control;
            Require(ArrowGeometry.Middle(arrow) == middle + new Vector(0, -75), "Middle handle follows pointer");
            Snapshot("curved-selected");
            Click("Undo (Ctrl+Z)"); Require(arrow.Control == neutral, "Undo bend");
            Click("Redo (Ctrl+Y)"); Require(arrow.Control == bent, "Redo bend");
            var oldStart = arrow.Start; Gesture(oldStart, oldStart + new Vector(-30, 20));
            Require(arrow.Start == oldStart + new Vector(-30, 20), "Start handle");
            var oldEnd = arrow.End; Gesture(oldEnd, oldEnd + new Vector(40, 20));
            Require(arrow.End == oldEnd + new Vector(40, 20), "End handle");
            var beforeMove = EditorGeometry.Capture(arrow); var body = ArrowGeometry.At(arrow, .25);
            Gesture(body, body + new Vector(20, 25));
            Require(arrow.Start == beforeMove.Start!.Value + new Vector(20, 25) && arrow.Control == beforeMove.Control!.Value + new Vector(20, 25), "Body translates all geometry");
            Click("Undo (Ctrl+Z)"); Require(arrow.Control == beforeMove.Control, "Undo body movement");
            Click("Redo (Ctrl+Y)");
            checks.Add("PASS handler-level bend, start/end, whole body, Undo/Redo and selected adorners");

            foreach (var (tool, start, end) in new[] {
                ("Rectangle", new Point(280, 355), new Point(680, 427)),
                ("Line", new Point(40, 480), new Point(230, 510)),
                ("Freehand", new Point(300, 500), new Point(420, 535)),
                ("Highlight", new Point(304, 153), new Point(430, 181)),
                ("Blur", new Point(720, 300), new Point(885, 340)),
                ("Pixelate", new Point(710, 380), new Point(850, 420)),
                ("Step marker", new Point(250, 390), new Point(250, 390)) })
            {
                Click(tool); Gesture(start, end);
                Require(editor.CurrentTool != EditorTool.Arrow, "Manual tool choice persists");
                if (tool == "Rectangle")
                {
                    var rectangle = editor.Document.Elements.OfType<RectangleElement>().Single();
                    var bounds = rectangle.Bounds;
                    Gesture(bounds.BottomRight, bounds.BottomRight + new Vector(20, 10));
                    Require(rectangle.Bounds.Width == bounds.Width + 20 && rectangle.Bounds.Height == bounds.Height + 10,
                        "Selected resize handles remain usable while the drawing tool stays active");
                }
            }
            var text = new TextElement("Check this setting", new Point(270, 100)) { FontSize = 26 };
            Click("Text"); Add(text); Snapshot("text-context");
            Add(new ImageElement(EditorRenderer.ToBitmapSource(image), new Rect(40, 40, 160, 100)));
            Click("Crop"); Gesture(new(10, 10), new(940, 580));
            await EditorRenderer.SavePngAsync(editor.Document, Path.Combine(output, "all-tools-export.png"), CancellationToken.None);
            await EditorRenderer.SaveAsync(editor.Document, Path.Combine(output, "all-tools-export.jpg"), "Jpg", CancellationToken.None);
            var exported = EditorRenderer.LoadImage(Path.Combine(output, "all-tools-export.png"));
            Require(exported.PixelWidth == 930 && exported.PixelHeight == 570, "Crop raster dimensions");
            checks.Add("PASS non-modal tool handlers; Text/Image model composition; PNG/JPG export and crop");
            // Remove only harness annotations using the production command stack before closing.
            for (var i = 0; i < 100 && Find<Button>("Undo (Ctrl+Z)").IsEnabled; i++) Click("Undo (Ctrl+Z)");
            Require(editor.Document.Elements.Count == 0, "Undo all operations");
            editor.Width = 900; editor.Height = 600; await Idle(); Call("Fit"); Snapshot("compact-initial");
            Click("Arrow"); Gesture(new(180, 170), new(670, 330)); Snapshot("compact-selected");
            foreach (var width in new[] { 1d, 5d, 12d })
            {
                var slider = Descendants<Slider>(editor).Single(s => AutomationProperties.GetName(s) == "Stroke width");
                slider.Value = width; Snapshot($"stroke-{width}");
            }
            foreach (var zoom in new[] { .25, 1d, 2d }) { Call("SetZoom", zoom); Snapshot($"zoom-{zoom}"); }
            checks.Add("PASS WPF rendered at 1260x820 and 900x600; stroke 1/5/12; zoom 25/100/200 percent");
            var save = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var saveCalls = 0;
            editor.SaveRequestedAsync = _ => { saveCalls++; return save.Task; };
            var committing = (Task)typeof(EditorWindow).GetMethod("CommitAsync", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(editor, null)!;
            Require(editor.IsSaving && !editor.IsEnabled, "Saving is busy before completion");
            await (Task)typeof(EditorWindow).GetMethod("CommitAsync", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(editor, null)!;
            Require(saveCalls == 1, "Repeated Save cannot start another export");
            editor.Close(); Require(editor.IsVisible, "Close is vetoed during save");
            var annotationCount = editor.Document.Elements.Count;
            save.SetException(new IOException("Injected destination failure")); await committing;
            Require(editor.IsVisible && editor.IsEnabled && !editor.IsSaving && editor.Document.Elements.Count == annotationCount, "Save failure retains editable document");
            Snapshot("save-failure-retained");
            editor.SaveRequestedAsync = null;
            checks.Add("PASS pending save prevents close; failed save retains annotations and enables retry");
            for (var i = 0; i < 100 && Find<Button>("Undo (Ctrl+Z)").IsEnabled; i++) Click("Undo (Ctrl+Z)");
            Click("Text"); Gesture(new(120, 100), new(120, 100));
            var input = Find<TextBox>("Annotation text"); input.Text = "Проверка текста\nSecond line";
            Require(editor.Document.Elements.Count == 0, "Typing remains a draft");
            var typing = new System.Diagnostics.Stopwatch();
            var durations = new List<double>();
            for (var i = 0; i < 40; i++) { typing.Restart(); input.AppendText("x"); input.UpdateLayout(); typing.Stop(); durations.Add(typing.Elapsed.TotalMilliseconds); }
            input.Text = "Проверка текста\nSecond line";
            checks.Add($"MEASURE inline text append+layout p95={durations.Order().ElementAt(37):F2}ms; no document mutation during typing (not physical input latency)");
            foreach (var size in new[] { (1260d, 820d), (900d, 600d) })
            {
                editor.Width = size.Item1; editor.Height = size.Item2; await Idle();
                foreach (var zoom in new[] { .25, 1d, 2d }) { Call("SetZoom", zoom); Snapshot($"inline-{size.Item1}-{zoom}"); }
            }
            if (Keyboard.Modifiers == ModifierKeys.None)
            {
                input.RaiseEvent(new System.Windows.Input.KeyEventArgs(System.Windows.Input.Keyboard.PrimaryDevice, PresentationSource.FromVisual(editor), Environment.TickCount, System.Windows.Input.Key.Enter) { RoutedEvent = System.Windows.Input.Keyboard.PreviewKeyDownEvent });
                Require(Descendants<TextBox>(editor).Contains(input) && editor.Document.Elements.Count == 0, "Enter must keep text editing active; modifiers=" + Keyboard.Modifiers + "; draft present=" + Descendants<TextBox>(editor).Contains(input) + "; elements=" + editor.Document.Elements.Count);
                input.CaretIndex = input.Text.Length;
                var textBeforeEnter = input.Text;
                input.RaiseEvent(new System.Windows.Input.KeyEventArgs(System.Windows.Input.Keyboard.PrimaryDevice, PresentationSource.FromVisual(editor), Environment.TickCount, System.Windows.Input.Key.Enter) { RoutedEvent = System.Windows.Input.Keyboard.KeyDownEvent });
                Require(input.Text.TrimEnd('\r', '\n') == textBeforeEnter && input.Text.Length > textBeforeEnter.Length, "Native Enter inserts newline rather than committing or saving");
            }
            else checks.Add("BLOCKED: unmodified Enter regression; the off-screen WPF input device reports " + Keyboard.Modifiers + ". Physical keyboard acceptance remains required.");
            input.AppendText("Третья строка");
            Snapshot("enter-newline");
            Call("FinishTextEdit", true);
            var label = editor.Document.Elements.OfType<TextElement>().Single();
            var prior = label.Text;
            Call("BeginTextEdit", label, label.Bounds.Location); Find<TextBox>("Annotation text").Text = "Cancelled"; Call("FinishTextEdit", false);
            Require(label.Text == prior, "Cancel restores existing text");
            editor.RaiseEvent(new System.Windows.Input.KeyEventArgs(System.Windows.Input.Keyboard.PrimaryDevice, PresentationSource.FromVisual(editor), Environment.TickCount, System.Windows.Input.Key.F2) { RoutedEvent = System.Windows.Input.Keyboard.KeyDownEvent });
            Find<TextBox>("Annotation text").Text = "Outside click";
            var outside = new System.Windows.Input.MouseButtonEventArgs(System.Windows.Input.Mouse.PrimaryDevice, Environment.TickCount, System.Windows.Input.MouseButton.Left) { RoutedEvent = System.Windows.Input.Mouse.PreviewMouseDownEvent };
            Descendants<EditorSurface>(editor).Single().RaiseEvent(outside);
            Require(outside.Handled && label.Text == "Outside click" && editor.Document.Elements.Count == 1, "Outside click commits and is consumed");
            Click("Undo (Ctrl+Z)");
            Call("BeginTextEdit", label, label.Bounds.Location); Find<TextBox>("Annotation text").Text = "Исправлено"; Call("FinishTextEdit", true);
            Click("Undo (Ctrl+Z)"); Require(label.Text == prior, "One Undo reverses text edit");
            Click("Redo (Ctrl+Y)"); Require(label.Text == "Исправлено", "Redo text edit");
            Call("BeginTextEdit", label, label.Bounds.Location); Call("FinishTextEdit", true);
            Click("Undo (Ctrl+Z)"); Require(label.Text == prior, "Unchanged edit does not add history");
            Call("BeginTextEdit", label, label.Bounds.Location); Find<TextBox>("Annotation text").Text = ""; Call("FinishTextEdit", true);
            Require(editor.Document.Elements.Count == 0, "Empty existing text is deleted");
            Click("Undo (Ctrl+Z)"); Require(editor.Document.Elements.Contains(label), "Undo cleared text");
            Call("BeginTextEdit", label, label.Bounds.Location); Find<TextBox>("Annotation text").Text = "Tool switch"; Click("Arrow");
            Require(label.Text == "Tool switch" && !Descendants<TextBox>(editor).Any(x => AutomationProperties.GetName(x) == "Annotation text"), "Tool switch commits input");
            Call("BeginTextEdit", label, label.Bounds.Location); Find<TextBox>("Annotation text").Text = "Save draft";
            editor.SaveRequestedAsync = _ => { Require(label.Text == "Save draft", "Save commits active draft"); throw new IOException("Fixture save failure"); };
            await (Task)typeof(EditorWindow).GetMethod("CommitAsync", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(editor, null)!;
            checks.Add("PASS inline text: draft, cancel, multiline, undo/redo, empty deletion, no-op, tool switch and save during input at both window sizes and all zooms");
        }
        finally
        {
            // A failing test should never open a discard confirmation on the user's desktop.
            typeof(EditorWindow).GetField("_committing", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(editor, true);
            editor.Close();
        }
        var topology = new Win32MonitorTopologyService();
        foreach (var monitor in topology.GetMonitors()) checks.Add($"TOPOLOGY {monitor.Bounds} dpi={monitor.DpiX}/{monitor.DpiY}");
        var fixtureMonitor = new MonitorDescriptor("fixture", new(0, 0, 1280, 800), new(0, 0, 1280, 760), 96, 96, true);
        var selector = new RegionSelectorWindow(fixtureMonitor, _ => { }, () => { }, allowFullMonitor: true)
            { Left = -15000, Top = -15000, ShowActivated = false, Topmost = false };
        selector.Show(); await Idle(); VisualHarness.CaptureContent(selector, Path.Combine(output, "selector-hint.png")); selector.Close();
        await ArrowSamples(output);
        var retryEditor = new EditorWindow(image, logger) { Left = -15000, Top = -15000, ShowActivated = false, WindowStartupLocation = WindowStartupLocation.Manual };
        retryEditor.SaveRequestedAsync = _ => Task.CompletedTask;
        _ = retryEditor.Dispatcher.BeginInvoke(() => typeof(EditorWindow).GetMethod("Commit", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(retryEditor, null));
        Require(await retryEditor.ShowAsync(), "Successful save closes the modeless editor");
        checks.Add("PASS successful save closes the modeless editor");
        await CheckSaveSession(output, image, logger, checks);
        await CheckScreenshotFlow(output, image, logger, checks);
        await CheckStyles(output, image, logger, checks);
        await CheckStepScaling(output, image, logger, checks);
        await CheckCropPreview(output, image, logger, checks);
        using (var unavailableSelector = new RegionSelectorService(new EmptyTopology(), logger))
        {
            for (var attempt = 0; attempt < 2; attempt++)
            {
                try { await unavailableSelector.SelectAsync().WaitAsync(TimeSpan.FromSeconds(3)); throw new InvalidOperationException("Empty topology was accepted."); }
                catch (InvalidOperationException ex) when (ex.Message.StartsWith("No display is available", StringComparison.Ordinal)) { }
            }
        }
        checks.Add("PASS selector initialization failure cleans its state and permits another attempt");
        checks.Add("NOT RUN: physical input/global Ctrl+E, paste into an external application, native file dialogs, drag/drop, second-monitor hardware acceptance. Native clipboard command/readback/busy-retry checks ran above.");
        await File.WriteAllLinesAsync(Path.Combine(output, "results.txt"), checks);

        void Call(string method, params object[] args) => typeof(EditorWindow).GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(editor, args);
        T Find<T>(string name) where T : DependencyObject => Descendants<T>(editor).Single(x => AutomationProperties.GetName(x) == name);
        void Click(string name) => Find<ButtonBase>(name).RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        void Gesture(Point start, Point end) { Call("BeginInteraction", start); Call("ContinueInteraction", end); Call("EndInteraction", end); editor.UpdateLayout(); }
        void Add(EditorElement element) => Call("Add", element);
        void Snapshot(string name) { editor.UpdateLayout(); VisualHarness.CaptureContent(editor, Path.Combine(output, name + ".png")); }
    }

    private static Task Idle() => System.Windows.Application.Current.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle).Task;
    private static async Task CheckCropPreview(string output, CapturedImage image, IAppLogger logger, List<string> checks)
    {
        // Real screenshot pixels are opaque; the synthetic text fixture contains fractional alpha at glyph edges.
        var pixels = image.Bgra32.ToArray();
        for (var i = 3; i < pixels.Length; i += 4) pixels[i] = 255;
        image = new CapturedImage(image.Width, image.Height, pixels);
        var editor = new EditorWindow(image, logger) { Left = -15000, Top = -15000, ShowActivated = false, WindowStartupLocation = WindowStartupLocation.Manual };
        editor.Show(); await Idle();
        try
        {
            var viewport = Field<Canvas>("_viewport"); var surface = Field<EditorSurface>("_surface");
            var scroll = Field<ScrollViewer>("_scroll");
            var crop = new Rect(240, 120, 480, 300);
            Click("Crop"); Gesture(crop.BottomRight, crop.TopLeft);
            Require(editor.Document.VisibleBounds == crop && viewport.Width == 480 && viewport.Height == 300, "Crop immediately shrinks viewport");
            Require(Field<TextBlock>("_dimensions").Text == "480 × 300 px", "Crop updates displayed dimensions");
            Gesture(crop.TopLeft, crop.BottomRight);
            Click("Undo (Ctrl+Z)"); Require(!editor.Document.HasCrop, "Unchanged crop adds no history");
            Click("Redo (Ctrl+Y)");
            foreach (var size in new[] { (900d, 600d), (1260d, 820d) })
            {
                editor.Width = size.Item1; editor.Height = size.Item2; await Idle();
                foreach (var zoom in new[] { .25, 1d, 2d })
                {
                    Call("SetZoom", zoom); editor.UpdateLayout(); await Idle();
                    var origin = surface.TranslatePoint(crop.TopLeft, viewport);
                    Require(origin == new Point(0, 0), "Cropped origin maps to viewport origin");
                    var mapped = viewport.TranslatePoint(new Point(40, 50), surface);
                    Require(mapped == new Point(280, 170), "Pointer coordinates retain original document space");
                    Require(Math.Abs(scroll.ExtentWidth - crop.Width * zoom) < 2,
                        $"Scroll extent follows cropped image and zoom: extent={scroll.ExtentWidth}, viewport={scroll.ViewportWidth}, image={crop.Width * zoom}, zoom={zoom}");
                    scroll.ScrollToHorizontalOffset(40); scroll.ScrollToVerticalOffset(30); editor.UpdateLayout();
                    Require(surface.TranslatePoint(viewport.TranslatePoint(new Point(40, 50), surface), viewport) == new Point(40, 50),
                        "Coordinates remain reversible after scrolling");
                    VisualHarness.CaptureContent(editor, Path.Combine(output, $"crop-{size.Item1}-{zoom}.png"));
                }
            }
            Call("SetZoom", 1d); editor.UpdateLayout(); await Idle();
            var content = (FrameworkElement)editor.Content;
            var windowBitmap = new System.Windows.Media.Imaging.RenderTargetBitmap((int)content.ActualWidth, (int)content.ActualHeight, 96, 96, PixelFormats.Pbgra32);
            windowBitmap.Render(content);
            var originInWindow = viewport.TranslatePoint(new Point(), content);
            var bitmap = new System.Windows.Media.Imaging.CroppedBitmap(windowBitmap,
                new Int32Rect((int)Math.Round(originInWindow.X), (int)Math.Round(originInWindow.Y), 480, 300));
            var preview = EditorRenderer.ToCapturedImage(bitmap);
            var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder(); encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
            using (var stream = File.Create(Path.Combine(output, "crop-preview-actual.png"))) encoder.Save(stream);
            var path = Path.Combine(output, "crop-preview-export.png");
            await EditorRenderer.SavePngAsync(editor.Document, path, default);
            var exported = EditorRenderer.ToCapturedImage(EditorRenderer.LoadImage(path)).Bgra32;
            var differences = Enumerable.Range(0, exported.Length).Where(i => exported[i] != preview.Bgra32[i]).ToArray();
            Require(differences.Length == 0, $"Actual WPF crop preview matches exported pixels: {differences.Length}/{exported.Length}, first=" +
                string.Join(";", differences.Take(8).Select(i => $"{i}: {preview.Bgra32[i]}/{exported[i]}")));
            Click("Arrow"); Gesture(new(280, 170), new(560, 330));
            var arrow = editor.Document.Elements.OfType<ArrowElement>().Single();
            Require(arrow.Start == new Point(280, 170), "Drawing after crop uses document coordinates");
            Click("Text"); Gesture(new(320, 230), new(320, 230));
            var input = Descendants<TextBox>(editor).Single(x => AutomationProperties.GetName(x) == "Annotation text");
            input.Text = "После обрезки\nAfter crop"; Call("FinishTextEdit", true);
            Click("Crop");
            Call("BeginInteraction", new Point(300, 200)); Call("ContinueInteraction", new Point(500, 350)); Call("CancelInteraction");
            Require(editor.Document.VisibleBounds == crop, "Escape keeps previous crop");
            Click("Crop"); Gesture(new(300, 200), new(900, 900));
            var second = new Rect(300, 200, 420, 220);
            Require(editor.Document.VisibleBounds == second, "Repeated crop clips to current bounds");
            Click("Undo (Ctrl+Z)"); Require(editor.Document.VisibleBounds == crop && viewport.Width == 480, "Undo restores visible crop");
            Click("Redo (Ctrl+Y)"); Require(editor.Document.VisibleBounds == second && viewport.Width == 420, "Redo reapplies visible crop");
            Call("InsertImage", editor.Document.BaseImage, (Point)typeof(EditorWindow).GetProperty("ImageInsertionPoint", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(editor)!);
            Require(second.Contains(editor.Document.Elements.OfType<ImageElement>().Single().Bounds), "Inserted image fits inside cropped result");
            Click("Crop"); Gesture(new(350, 250), new(350, 250));
            Require(editor.Document.VisibleBounds == second, "Click without drag does not crop");
            VisualHarness.CaptureContent(editor, Path.Combine(output, "crop-edited.png"));
            while (Field<EditorCommandHistory>("_history").CanUndo) Click("Undo (Ctrl+Z)");
            Require(editor.Document.VisibleBounds == new Rect(0, 0, image.Width, image.Height) && viewport.Width == image.Width, "Undo restores original image and viewport");
            checks.Add("PASS Crop: immediate WPF preview equals exported pixels; dimensions/scroll/coordinates at 900x600 and 1260x820, 25/100/200%; reverse/repeated/outside/tiny/cancelled crop; post-crop arrow/text/image; Undo/Redo restores image");

            T Field<T>(string name) => (T)typeof(EditorWindow).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(editor)!;
            void Call(string method, params object[] args) => typeof(EditorWindow).GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(editor, args);
            void Click(string name) { Descendants<ButtonBase>(editor).Single(x => AutomationProperties.GetName(x) == name).RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent)); editor.UpdateLayout(); }
            void Gesture(Point start, Point end) { Call("BeginInteraction", start); Call("ContinueInteraction", end); Call("EndInteraction", end); editor.UpdateLayout(); }
        }
        finally
        {
            typeof(EditorWindow).GetField("_committing", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(editor, true);
            editor.Close();
        }
    }
    private static async Task CheckStepScaling(string output, CapturedImage image, IAppLogger logger, List<string> checks)
    {
        var editor = new EditorWindow(image, logger) { Left = -15000, Top = -15000, ShowActivated = false, WindowStartupLocation = WindowStartupLocation.Manual };
        editor.Show(); await Idle();
        void Call(string method, params object[] args) => typeof(EditorWindow).GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(editor, args);
        void Click(string name) => Descendants<ButtonBase>(editor).Single(x => AutomationProperties.GetName(x) == name).RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        void Gesture(Point start, Point end) { Call("BeginInteraction", start); Call("ContinueInteraction", end); Call("EndInteraction", end); }
        try
        {
            Click("Step marker"); Gesture(new(300, 280), new(300, 280));
            var step = editor.Document.Elements.OfType<StepMarkerElement>().Single();
            var slider = Descendants<Slider>(editor).Single(x => AutomationProperties.GetName(x) == "Step size");
            Require(slider.IsVisible && step.Bounds.Width == 64, "Larger default step and visible size control");
            Require(!Descendants<Slider>(editor).Single(x => AutomationProperties.GetName(x) == "Stroke width").IsVisible, "No ineffective stroke control for steps");
            foreach (var size in new[] { (900d, 600d), (1260d, 820d) })
            {
                editor.Width = size.Item1; editor.Height = size.Item2; await Idle();
                foreach (var diameter in new[] { 24d, 64d, 128d, 240d })
                {
                    slider.Value = diameter;
                    Require(step.Bounds == new Rect(300 - diameter / 2, 280 - diameter / 2, diameter, diameter), "Step slider updates centered geometry");
                    Call("SetZoom", 1d);
                    VisualHarness.CaptureContent(editor, Path.Combine(output, $"step-{size.Item1}-{diameter}.png"));
                }
            }
            slider.Value = 128;
            Click("Undo (Ctrl+Z)"); Require(step.Bounds.Width == 240, "Undo step size");
            Click("Redo (Ctrl+Y)"); Require(step.Bounds.Width == 128, "Redo step size");
            var corner = step.Bounds.BottomRight;
            Gesture(corner, corner + new Vector(64, 32));
            Require(step.Bounds.Width == 192 && step.Bounds.Height == 192, "Corner resizing scales step as a circle");
            Click("Undo (Ctrl+Z)"); Require(step.Bounds.Width == 128, "Undo corner resize");
            Click("Redo (Ctrl+Y)"); Require(step.Bounds.Width == 192, "Redo corner resize");
            Gesture(new(650, 400), new(650, 400));
            Require(editor.Document.Elements.OfType<StepMarkerElement>().Last().Bounds.Width == 192, "Next step remembers resized diameter");
            foreach (var zoom in new[] { .25, 1d, 2d })
            {
                Call("SetZoom", zoom);
                VisualHarness.CaptureContent(editor, Path.Combine(output, $"step-zoom-{zoom}.png"));
            }
            await EditorRenderer.SavePngAsync(editor.Document, Path.Combine(output, "step-export.png"), default);
            checks.Add("PASS Step: default 64px; 24/64/128/240px slider at both window sizes; centered circle; corner scaling and Undo/Redo; next-step size; rendered 25/100/200% and PNG export.");
        }
        finally { typeof(EditorWindow).GetField("_committing", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(editor, true); editor.Close(); }
    }

    private static async Task CheckStyles(string output, CapturedImage image, IAppLogger logger, List<string> checks)
    {
        var paths = new AppPaths(Path.Combine(output, "style-profile"), output);
        var store = new JsonSettingsStore(paths, logger);
        var settings = await store.LoadAsync(default);
        var session = new SnappySnap.Application.SettingsSession(settings, store);
        var editor = new EditorWindow(image, logger, styles: settings.Editor) { Left = -15000, Top = -15000, ShowActivated = false, WindowStartupLocation = WindowStartupLocation.Manual };
        editor.StyleChanged += session.SetStyle;
        editor.Show(); await Idle();
        try
        {
            Call("BeginInteraction", new Point(100, 100)); Call("EndInteraction", new Point(400, 220));
            var arrow = editor.Document.Elements.OfType<ArrowElement>().Single();
            Call("ApplyStyle", (Func<ElementStyle, ElementStyle>)(style => style with { StrokeWidth = 9, Color = Colors.DodgerBlue }));
            var history = (EditorCommandHistory)typeof(EditorWindow).GetField("_history", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(editor)!;
            history.Undo(editor.Document); Require(arrow.StrokeWidth == 5, "Undo restores existing arrow");
            Call("BeginInteraction", new Point(200, 350)); Call("EndInteraction", new Point(500, 420));
            var next = editor.Document.Elements.OfType<ArrowElement>().Last();
            Require(next.StrokeWidth == 9 && next.Color == Colors.DodgerBlue, "Next arrow uses remembered style even after Undo");
            Descendants<ButtonBase>(editor).Single(x => AutomationProperties.GetName(x) == "Step marker").RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            Call("BeginInteraction", new Point(650, 300)); Call("EndInteraction", new Point(650, 300));
            Descendants<Slider>(editor).Single(x => AutomationProperties.GetName(x) == "Step size").Value = 144;
            await session.FlushPreferencesAsync(default);
            VisualHarness.CaptureContent(editor, Path.Combine(output, "remembered-arrow.png"));
        }
        finally { typeof(EditorWindow).GetField("_committing", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(editor, true); editor.Close(); }
        var reloaded = await new JsonSettingsStore(paths, logger).LoadAsync(default);
        var restarted = new EditorWindow(image, logger, styles: reloaded.Editor) { Left = -15000, Top = -15000, ShowActivated = false, WindowStartupLocation = WindowStartupLocation.Manual };
        restarted.Show(); await Idle();
        try
        {
            Require(restarted.CurrentTool == EditorTool.Arrow, "Restart always opens Arrow");
            typeof(EditorWindow).GetMethod("BeginInteraction", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(restarted, [new Point(100, 100)]);
            typeof(EditorWindow).GetMethod("EndInteraction", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(restarted, [new Point(300, 200)]);
            var arrow = restarted.Document.Elements.OfType<ArrowElement>().Single();
            Require(arrow.StrokeWidth == 9 && arrow.Color == Colors.DodgerBlue, "New editor loads persisted arrow style");
            Descendants<ButtonBase>(restarted).Single(x => AutomationProperties.GetName(x) == "Step marker").RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            typeof(EditorWindow).GetMethod("BeginInteraction", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(restarted, [new Point(650, 300)]);
            typeof(EditorWindow).GetMethod("EndInteraction", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(restarted, [new Point(650, 300)]);
            Require(restarted.Document.Elements.OfType<StepMarkerElement>().Single().Bounds.Width == 144, "New editor restores step size from JSON");
        }
        finally { typeof(EditorWindow).GetField("_committing", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(restarted, true); restarted.Close(); }
        checks.Add("PASS WPF tool preferences: style change, independent document Undo, next arrow, JSON reload and new editor starting Arrow");
        void Call(string method, params object[] args) => typeof(EditorWindow).GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(editor, args);
    }
    private static async Task CheckSaveSession(string output, CapturedImage image, IAppLogger logger, List<string> checks)
    {
        var root = Path.Combine(output, "save-scenarios"); Directory.CreateDirectory(root);
        await using var repository = new SnappySnap.History.SqliteHistoryRepository(Path.Combine(root, "history.db"), logger);
        var failing = new FailingIndex(repository);
        var service = new SnappySnap.Application.ShelfService(failing, new SnappySnap.History.ThumbnailService(Path.Combine(root, "thumbs"), repository, logger));
        var document = new EditorDocument(image);
        var notices = new List<string>();
        var copies = 0;
        var session = new ScreenshotSaveSession(document, service, () => root, new(0, 0, image.Width, image.Height),
            _ => { copies++; throw new IOException("Clipboard fixture"); }, notices.Add, logger);
        var destination = Path.Combine(root, "chosen.png");
        var fileRequest = new EditorSaveRequest("Png", destination, EditorSaveMode.File);
        try { await session.SaveAsync(fileRequest); throw new InvalidOperationException("Expected indexing failure"); }
        catch (IOException ex) when (ex.Message.Contains("Could not add it to Shelf", StringComparison.Ordinal)) { }
        Require(File.Exists(destination) && copies == 0, "Failed indexing retains file and defers clipboard");
        await session.SaveAsync(fileRequest);
        Require((await repository.GetRecentAsync(20, default)).Count == 1 && copies == 1 && notices.Count == 1, "Retry same destination; clipboard failure remains a success");
        var originalItem = (await repository.GetRecentAsync(20, default)).Single();
        var shelfSession = new ScreenshotSaveSession(document, service, () => root, new(0, 0, image.Width, image.Height), _ => { }, notices.Add, logger, destination);
        document.CropRect = new Rect(0, 0, 50, 40);
        await shelfSession.SaveAsync(new("Jpg"));
        var replaced = (await repository.GetRecentAsync(20, default)).Single();
        Require(replaced.Id == originalItem.Id && replaced.WidthPx == 50 && replaced.HeightPx == 40 && replaced.FilePath == destination,
            "Shelf replacement retains ID, path and PNG format despite JPG copy selection");
        var blocked = Path.Combine(root, "not-a-folder"); await File.WriteAllTextAsync(blocked, "fixture");
        try { await session.SaveAsync(new("Png", Path.Combine(blocked, "image.png"), EditorSaveMode.File)); throw new InvalidOperationException("Invalid destination accepted"); }
        catch (IOException ex) when (ex.Message.StartsWith("Could not write", StringComparison.Ordinal)) { }
        var modal = new EditorWindow(image, logger) { Left = -15000, Top = -15000, ShowActivated = false, WindowStartupLocation = WindowStartupLocation.Manual };
        var modalSession = new ScreenshotSaveSession(modal.Document, service, () => root, new(0, 0, image.Width, image.Height), _ => { }, notices.Add, logger);
        modal.SaveRequestedAsync = modalSession.SaveAsync;
        _ = modal.Dispatcher.BeginInvoke(async () =>
        {
            void Edit(string method, params object?[] args) => typeof(EditorWindow).GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(modal, args);
            Edit("BeginTextEdit", null, new Point(230, 110));
            Descendants<TextBox>(modal).Single(x => AutomationProperties.GetName(x) == "Annotation text").Text = "Проверка";
            Edit("FinishTextEdit", true);
            var text = modal.Document.Elements.OfType<TextElement>().Single();
            Edit("BeginTextEdit", text, text.Bounds.Location);
            Descendants<TextBox>(modal).Single(x => AutomationProperties.GetName(x) == "Annotation text").Text = "Проверка исправлена";
            Edit("FinishTextEdit", true);
            var history = (EditorCommandHistory)typeof(EditorWindow).GetField("_history", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(modal)!;
            history.Undo(modal.Document); Require(text.Text == "Проверка", "End-to-end Undo");
            history.Redo(modal.Document); Require(text.Text == "Проверка исправлена", "End-to-end Redo");
            await (Task)typeof(EditorWindow).GetMethod("SaveAndCloseAsync", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(modal, [new EditorSaveRequest("Jpg", Path.Combine(root, "as.jpg"), EditorSaveMode.File)])!;
        });
        Require(await modal.ShowAsync() && File.Exists(Path.Combine(root, "as.jpg")), "Save As closes editor and indexes selected JPEG");
        var state = new ShelfState();
        var shelfContent = new ShelfContent(service, AppSettings.Defaults(), logger, state);
        var shelfWindow = new ShelfWindow(shelfContent, false) { Left = -15000, Top = -15000, ShowActivated = false, WindowStartupLocation = WindowStartupLocation.Manual };
        shelfWindow.Loaded += (_, _) => { shelfWindow.Left = -15000; shelfWindow.Top = -15000; };
        shelfWindow.Show();
        try
        {
            await shelfContent.LoadAsync(); await Idle();
            Require(state.Rows.Count(row => row.Item.FilePath == destination) == 1, "Shelf shows only one replacement");
            Require(state.Selected?.Item.FilePath == Path.Combine(root, "as.jpg"), "Shelf selects the Save As result");
            VisualHarness.CaptureContent(shelfWindow, Path.Combine(output, "saved-shelf.png"));
        }
        finally { shelfWindow.Close(); }
        checks.Add("PASS shared save: retained file after index failure, retry without duplicate, clipboard failure, Shelf replacement preserving path/ID/format, invalid destination and modal Save to file");
        checks.Add("PASS WPF end-to-end: create text, edit, Undo, Redo, Save As JPEG, modal close, selected Shelf result and no duplicate replacement row");
    }

    private static async Task CheckScreenshotFlow(string output, CapturedImage image, IAppLogger logger, List<string> checks)
    {
        var root = Path.Combine(output, "screenshot-flow"); Directory.CreateDirectory(root);
        await using var repository = new SnappySnap.History.SqliteHistoryRepository(Path.Combine(root, "history.db"), logger);
        var failing = new FailingIndex(repository) { FailNext = false };
        var service = new SnappySnap.Application.ShelfService(failing, new SnappySnap.History.ThumbnailService(Path.Combine(root, "thumbs"), repository, logger));
        var editor = new EditorWindow(image, logger) { Left = -15000, Top = -15000, ShowActivated = false, WindowStartupLocation = WindowStartupLocation.Manual };
        var copies = 0;
        var session = new ScreenshotSaveSession(editor.Document, service, () => root, new(0, 0, image.Width, image.Height), _ => copies++, _ => { }, logger);
        await session.SaveOriginalAsync("Png");
        var original = (await repository.GetRecentAsync(20, default)).Single();
        var originalBytes = await File.ReadAllBytesAsync(original.FilePath);
        Require(!editor.IsLoaded && copies == 0 && File.Exists(original.FilePath), "Original indexed before editor opens, without clipboard callback");
        editor.Show(); await Idle();
        var clipboardBefore = Clipboard.GetDataObject();
        try
        {
            Call("BeginTextEdit", null, new Point(20, 20));
            var input = Descendants<TextBox>(editor).Single(x => AutomationProperties.GetName(x) == "Annotation text");
            input.Text = "Copy draft"; input.Select(0, 4);
            ApplicationCommands.Copy.Execute(null, input);
            Require(Clipboard.GetText() == "Copy" && editor.Document.Elements.Count == 0, "Native Copy inside text field copies text without committing");
            // Button commits the pending text; all-image copy leaves editor and saved original unchanged.
            Descendants<Button>(editor).Single(x => AutomationProperties.GetName(x) == "Copy").RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            await WaitForCopy();
            Require(editor.Document.Elements.OfType<TextElement>().Single().Text == "Copy draft", "Copy button commits active draft");
            Descendants<ButtonBase>(editor).Single(x => AutomationProperties.GetName(x) == "Crop").RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            Call("BeginInteraction", new Point(10, 10));
            Call("EndInteraction", new Point(330, 210));
            var surface = Descendants<EditorSurface>(editor).Single();
            ApplicationCommands.Copy.Execute(null, surface);
            await WaitForCopy();
            var pasted = Clipboard.GetImage();
            Require(pasted is not null && pasted.PixelWidth == 320 && pasted.PixelHeight == 200, "Ctrl+C command places current crop in native image clipboard");
            var expected = await EditorRenderer.RenderAsync(editor.Document, false, default);
            var actualPixels = EditorRenderer.ToCapturedImage(pasted!).Bgra32;
            var expectedPixels = new byte[expected.PixelWidth * expected.PixelHeight * 4];
            expected.CopyPixels(expectedPixels, expected.PixelWidth * 4, 0);
            // Windows DIB preserves the rendered RGB bytes but exposes opaque alpha.
            var mismatch = Enumerable.Range(0, actualPixels.Length).Where(i => i % 4 != 3 && actualPixels[i] != expectedPixels[i]).Take(4).ToArray();
            Require(mismatch.Length == 0, "Clipboard RGB pixels match current annotated render without selection adorners: " + string.Join(", ", mismatch.Select(i => $"channel {i % 4}: {actualPixels[i]} vs {expectedPixels[i]}")));
            var afterCopy = await File.ReadAllBytesAsync(original.FilePath);
            Require(editor.IsVisible && copies == 0 && originalBytes.SequenceEqual(afterCopy), "Copy keeps editor open and original file untouched");
            Require(((EditorCommandHistory)typeof(EditorWindow).GetField("_history", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(editor)!).CanUndo, "Copy does not discard pending edits");
            VisualHarness.CaptureContent(editor, Path.Combine(output, "screenshot-copy.png"));

            var locked = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var lockTask = Task.Run(() =>
            {
                if (!OpenClipboard(0)) { locked.SetException(new IOException("Could not lock fixture clipboard")); return; }
                try { locked.SetResult(); release.Task.GetAwaiter().GetResult(); }
                finally { CloseClipboard(); }
            });
            try
            {
                await locked.Task;
                await (Task)Call("CopyAsync")!;
                Require(editor.IsVisible && editor.IsEnabled && Descendants<TextBlock>(editor).Any(x => x.Text.StartsWith("Could not copy the image.", StringComparison.Ordinal)), "Busy clipboard retains editor and exposes retry message");
            }
            finally { release.TrySetResult(); await lockTask; }
            await (Task)Call("CopyAsync")!;
            Require(Clipboard.ContainsImage(), "Explicit clipboard retry succeeds");
            checks.Add("PASS native clipboard: text Copy, image Copy command/button, crop/annotation pixels, draft commit, unchanged original, busy clipboard and retry; editor remains open");

            await using (var fileLock = new FileStream(original.FilePath, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                try { await session.SaveAsync(new("Jpg")); throw new InvalidOperationException("Locked original overwritten"); }
                catch (IOException) { }
            }
            var afterFailure = await File.ReadAllBytesAsync(original.FilePath);
            Require(originalBytes.SequenceEqual(afterFailure), "Failed overwrite retains old file bytes");
            await session.SaveAsync(new("Jpg"));
            var replaced = (await repository.GetRecentAsync(20, default)).Single();
            Require(replaced.Id == original.Id && replaced.FilePath == original.FilePath && replaced.WidthPx == 320 && replaced.HeightPx == 200, "Save replaces original in place");
            var savedBytes = await File.ReadAllBytesAsync(original.FilePath);
            failing.FailNext = true;
            var newRequest = new EditorSaveRequest("Jpg", Mode: EditorSaveMode.NewCopy);
            try { await session.SaveAsync(newRequest); throw new InvalidOperationException("Expected new-copy index failure"); }
            catch (IOException ex) when (ex.Message.Contains("Could not add it to Shelf", StringComparison.Ordinal)) { }
            var copyPath = Directory.GetFiles(root, "*.jpg", SearchOption.AllDirectories).Single(path => !path.Contains("thumbs", StringComparison.Ordinal));
            await session.SaveAsync(newRequest);
            var rows = await repository.GetRecentAsync(20, default);
            var afterNewCopy = await File.ReadAllBytesAsync(original.FilePath);
            Require(rows.Count == 2 && rows.Any(x => x.FilePath == copyPath) && savedBytes.SequenceEqual(afterNewCopy), "New-copy retry indexes the same file once and preserves source");
            checks.Add("PASS initial autosave without clipboard; replacement retains ID/path/format; locked overwrite retains bytes; new JPG copy index retry produces exactly one additional file/item");
        }
        catch (Exception ex)
        {
            logger.Error("Screenshot flow harness failed.", ex);
            throw;
        }
        finally
        {
            try { if (clipboardBefore is not null) Clipboard.SetDataObject(clipboardBefore, true); else Clipboard.Clear(); }
            catch (System.Runtime.InteropServices.COMException ex) { logger.Error("Harness could not restore clipboard.", ex); }
            typeof(EditorWindow).GetField("_committing", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(editor, true); editor.Close();
        }
        // Initial index failure must preserve its generated destination for the editor's normal Save.
        var recoveryDocument = new EditorDocument(image);
        var recovery = new ScreenshotSaveSession(recoveryDocument, service, () => root, new(0, 0, image.Width, image.Height), _ => copies++, _ => { }, logger);
        failing.FailNext = true;
        try { await recovery.SaveOriginalAsync("Jpg"); throw new InvalidOperationException("Expected initial index failure"); }
        catch (IOException ex) when (ex.Message.Contains("Could not add it to Shelf", StringComparison.Ordinal)) { }
        var filesBeforeRetry = Directory.GetFiles(root, "*.jpg", SearchOption.AllDirectories).Where(x => !x.Contains("thumbs", StringComparison.Ordinal)).Order().ToArray();
        await recovery.SaveAsync(new("Png")); // Changing the new-copy selector must not abandon the initial JPEG.
        Require(filesBeforeRetry.SequenceEqual(Directory.GetFiles(root, "*.jpg", SearchOption.AllDirectories).Where(x => !x.Contains("thumbs", StringComparison.Ordinal)).Order()), "Initial save retry does not allocate another file");
        Require((await repository.GetRecentAsync(20, default)).Count == 3, "Initial failure retry creates one history item");
        checks.Add("PASS failed initial indexing followed by normal Save reuses initial destination/format even after new-copy format changes");

        foreach (var saveNew in new[] { false, true })
        {
            var modal = new EditorWindow(image, logger) { Left = -15000, Top = -15000, ShowActivated = false, WindowStartupLocation = WindowStartupLocation.Manual };
            var modalSession = new ScreenshotSaveSession(modal.Document, service, () => root, new(0, 0, image.Width, image.Height), _ => { }, _ => { }, logger);
            await modalSession.SaveOriginalAsync("Png");
            var before = await repository.GetRecentAsync(20, default);
            modal.SaveRequestedAsync = modalSession.SaveAsync;
            _ = modal.Dispatcher.BeginInvoke(() => Descendants<Button>(modal).Single(x => AutomationProperties.GetName(x) == (saveNew ? "Save as new" : "Save")).RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent)));
            Require(await modal.ShowAsync(), "Successful save button closes modeless editor");
            Require((await repository.GetRecentAsync(20, default)).Count == before.Count + (saveNew ? 1 : 0), "Save button replaces; Save as new button adds exactly one item");
        }
        var blockedRoot = Path.Combine(root, "blocked-root"); await File.WriteAllTextAsync(blockedRoot, "fixture");
        var failedEditor = new EditorWindow(image, logger) { Left = -15000, Top = -15000, ShowActivated = false, WindowStartupLocation = WindowStartupLocation.Manual };
        var failedSession = new ScreenshotSaveSession(failedEditor.Document, service, () => blockedRoot, new(0, 0, image.Width, image.Height), _ => { }, _ => { }, logger);
        var countBeforeFailure = (await repository.GetRecentAsync(20, default)).Count;
        try { await failedSession.SaveOriginalAsync("Png"); throw new InvalidOperationException("Expected initial write failure"); }
        catch (IOException ex) { failedEditor.ShowInitialSaveFailure(ex.Message); }
        failedEditor.Show(); await Idle();
        try
        {
            Require(failedEditor.IsVisible && failedEditor.IsEnabled && Descendants<Button>(failedEditor).Single(x => AutomationProperties.GetName(x) == "Copy").IsEnabled,
                "Initial write failure opens usable editor with Copy");
            Require(Descendants<Button>(failedEditor).Single(x => AutomationProperties.GetName(x) == "Retry").IsVisible, "Initial failure exposes retry after Loaded refresh");
            Require((await repository.GetRecentAsync(20, default)).Count == countBeforeFailure, "Initial write failure adds no phantom item");
            VisualHarness.CaptureContent(failedEditor, Path.Combine(output, "initial-save-failure.png"));
        }
        finally { typeof(EditorWindow).GetField("_committing", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(failedEditor, true); failedEditor.Close(); }
        checks.Add("PASS Save and Save as new buttons close real modeless editors with expected item counts; initial write failure keeps editor/Copy/Retry available without a phantom Shelf item");

        object? Call(string method, params object?[] args) => typeof(EditorWindow).GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(editor, args);
        async Task WaitForCopy()
        {
            var deadline = DateTime.UtcNow.AddSeconds(10);
            while (editor.IsSaving && DateTime.UtcNow < deadline) await Task.Delay(20);
            Require(!editor.IsSaving, "Copy completed within timeout");
        }
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool OpenClipboard(nint window);
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool CloseClipboard();

    private sealed class FailingIndex(IHistoryRepository inner) : IHistoryRepository
    {
        public bool FailNext { get; set; } = true;
        public Task<HistoryItem> AddAsync(NewHistoryItem item, CancellationToken token)
        { if (FailNext) { FailNext = false; throw new IOException("Injected index failure"); } return inner.AddAsync(item, token); }
        public Task<HistoryItem> UpdateScreenshotAsync(Guid id, NewHistoryItem item, CancellationToken token) => inner.UpdateScreenshotAsync(id, item, token);
        public Task<IReadOnlyList<HistoryItem>> GetRecentAsync(int count, CancellationToken token) => inner.GetRecentAsync(count, token);
        public IAsyncEnumerable<HistoryItem> EnumerateOlderAsync(DateTimeOffset? before, CancellationToken token) => inner.EnumerateOlderAsync(before, token);
        public Task DeleteAsync(Guid id, CancellationToken token) => inner.DeleteAsync(id, token);
        public Task<HistoryItem?> GetByIdAsync(Guid id, CancellationToken token) => inner.GetByIdAsync(id, token);
        public Task<HistoryItem?> FindByPathAsync(string path, CancellationToken token) => inner.FindByPathAsync(path, token);
        public Task UpdateThumbnailAsync(Guid id, DateTimeOffset created, string? path, ThumbnailState state, CancellationToken token) => inner.UpdateThumbnailAsync(id, created, path, state, token);
    }
    private sealed class EmptyTopology : IMonitorTopologyService
    {
        public IReadOnlyList<MonitorDescriptor> GetMonitors() => [];
        public VirtualPixelRect VirtualDesktopBounds => default;
    }
    private static async Task ArrowSamples(string output)
    {
        const int width = 960, height = 700;
        var pixels = new byte[width * height * 4];
        for (var i = 0; i < pixels.Length; i += 4) { pixels[i] = 34; pixels[i + 1] = 30; pixels[i + 2] = 25; pixels[i + 3] = 255; }
        var doc = new EditorDocument(new CapturedImage(width, height, pixels));
        for (var column = 0; column < 3; column++)
        {
            var x = 20 + column * 320; var stroke = new[] { 1d, 5d, 12d }[column];
            doc.Elements.Add(new TextElement($"Stroke {stroke}", new(x, 10)) { Color = Colors.White });
            for (var row = 0; row < 6; row++)
            {
                var y = 65 + row * 105;
                var arrow = row switch
                {
                    0 => new ArrowElement(new(x + 20, y + 30), new(x + 265, y + 30)),
                    1 => new ArrowElement(new(x + 30, y), new(x + 240, y + 55)),
                    2 => new ArrowElement(new(x + 20, y + 50), new(x + 260, y + 50)) { Control = new(x + 140, y - 55) },
                    3 => new ArrowElement(new(x + 20, y), new(x + 260, y)) { Control = new(x + 140, y + 100) },
                    4 => new ArrowElement(new(x + 100, y), new(x + 100, y + 70)) { Control = new(x + 210, y + 35) },
                    _ => new ArrowElement(new(x + 25, y + 20), new(x + 55, y + 20))
                };
                arrow.StrokeWidth = stroke; doc.Elements.Add(arrow);
                if (row == 5)
                {
                    doc.Elements.Add(new ArrowElement(new(x + 120, y), new(x + 120, y + 55)) { StrokeWidth = stroke });
                    doc.Elements.Add(new ArrowElement(new(x + 210, y + 20), new(x + 216, y + 24)) { StrokeWidth = stroke });
                }
            }
        }
        await EditorRenderer.SavePngAsync(doc, Path.Combine(output, "arrow-geometry-sheet.png"), CancellationToken.None);
    }
    private static void Require(bool value, string name) { if (!value) throw new InvalidOperationException(name); }
    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T value) yield return value;
            foreach (var nested in Descendants<T>(child)) yield return nested;
        }
    }
}
#endif
