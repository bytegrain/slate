using System.Collections;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using Slate.Data;

namespace Slate.Wpf;

/// <summary>
/// The scrolling body: an <see cref="IScrollInfo"/> panel that realises only the rows in the viewport (plus overscan),
/// recycling <see cref="GridRowView"/>s. Row geometry comes from <see cref="GridController"/>.
/// </summary>
internal sealed class GridSurface : Panel, IScrollInfo
{
    private const int Overscan = 4;
    private readonly DataGrid _owner;
    private readonly Dictionary<int, GridRowView> _realized = new();
    private readonly Stack<GridRowView> _pool = new();
    private Size _viewport;
    private Size _extent;
    private Vector _offset;
    private ViewportRange _window;
    private readonly GridPinShadows _shadows;

    public GridSurface(DataGrid owner)
    {
        _owner = owner;
        ClipToBounds = true;
        Focusable = false;
        _shadows = new GridPinShadows(Children);
    }

    public ViewportRange WindowRange => _window;

    public IEnumerable<GridRowView> RealizedRows => _realized.Values;

    public GridRowView? RowView(int index) => _realized.GetValueOrDefault(index);

    /// <summary>Rows, columns or geometry changed: rebind everything.</summary>
    public void InvalidateView()
    {
        foreach (var row in _realized.Values) row.Invalidate();
        InvalidateMeasure();
        InvalidateArrange();
    }

    /// <summary>Repaints row backgrounds only (flash animation).</summary>
    public void InvalidateRowVisuals()
    {
        foreach (var row in _realized.Values) row.InvalidateVisual();
    }

    /// <summary>Only row visuals changed (selection, hover, tokens).</summary>
    public void InvalidateRows()
    {
        foreach (var row in _realized.Values) row.Refresh();
    }

    protected override Size MeasureOverride(Size available)
    {
        var width = double.IsInfinity(available.Width) ? Math.Max(_owner.ActualWidth, 400) : available.Width;
        var height = double.IsInfinity(available.Height) ? 400 : available.Height;
        var c = _owner.Controller;

        _owner.ResolveLayout(width);
        var extent = new Size(Math.Max(width, _owner.ContentWidth), c.TotalHeight);
        var viewport = new Size(width, height);
        if (extent != _extent || viewport != _viewport)
        {
            _extent = extent;
            _viewport = viewport;
            _offset = new Vector(Math.Clamp(_offset.X, 0, Math.Max(0, extent.Width - width)), Math.Clamp(_offset.Y, 0, Math.Max(0, extent.Height - height)));
            ScrollOwner?.InvalidateScrollInfo();
            _owner.OnHorizontalOffset(_offset.X);
        }

        _window = c.Window(_offset.Y, height, Overscan);
        var keep = new HashSet<int>();
        for (var i = _window.First; i < _window.First + _window.Count; i++)
        {
            keep.Add(i);
            if (!_realized.TryGetValue(i, out var view))
            {
                view = _pool.Count > 0 ? _pool.Pop() : new GridRowView(_owner);
                if (!Children.Contains(view)) Children.Add(view);
                view.Visibility = Visibility.Visible;
                _realized[i] = view;
                view.Bind(i);
            }
            else if (view.IsStale)
            {
                view.Bind(i);
            }
            view.Measure(new Size(width, c.HeightOf(i)));
        }

        foreach (var (index, view) in _realized.Where(kv => !keep.Contains(kv.Key)).ToList())
        {
            _realized.Remove(index);
            view.Unbind();
            view.Visibility = Visibility.Collapsed;
            _pool.Push(view);
        }

        _shadows.Measure();
        if (c.IsServer && _window.Count > 0)
            _ = c.EnsureLoadedAsync(_window.First, _window.Count);

        return viewport;
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var c = _owner.Controller;
        foreach (var (index, view) in _realized)
            view.Arrange(new Rect(0, c.OffsetOf(index) - _offset.Y, finalSize.Width, c.HeightOf(index)));
        foreach (var view in _pool)
            view.Arrange(new Rect(0, 0, 0, 0));
        _shadows.Arrange(_owner, finalSize);
        return finalSize;
    }

    // ---- IScrollInfo ----

    public bool CanVerticallyScroll { get; set; } = true;
    public bool CanHorizontallyScroll { get; set; } = true;
    public double ExtentWidth => _extent.Width;
    public double ExtentHeight => _extent.Height;
    public double ViewportWidth => _viewport.Width;
    public double ViewportHeight => _viewport.Height;
    public double HorizontalOffset => _offset.X;
    public double VerticalOffset => _offset.Y;
    public ScrollViewer? ScrollOwner { get; set; }

    private double Line => _owner.Controller.RowHeight;

    public void LineUp() => SetVerticalOffset(VerticalOffset - Line);
    public void LineDown() => SetVerticalOffset(VerticalOffset + Line);
    public void LineLeft() => SetHorizontalOffset(HorizontalOffset - 40);
    public void LineRight() => SetHorizontalOffset(HorizontalOffset + 40);
    public void PageUp() => SetVerticalOffset(VerticalOffset - ViewportHeight);
    public void PageDown() => SetVerticalOffset(VerticalOffset + ViewportHeight);
    public void PageLeft() => SetHorizontalOffset(HorizontalOffset - ViewportWidth);
    public void PageRight() => SetHorizontalOffset(HorizontalOffset + ViewportWidth);
    public void MouseWheelUp() => SetVerticalOffset(VerticalOffset - Line * 3);
    public void MouseWheelDown() => SetVerticalOffset(VerticalOffset + Line * 3);
    public void MouseWheelLeft() => SetHorizontalOffset(HorizontalOffset - 40);
    public void MouseWheelRight() => SetHorizontalOffset(HorizontalOffset + 40);

    public void SetHorizontalOffset(double offset)
    {
        offset = Math.Clamp(offset, 0, Math.Max(0, ExtentWidth - ViewportWidth));
        if (Math.Abs(offset - _offset.X) < 0.5) return;
        _offset.X = offset;
        ScrollOwner?.InvalidateScrollInfo();
        _owner.OnHorizontalOffset(offset);
        foreach (var row in _realized.Values) row.InvalidateMeasure();
        InvalidateMeasure();
    }

    public void SetVerticalOffset(double offset)
    {
        offset = Math.Clamp(offset, 0, Math.Max(0, ExtentHeight - ViewportHeight));
        if (Math.Abs(offset - _offset.Y) < 0.5) return;
        _offset.Y = offset;
        ScrollOwner?.InvalidateScrollInfo();
        InvalidateMeasure();
    }

    public Rect MakeVisible(Visual visual, Rect rectangle) => rectangle;
}

/// <summary>Clipping host for a row's scrolling (unpinned) cells, so they slide beneath pinned columns.</summary>
internal sealed class GridLane : Panel
{
    public GridLane()
    {
        ClipToBounds = true;
        SnapsToDevicePixels = true;
    }

    /// <summary>X of the lane's left edge in row coordinates.</summary>
    public double Origin { get; set; }

    public Func<UIElement, Rect>? Place { get; set; }

    protected override Size MeasureOverride(Size availableSize)
    {
        foreach (UIElement child in Children) child.Measure(availableSize);
        return new Size(0, 0);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        foreach (UIElement child in Children)
        {
            var r = Place?.Invoke(child) ?? Rect.Empty;
            child.Arrange(r.IsEmpty ? new Rect() : new Rect(r.X - Origin, 0, r.Width, finalSize.Height));
        }
        return finalSize;
    }
}

/// <summary>One realised row: chrome cells (selection checkbox, detail toggle), pinned cells, a clipped lane of scrolling
/// cells, or a group / detail / skeleton row. Background states are painted in <see cref="OnRender"/>.</summary>
internal sealed class GridRowView : Panel
{
    private readonly DataGrid _owner;
    private readonly Dictionary<string, GridCellView> _cells = new(StringComparer.Ordinal);
    private readonly GridLane _lane = new();
    private readonly Border _active;
    private CheckBox? _check;
    private Button? _detailToggle;
    private FrameworkElement? _groupContent;
    private ContentPresenter? _detail;
    private string? _boundKey;
    private GridRowKind? _boundKind;
    private bool _boundHasItem;
    private int _layoutVersion = -1;

    public GridRowView(DataGrid owner)
    {
        _owner = owner;
        ClipToBounds = true;
        SnapsToDevicePixels = true;
        _lane.Place = child => child is GridCellView cell && _owner.CurrentLayout.Find(cell.Column.Field) is { } col
            ? new Rect(_owner.ColumnX(col, RenderSize.Width), 0, col.Width, 0)
            : Rect.Empty;
        Children.Add(_lane);
        _active = new Border { BorderThickness = new Thickness(2), IsHitTestVisible = false, Visibility = Visibility.Collapsed };
        _active.SetResourceReference(Border.BorderBrushProperty, GridKeys.ActiveCell);
        Children.Add(_active);
    }

    public int Index { get; private set; } = -1;

    public GridViewRow<object>? Row { get; private set; }

    public bool IsHover { get; private set; }

    public IEnumerable<GridCellView> Cells => _cells.Values;

    public GridCellView? Cell(string field) => _cells.GetValueOrDefault(field);

    public void Invalidate() => _boundKey = null;

    /// <summary>The view must rebind before it is measured (its row changed or the view was invalidated).</summary>
    public bool IsStale => _boundKey is null;

    public void Unbind()
    {
        if (_cells.Values.Any(c => c.IsEditing)) _owner.OnEditorRecycled();
        Index = -1;
        Row = null;
        _boundKey = null;
    }

    /// <summary>Points the view at a row index; rebuilds children only when the row kind or column set changed.</summary>
    public void Bind(int index)
    {
        var row = _owner.Controller.RowAt(index);
        if (Index != index && _cells.Values.Any(c => c.IsEditing)) _owner.OnEditorRecycled();
        Index = index;
        Row = row;
        var key = row is null ? null : row.Key + (row.Item is null ? "#ph" : "") + (row.Expanded ? "+" : "-");
        var sameStructure = row?.Kind == _boundKind && _layoutVersion == _owner.LayoutVersion;
        // Recycling: a data row view rebinds to another data row by updating its cells in place.
        var recycle = sameStructure && row is { Kind: GridRowKind.Data, Item: not null } && _boundHasItem;
        if ((key == _boundKey && sameStructure) || recycle)
        {
            _boundKey = key;
            Refresh();
            return;
        }

        _boundKey = key;
        _boundKind = row?.Kind;
        _boundHasItem = row?.Item is not null;
        _layoutVersion = _owner.LayoutVersion;
        Rebuild();
    }

    /// <summary>Updates cell values and states without changing structure.</summary>
    public void Refresh()
    {
        if (Row is { Kind: GridRowKind.Data, Item: { } item } row)
        {
            foreach (var cell in _cells.Values) cell.Update(item, row);
            if (_check is not null) _check.IsChecked = _owner.Controller.IsSelected(row.Key);
            if (_detailToggle is not null) GridCellFactory.SetChevron(_detailToggle, _owner.Controller.IsDetailOpen(row.Key));
        }
        else if (Row is { Kind: GridRowKind.Group } group)
        {
            foreach (var cell in _cells.Values) cell.UpdateAggregate(group);
        }
        UpdateActive();
        InvalidateMeasure();
        InvalidateArrange();
        InvalidateVisual();
    }

    private void RemoveCell(string field)
    {
        var cell = _cells[field];
        if (cell.Parent is Panel p) p.Children.Remove(cell);
        _cells.Remove(field);
    }

    private void Rebuild()
    {
        foreach (var field in _cells.Keys.ToList()) RemoveCell(field);
        if (_check is not null) { Children.Remove(_check); _check = null; }
        if (_detailToggle is not null) { Children.Remove(_detailToggle); _detailToggle = null; }
        if (_groupContent is not null) { Children.Remove(_groupContent); _groupContent = null; }
        if (_detail is not null) { Children.Remove(_detail); _detail = null; }

        if (Row is not { } row)
            return;

        switch (row.Kind)
        {
            case GridRowKind.Group:
                _groupContent = GridCellFactory.GroupContent(_owner, row);
                Children.Insert(0, _groupContent);
                foreach (var col in _owner.CurrentLayout.Columns)
                {
                    if (col.Column.Aggregate == GridAggregate.None || row.Aggregates is null || !row.Aggregates.ContainsKey(col.Field)) continue;
                    var cell = new GridCellView(_owner, col.Column, aggregate: true);
                    _cells[col.Field] = cell;
                    if (col.Pin == GridPin.None) _lane.Children.Add(cell);
                    else Children.Insert(Children.IndexOf(_active), cell);
                }
                break;
            case GridRowKind.Detail:
                _detail = new ContentPresenter { Content = row.Item, ContentTemplate = _owner.RowDetail };
                Children.Insert(0, _detail);
                break;
            default:
                if (row.Item is null) break; // server placeholder: skeleton bars painted in OnRender
                if (_owner.ShowSelectionColumn)
                {
                    _check = new CheckBox { Focusable = false, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
                    _check.Click += (_, _) => { if (Index >= 0) _owner.OnRowCheckboxClick(Index); };
                    System.Windows.Automation.AutomationProperties.SetName(_check, "Select row");
                    Children.Insert(0, _check);
                }
                if (_owner.RowDetail is not null)
                {
                    _detailToggle = GridCellFactory.ChevronButton("Toggle details");
                    _detailToggle.Click += (_, e) =>
                    {
                        e.Handled = true;
                        if (Row is { } r) _owner.Controller.ToggleDetail(r.Key);
                    };
                    Children.Insert(0, _detailToggle);
                }
                SyncColumns();
                break;
        }

        Refresh();
    }

    /// <summary>Column virtualisation: realise cells for pinned columns and the visible scrolling range.</summary>
    private void SyncColumns()
    {
        var layout = _owner.CurrentLayout;
        var (first, last) = layout.ScrollingRange(_owner.HorizontalOffset, _owner.BodyWidth);
        var wanted = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < layout.Columns.Count; i++)
        {
            var col = layout.Columns[i];
            if (col.Pin != GridPin.None || (first >= 0 && i >= first && i <= last)) wanted.Add(col.Field);
        }

        foreach (var field in _cells.Keys.Where(f => !wanted.Contains(f)).ToList()) RemoveCell(field);

        var added = false;
        foreach (var col in layout.Columns)
        {
            if (!wanted.Contains(col.Field) || _cells.ContainsKey(col.Field)) continue;
            var cell = new GridCellView(_owner, col.Column);
            _cells[col.Field] = cell;
            if (col.Pin == GridPin.None) _lane.Children.Add(cell);
            else Children.Insert(Children.IndexOf(_active), cell);
            added = true;
        }

        if (added && Row is { Kind: GridRowKind.Data, Item: { } item } row)
            foreach (var cell in _cells.Values) cell.Update(item, row);
    }

    internal void UpdateActive()
    {
        var show = _owner.ShowActiveCell && Row is not null && _owner.Controller.Active.Row == Index;
        _active.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        InvalidateArrange();
    }

    protected override Size MeasureOverride(Size available)
    {
        if (Row is { Kind: GridRowKind.Data, Item: not null }) SyncColumns();
        var h = double.IsInfinity(available.Height) ? _owner.Controller.RowHeight : available.Height;
        var w = double.IsInfinity(available.Width) ? _owner.BodyWidth : available.Width;
        foreach (UIElement child in Children)
            child.Measure(new Size(w, h));
        return new Size(w, h);
    }

    protected override Size ArrangeOverride(Size size)
    {
        var h = size.Height;
        var x = 0.0;
        if (_owner.ShowSelectionColumn)
        {
            _check?.Arrange(new Rect(0, 0, DataGrid.CheckColumnWidth, h));
            x += DataGrid.CheckColumnWidth;
        }
        if (_owner.RowDetail is not null)
        {
            _detailToggle?.Arrange(new Rect(x + 4, (h - 24) / 2, 24, 24));
            x += DataGrid.DetailColumnWidth;
        }

        _groupContent?.Arrange(new Rect(0, 0, size.Width, h));
        _detail?.Arrange(new Rect(_owner.Prefix, 0, Math.Max(0, size.Width - _owner.Prefix), h));

        var lane = _owner.LaneBounds(size.Width);
        _lane.Origin = lane.Left;
        _lane.InvalidateArrange(); // cells move inside the lane on scroll even when the lane's own rect doesn't change
        _lane.Arrange(new Rect(lane.Left, 0, lane.Width, h));

        foreach (var (field, cell) in _cells)
        {
            if (cell.Parent == _lane) continue;
            if (_owner.CurrentLayout.Find(field) is not { } col) { cell.Arrange(new Rect()); continue; }
            cell.Arrange(new Rect(_owner.ColumnX(col, size.Width), 0, col.Width, h));
        }

        if (_active.Visibility == Visibility.Visible && Row is { } row)
        {
            var layout = _owner.CurrentLayout;
            var activeCol = _owner.Controller.Active.Column;
            if (row.Kind != GridRowKind.Data || activeCol >= layout.Columns.Count)
                _active.Arrange(new Rect(0, 0, size.Width, h));
            else
            {
                var col = layout.Columns[activeCol];
                _active.Arrange(new Rect(_owner.ColumnX(col, size.Width), 0, col.Width, h));
            }
        }
        else
        {
            _active.Arrange(new Rect());
        }
        return size;
    }

    protected override void OnRender(DrawingContext dc)
    {
        var rect = new Rect(RenderSize);
        if (Row is not { } row) return;
        var c = _owner.Controller;

        dc.DrawRectangle(_owner.TokenSurface, null, rect);
        if (row.Kind == GridRowKind.Group) dc.DrawRectangle(_owner.TokenGroup, null, rect);
        else if (_owner.Striped && Index % 2 == 1) dc.DrawRectangle(_owner.TokenStripe, null, rect);

        if (row is { Kind: GridRowKind.Data, Item: { } item })
        {
            if (c.RowTone?.Invoke(item) is { } tone && _owner.FindBrush(GridKeys.ToneBackground(tone)) is { } toneBrush)
                dc.DrawRectangle(toneBrush, null, rect);
            if (IsHover) dc.DrawRectangle(_owner.TokenHover, null, rect);
            if (c.IsSelected(row.Key))
            {
                dc.DrawRectangle(_owner.TokenSelected, null, rect);
                dc.DrawRectangle(_owner.TokenIndicator, null, new Rect(0, 0, 2, rect.Height));
            }
            var flash = c.FlashStrength(row.Key, Environment.TickCount64);
            if (flash > 0)
            {
                var brush = _owner.TokenSelected.CloneCurrentValue();
                brush.Opacity = Math.Min(1, flash * 2.5);
                dc.DrawRectangle(brush, null, rect);
            }
        }
        else if (row is { Kind: GridRowKind.Data, Item: null })
        {
            // Server placeholder: skeleton bars for every visible column.
            foreach (var col in _owner.CurrentLayout.Columns)
            {
                var x = _owner.ColumnX(col, rect.Width) + _owner.TokenPadding;
                var w = Math.Max(0, (col.Width - 2 * _owner.TokenPadding) * (0.45 + (Index * 7 + col.Index * 13) % 40 / 100.0));
                if (x + w < 0 || x > rect.Width) continue;
                dc.DrawRoundedRectangle(_owner.TokenSkeleton, null, new Rect(x, rect.Height / 2 - 5, w, 10), 4, 4);
            }
        }
        else if (IsHover && row.Kind == GridRowKind.Group)
        {
            dc.DrawRectangle(_owner.TokenHover, null, rect);
        }

        var pen = new Pen(_owner.TokenBorder, 1);
        dc.DrawLine(pen, new Point(0, rect.Height - 0.5), new Point(rect.Width, rect.Height - 0.5));
        if (_owner.Bordered && row.Kind == GridRowKind.Data)
        {
            var lane = _owner.LaneBounds(rect.Width);
            foreach (var col in _owner.CurrentLayout.Columns)
            {
                var x = _owner.ColumnX(col, rect.Width) + col.Width - 0.5;
                if (col.Pin == GridPin.None && (x < lane.Left || x > lane.Right)) continue;
                dc.DrawLine(pen, new Point(x, 0), new Point(x, rect.Height));
            }
        }
    }

    protected override void OnMouseEnter(MouseEventArgs e)
    {
        base.OnMouseEnter(e);
        IsHover = true;
        InvalidateVisual();
    }

    protected override void OnMouseLeave(MouseEventArgs e)
    {
        base.OnMouseLeave(e);
        IsHover = false;
        InvalidateVisual();
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        if (Index < 0 || e.Handled) return;
        _owner.OnRowMouseDown(this, e);
    }

    protected override void OnMouseRightButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseRightButtonUp(e);
        if (Index >= 0 && Row is { Kind: GridRowKind.Data, Item: not null } row && _owner.SelectionMode != GridSelectionMode.None && !_owner.Controller.IsSelected(row.Key))
            _owner.Controller.Click(Index);
    }

    protected override System.Windows.Automation.Peers.AutomationPeer OnCreateAutomationPeer() => new GridRowAutomationPeer(this, _owner);

    /// <summary>The column index (among visible columns) at an x position in this row, or -1 in chrome.</summary>
    internal int ColumnAt(double x)
    {
        if (x < _owner.Prefix) return -1;
        var layout = _owner.CurrentLayout;
        foreach (var pinnedPass in new[] { true, false })
        {
            foreach (var col in layout.Columns)
            {
                if ((col.Pin != GridPin.None) != pinnedPass) continue;
                var left = _owner.ColumnX(col, RenderSize.Width);
                if (x >= left && x < left + col.Width) return col.Index;
            }
        }
        return -1;
    }
}

/// <summary>A cell: hosts the type-specific content (or an inline editor) with the column's padding.</summary>
internal sealed class GridCellView : Decorator
{
    private readonly DataGrid _owner;
    private readonly bool _aggregate;
    private FrameworkElement? _content;
    private FrameworkElement? _editor;

    public GridCellView(DataGrid owner, GridColumn<object> column, bool aggregate = false)
    {
        _owner = owner;
        _aggregate = aggregate;
        Column = column;
        ClipToBounds = true;
        SnapsToDevicePixels = true;
    }

    public GridColumn<object> Column { get; }

    public bool IsEditing => _editor is not null;

    public string? Error { get; private set; }

    /// <summary>The displayed text (for automation and tests).</summary>
    public string Text { get; private set; } = "";

    public void Update(object item, GridViewRow<object> row)
    {
        if (_editor is not null) return;
        var def = _owner.ColumnDefinition(Column.Field);
        Text = _owner.Controller.Text(item, row.Key, Column);
        _content = GridCellFactory.Update(_owner, Column, def, _content, item, row);
        if (_content is TextBlock tb)
            tb.FontStyle = _owner.Controller.Edit.HasPending(row.Key, Column.Field) ? FontStyles.Italic : FontStyles.Normal;

        FrameworkElement shown = _content;
        if (_owner.ChildrenSelector is not null && _owner.CurrentLayout.Columns.Count > 0 && _owner.CurrentLayout.Columns[0].Field == Column.Field)
        {
            Child = null;
            shown = GridCellFactory.TreeWrap(_owner, _content, row);
        }
        if (!ReferenceEquals(Child, shown))
        {
            Child = null;
            Child = shown;
        }
    }

    public void UpdateAggregate(GridViewRow<object> row)
    {
        var value = row.Aggregates?.GetValueOrDefault(Column.Field);
        var tb = _content as TextBlock ?? GridCellFactory.Text(_owner, Column, mono: true);
        _content = tb;
        tb.Text = Text = GridCellFactory.AggregateText(Column, value);
        tb.FontWeight = FontWeights.SemiBold;
        if (!ReferenceEquals(Child, tb)) Child = tb;
    }

    /// <summary>Swaps the content for an inline editor.</summary>
    public void ShowEditor(FrameworkElement editor)
    {
        _editor = editor;
        Child = editor;
        InvalidateMeasure();
    }

    public void HideEditor()
    {
        _editor = null;
        Child = null;
        SetError(null);
        InvalidateMeasure();
    }

    public void SetError(string? error)
    {
        Error = error;
        ToolTip = error;
        InvalidateVisual();
    }

    protected override System.Windows.Automation.Peers.AutomationPeer OnCreateAutomationPeer() => new GridCellAutomationPeer(this, _owner);

    protected override Size MeasureOverride(Size constraint)
    {
        var width = _owner.CurrentLayout.Find(Column.Field)?.Width ?? 0;
        var h = double.IsInfinity(constraint.Height) ? _owner.Controller.RowHeight : constraint.Height;
        Child?.Measure(new Size(Math.Max(0, width - 2 * _owner.TokenPadding), h));
        return new Size(0, 0);
    }

    protected override Size ArrangeOverride(Size size)
    {
        if (Child is not null)
        {
            var pad = _owner.TokenPadding;
            var inner = _editor is not null
                ? new Rect(2, 2, Math.Max(0, size.Width - 4), Math.Max(0, size.Height - 4))
                : new Rect(pad, 0, Math.Max(0, size.Width - 2 * pad), size.Height);
            Child.Arrange(inner);
        }
        return size;
    }

    protected override void OnRender(DrawingContext dc)
    {
        var rect = new Rect(RenderSize);
        if (_editor is not null)
            dc.DrawRectangle(_owner.TokenSurface, null, rect);
        if (Error is not null)
            dc.DrawRectangle(null, new Pen(_owner.TokenDanger, 2), new Rect(1, 1, Math.Max(0, rect.Width - 2), Math.Max(0, rect.Height - 2)));
    }
}


/// <summary>Builds and updates cell content per column type.</summary>
internal static class GridCellFactory
{
    public static TextBlock Text(DataGrid owner, GridColumn<object> column, bool mono = false)
    {
        var tb = new TextBlock
        {
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
            TextAlignment = column.EffectiveAlign switch { GridAlign.End => TextAlignment.Right, GridAlign.Center => TextAlignment.Center, _ => TextAlignment.Left },
        };
        tb.SetResourceReference(TextBlock.ForegroundProperty, GridKeys.Text);
        if (mono || column.Type is GridColumnType.Number or GridColumnType.Date)
            tb.SetResourceReference(TextBlock.FontFamilyProperty, GridKeys.MonoFont);
        return tb;
    }

    /// <summary>Returns the content element for a cell, reusing <paramref name="current"/> when it fits.</summary>
    public static FrameworkElement Update(DataGrid owner, GridColumn<object> column, GridColumn? def, FrameworkElement? current, object item, GridViewRow<object> row)
    {
        var c = owner.Controller;
        var value = c.Value(item, row.Key, column);

        if (def?.Template is { } template)
        {
            var presenter = current as ContentPresenter ?? new ContentPresenter { VerticalAlignment = VerticalAlignment.Center };
            presenter.ContentTemplate = template;
            presenter.Content = item;
            return presenter;
        }

        switch (column.Type)
        {
            case GridColumnType.Boolean:
            {
                var icon = current as Icon ?? new Icon { Size = 16, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Left };
                icon.SetResourceReference(Icon.ForegroundProperty, GridKeys.AccentText);
                icon.Kind = GridValues.ToBool(value) == true ? "check" : null;
                return icon;
            }
            case GridColumnType.Enum:
            {
                var text = column.DisplayText(value);
                var badge = current as Badge ?? new Badge { Size = ControlSize.Small, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Left };
                badge.Content = text;
                badge.Tone = column.EnumTones?.GetValueOrDefault(text, Tone.Neutral) ?? Tone.Neutral;
                badge.Visibility = text.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
                return badge;
            }
            case GridColumnType.Progress:
            {
                var bar = current as GridProgressCell ?? new GridProgressCell(owner);
                bar.Value = GridValues.ToDouble(value);
                bar.Text = column.DisplayText(value);
                bar.Tone = def?.CellTone?.Invoke(item);
                bar.InvalidateVisual();
                return bar;
            }
            case GridColumnType.Sparkline:
            {
                var spark = current as GridSparklineCell ?? new GridSparklineCell(owner);
                spark.Values = value is IEnumerable e and not string ? e.Cast<object?>().Select(v => GridValues.ToDouble(v) ?? 0).ToArray() : [];
                spark.InvalidateVisual();
                return spark;
            }
            case GridColumnType.Actions:
            {
                if (current is Button existing) { existing.Tag = item; return existing; }
                var button = new Button { Tag = item, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Left, Focusable = false };
                Sl.SetVariant(button, ButtonVariant.Ghost);
                Sl.SetSize(button, ControlSize.Small);
                Sl.SetIconOnly(button, true);
                Sl.SetStartIcon(button, "more-horizontal");
                Sl.SetLabel(button, "Row actions");
                button.Click += (_, e) =>
                {
                    e.Handled = true;
                    OpenActions(def, button);
                };
                return button;
            }
            default:
            {
                var tb = current as TextBlock ?? Text(owner, column);
                tb.Text = column.DisplayText(value);
                tb.FontStyle = FontStyles.Normal;
                if (def?.CellTone?.Invoke(item) is { } tone)
                    tb.SetResourceReference(TextBlock.ForegroundProperty, GridKeys.ToneForeground(tone));
                else
                    tb.SetResourceReference(TextBlock.ForegroundProperty, GridKeys.Text);
                return tb;
            }
        }
    }

    /// <summary>Opens a row's actions: the column's <see cref="GridColumn.Actions"/> as a menu, or its XAML ActionsMenu.</summary>
    public static void OpenActions(GridColumn? def, FrameworkElement anchor)
    {
        if (def is null || anchor.Tag is not { } item) return;
        ContextMenu? menu = null;
        if (def.Actions is { } actions)
        {
            menu = new ContextMenu();
            foreach (var action in actions(item))
            {
                var menuItem = new MenuItem { Header = action.Label, InputGestureText = action.Shortcut ?? "" };
                if (action.Icon is not null) menuItem.Icon = new Icon { Kind = action.Icon, Size = 16 };
                if (action.Tone != Tone.Neutral) menuItem.SetResourceReference(Control.ForegroundProperty, GridKeys.ToneForeground(action.Tone));
                var invoke = action.Invoke;
                menuItem.Click += (_, _) => invoke();
                menu.Items.Add(menuItem);
            }
        }
        else if (def.ActionsMenu is { } xamlMenu)
        {
            menu = xamlMenu;
            menu.DataContext = item;
        }
        if (menu is null || menu.Items.Count == 0) return;
        menu.PlacementTarget = anchor;
        menu.Placement = PlacementMode.Bottom;
        menu.IsOpen = true;
    }

    public static string AggregateText(GridColumn<object> column, object? value)
    {
        var symbol = column.Aggregate switch
        {
            GridAggregate.Sum => "Σ ",
            GridAggregate.Avg => "avg ",
            GridAggregate.Min => "min ",
            GridAggregate.Max => "max ",
            GridAggregate.Count => "n ",
            _ => "",
        };
        var text = column.Aggregate == GridAggregate.Count
            ? Convert.ToString(value, CultureInfo.InvariantCulture) ?? ""
            : column.DisplayText(value);
        return symbol + text;
    }

    public static Button ChevronButton(string label)
    {
        var b = new Button { Focusable = false, Width = 24, Height = 24 };
        Sl.SetVariant(b, ButtonVariant.Ghost);
        Sl.SetSize(b, ControlSize.Small);
        Sl.SetIconOnly(b, true);
        Sl.SetLabel(b, label);
        SetChevron(b, false);
        return b;
    }

    public static void SetChevron(Button b, bool open) => Sl.SetStartIcon(b, open ? "chevron-down" : "chevron-right");

    /// <summary>Group row: chevron, "Column: value", row count, all as one full-width row.</summary>
    public static FrameworkElement GroupContent(DataGrid owner, GridViewRow<object> row)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        panel.Margin = new Thickness(owner.Prefix + 8 + row.Depth * 20, 0, 0, 0);
        var chevron = new Icon { Kind = row.Expanded ? "chevron-down" : "chevron-right", Size = 14, Margin = new Thickness(0, 0, 8, 0) };
        chevron.SetResourceReference(Icon.ForegroundProperty, GridKeys.TextSecondary);
        panel.Children.Add(chevron);

        var column = owner.Controller.Column(row.GroupField ?? "");
        var title = new TextBlock { Text = (column?.DisplayTitle ?? row.GroupField) + ": ", VerticalAlignment = VerticalAlignment.Center };
        title.SetResourceReference(TextBlock.ForegroundProperty, GridKeys.TextSecondary);
        panel.Children.Add(title);

        var valueText = string.IsNullOrEmpty(row.GroupKeyText) ? "(empty)" : row.GroupKeyText;
        if (column?.Type == GridColumnType.Enum)
        {
            panel.Children.Add(new Badge
            {
                Content = valueText,
                Size = ControlSize.Small,
                Tone = column.EnumTones?.GetValueOrDefault(row.GroupKeyText ?? "", Tone.Neutral) ?? Tone.Neutral,
                VerticalAlignment = VerticalAlignment.Center,
            });
        }
        else
        {
            var value = new TextBlock { Text = valueText, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center };
            value.SetResourceReference(TextBlock.ForegroundProperty, GridKeys.Text);
            panel.Children.Add(value);
        }

        var count = new TextBlock { Text = "  " + row.RowCount.ToString("N0", CultureInfo.CurrentCulture), VerticalAlignment = VerticalAlignment.Center };
        count.SetResourceReference(TextBlock.ForegroundProperty, GridKeys.TextTertiary);
        count.SetResourceReference(TextBlock.FontFamilyProperty, GridKeys.MonoFont);
        panel.Children.Add(count);
        return panel;
    }

    /// <summary>Tree data: indents the first cell and adds an expander for nodes with children.</summary>
    public static FrameworkElement TreeWrap(DataGrid owner, FrameworkElement content, GridViewRow<object> row)
    {
        if (content.Parent is Panel old) old.Children.Remove(content);
        var dock = new DockPanel { LastChildFill = true };
        var indent = new Border { Width = row.Depth * 18 };
        DockPanel.SetDock(indent, Dock.Left);
        dock.Children.Add(indent);
        FrameworkElement expander;
        if (row.HasChildren)
        {
            var b = ChevronButton(row.Expanded ? "Collapse" : "Expand");
            SetChevron(b, row.Expanded);
            b.Margin = new Thickness(-6, 0, 2, 0);
            var index = row.Index;
            b.Click += (_, e) => { e.Handled = true; owner.Controller.ToggleExpand(index); };
            expander = b;
        }
        else
        {
            expander = new Border { Width = 20 };
        }
        DockPanel.SetDock(expander, Dock.Left);
        dock.Children.Add(expander);
        dock.Children.Add(content);
        return dock;
    }
}

/// <summary>Progress cell: a slim bar plus its formatted value.</summary>
internal sealed class GridProgressCell(DataGrid owner) : FrameworkElement
{
    public double? Value { get; set; }
    public string Text { get; set; } = "";
    public Tone? Tone { get; set; }

    protected override Size MeasureOverride(Size availableSize) => new(0, 0);

    protected override void OnRender(DrawingContext dc)
    {
        var w = RenderSize.Width;
        var h = RenderSize.Height;
        var textWidth = 44.0;
        var barWidth = Math.Max(0, w - textWidth - 8);
        var barRect = new Rect(0, h / 2 - 3, barWidth, 6);
        dc.DrawRoundedRectangle(owner.TokenTrack, null, barRect, 3, 3);
        if (Value is { } v)
        {
            var fraction = Math.Clamp(v > 1 ? v / 100 : v, 0, 1);
            var fill = Tone is { } t ? owner.FindBrush(GridKeys.ToneSolid(t)) ?? owner.TokenFill : owner.TokenFill;
            dc.DrawRoundedRectangle(fill, null, new Rect(barRect.X, barRect.Y, barRect.Width * fraction, barRect.Height), 3, 3);
        }
        var ft = new FormattedText(Text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
            new Typeface(owner.TokenMono, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal), 12, owner.TokenText, VisualTreeHelper.GetDpi(this).PixelsPerDip);
        dc.DrawText(ft, new Point(w - ft.Width, (h - ft.Height) / 2));
    }
}

/// <summary>Sparkline cell: mini bars, the last one in the accent colour.</summary>
internal sealed class GridSparklineCell(DataGrid owner) : FrameworkElement
{
    public double[] Values { get; set; } = [];

    protected override Size MeasureOverride(Size availableSize) => new(0, 0);

    protected override void OnRender(DrawingContext dc)
    {
        if (Values.Length == 0) return;
        var w = RenderSize.Width;
        var h = RenderSize.Height - 10;
        var max = Math.Max(Values.Max(), 1e-9);
        var min = Math.Min(0, Values.Min());
        var gap = 2.0;
        var barW = Math.Max(1, (w - gap * (Values.Length - 1)) / Values.Length);
        for (var i = 0; i < Values.Length; i++)
        {
            var frac = (Values[i] - min) / (max - min);
            var bh = Math.Max(1, frac * h);
            var brush = i == Values.Length - 1 ? owner.TokenFill : owner.TokenTrack;
            dc.DrawRoundedRectangle(brush, null, new Rect(i * (barW + gap), 5 + h - bh, barW, bh), 1, 1);
        }
    }
}
