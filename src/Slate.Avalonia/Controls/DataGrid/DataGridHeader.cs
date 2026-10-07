using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Slate.Data;

namespace Slate.Avalonia.Controls;

/// <summary>The sticky header: select-all checkbox, then one <see cref="DataGridHeaderCell"/> per visible column.</summary>
public class DataGridHeaderRow : Panel
{
    private readonly Dictionary<string, DataGridHeaderCell> _cells = new(StringComparer.Ordinal);
    private CheckBox? _selectAll;
    private Border? _dropIndicator;

    internal DataGrid? Owner { get; set; }

    public DataGridHeaderRow()
    {
        Classes.Add("sl-grid-header");
        ClipToBounds = true;
    }

    public IReadOnlyDictionary<string, DataGridHeaderCell> Cells => _cells;

    public CheckBox? SelectAll => _selectAll;

    internal void Rebuild()
    {
        if (Owner is not { } grid) return;
        Height = grid.HeaderHeight;

        if (grid.SelectionMode == GridSelectionMode.Multi)
        {
            if (_selectAll is null)
            {
                _selectAll = new CheckBox { Classes = { "sl-grid-select" }, IsThreeState = true, Focusable = true, MinWidth = 0, Padding = default };
                AutomationProperties.SetName(_selectAll, "Select all rows");
                _selectAll.Click += (_, e) => { grid.ToggleSelectAll(); e.Handled = true; };
                Children.Add(_selectAll);
            }
            _selectAll.IsChecked = grid.HeaderSelectionState switch { SelectAllState.All => true, SelectAllState.None => false, _ => null };
        }
        else if (_selectAll is not null)
        {
            Children.Remove(_selectAll);
            _selectAll = null;
        }

        var fields = grid.Layout.Columns.Select(c => c.Field).ToHashSet(StringComparer.Ordinal);
        foreach (var field in _cells.Keys.Where(f => !fields.Contains(f)).ToList())
        {
            Children.Remove(_cells[field]);
            _cells.Remove(field);
        }
        foreach (var col in grid.Layout.Columns)
        {
            if (!_cells.TryGetValue(col.Field, out var cell))
            {
                cell = new DataGridHeaderCell(grid, col.Field);
                _cells[col.Field] = cell;
                Children.Add(cell);
            }
            cell.Update(col);
        }

        _dropIndicator ??= new Border { Classes = { "sl-grid-drop-indicator" }, IsVisible = false, Width = 2, ZIndex = 10 };
        if (!Children.Contains(_dropIndicator)) Children.Add(_dropIndicator);
        InvalidateArrange();
    }

    internal void ShowDropIndicator(double? x)
    {
        if (_dropIndicator is null) return;
        _dropIndicator.IsVisible = x is not null;
        _dropIndicator.Tag = x;
        InvalidateArrange();
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var h = Owner?.HeaderHeight ?? 40;
        foreach (var child in Children)
            child.Measure(new Size(double.PositiveInfinity, h));
        return new Size(0, h);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        if (Owner is not { } grid) return finalSize;
        var h = finalSize.Height;
        var lead = grid.LeadWidth;
        var scrollX = grid.ScrollX;
        var vw = grid.ViewportWidth;
        _selectAll?.Arrange(new Rect(0, 0, DataGrid.SelectionColumnWidth, h));
        foreach (var (field, cell) in _cells)
        {
            var col = grid.Layout.Find(field);
            if (col is null) { cell.Arrange(default); continue; }
            var x = grid.CellX(col, scrollX, vw) - scrollX + lead;
            cell.Arrange(new Rect(x, 0, col.Width, h));
            cell.ZIndex = col.Pin == GridPin.None ? 0 : 2;
        }
        if (_dropIndicator is { IsVisible: true, Tag: double dx })
            _dropIndicator.Arrange(new Rect(dx - 1, 4, 2, h - 8));
        if (_selectAll is not null) _selectAll.ZIndex = 3;
        return finalSize;
    }

    /// <summary>The column insertion index for a header-relative x (drag reorder).</summary>
    internal (int Index, double X) DropTarget(double x)
    {
        var grid = Owner!;
        var cols = grid.Layout.Columns;
        var lead = grid.LeadWidth;
        for (var i = 0; i < cols.Count; i++)
        {
            var left = grid.CellX(cols[i], grid.ScrollX, grid.ViewportWidth) - grid.ScrollX + lead;
            if (x < left + cols[i].Width / 2)
                return (grid.Layout.Order.ToList().IndexOf(cols[i].Field), left);
        }
        var lastCol = cols[^1];
        return (grid.Layout.Order.Count, grid.CellX(lastCol, grid.ScrollX, grid.ViewportWidth) - grid.ScrollX + lead + lastCol.Width);
    }
}

/// <summary>
/// A column header: click to sort (Shift adds a secondary sort), drag to reorder or onto the group bar, drag the edge
/// to resize (double-click it to auto-fit), filter popover and the column menu.
/// </summary>
public class DataGridHeaderCell : Border
{
    private readonly DataGrid _grid;
    private readonly TextBlock _title = new() { VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly Icon _sortIcon = new() { Size = 14, VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _sortOrder = new() { Classes = { "sl-grid-sort-order" }, VerticalAlignment = VerticalAlignment.Center };
    private readonly Button _filterButton = new() { Classes = { "ghost", "sl-grid-header-button" }, Focusable = false };
    private readonly Popover _filter = new() { Placement = PopoverPlacement.BottomStart };
    private readonly Button _menuButton = new() { Classes = { "ghost", "sl-grid-header-button" }, Focusable = false };
    private readonly Menu _menu = new() { Placement = PopoverPlacement.BottomEnd };
    private readonly Border _grip = new() { Classes = { "sl-grid-resize-grip" }, Width = 8, Cursor = new Cursor(StandardCursorType.SizeWestEast), Background = Brushes.Transparent };
    private Point? _pressAt;
    private bool _dragging;
    private double _resizeStart;
    private double _resizeOrigin;

    internal DataGridHeaderCell(DataGrid grid, string field)
    {
        _grid = grid;
        Field = field;
        Classes.Add("sl-grid-header-cell");
        Focusable = true;
        ClipToBounds = true;

        _filterButton.Content = new Icon { Kind = "filter", Size = 13 };
        AutomationProperties.SetName(_filterButton, "Filter");
        _filter.Anchor = _filterButton;
        _filter.OpenChanged += (_, _) => { if (_filter.Open) _filter.Content = new DataGridFilterEditor(_grid, Field, () => _filter.Open = false); };

        _menuButton.Content = new Icon { Kind = "more-horizontal", Size = 14 };
        AutomationProperties.SetName(_menuButton, "Column menu");
        _menu.Trigger = _menuButton;
        _menu.MenuPopup.Opening += (_, _) => BuildMenu();
        BuildMenu();

        var grid2 = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto,Auto,Auto") };
        grid2.Children.Add(_title);
        Grid.SetColumn(_sortIcon, 1);
        Grid.SetColumn(_sortOrder, 2);
        Grid.SetColumn(_filter, 3);
        Grid.SetColumn(_menu, 4);
        grid2.Children.Add(_sortIcon);
        grid2.Children.Add(_sortOrder);
        grid2.Children.Add(_filter);
        grid2.Children.Add(_menu);

        var root = new Panel();
        root.Children.Add(grid2);
        _grip.HorizontalAlignment = HorizontalAlignment.Right;
        _grip.Margin = new Thickness(0, 0, -4, 0);
        root.Children.Add(_grip);
        Child = root;

        _grip.PointerPressed += OnGripPressed;
        _grip.PointerMoved += OnGripMoved;
        _grip.PointerReleased += (_, e) => { e.Pointer.Capture(null); e.Handled = true; };
        _grip.DoubleTapped += (_, e) => { _grid.AutoSizeColumn(Field); e.Handled = true; };

        // Filter and menu affordances appear on hover/focus (and stay while a filter is active), keeping headers calm.
        PointerEntered += (_, _) => { _hover = true; UpdateAffordances(); };
        PointerExited += (_, _) => { _hover = false; UpdateAffordances(); };
        GotFocus += (_, _) => UpdateAffordances();
        LostFocus += (_, _) => UpdateAffordances();
        _filter.OpenChanged += (_, _) => UpdateAffordances();
        UpdateAffordances();

        PointerPressed += OnPressed;
        PointerMoved += OnMoved;
        PointerReleased += OnReleased;
        KeyDown += OnKeyDown;
    }

    public string Field { get; }

    private bool _hover;

    private void UpdateAffordances()
    {
        var show = _hover || IsKeyboardFocusWithin || _filter.Open || Classes.Contains("filtered");
        _filter.Opacity = show ? 1 : 0;
        _menu.Opacity = _hover || IsKeyboardFocusWithin ? 1 : 0;
    }

    public TextBlock Title => _title;
    public Menu ColumnMenu => _menu;
    public Popover FilterPopover => _filter;

    internal void Update(ResolvedColumn<object> col)
    {
        var c = col.Column;
        _title.Text = c.DisplayTitle;
        _title.TextAlignment = c.EffectiveAlign == GridAlign.End ? TextAlignment.Right : TextAlignment.Left;
        var sorts = _grid.State.Sorts;
        var sortIndex = sorts.ToList().FindIndex(s => s.Field == Field);
        var dir = sortIndex >= 0 ? sorts[sortIndex].Direction : (SortDirection?)null;
        _sortIcon.Kind = dir switch { SortDirection.Ascending => "arrow-up", SortDirection.Descending => "arrow-down", _ => null };
        _sortIcon.IsVisible = dir is not null;
        _sortOrder.Text = sorts.Count > 1 && sortIndex >= 0 ? (sortIndex + 1).ToString(System.Globalization.CultureInfo.InvariantCulture) : "";
        _sortOrder.IsVisible = _sortOrder.Text.Length > 0;
        var filtered = _grid.State.Filters.Any(f => f.Field == Field);
        Classes.Set("filtered", filtered);
        UpdateAffordances();
        Classes.Set("sorted", dir is not null);
        Classes.Set("pinned", col.Pin != GridPin.None);
        _filter.IsVisible = c.Filterable;
        _grip.IsVisible = c.Resizable;
        AutomationProperties.SetName(this, c.DisplayTitle + (dir switch { SortDirection.Ascending => ", sorted ascending", SortDirection.Descending => ", sorted descending", _ => "" }) + (filtered ? ", filtered" : ""));
    }

    private void BuildMenu()
    {
        var col = _grid.EngineColumns.FirstOrDefault(c => c.Field == Field);
        if (col is null) return;
        var state = _grid.State;
        var items = _menu.Items;
        items.Clear();

        MenuItem Item(string header, string? icon, Action run, bool enabled = true)
        {
            var mi = new MenuItem { Header = header, IsEnabled = enabled };
            if (icon is not null) Sl.SetStartIcon(mi, icon);
            mi.Click += (_, _) => run();
            items.Add(mi);
            return mi;
        }

        if (col.Sortable)
        {
            Item("Sort ascending", "arrow-up", () => _grid.Apply(_grid.State.SetSorts(new GridSort(Field, SortDirection.Ascending))));
            Item("Sort descending", "arrow-down", () => _grid.Apply(_grid.State.SetSorts(new GridSort(Field, SortDirection.Descending))));
            if (state.SortOf(Field) is not null)
                Item("Clear sort", "x", () => _grid.Apply(_grid.State.SetSorts([.. _grid.State.Sorts.Where(s => s.Field != Field)])));
            items.Add(new Separator());
        }
        if (col.Filterable)
        {
            Item("Filter…", "filter", () => _filter.Open = true);
            if (state.Filters.Any(f => f.Field == Field))
                Item("Clear filter", "x", () => _grid.ClearFilter(Field));
        }
        if (!_grid.IsServerMode)
        {
            if (state.GroupBy.Contains(Field)) Item("Ungroup", "group", () => _grid.RemoveGroupBy(Field));
            else Item("Group by this column", "group", () => _grid.AddGroupBy(Field));
        }
        items.Add(new Separator());
        var pin = _grid.Layout.Find(Field)?.Pin ?? GridPin.None;
        if (pin != GridPin.Start) Item("Pin to start", "pin", () => _grid.PinColumn(Field, GridPin.Start));
        if (pin != GridPin.End) Item("Pin to end", "pin", () => _grid.PinColumn(Field, GridPin.End));
        if (pin != GridPin.None) Item("Unpin", "x", () => _grid.PinColumn(Field, GridPin.None));
        if (col.Resizable) Item("Auto-size", "columns", () => _grid.AutoSizeColumn(Field));
        if (col.Hideable) Item("Hide column", "eye-off", () => _grid.SetColumnHidden(Field, true));
    }

    private void OnPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        _pressAt = e.GetPosition(this);
        _dragging = false;
        e.Pointer.Capture(this);
    }

    private void OnMoved(object? sender, PointerEventArgs e)
    {
        if (_pressAt is not { } start) return;
        var p = e.GetPosition(this);
        var col = _grid.EngineColumns.FirstOrDefault(c => c.Field == Field);
        if (!_dragging && Math.Abs(p.X - start.X) + Math.Abs(p.Y - start.Y) > 5 && col?.Reorderable != false)
        {
            _dragging = true;
            Classes.Add("dragging");
        }
        if (!_dragging || Parent is not DataGridHeaderRow header) return;
        if (_grid.IsOverGroupBar(e))
        {
            header.ShowDropIndicator(null);
            _grid.HighlightGroupBar(true);
            return;
        }
        _grid.HighlightGroupBar(false);
        var (_, x) = header.DropTarget(e.GetPosition(header).X);
        header.ShowDropIndicator(x);
    }

    private void OnReleased(object? sender, PointerReleasedEventArgs e)
    {
        e.Pointer.Capture(null);
        var wasDragging = _dragging;
        _dragging = false;
        _pressAt = null;
        Classes.Remove("dragging");
        if (Parent is DataGridHeaderRow header) header.ShowDropIndicator(null);
        _grid.HighlightGroupBar(false);

        if (wasDragging)
        {
            if (_grid.IsOverGroupBar(e)) _grid.AddGroupBy(Field);
            else if (Parent is DataGridHeaderRow h)
            {
                var (index, _) = h.DropTarget(e.GetPosition(h).X);
                var current = _grid.Layout.Order.ToList().IndexOf(Field);
                _grid.MoveColumn(Field, index > current ? index - 1 : index);
            }
            return;
        }
        if (_grid.EngineColumns.FirstOrDefault(c => c.Field == Field)?.Sortable == true)
            _grid.ToggleSort(Field, additive: e.KeyModifiers.HasFlag(KeyModifiers.Shift));
    }

    private void OnGripPressed(object? sender, PointerPressedEventArgs e)
    {
        _resizeStart = e.GetPosition(Parent as Visual ?? this).X;
        _resizeOrigin = Bounds.Width;
        e.Pointer.Capture(_grip);
        e.Handled = true;
    }

    private void OnGripMoved(object? sender, PointerEventArgs e)
    {
        if (!ReferenceEquals(e.Pointer.Captured, _grip)) return;
        var x = e.GetPosition(Parent as Visual ?? this).X;
        _grid.ResizeColumn(Field, _resizeOrigin + (x - _resizeStart));
        e.Handled = true;
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Enter or Key.Space:
                _grid.ToggleSort(Field, e.KeyModifiers.HasFlag(KeyModifiers.Shift));
                e.Handled = true;
                break;
            case Key.Left or Key.Right when e.KeyModifiers.HasFlag(KeyModifiers.Alt):
                var order = _grid.Layout.Order.ToList();
                var i = order.IndexOf(Field) + (e.Key == Key.Left ? -1 : 1);
                if (i >= 0 && i < order.Count) _grid.MoveColumn(Field, i);
                e.Handled = true;
                break;
            case Key.F10 when e.KeyModifiers.HasFlag(KeyModifiers.Shift):
            case Key.Apps:
                _menu.Open = true;
                e.Handled = true;
                break;
        }
    }

    protected override AutomationPeer OnCreateAutomationPeer() => new HeaderPeer(this);

    private sealed class HeaderPeer(DataGridHeaderCell owner) : ControlAutomationPeer(owner), IInvokeProvider
    {
        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.HeaderItem;
        public void Invoke() => owner._grid.ToggleSort(owner.Field);
    }
}

/// <summary>Aggregate footer row (<c>ShowFooter</c>): one value per column with an aggregate.</summary>
public class DataGridFooterRow : Panel
{
    private readonly Dictionary<string, TextBlock> _cells = new(StringComparer.Ordinal);
    private readonly TextBlock _label = new() { Text = "Total", Classes = { "sl-grid-footer-label" }, VerticalAlignment = VerticalAlignment.Center };

    internal DataGrid? Owner { get; set; }

    public DataGridFooterRow()
    {
        Classes.Add("sl-grid-footer");
        ClipToBounds = true;
        Children.Add(_label);
    }

    public IReadOnlyDictionary<string, TextBlock> Cells => _cells;

    internal void Rebuild()
    {
        if (Owner is not { } grid) return;
        IsVisible = grid.ShowFooter && !grid.IsServerMode;
        if (!IsVisible) return;
        Height = grid.RowHeight;
        var totals = grid.Result?.Totals ?? new Dictionary<string, object?>();
        foreach (var tb in _cells.Values) Children.Remove(tb);
        _cells.Clear();
        foreach (var col in grid.Layout.Columns.Where(c => c.Column.Aggregate != GridAggregate.None))
        {
            var tb = new TextBlock
            {
                Classes = { "mono", "sl-grid-footer-cell" },
                VerticalAlignment = VerticalAlignment.Center,
                TextAlignment = TextAlignment.Right,
                Text = grid.AggregateText(col.Field, totals.GetValueOrDefault(col.Field), withName: false),
            };
            var full = tb.Text;
            tb.Tag = full.Contains(' ') ? full[(full.IndexOf(' ') + 1)..] : full;
            AutomationProperties.SetName(tb, $"{col.Column.DisplayTitle} {col.Column.Aggregate}: {tb.Text}");
            _cells[col.Field] = tb;
            Children.Add(tb);
        }
        InvalidateMeasure();
        InvalidateArrange();
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        foreach (var c in Children) c.Measure(new Size(double.PositiveInfinity, availableSize.Height));
        // Narrow columns drop the "sum"/"avg" prefix (still in the automation name) rather than truncating the number.
        if (Owner is { } grid)
        {
            var pad = grid.Resource("Sl.Component.Grid.CellPaddingX", 12);
            foreach (var (field, tb) in _cells)
                if (grid.Layout.Find(field) is { } col && tb.DesiredSize.Width > col.Width - 2 * pad && tb.Tag is string bare && tb.Text != bare)
                {
                    tb.Text = bare;
                    tb.Measure(new Size(double.PositiveInfinity, availableSize.Height));
                }
        }
        return new Size(0, Owner?.RowHeight ?? 32);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        if (Owner is not { } grid) return finalSize;
        var pad = grid.Resource("Sl.Component.Grid.CellPaddingX", 12);
        _label.Arrange(new Rect(pad, 0, 120, finalSize.Height));
        foreach (var (field, tb) in _cells)
        {
            var col = grid.Layout.Find(field);
            if (col is null) continue;
            var x = grid.CellX(col, grid.ScrollX, grid.ViewportWidth) - grid.ScrollX + grid.LeadWidth;
            tb.Arrange(new Rect(x + pad, 0, Math.Max(0, col.Width - 2 * pad), finalSize.Height));
        }
        return finalSize;
    }
}
