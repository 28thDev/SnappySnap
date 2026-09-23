using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Threading;
using SnappySnap.Core;
using SnappySnap.Presentation;

namespace SnappySnap.App;

/// <summary>A short, non-activating startup acknowledgement. No profile or timer survives the window.</summary>
internal sealed class StartupNoticeWindow : Window
{
    private static readonly TimeSpan StartupLifetime = TimeSpan.FromSeconds(5);
    private readonly DispatcherTimer _dismiss = new() { Interval = StartupLifetime };

    public StartupNoticeWindow(HotkeySettings hotkeys, IReadOnlySet<int> unavailable, IAppLogger logger)
    {
        Title = "SnappySnap"; Width = 410; SizeToContent = SizeToContent.Height;
        WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize; AllowsTransparency = true;
        Background = Brushes.Transparent; ShowInTaskbar = false; ShowActivated = false; Topmost = true; Focusable = false;
        var content = new StackPanel();
        var heading = new DockPanel { Margin = new Thickness(0, 0, 0, 10) };
        var mark = Ui.BrandMark(28); mark.Margin = new Thickness(0, 0, 11, 0); DockPanel.SetDock(mark, Dock.Left); heading.Children.Add(mark);
        var readyContent = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        readyContent.Children.Add(new System.Windows.Shapes.Ellipse { Width = 6, Height = 6, Fill = Ui.Brush("Accent"), Margin = new Thickness(0, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center });
        readyContent.Children.Add(Ui.Text("Ready", 10, "Accent"));
        var ready = new Border { Child = readyContent, Background = Ui.Brush("AccentSurface"), CornerRadius = new CornerRadius(10), Padding = new Thickness(8, 4, 8, 4), VerticalAlignment = VerticalAlignment.Center };
        DockPanel.SetDock(ready, Dock.Right); heading.Children.Add(ready);
        var title = Ui.Text("SnappySnap started", 16); title.FontWeight = FontWeights.SemiBold;
        heading.Children.Add(title); content.Children.Add(heading);
        void Shortcut(int id, string label, string? value)
        {
            if (string.IsNullOrWhiteSpace(value)) return;
            var row = new Grid { Margin = new Thickness(0, 3, 0, 3), MinHeight = 29 };
            row.ColumnDefinitions.Add(new ColumnDefinition()); row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.Children.Add(Ui.Text(label, 12, "Muted"));
            UIElement shortcut;
            if (unavailable.Contains(id))
            {
                shortcut = Ui.Text("Shortcut unavailable", 11, "Warning");
            }
            else
            {
                var keys = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
                var parts = value.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
                for (var index = 0; index < parts.Length; index++)
                {
                    if (index > 0)
                    {
                        var plus = Ui.Text("+", 10, "Muted"); plus.Margin = new Thickness(3, 0, 3, 0); keys.Children.Add(plus);
                    }
                    var key = Ui.Text(parts[index], 10, "Accent"); key.FontWeight = FontWeights.SemiBold;
                    keys.Children.Add(new Border { Child = key, Background = Ui.Brush("AccentSurface"), BorderBrush = Ui.Brush("Border"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(4), Padding = new Thickness(6, 3, 6, 3) });
                }
                shortcut = keys;
            }
            Grid.SetColumn(shortcut, 1); row.Children.Add(shortcut); content.Children.Add(row);
        }
        Shortcut(1001, "Screenshot region", hotkeys.RegionScreenshot);
        Shortcut(1002, "Start / stop recording", hotkeys.RegionVideo);
        Shortcut(1003, "Pause / resume recording", hotkeys.PauseResumeVideo);
        Shortcut(1004, "Open Shelf", hotkeys.OpenShelf);
        var progress = new ProgressBar { Minimum = 0, Maximum = 1, Value = 1, Height = 3, Margin = new Thickness(0, 10, 0, 0), IsHitTestVisible = false };
        content.Children.Add(progress);
        var card = Ui.Card(content, 18);
        card.CornerRadius = new CornerRadius(10);
        card.Effect = new DropShadowEffect { Color = Colors.Black, BlurRadius = 16, ShadowDepth = 2, Opacity = 0.2 };
        Content = new Border { Child = card, Margin = new Thickness(10) };
        SourceInitialized += (_, _) =>
        {
            var handle = new WindowInteropHelper(this).Handle;
            OverlayWindowStyles.MakeNoActivate(handle);
            _ = new SnappySnap.Capture.WindowCaptureExclusionService(logger).TryExcludeWindow(handle);
        };
        Loaded += (_, _) =>
        {
            var work = SystemParameters.WorkArea;
            Left = Math.Max(work.Left, work.Right - ActualWidth - 20); Top = Math.Max(work.Top, work.Bottom - ActualHeight - 20);
            progress.BeginAnimation(RangeBase.ValueProperty, new DoubleAnimation(1, 0, StartupLifetime) { EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseInOut } });
            _dismiss.Start();
        };
        _dismiss.Tick += (_, _) => Close();
        Closed += (_, _) => _dismiss.Stop();
    }
}
