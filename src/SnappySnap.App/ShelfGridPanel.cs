using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace SnappySnap.App;

/// <summary>Uniform capture cards sized from the preview height, with only viewport rows realized.</summary>
public sealed class ShelfGridPanel : VirtualizingPanel, IScrollInfo
{
    public static readonly DependencyProperty ThumbnailHeightProperty = DependencyProperty.Register(nameof(ThumbnailHeight), typeof(double), typeof(ShelfGridPanel), new FrameworkPropertyMetadata(164d, FrameworkPropertyMetadataOptions.AffectsMeasure));
    public double ThumbnailHeight { get => (double)GetValue(ThumbnailHeightProperty); set => SetValue(ThumbnailHeightProperty, value); }
    public double CardMinimumWidth => 260 * ThumbnailHeight / 164;
    public double RowHeight => ThumbnailHeight + 70;
    private int _columns = 1;
    private double _cellWidth, _offset;
    private double _measuredRowHeight;
    private Size _extent, _viewport;
    public bool CanHorizontallyScroll { get; set; }
    public bool CanVerticallyScroll { get; set; }
    public double ExtentWidth => _extent.Width;
    public double ExtentHeight => _extent.Height;
    public double ViewportWidth => _viewport.Width;
    public double ViewportHeight => _viewport.Height;
    public double HorizontalOffset => 0;
    public double VerticalOffset => _offset;
    public ScrollViewer ScrollOwner { get; set; } = null!;
    public (int Index, double Top) GetViewportAnchor() => ((int)(_offset / RowHeight) * _columns, -(_offset % RowHeight));
    public void RestoreViewportAnchor(int index, double top) => SetVerticalOffset(index / _columns * RowHeight - top);
    internal Rect GetItemBounds(int index, Thickness margin) => new(
        index % _columns * _cellWidth + margin.Left, index / _columns * RowHeight + margin.Top,
        Math.Max(0, _cellWidth - margin.Left - margin.Right), Math.Max(0, RowHeight - margin.Top - margin.Bottom));
    protected override Size MeasureOverride(Size availableSize)
    {
        var owner = ItemsControl.GetItemsOwner(this);
        if (owner is null) return default;
        var width = double.IsInfinity(availableSize.Width) ? CardMinimumWidth : availableSize.Width;
        var height = double.IsInfinity(availableSize.Height) ? RowHeight : availableSize.Height;
        var columns = Math.Max(1, (int)(width / CardMinimumWidth));
        var reflow = _viewport.Height > 0 && (_columns != columns || _measuredRowHeight != RowHeight || _viewport.Height != height);
        var wasAtBottom = _offset >= Math.Max(0, ExtentHeight - ViewportHeight) - .5;
        var anchoredOffset = _offset;
        if (reflow && !wasAtBottom)
        {
            var anchorIndex = (int)(_offset / _measuredRowHeight) * _columns;
            var rowFraction = _offset % _measuredRowHeight / _measuredRowHeight;
            anchoredOffset = (anchorIndex / columns + rowFraction) * RowHeight;
        }
        _columns = columns;
        _cellWidth = width / _columns;
        _viewport = new Size(width, height);
        _extent = new Size(width, Math.Ceiling(owner.Items.Count / (double)_columns) * RowHeight);
        if (reflow) _offset = wasAtBottom ? ExtentHeight - ViewportHeight : anchoredOffset;
        _offset = Math.Clamp(_offset, 0, Math.Max(0, ExtentHeight - ViewportHeight));
        _measuredRowHeight = RowHeight;
        ScrollOwner?.InvalidateScrollInfo();
        var first = Math.Max(0, ((int)(_offset / RowHeight) - 1) * _columns);
        var last = Math.Min(owner.Items.Count - 1, ((int)Math.Ceiling((_offset + height) / RowHeight) + 1) * _columns - 1);
        var children = InternalChildren;
        var generator = ItemContainerGenerator;
        for (var i = children.Count - 1; i >= 0; i--)
        {
            var position = new GeneratorPosition(i, 0);
            var index = generator.IndexFromGeneratorPosition(position);
            if (index < first || index > last)
            {
                ((IRecyclingItemContainerGenerator)generator).Recycle(position, 1);
                RemoveInternalChildRange(i, 1);
            }
        }
        var start = generator.GeneratorPositionFromIndex(first);
        var childIndex = start.Offset == 0 ? start.Index : start.Index + 1;
        using (generator.StartAt(start, GeneratorDirection.Forward, true))
        {
            for (var index = first; index <= last; index++, childIndex++)
            {
                var child = (UIElement)generator.GenerateNext(out var created);
                // ListBox can generate a selected/focused container before it belongs to the panel.
                if (created || VisualTreeHelper.GetParent(child) != this)
                {
                    if (childIndex >= children.Count) AddInternalChild(child); else InsertInternalChild(childIndex, child);
                    generator.PrepareItemContainer(child);
                }
                child.Measure(new Size(_cellWidth, RowHeight));
            }
        }
        return _viewport;
    }
    protected override Size ArrangeOverride(Size finalSize)
    {
        for (var i = 0; i < InternalChildren.Count; i++)
        {
            var index = ItemContainerGenerator.IndexFromGeneratorPosition(new GeneratorPosition(i, 0));
            InternalChildren[i].Arrange(new Rect(index % _columns * _cellWidth, index / _columns * RowHeight - _offset, _cellWidth, RowHeight));
        }
        return finalSize;
    }
    protected override void OnItemsChanged(object sender, ItemsChangedEventArgs args)
    {
        if (args.Action == System.Collections.Specialized.NotifyCollectionChangedAction.Reset)
            RemoveInternalChildRange(0, InternalChildren.Count);
        else if (args.ItemUICount > 0 && args.Action is System.Collections.Specialized.NotifyCollectionChangedAction.Remove or System.Collections.Specialized.NotifyCollectionChangedAction.Replace or System.Collections.Specialized.NotifyCollectionChangedAction.Move)
            RemoveInternalChildRange(args.Action == System.Collections.Specialized.NotifyCollectionChangedAction.Move ? args.OldPosition.Index : args.Position.Index, args.ItemUICount);
        InvalidateMeasure();
    }
    protected override void BringIndexIntoView(int index)
    {
        var top = index / _columns * RowHeight;
        if (top < _offset) SetVerticalOffset(top);
        else if (top + RowHeight > _offset + ViewportHeight) SetVerticalOffset(top + RowHeight - ViewportHeight);
    }
    public Rect MakeVisible(Visual visual, Rect rectangle)
    {
        var owner = ItemsControl.GetItemsOwner(this);
        var container = ItemsControl.ContainerFromElement(owner, visual);
        if (container is not null) BringIndexIntoView(owner.ItemContainerGenerator.IndexFromContainer(container));
        return rectangle;
    }
    public void SetVerticalOffset(double offset) { _offset = Math.Clamp(offset, 0, Math.Max(0, ExtentHeight - ViewportHeight)); ScrollOwner?.InvalidateScrollInfo(); InvalidateMeasure(); }
    public void SetHorizontalOffset(double offset) { }
    public void LineUp() => SetVerticalOffset(_offset - 40);
    public void LineDown() => SetVerticalOffset(_offset + 40);
    public void MouseWheelUp() => SetVerticalOffset(_offset - 120);
    public void MouseWheelDown() => SetVerticalOffset(_offset + 120);
    public void PageUp() => SetVerticalOffset(_offset - ViewportHeight);
    public void PageDown() => SetVerticalOffset(_offset + ViewportHeight);
    public void LineLeft() { }
    public void LineRight() { }
    public void MouseWheelLeft() { }
    public void MouseWheelRight() { }
    public void PageLeft() { }
    public void PageRight() { }
}
