using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Shell;
using System.Windows.Interop;
using System.Runtime.InteropServices;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media.Imaging;
using SnappySnap.Localization;
using System.Collections.ObjectModel;

namespace SnappySnap.Presentation;

public static class Ui
{
    // Bind shared brush colors so WPF cannot freeze them when a style first uses them.
    private static readonly ObservableCollection<Color> ThemeColors = new();
    public static Brush Brush(string key) => (Brush)System.Windows.Application.Current.FindResource(key + "Brush");
    public static void Localize(DependencyObject target, DependencyProperty property, string text, params object?[] arguments) => BindingOperations.SetBinding(target, property,
        new Binding(nameof(L.Language)) { Source = L.Current, Converter = Translation.Instance, ConverterParameter = (text, arguments) });
    public static TextBlock Text(string text, double size = 13, string color = "Text")
    {
        var block = new TextBlock { FontSize = size, Foreground = Brush(color), VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap };
        Localize(block, TextBlock.TextProperty, text); return block;
    }
    private sealed class Translation : IValueConverter
    {
        public static Translation Instance { get; } = new();
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            var (text, arguments) = ((string, object?[]))parameter;
            return arguments.Length == 0 ? L.T(text) : L.F(text, arguments);
        }
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
    }
    public static TextBlock Icon(string glyph, double size = 18) => new() { Text = glyph, FontFamily = new FontFamily("Segoe Fluent Icons"), FontSize = size, VerticalAlignment = VerticalAlignment.Center };
    public static StackPanel Label(string glyph, string label)
    {
        var p = new StackPanel { Orientation = Orientation.Horizontal };
        var icon = Icon(glyph); icon.Margin = new Thickness(0, 0, 8, 0); p.Children.Add(icon);
        var text = Text(label); text.ClearValue(TextBlock.ForegroundProperty); p.Children.Add(text); return p;
    }
    public static Button Button(string label, string glyph, RoutedEventHandler action, string? style = null)
    {
        var b = new Button { Content = string.IsNullOrEmpty(glyph) ? label : Label(glyph, label), Margin = new Thickness(0, 0, 8, 0) };
        if (string.IsNullOrEmpty(glyph)) Localize(b, ContentControl.ContentProperty, label);
        if (style is not null) b.SetResourceReference(FrameworkElement.StyleProperty, style);
        Localize(b, AutomationProperties.NameProperty, label); b.Click += action; return b;
    }
    public static Border Card(UIElement child, double padding = 16) => new() { Child = child, Background = Brush("Surface"), BorderBrush = Brush("Border"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8), Padding = new Thickness(padding) };
    public static Border Chip(string text, string color = "Muted") => new() { Child = Text(text, 11, color), Background = Brush("Raised"), CornerRadius = new CornerRadius(10), Padding = new Thickness(9, 3, 9, 3), HorizontalAlignment = HorizontalAlignment.Left };
    public static FrameworkElement Brand(bool compact = false)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        if (!compact)
        {
            var g = new Grid { Width = 30, Height = 30, Margin = new Thickness(0, 0, 12, 0) };
            g.Children.Add(BrandMark(30)); panel.Children.Add(g);
        }
        var title = new TextBlock { Foreground = Brush("Text"), FontSize = compact ? 14 : 23, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center };
        title.Inlines.Add(new Run("Snappy")); title.Inlines.Add(new Run("Snap") { Foreground = Brush("Accent") }); panel.Children.Add(title); return panel;
    }
    public static void InstallTheme()
    {
        System.Windows.Application.Current.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("/SnappySnap.Editor;component/Presentation/Theme.xaml", UriKind.Relative) });
    }
    public static FrameworkElement BrandMark(double size = 22)
    {
        return new Image { Width = size, Height = size, Source = new BitmapImage(new Uri("pack://application:,,,/SnappySnap.Editor;component/Presentation/SnappySnap.png", UriKind.Absolute)) };
    }
    public static bool SystemUsesLightTheme(bool taskbar = false)
    {
        using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
        return key?.GetValue(taskbar ? "SystemUsesLightTheme" : "AppsUseLightTheme") is not int value || value != 0;
    }
    public static void ApplyTheme(string theme)
    {
        var light = theme == "Light" || theme == "System" && SystemUsesLightTheme();
        var colors = light
            ? new[] { "#F4F5F3", "#FFFFFF", "#E9EDE9", "#D4DAD6", "#252B2D", "#5F6C70", "#315F50", "#FFFFFF", "#DDEBE3", "#A72B34", "#795500", "#758289" }
            : new[] { "#202325", "#292D30", "#343A3E", "#363D41", "#EEF0F1", "#ACB4B8", "#9CC7B3", "#17342A", "#33473E", "#ECA19A", "#D9BC7C", "#758289" };
        var names = new[] { "Background", "Surface", "Raised", "Border", "Text", "Muted", "Accent", "AccentText", "AccentSurface", "Danger", "Warning", "ControlBorder" };
        if (SystemParameters.HighContrast)
        {
            for (var i = 0; i < names.Length; i++) colors[i] = (names[i] switch
            {
                "Background" or "Surface" or "Raised" => SystemColors.WindowColor,
                "Accent" or "AccentSurface" => SystemColors.HighlightColor,
                "AccentText" => SystemColors.HighlightTextColor,
                _ => SystemColors.WindowTextColor
            }).ToString(CultureInfo.InvariantCulture);
        }
        var resources = System.Windows.Application.Current.Resources.MergedDictionaries.Last(dictionary => dictionary.Source?.OriginalString.EndsWith("/Presentation/Theme.xaml", StringComparison.Ordinal) == true);
        for (var i = 0; i < names.Length; i++)
        {
            var color = (Color)ColorConverter.ConvertFromString(colors[i]);
            if (ThemeColors.Count <= i)
            {
                ThemeColors.Add(color);
                var brush = new SolidColorBrush();
                BindingOperations.SetBinding(brush, SolidColorBrush.ColorProperty, new Binding("[" + i + "]") { Source = ThemeColors });
                resources[names[i] + "Brush"] = brush;
            }
            else ThemeColors[i] = color;
        }
    }
    public static DockPanel Shell(Window window, string subtitle, UIElement content, UIElement? navigation = null, UIElement? actions = null, bool compact = false)
    {
        window.SetResourceReference(Window.BackgroundProperty, "BackgroundBrush"); window.SetResourceReference(Window.ForegroundProperty, "TextBrush");
        window.FontFamily = new FontFamily("Segoe UI"); window.FontSize = 13; window.UseLayoutRounding = true;
        window.WindowStyle = WindowStyle.None;
        Localize(window, Window.TitleProperty, window.Title);
        WorkAreaChrome.Attach(window);
        WindowChrome.SetWindowChrome(window, new WindowChrome { CaptionHeight = 36, ResizeBorderThickness = new Thickness(6), GlassFrameThickness = new Thickness(0), CornerRadius = new CornerRadius(8), UseAeroCaptionButtons = false });
        var root = new DockPanel();
        var title = new DockPanel { Height = 36, Background = Brush("Surface"), LastChildFill = true };
        var chrome = new StackPanel { Orientation = Orientation.Horizontal }; DockPanel.SetDock(chrome, Dock.Right);
        void Caption(string name, string glyph, RoutedEventHandler handler)
        {
            var b = Button(name, "", handler, "GhostButton"); b.Content = Icon(glyph, 10); b.Width = 44; b.Margin = new Thickness(0); Localize(b, FrameworkElement.ToolTipProperty, name); WindowChrome.SetIsHitTestVisibleInChrome(b, true); chrome.Children.Add(b);
        }
        if (window.ResizeMode != ResizeMode.NoResize) Caption("Minimize", "\uE921", (_, _) => window.WindowState = WindowState.Minimized);
        if (window.ResizeMode != ResizeMode.NoResize) Caption("Maximize / restore", "\uE922", (_, _) => window.WindowState = window.WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized);
        Caption("Close", "\uE8BB", (_, _) => window.Close()); title.Children.Add(chrome);
        var mark = BrandMark(); mark.Margin = new Thickness(10, 0, 8, 0); DockPanel.SetDock(mark, Dock.Left); title.Children.Add(mark);
        var caption = Text("", 12); caption.SetBinding(TextBlock.TextProperty, new Binding(nameof(Window.Title)) { Source = window }); title.Children.Add(caption); DockPanel.SetDock(title, Dock.Top); root.Children.Add(title);
        if (actions is not null)
        {
        var header = new DockPanel { LastChildFill = true, Margin = new Thickness(20, 16, 20, 16) };
        header.Children.Add(actions);
        var headerFrame = new Border { Child = header, BorderBrush = Brush("Border"), BorderThickness = new Thickness(0, 0, 0, 1) }; DockPanel.SetDock(headerFrame, Dock.Top); root.Children.Add(headerFrame);
        }
        if (navigation is not null) { var nav = new Border { Width = 178, Child = navigation, BorderBrush = Brush("Border"), BorderThickness = new Thickness(0, 0, 1, 0), Padding = new Thickness(8, 8, 8, 12) }; DockPanel.SetDock(nav, Dock.Left); root.Children.Add(nav); }
        root.Children.Add(content); window.Content = new Border { Background = Brush("Background"), BorderBrush = Brush("Border"), BorderThickness = new Thickness(1), Child = root }; return root;
    }
    public static void FitInitialBounds(Window window) => WorkAreaChrome.FitInitialBounds(window);
    public static DockPanel Navigation(string active, Action shelf, Action settings)
    {
        var panel = new DockPanel();
        var items = new StackPanel();
        foreach (var (name, glyph, action) in new[] { ("Shelf", "\uEB9F", shelf), ("Settings", "\uE713", settings) })
        {
            var b = new ToggleButton { Content = Label(glyph, name), IsChecked = name == active, HorizontalContentAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 0, 0, 5) };
            Localize(b, AutomationProperties.NameProperty, name); b.Click += (_, _) => { b.IsChecked = name == active; action(); }; items.Children.Add(b);
        }
        panel.Children.Add(items); return panel;
    }
}

/// <summary>WM_GETMINMAXINFO and MONITORINFO both use physical pixels, including negative origins.</summary>
internal static class WorkAreaChrome
{
    public static void FitInitialBounds(Window window)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        if (!GetMonitorInfo(MonitorFromWindow(hwnd, 2), ref info) || !GetWindowRect(hwnd, out var rect)) return;
        var scale = GetDpiForWindow(hwnd) / 96d;
        var workWidth = info.Work.Right - info.Work.Left;
        var workHeight = info.Work.Bottom - info.Work.Top;
        window.MinWidth = Math.Min(window.MinWidth, workWidth / scale);
        window.MinHeight = Math.Min(window.MinHeight, workHeight / scale);
        var width = Math.Min(rect.Right - rect.Left, workWidth);
        var height = Math.Min(rect.Bottom - rect.Top, workHeight);
        SetWindowPos(hwnd, 0, Math.Clamp(rect.Left, info.Work.Left, info.Work.Right - width),
            Math.Clamp(rect.Top, info.Work.Top, info.Work.Bottom - height), width, height, 0x0010 | 0x0004);
    }
    public static void Attach(Window window)
    {
        HwndSource? source = null;
        nint Hook(nint hwnd, int message, nint wParam, nint lParam, ref bool handled)
        {
            if (message != 0x0024) return 0;
            var monitor = MonitorFromWindow(hwnd, 2);
            var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
            if (!GetMonitorInfo(monitor, ref info)) return 0;
            var limits = Marshal.PtrToStructure<MinMaxInfo>(lParam);
            limits.MaxPosition = new NativePoint { X = info.Work.Left - info.Monitor.Left, Y = info.Work.Top - info.Monitor.Top };
            limits.MaxSize = new NativePoint { X = info.Work.Right - info.Work.Left, Y = info.Work.Bottom - info.Work.Top };
            Marshal.StructureToPtr(limits, lParam, false);
            // WPF's Window hook still needs this message to apply MinWidth/MinHeight tracking limits.
            // It updates tracking sizes, while preserving our monitor-relative maximize rectangle.
            return 0;
        }
        window.SourceInitialized += (_, _) => { source = HwndSource.FromHwnd(new WindowInteropHelper(window).Handle); source?.AddHook(Hook); };
        window.Closed += (_, _) => source?.RemoveHook(Hook);
    }
    [StructLayout(LayoutKind.Sequential)] private struct NativePoint { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] private struct NativeRect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] private struct MonitorInfo { public int Size; public NativeRect Monitor, Work; public uint Flags; }
    [StructLayout(LayoutKind.Sequential)] private struct MinMaxInfo { public NativePoint Reserved, MaxSize, MaxPosition, MinTrackSize, MaxTrackSize; }
    [DllImport("user32.dll")] private static extern nint MonitorFromWindow(nint hwnd, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetMonitorInfo(nint monitor, ref MonitorInfo info);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetWindowRect(nint hwnd, out NativeRect rect);
    [DllImport("user32.dll")] private static extern uint GetDpiForWindow(nint hwnd);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool SetWindowPos(nint hwnd, nint after, int x, int y, int width, int height, uint flags);
}
