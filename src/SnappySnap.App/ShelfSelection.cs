using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using SnappySnap.Presentation;
using Point = System.Windows.Point;

namespace SnappySnap.App;

public sealed partial class ShelfContent
{
    private Point _dragStart;
    private ShelfRow? _dragRow;
    private bool _deferredClick;
    private Point _marqueeStart;
    private HashSet<ShelfRow>? _marqueeSelection;
    private ModifierKeys _marqueeModifiers;
    private readonly Canvas _selectionCanvas = new() { IsHitTestVisible = false, ClipToBounds = true };
    private readonly System.Windows.Shapes.Rectangle _selectionRectangle = new()
    {
        Stroke = Ui.Brush("Accent"), StrokeThickness = 1,
        Visibility = Visibility.Collapsed
    };
    private readonly DispatcherTimer _selectionScroll = new() { Interval = TimeSpan.FromMilliseconds(40) };

    private void ConfigureSelection()
    {
        var fill = Ui.Brush("Accent").Clone(); fill.Opacity = .12; _selectionRectangle.Fill = fill;
        _selectionCanvas.Children.Add(_selectionRectangle);
        _list.PreviewMouseLeftButtonDown += OnSelectionDown;
        _list.PreviewMouseMove += OnSelectionMove;
        _list.PreviewMouseLeftButtonUp += (_, e) =>
        {
            if (_deferredClick && _dragRow is { } clicked) { _list.UnselectAll(); _list.SelectedItem = clicked; }
            if (_marqueeSelection is not null || _deferredClick) e.Handled = true;
            EndSelectionGesture();
        };
        _list.LostMouseCapture += (_, _) => EndSelectionGesture();
        _list.PreviewMouseRightButtonDown += (_, e) =>
        {
            if (ItemsControl.ContainerFromElement(_list, e.OriginalSource as DependencyObject) is ListViewItem item)
            {
                if (!item.IsSelected) _list.SelectedItem = item.Content;
                // WPF's default right-click selection would collapse a selected group.
                e.Handled = true;
            }
        };
        _list.PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape && CancelSelectionGesture()) e.Handled = true;
            else if (e.Key == Key.A && Keyboard.Modifiers == ModifierKeys.Control)
            {
                _list.SelectAll();
                e.Handled = true;
            }
        };
        _selectionScroll.Tick += (_, _) =>
        {
            if (_marqueeSelection is null || Mouse.LeftButton != MouseButtonState.Pressed) { EndSelectionGesture(); return; }
            if (FindVisual<ShelfGridPanel>(_list) is not { } panel) return;
            var point = Mouse.GetPosition(panel);
            if (point.Y < 20) panel.SetVerticalOffset(panel.VerticalOffset - 20);
            else if (point.Y > panel.ViewportHeight - 20) panel.SetVerticalOffset(panel.VerticalOffset + 20);
            _list.UpdateLayout();
            UpdateMarquee(Mouse.GetPosition(panel), panel);
        };
        Unloaded += (_, _) => EndSelectionGesture();
    }

    private void OnSelectionDown(object sender, MouseButtonEventArgs e)
    {
        EndSelectionGesture();
        _dragStart = e.GetPosition(_list);
        var item = ItemsControl.ContainerFromElement(_list, e.OriginalSource as DependencyObject) as ListViewItem;
        _dragRow = item?.Content as ShelfRow;
        if (item is not null)
        {
            // Delay collapsing a group until mouse-up so any selected card can drag the whole group.
            _deferredClick = item.IsSelected && Keyboard.Modifiers == ModifierKeys.None && e.ClickCount == 1;
            if (_deferredClick) { _list.Focus(); e.Handled = true; }
            return;
        }
        if (FindVisual<ShelfGridPanel>(_list) is not { } panel) return;
        var point = e.GetPosition(panel);
        // Scrollbars and list chrome are not selection surfaces.
        if (!new Rect(0, 0, panel.ViewportWidth, panel.ViewportHeight).Contains(point)) return;
        BeginMarquee(point, panel, Keyboard.Modifiers);
        _list.Focus(); _list.CaptureMouse(); _selectionScroll.Start(); e.Handled = true;
    }

    private void OnSelectionMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed) { EndSelectionGesture(); return; }
        if (_marqueeSelection is not null && FindVisual<ShelfGridPanel>(_list) is { } panel)
        {
            UpdateMarquee(e.GetPosition(panel), panel); e.Handled = true; return;
        }
        var point = e.GetPosition(_list);
        if (_dragRow is null || (Math.Abs(point.X - _dragStart.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(point.Y - _dragStart.Y) < SystemParameters.MinimumVerticalDragDistance)) return;
        // Ctrl-click can deselect the pressed card: it must not drag unrelated selected files.
        if (!_list.SelectedItems.Contains(_dragRow)) { EndSelectionGesture(); return; }
        var data = CreateSelectedFileDrop();
        EndSelectionGesture(); e.Handled = true;
        if (data is null) return;
        _state.IsDragging = true;
        try { DragDrop.DoDragDrop(_list, data, DragDropEffects.Copy); }
        catch (Exception ex) { ShowFailure("Could not drag captures. Check the files and try again.", ex); }
        finally { _state.IsDragging = false; }
    }

    internal DataObject? CreateSelectedFileDrop()
    {
        var selected = _list.SelectedItems.Cast<ShelfRow>().ToHashSet();
        var paths = _rows.Where(selected.Contains).Select(row => row.Item.FilePath).ToArray();
        if (paths.Length == 0) return null;
        if (paths.Any(path => !File.Exists(path)))
        {
            _message.Text = SnappySnap.Localization.L.T("Some selected files were moved or deleted. Refresh or change the selection and try again.");
            _message.Visibility = Visibility.Visible;
            return null;
        }
        return new DataObject(DataFormats.FileDrop, paths);
    }

    internal void BeginMarquee(Point point, ShelfGridPanel panel, ModifierKeys modifiers)
    {
        _marqueeStart = new Point(point.X, point.Y + panel.VerticalOffset);
        _marqueeModifiers = modifiers;
        _marqueeSelection = _list.SelectedItems.Cast<ShelfRow>().ToHashSet();
        if ((modifiers & (ModifierKeys.Control | ModifierKeys.Shift)) == 0) _list.UnselectAll();
    }

    internal void UpdateMarquee(Point point, ShelfGridPanel panel)
    {
        if (_marqueeSelection is null) return;
        point = new Point(Math.Clamp(point.X, 0, panel.ViewportWidth), Math.Clamp(point.Y, 0, panel.ViewportHeight));
        var area = new Rect(_marqueeStart, new Point(point.X, point.Y + panel.VerticalOffset));
        var selected = new HashSet<ShelfRow>();
        var margin = RealizedItems(_list).FirstOrDefault()?.Margin ?? new Thickness();
        for (var i = 0; i < _rows.Count; i++)
        {
            var hit = area.Width > 0 && area.Height > 0 && area.IntersectsWith(panel.GetItemBounds(i, margin));
            var wasSelected = _marqueeSelection.Contains(_rows[i]);
            var include = (_marqueeModifiers & ModifierKeys.Control) != 0 ? hit ^ wasSelected
                : (_marqueeModifiers & ModifierKeys.Shift) != 0 ? hit || wasSelected : hit;
            if (include) selected.Add(_rows[i]);
        }
        SetSelection(selected);
        var origin = panel.TranslatePoint(new Point(area.X, area.Y - panel.VerticalOffset), _selectionCanvas);
        Canvas.SetLeft(_selectionRectangle, origin.X); Canvas.SetTop(_selectionRectangle, origin.Y);
        _selectionRectangle.Width = area.Width; _selectionRectangle.Height = area.Height;
        _selectionRectangle.Visibility = Visibility.Visible;
    }

    private void SetSelection(HashSet<ShelfRow> selected)
    {
        foreach (var row in _list.SelectedItems.Cast<ShelfRow>().Where(row => !selected.Contains(row)).ToArray()) _list.SelectedItems.Remove(row);
        foreach (var row in _rows.Where(selected.Contains)) if (!_list.SelectedItems.Contains(row)) _list.SelectedItems.Add(row);
    }

    internal void EndSelectionGesture()
    {
        _selectionScroll.Stop(); _marqueeSelection = null; _dragRow = null; _deferredClick = false;
        _selectionRectangle.Visibility = Visibility.Collapsed;
        if (_list.IsMouseCaptured) _list.ReleaseMouseCapture();
    }

    internal bool CancelSelectionGesture()
    {
        if (_marqueeSelection is not { } previous) return false;
        SetSelection(previous); EndSelectionGesture(); return true;
    }
}
