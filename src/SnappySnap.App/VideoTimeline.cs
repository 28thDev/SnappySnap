using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using SnappySnap.Core;
using SnappySnap.Presentation;
using SnappySnap.Localization;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
namespace SnappySnap.App;

/// <summary>A ripple-edited track. Gestures are expressed only in output time.</summary>
public sealed class VideoTimeline : FrameworkElement
{
    public VideoEditSession? Edit { get; set; }
    public double Duration => Edit?.Duration.TotalSeconds ?? 1;
    private double _position;
    public double Position
    {
        get => _position;
        set
        {
            var previous = _position; _position = value;
            if (previous != value && UIElementAutomationPeer.FromElement(this) is VideoTimelinePeer peer)
                peer.RaisePropertyChangedEvent(RangeValuePatternIdentifiers.ValueProperty, previous, value);
        }
    }
    public double SelectionStart { get; set; }
    public double SelectionEnd { get; set; }
    public IReadOnlyList<ImageSource> Frames { get; set; } = Array.Empty<ImageSource>();
    public event Action<double, double>? TrimChanged;
    public event Action<double>? SeekRequested;
    public event Action? SelectionChanged;
    public event Action? ScrubStarted;
    public event Action? ScrubCompleted;
    private int _handle;
    private double _anchor, _downX, _trimStart, _trimEnd;
    public VideoTimeline()
    {
        Height = 112; Focusable = true;
        Ui.Localize(this, AutomationProperties.NameProperty, "Edited video timeline");
        Ui.Localize(this, AutomationProperties.HelpTextProperty, "Drag to select; Delete cuts. Arrow keys seek; Shift changes the step to one second. Exact time fields provide keyboard selection.");
    }
    protected override AutomationPeer OnCreateAutomationPeer() => new VideoTimelinePeer(this);
    public void NotifySelectionChanged()
    {
        if (UIElementAutomationPeer.FromElement(this) is VideoTimelinePeer peer) peer.SelectionChanged();
    }
    private sealed class VideoTimelinePeer(VideoTimeline owner) : FrameworkElementAutomationPeer(owner), IRangeValueProvider, IValueProvider
    {
        private string _selection = "";
        protected override string GetClassNameCore() => nameof(VideoTimeline);
        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Slider;
        public override object? GetPattern(PatternInterface patternInterface) => patternInterface is PatternInterface.RangeValue or PatternInterface.Value ? this : base.GetPattern(patternInterface);
        bool IRangeValueProvider.IsReadOnly => !owner.IsEnabled || owner.Edit is null;
        double IRangeValueProvider.Value => owner.Position;
        double IRangeValueProvider.Minimum => 0;
        double IRangeValueProvider.Maximum => owner.Duration;
        double IRangeValueProvider.SmallChange => .1;
        double IRangeValueProvider.LargeChange => 1;
        void IRangeValueProvider.SetValue(double value)
        {
            if (!owner.IsEnabled) throw new ElementNotEnabledException();
            if (owner.Edit is null || !double.IsFinite(value) || value < 0 || value > owner.Duration) throw new ArgumentOutOfRangeException(nameof(value));
            owner.Position = value; owner.SeekRequested?.Invoke(value); owner.InvalidateVisual();
        }
        bool IValueProvider.IsReadOnly => true;
        string IValueProvider.Value => L.F("Selection: {0:0.###} to {1:0.###} seconds", owner.SelectionStart, owner.SelectionEnd);
        void IValueProvider.SetValue(string value) => throw new InvalidOperationException("Use the accessible selection start and end fields.");
        public void SelectionChanged()
        {
            var text = ((IValueProvider)this).Value;
            if (text != _selection) { RaisePropertyChangedEvent(ValuePatternIdentifiers.ValueProperty, _selection, text); _selection = text; }
        }
    }
    private double X(double seconds) => 12 + seconds / Math.Max(Duration, .001) * Math.Max(1, ActualWidth - 24);
    private double Seconds(double x) => Math.Clamp((x - 12) / Math.Max(1, ActualWidth - 24) * Duration, 0, Duration);
    protected override void OnRender(DrawingContext drawingContext)
    {
        drawingContext.DrawRectangle(Brushes.Transparent, null, new Rect(RenderSize));
        var width = Math.Max(1, ActualWidth - 24); var track = new Rect(12, 30, width, 52);
        drawingContext.DrawRoundedRectangle(Ui.Brush("Raised"), new Pen(Ui.Brush("Border"), 1), track, 4, 4);
        drawingContext.PushClip(new RectangleGeometry(track));
        if (Frames.Count > 0 && Edit is not null)
        {
            var cells = Math.Max(1, (int)Math.Ceiling(width / 90));
            for (var i = 0; i < cells; i++)
            {
                var source = Edit.ToSource(TimeSpan.FromSeconds(Duration * (i + .5) / cells));
                var index = Math.Clamp((int)(source.TotalSeconds / Edit.SourceDuration.TotalSeconds * Frames.Count), 0, Frames.Count - 1);
                var cell = new Rect(12 + width * i / cells, 30, width / cells, 52);
                var aspect = Frames[index].Width / Frames[index].Height;
                var imageWidth = Math.Max(cell.Width, cell.Height * aspect); var imageHeight = imageWidth / aspect;
                drawingContext.PushClip(new RectangleGeometry(cell));
                drawingContext.DrawImage(Frames[index], new Rect(cell.X + (cell.Width - imageWidth) / 2, cell.Y + (cell.Height - imageHeight) / 2, imageWidth, imageHeight));
                drawingContext.Pop();
            }
        }
        if (Edit is not null)
        {
            var cursor = 0d;
            foreach (var segment in Edit.Segments) { cursor += segment.Duration.TotalSeconds; drawingContext.DrawLine(new Pen(Ui.Brush("Accent"), 2), new Point(X(cursor), 30), new Point(X(cursor), 82)); }
        }
        if (SelectionEnd > SelectionStart)
            drawingContext.DrawRectangle(new SolidColorBrush(Color.FromArgb(85, 255, 59, 48)), new Pen(Ui.Brush("Danger"), 2), new Rect(X(SelectionStart), 30, Math.Max(0, X(SelectionEnd) - X(SelectionStart)), 52));
        drawingContext.Pop();
        for (var i = 0; i <= 6; i++)
        {
            var value = Duration * i / 6; var text = Label(TimeSpan.FromSeconds(value).ToString(@"mm\:ss\.f", CultureInfo.InvariantCulture));
            drawingContext.DrawText(text, new Point(Math.Clamp(X(value) - text.Width / 2, 0, Math.Max(0, ActualWidth - text.Width)), 4));
        }
        foreach (var value in new[] { IsMouseCaptured ? _trimStart : 0, IsMouseCaptured ? _trimEnd : Duration })
            drawingContext.DrawRoundedRectangle(Ui.Brush("Accent"), null, new Rect(X(value) - 5, 28, 10, 56), 2, 2);
        if (SelectionEnd > SelectionStart) foreach (var value in new[] { SelectionStart, SelectionEnd })
            drawingContext.DrawRectangle(Ui.Brush("Danger"), null, new Rect(X(value) - 4, 32, 8, 48));
        drawingContext.DrawLine(new Pen(Ui.Brush("Text"), 2), new Point(X(Position), 22), new Point(X(Position), 86));
        drawingContext.DrawEllipse(Ui.Brush("Text"), null, new Point(X(Position), 24), 5, 5);
        drawingContext.DrawText(Label(L.T("Drag to select · Delete to cut · Edge handles trim")), new Point(12, 93));
        if (IsKeyboardFocused) drawingContext.DrawRoundedRectangle(null, new Pen(Ui.Brush("Accent"), 2), new Rect(1, 1, Math.Max(0, ActualWidth - 2), Math.Max(0, ActualHeight - 2)), 4, 4);
    }
    private static FormattedText Label(string text) => new(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, new Typeface("Segoe UI"), 10, Ui.Brush("Muted"), 1);
    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        Focus(); var point = e.GetPosition(this); _downX = point.X; _anchor = Seconds(point.X); _trimStart = 0; _trimEnd = Duration;
        _handle = point.Y < 30 && Math.Abs(point.X - X(Position)) <= 10 ? 5 : Math.Abs(point.X - X(0)) < 10 ? 1 : Math.Abs(point.X - X(Duration)) < 10 ? 2 :
            SelectionEnd > SelectionStart && Math.Abs(point.X - X(SelectionStart)) < 10 ? 3 : SelectionEnd > SelectionStart && Math.Abs(point.X - X(SelectionEnd)) < 10 ? 4 : 0;
        CaptureMouse(); ScrubStarted?.Invoke(); e.Handled = true;
    }
    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (!IsMouseCaptured) { Cursor = Cursors.Cross; return; }
        var seconds = Seconds(e.GetPosition(this).X);
        switch (_handle)
        {
            case 1: _trimStart = Math.Min(seconds, _trimEnd - .05); break;
            case 2: _trimEnd = Math.Max(seconds, _trimStart + .05); break;
            case 3: SelectionStart = Math.Min(seconds, SelectionEnd); SelectionChanged?.Invoke(); break;
            case 4: SelectionEnd = Math.Max(seconds, SelectionStart); SelectionChanged?.Invoke(); break;
            case 5: Position = seconds; SeekRequested?.Invoke(seconds); break;
            default: SelectionStart = Math.Min(_anchor, seconds); SelectionEnd = Math.Max(_anchor, seconds); SelectionChanged?.Invoke(); break;
        }
        InvalidateVisual();
    }
    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        if (!IsMouseCaptured) return;
        var click = Math.Abs(e.GetPosition(this).X - _downX) < 4;
        if (_handle is 1 or 2 && !click) TrimChanged?.Invoke(_trimStart, _trimEnd);
        else if (_handle == 0 && click) { SelectionStart = SelectionEnd = 0; Position = _anchor; SeekRequested?.Invoke(_anchor); SelectionChanged?.Invoke(); }
        ReleaseMouseCapture(); e.Handled = true;
    }
    protected override void OnLostMouseCapture(MouseEventArgs e)
    {
        base.OnLostMouseCapture(e);
        // Both normal mouse-up and interruption must release the editor's scrubbing state.
        ScrubCompleted?.Invoke(); InvalidateVisual();
    }
}
