using System.Collections;
using System.Globalization;
using Slate.Data;

namespace Slate.Wpf;

/// <summary>Which rows an export or copy covers.</summary>
public enum GridExportScope
{
    /// <summary>Filtered rows in view order (ignores collapsed groups and paging).</summary>
    Visible,
    Selected,
    /// <summary>Every row, unfiltered.</summary>
    All,
}

/// <summary>A removable chip for an active column filter.</summary>
public sealed record GridFilterChip(string Field, string Title, string Text);

/// <summary>
/// The DataGrid's wiring to the Slate.Core engine (docs/design/data-grid.md): pipeline, layout, row geometry,
/// selection, keyboard, editing, server data and export. Holds no WPF types, so the cross-platform test suite
/// compiles this file directly; <see cref="DataGrid"/> only draws what this reports and forwards input to it.
/// </summary>
public sealed class GridController
{
    private IReadOnlyList<object> _items = [];
    private IReadOnlyList<GridColumn<object>> _columns = [];
    private DataPipeline<object> _pipeline;
    private GridPipelineResult<object>? _result;
    private GridRowCache<object>? _cache;
    private IGridDataSource<object>? _source;
    private readonly Dictionary<string, object> _itemsByKey = new(StringComparer.Ordinal);
    private readonly Dictionary<string, long> _flash = new(StringComparer.Ordinal);
    private double[] _offsets = [0];
    private Func<object, object?>? _rowKey;
    private Func<object, IEnumerable<object>?>? _children;
    private GridPagination _pagination;
    private GridSelectionMode _selectionMode;
    private GridEditMode _editMode;

    public GridController()
    {
        _pipeline = new DataPipeline<object>(_columns);
        Selection = new SelectionModel<string>(GridSelectionMode.None);
        Selection.Changed += OnSelectionChanged;
        Edit = new EditSession<object>(GridEditMode.None);
        HookEdit();
    }

    // ---- inputs ----------------------------------------------------------------------------------------------

    public GridState State { get; private set; } = GridState.Empty;

    public IReadOnlyList<GridColumn<object>> Columns => _columns;

    /// <summary>Client-mode items (a snapshot list; call <see cref="SetItems"/> again or <see cref="Invalidate"/> after changes).</summary>
    public IReadOnlyList<object> Items => _items;

    public bool IsServer => _source is not null;

    /// <summary>Row height for data and group rows (px).</summary>
    public double RowHeight { get; set; } = 32;

    /// <summary>Height of an open row-detail row (px).</summary>
    public double DetailHeight { get; set; } = 160;

    /// <summary>Skeleton rows shown in server mode before the first block arrives.</summary>
    public int SkeletonRows { get; set; } = 12;

    /// <summary>Server mode: rows requested per block.</summary>
    public int BlockSize { get; set; } = 100;

    public Func<object, Tone?>? RowTone { get; set; }

    public GridPagination Pagination
    {
        get => _pagination;
        set { _pagination = value; RebuildPipeline(); }
    }

    public GridSelectionMode SelectionMode
    {
        get => _selectionMode;
        set
        {
            if (_selectionMode == value) return;
            _selectionMode = value;
            Selection.Changed -= OnSelectionChanged;
            Selection = new SelectionModel<string>(value);
            Selection.Changed += OnSelectionChanged;
            SelectionChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public GridEditMode EditMode
    {
        get => _editMode;
        set
        {
            if (_editMode == value) return;
            _editMode = value;
            Edit.Changed -= OnEditChanged;
            Edit.Committed -= OnEditCommitted;
            Edit = new EditSession<object>(value);
            HookEdit();
            EditChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public SelectionModel<string> Selection { get; private set; }

    public EditSession<object> Edit { get; private set; }

    /// <summary>The keyboard's active cell (row index in the view, column index among visible columns).</summary>
    public GridCell Active { get; set; }

    /// <summary>Raised whenever the view state changed (the renderer persists/binds it).</summary>
    public event EventHandler? StateChanged;

    /// <summary>Rows changed (pipeline re-run, server block arrived, items replaced). May fire off the UI thread in server mode.</summary>
    public event EventHandler? ViewChanged;

    public event EventHandler? SelectionChanged;

    public event EventHandler? EditChanged;

    /// <summary>A value was written through a column setter (cell mode commit or batch commit-all).</summary>
    public event EventHandler<GridCellChange<object>>? CellCommitted;

    public void SetColumns(IReadOnlyList<GridColumn<object>> columns)
    {
        _columns = columns;
        RebuildPipeline();
    }

    public void SetItems(IEnumerable? items)
    {
        _items = items switch
        {
            null => [],
            IReadOnlyList<object> list => list,
            IList list => new ListAdapter(list),
            _ => items.Cast<object>().ToList(),
        };
        _pipeline.Invalidate();
        Refresh();
    }

    public void SetRowKey(Func<object, object?>? rowKey)
    {
        _rowKey = rowKey;
        RebuildPipeline();
    }

    public void SetChildren(Func<object, IEnumerable<object>?>? children)
    {
        _children = children;
        RebuildPipeline();
    }

    /// <summary>Switches to server mode (null returns to client mode).</summary>
    public void SetDataSource(IGridDataSource<object>? source)
    {
        if (_cache is not null) _cache.Changed -= OnCacheChanged;
        _source = source;
        _cache = source is null ? null : new GridRowCache<object>(source, BlockSize);
        if (_cache is not null)
        {
            _cache.Changed += OnCacheChanged;
            _cache.SetQuery(ServerQuery());
        }
        Refresh();
    }

    /// <summary>Call after mutating items in place so the pipeline recomputes.</summary>
    public void Invalidate()
    {
        _pipeline.Invalidate();
        Refresh();
    }

    // ---- state -----------------------------------------------------------------------------------------------

    /// <summary>Replaces the whole state (e.g. restored from JSON). Returns false when nothing changed.</summary>
    public bool SetState(GridState state, bool raise = true)
    {
        if (state.Equals(State)) return false;
        var queryChanged = !ServerQuery(state).SameResultSet(ServerQuery()) || state.PageIndex != State.PageIndex || state.PageSize != State.PageSize;
        State = state;
        if (_cache is not null && queryChanged) _cache.SetQuery(ServerQuery());
        Refresh();
        if (raise) StateChanged?.Invoke(this, EventArgs.Empty);
        return true;
    }

    private bool Apply(Func<GridState, GridState> transition) => SetState(transition(State));

    public bool ToggleSort(string field, bool additive = false) =>
        Column(field) is { Sortable: true } && Apply(s => s.ToggleSort(field, additive));

    public bool SortBy(string field, SortDirection? direction)
    {
        if (Column(field) is not { Sortable: true }) return false;
        return Apply(s =>
        {
            var sorts = s.Sorts.Where(x => x.Field != field).ToList();
            return direction is { } d ? s.SetSorts([new GridSort(field, d), .. sorts]) : s.SetSorts([.. sorts]);
        });
    }

    public bool SetFilter(GridFilter filter) => Apply(s => s.SetFilter(filter));
    public bool ClearFilter(string field) => Apply(s => s.ClearFilter(field));
    public bool ClearFilters() => Apply(s => s.ClearFilters());
    public bool SetQuickFilter(string text) => Apply(s => s.SetQuickFilter(text ?? ""));

    public bool GroupBy(string field) => !IsServer && _children is null && Apply(s => s.AddGroupBy(field));
    public bool Ungroup(string field) => Apply(s => s.RemoveGroupBy(field));
    public bool SetGroupBy(IReadOnlyList<string> fields) => Apply(s => s.SetGroupBy([.. fields]));
    public bool ExpandAll() => Apply(s => s.ExpandAll());
    public bool CollapseAll() => Apply(s => s.CollapseAll());

    public bool Pin(string field, GridPin pin) => Apply(s => s.PinColumn(field, pin));
    public bool Hide(string field, bool hidden = true) => Column(field) is { } c && (!hidden || c.Hideable) && Apply(s => s.SetColumnHidden(field, hidden));

    public bool Resize(string field, double width) =>
        Column(field) is { Resizable: true } c && Apply(s => s.ResizeColumn(field, width, c.MinWidth, c.MaxWidth));

    /// <summary>Moves a column to <paramref name="toIndex"/> in the full column order.</summary>
    public bool Move(string field, int toIndex) =>
        Column(field) is { Reorderable: true } && Apply(s => s.MoveColumn(Layout().Order, field, toIndex));

    /// <summary>Keyboard reorder (Alt+Left/Right): moves past the next visible column.</summary>
    public bool MoveBy(string field, int delta)
    {
        var layout = Layout();
        var visible = layout.Columns.Select(c => c.Field).ToList();
        var at = visible.IndexOf(field);
        var target = at + delta;
        if (at < 0 || target < 0 || target >= visible.Count) return false;
        var order = layout.Order.ToList();
        return Move(field, order.IndexOf(visible[target]));
    }

    /// <summary>Double-click on a resize grip: fit the header and a sample of visible values.</summary>
    public bool AutoFit(string field)
    {
        if (Column(field) is not { Resizable: true } column) return false;
        var sample = new List<string>();
        for (var i = 0; i < RowCount && sample.Count < 200; i++)
        {
            if (RowAt(i) is { Kind: GridRowKind.Data, Item: { } item } row)
                sample.Add(column.DisplayText(Value(item, row.Key, column)));
        }
        var mono = column.Type is GridColumnType.Number or GridColumnType.Date;
        var width = GridLayout.EstimateWidth(column, sample, mono) + 28; // sort/menu affordances
        return Resize(field, Math.Clamp(width, column.MinWidth, column.MaxWidth));
    }

    public bool SetPage(int pageIndex) => Apply(s => s.SetPage(Math.Min(pageIndex, Math.Max(0, PageCount - 1))));
    public bool SetPageSize(int pageSize) => Apply(s => s.SetPageSize(pageSize));

    /// <summary>Expands or collapses a group row or tree node. Returns false for rows that can't expand.</summary>
    public bool ToggleExpand(int rowIndex)
    {
        if (RowAt(rowIndex) is not { } row) return false;
        if (row.Kind == GridRowKind.Group) return Apply(s => s.ToggleGroup(row.GroupId!));
        if (row.HasChildren) return Apply(s => s.ToggleGroup("row:" + row.Key));
        return false;
    }

    public bool ToggleDetail(string rowKey) => Apply(s => s.ToggleDetail(rowKey));

    public bool IsDetailOpen(string rowKey) => State.ExpandedDetails.Contains(rowKey);

    // ---- view ------------------------------------------------------------------------------------------------

    public GridPipelineResult<object>? Result => _result;

    /// <summary>Number of rows to render.</summary>
    public int RowCount
    {
        get
        {
            if (_cache is null) return _result?.Rows.Count ?? 0;
            if (_cache.TotalCount is not { } total) return SkeletonRows;
            return Pagination == GridPagination.Pages ? Math.Clamp(total - State.PageIndex * State.PageSize, 0, State.PageSize) : total;
        }
    }

    /// <summary>Rows matching the filters (data rows only).</summary>
    public int MatchingCount => _cache is not null ? _cache.TotalCount ?? 0 : _result?.FilteredCount ?? 0;

    public int TotalCount => _cache is not null ? _cache.TotalCount ?? 0 : _result?.TotalCount ?? 0;

    public int PageCount
    {
        get
        {
            if (_cache is not null)
                return Math.Max(1, (int)Math.Ceiling((_cache.TotalCount ?? 0) / (double)Math.Max(1, State.PageSize)));
            return _result?.PageCount ?? 1;
        }
    }

    public bool IsLoading(int index) => _cache is not null && !_cache.TryGet(ServerIndex(index), out _);

    /// <summary>The row at a view index; in server mode a placeholder (no item) while its block loads.</summary>
    public GridViewRow<object>? RowAt(int index)
    {
        if (index < 0 || index >= RowCount) return null;
        if (_cache is null) return _result!.Rows[index];
        var serverIndex = ServerIndex(index);
        if (_cache.TryGet(serverIndex, out var item))
            return new GridViewRow<object> { Kind = GridRowKind.Data, Index = index, Item = item, Key = ServerKey(item, serverIndex) };
        return new GridViewRow<object> { Kind = GridRowKind.Data, Index = index, Key = "#loading" + serverIndex.ToString(CultureInfo.InvariantCulture) };
    }

    /// <summary>Data-row keys in view order (range selection order).</summary>
    public IReadOnlyList<string> VisibleKeys
    {
        get
        {
            if (_cache is null) return _result?.VisibleKeys ?? [];
            var keys = new List<string>();
            for (var i = 0; i < RowCount; i++)
                if (RowAt(i) is { Item: not null } r) keys.Add(r.Key);
            return keys;
        }
    }

    public IReadOnlyDictionary<string, object?> Totals => _result?.Totals ?? new Dictionary<string, object?>();

    public GridColumn<object>? Column(string field) => _columns.FirstOrDefault(c => c.Field == field);

    public GridColumnLayout<object> Layout(double availableWidth = 0) => GridLayout.Resolve(_columns, State, availableWidth);

    /// <summary>Value to show in a cell: the pending batch edit if any, else the item's value.</summary>
    public object? Value(object item, string rowKey, GridColumn<object> column) => Edit.GetValue(item, rowKey, column);

    public string Text(object item, string rowKey, GridColumn<object> column) => column.DisplayText(Value(item, rowKey, column));

    public void Refresh()
    {
        var previous = _result;
        if (_cache is null)
            _result = _pipeline.Run(_items, DataState(State));
        else
            _result = null;
        if (_cache is not null || !ReferenceEquals(previous, _result)) RebuildKeyIndex();
        RebuildOffsets();
        ClampActive();
        ViewChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>The state with layout-only column settings (width, pin, order) removed, so resizing or pinning a column
    /// hits the pipeline's cache instead of re-sorting and re-filtering every row.</summary>
    internal static GridState DataState(GridState state) => state with
    {
        Columns = [.. state.Columns.Where(c => c.Hidden is not null).Select(c => new GridColumnState(c.Field, Hidden: c.Hidden)).OrderBy(c => c.Field, StringComparer.Ordinal)],
    };

    // ---- row geometry (detail rows are taller) ---------------------------------------------------------------

    public double TotalHeight => _offsets[^1];

    public double HeightOf(int index) => RowAt(index)?.Kind == GridRowKind.Detail ? DetailHeight : RowHeight;

    public double OffsetOf(int index) => _offsets[Math.Clamp(index, 0, _offsets.Length - 1)];

    /// <summary>Row index containing <paramref name="y"/> (clamped).</summary>
    public int IndexAt(double y)
    {
        if (RowCount == 0) return 0;
        var lo = 0;
        var hi = RowCount - 1;
        while (lo < hi)
        {
            var mid = (lo + hi + 1) / 2;
            if (_offsets[mid] <= y) lo = mid;
            else hi = mid - 1;
        }
        return lo;
    }

    /// <summary>Rows intersecting the viewport, plus overscan.</summary>
    public ViewportRange Window(double scrollTop, double viewportHeight, int overscan = 6)
    {
        if (RowCount == 0) return new ViewportRange(0, 0, 0, 0);
        scrollTop = Math.Clamp(scrollTop, 0, Math.Max(0, TotalHeight - viewportHeight));
        var first = Math.Max(0, IndexAt(scrollTop) - overscan);
        var last = Math.Min(RowCount, IndexAt(scrollTop + Math.Max(0, viewportHeight)) + 1 + overscan);
        return new ViewportRange(first, last - first, OffsetOf(first), TotalHeight);
    }

    /// <summary>Scroll offset that brings a row fully into view.</summary>
    public double ScrollToReveal(int index, double scrollTop, double viewportHeight)
    {
        var top = OffsetOf(index);
        var bottom = top + HeightOf(index);
        if (top < scrollTop) return top;
        if (bottom > scrollTop + viewportHeight) return Math.Max(0, bottom - viewportHeight);
        return scrollTop;
    }

    /// <summary>Server mode: load the blocks behind a viewport window (no-op in client mode).</summary>
    public Task EnsureLoadedAsync(int first, int count) =>
        _cache is null ? Task.CompletedTask : _cache.EnsureRangeAsync(ServerIndex(first), count);

    // ---- filters ---------------------------------------------------------------------------------------------

    public IReadOnlyList<GridFilterChip> FilterChips =>
        State.Filters.Select(f => Column(f.Field) is { } c ? new GridFilterChip(f.Field, c.DisplayTitle, Describe(f, c)) : null)
             .OfType<GridFilterChip>()
             .ToList();

    public static IReadOnlyList<FilterOperator> OperatorsFor(GridColumnType type) => type switch
    {
        GridColumnType.Number or GridColumnType.Progress or GridColumnType.Date =>
        [
            FilterOperator.Equals, FilterOperator.NotEquals, FilterOperator.LessThan, FilterOperator.LessThanOrEqual,
            FilterOperator.GreaterThan, FilterOperator.GreaterThanOrEqual, FilterOperator.Between, FilterOperator.IsEmpty, FilterOperator.IsNotEmpty,
        ],
        GridColumnType.Boolean => [FilterOperator.Equals, FilterOperator.IsEmpty],
        GridColumnType.Enum => [FilterOperator.AnyOf, FilterOperator.IsEmpty, FilterOperator.IsNotEmpty],
        _ => [FilterOperator.Contains, FilterOperator.Equals, FilterOperator.NotEquals, FilterOperator.StartsWith, FilterOperator.EndsWith, FilterOperator.IsEmpty, FilterOperator.IsNotEmpty],
    };

    public static string OperatorText(FilterOperator op) => op switch
    {
        FilterOperator.Contains => "contains",
        FilterOperator.Equals => "is",
        FilterOperator.NotEquals => "is not",
        FilterOperator.StartsWith => "starts with",
        FilterOperator.EndsWith => "ends with",
        FilterOperator.IsEmpty => "is empty",
        FilterOperator.IsNotEmpty => "is not empty",
        FilterOperator.LessThan => "<",
        FilterOperator.LessThanOrEqual => "≤",
        FilterOperator.GreaterThan => ">",
        FilterOperator.GreaterThanOrEqual => "≥",
        FilterOperator.Between => "between",
        FilterOperator.AnyOf => "is any of",
        _ => op.ToString(),
    };

    /// <summary>"Status is any of Ready, Failed", "Duration between 10 and 60".</summary>
    public static string Describe(GridFilter f, GridColumn<object> c)
    {
        string V(object? v) => v is null ? "" : c.DisplayText(v is string s && c.Type == GridColumnType.Date ? GridValues.ParseIsoDate(s) ?? (object)s : v);
        var op = OperatorText(f.Operator);
        return f.Operator switch
        {
            FilterOperator.IsEmpty or FilterOperator.IsNotEmpty => $"{c.DisplayTitle} {op}",
            FilterOperator.Between => $"{c.DisplayTitle} {op} {V(f.Value)} and {V(f.Value2)}",
            FilterOperator.AnyOf => $"{c.DisplayTitle} {op} {string.Join(", ", (f.Values ?? []).Select(V))}",
            _ => $"{c.DisplayTitle} {op} {V(f.Value)}",
        };
    }

    /// <summary>Distinct display values of a column (enum filter choices), in the column's order.</summary>
    public IReadOnlyList<string> DistinctValues(string field, int max = 200)
    {
        if (Column(field) is not { } c) return [];
        if (c.EnumOrder is { Count: > 0 } order) return order;
        return _items.Select(i => c.DisplayText(c.GetValue(i))).Where(t => t.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase)
                     .Order(StringComparer.OrdinalIgnoreCase).Take(max).ToList();
    }

    // ---- selection -------------------------------------------------------------------------------------------

    public void Click(int rowIndex, bool ctrl = false, bool shift = false)
    {
        if (RowAt(rowIndex) is { Kind: GridRowKind.Data, Item: not null } row)
            Selection.Click(row.Key, VisibleKeys, ctrl, shift);
    }

    public void Toggle(int rowIndex)
    {
        if (RowAt(rowIndex) is { Kind: GridRowKind.Data, Item: not null } row)
            Selection.Toggle(row.Key);
    }

    public void ExtendTo(int rowIndex)
    {
        if (RowAt(rowIndex) is { Kind: GridRowKind.Data, Item: not null } row)
            Selection.ExtendTo(row.Key, VisibleKeys);
    }

    public bool IsSelected(string rowKey) => Selection.IsSelected(rowKey);

    public SelectAllState HeaderState => Selection.HeaderState(SelectableKeys());

    /// <summary>Header checkbox: none/some → all filtered rows; all → none.</summary>
    public void ToggleAll()
    {
        if (HeaderState == SelectAllState.All) Selection.Clear();
        else Selection.SelectAll(SelectableKeys());
    }

    /// <summary>Server mode "select all N matching": selects rows not yet loaded too.</summary>
    public void SelectAllMatching() => Selection.SelectAllMatching(MatchingCount);

    public void ClearSelection() => Selection.Clear();

    /// <summary>Selected items that are known (client mode: all; server mode: loaded rows).</summary>
    public IReadOnlyList<object> SelectedItems =>
        Selection.AllMatching
            ? _itemsByKey.Where(kv => Selection.IsSelected(kv.Key)).Select(kv => kv.Value).ToList()
            : Selection.Selected.Select(k => _itemsByKey.GetValueOrDefault(k)).OfType<object>().ToList();

    public void SetSelectedItems(IEnumerable<object>? items)
    {
        var keys = (items ?? []).Select(KeyOf).OfType<string>().ToList();
        if (keys.ToHashSet().SetEquals(Selection.Selected) && !Selection.AllMatching) return;
        Selection.Set(keys);
    }

    /// <summary>Row key text for an item (null when it isn't in the grid).</summary>
    public string? KeyOf(object item)
    {
        if (_rowKey?.Invoke(item) is { } k) return k as string ?? Convert.ToString(k, CultureInfo.InvariantCulture);
        foreach (var (key, value) in _itemsByKey)
            if (ReferenceEquals(value, item)) return key;
        return null;
    }

    public object? ItemOf(string key) => _itemsByKey.GetValueOrDefault(key);

    private IReadOnlyCollection<string> SelectableKeys() =>
        _cache is not null ? VisibleKeys.ToList() : _result?.ItemKeys ?? (IReadOnlyCollection<string>)[];

    // ---- keyboard --------------------------------------------------------------------------------------------

    /// <summary>Moves the active cell; applies expand/collapse on group and tree rows.</summary>
    public GridNavResult Navigate(GridKey key, double viewportHeight, int columnCount)
    {
        var ctx = new GridNavContext
        {
            RowCount = RowCount,
            ColumnCount = columnCount,
            PageRows = GridViewport.PageRows(viewportHeight, RowHeight),
            Kind = i => RowAt(i)?.Kind ?? GridRowKind.Data,
            CanExpand = i => RowAt(i) is { } r && (r.Kind == GridRowKind.Group || r.HasChildren),
            IsExpanded = i => RowAt(i)?.Expanded ?? false,
        };
        var result = GridNavigator.Move(Active, key, ctx);
        if (result.Action != GridNavAction.None)
            ToggleExpand(result.Cell.Row);
        Active = result.Cell;
        ClampActive();
        return result;
    }

    // ---- editing ---------------------------------------------------------------------------------------------

    public bool CanEdit(int rowIndex, GridColumn<object> column) =>
        EditMode != GridEditMode.None && column.Editable && column.Setter is not null
        && RowAt(rowIndex) is { Kind: GridRowKind.Data, Item: not null };

    public bool BeginEdit(int rowIndex, GridColumn<object> column, string? initialText = null)
    {
        if (!CanEdit(rowIndex, column)) return false;
        var row = RowAt(rowIndex)!;
        if (!Edit.Begin(row.Item!, row.Key, column, initialText)) return false;
        return true;
    }

    /// <summary>Commits the open edit; on success moves the active cell like a spreadsheet.</summary>
    public bool CommitEdit(GridEditCommitKey? key = null, bool shift = false, int columnCount = 0, double viewportHeight = 0)
    {
        if (!Edit.Commit()) return false;
        if (EditMode == GridEditMode.Cell) Invalidate();
        if (key is { } k && columnCount > 0) Navigate(EditSession<object>.MoveAfterCommit(k, shift), viewportHeight, columnCount);
        return true;
    }

    public void CancelEdit() => Edit.Cancel();

    public IReadOnlyList<GridCellChange<object>> CommitAll()
    {
        var applied = Edit.CommitAll(_columns);
        if (applied.Count > 0) Invalidate();
        return applied;
    }

    public void DiscardAll() => Edit.DiscardAll();

    public int PendingCount => Edit.Pending.Count;

    // ---- export ----------------------------------------------------------------------------------------------

    /// <summary>Visible (non-hidden) columns in display order.</summary>
    public IReadOnlyList<GridColumn<object>> ExportColumns => Layout().Columns.Select(c => c.Column).ToList();

    public IReadOnlyList<object> RowsFor(GridExportScope scope) => scope switch
    {
        GridExportScope.All => _cache is not null ? LoadedItems() : _items,
        GridExportScope.Selected => (_cache is null ? _result?.Items ?? [] : LoadedItems()).Where(i => KeyOf(i) is { } k && Selection.IsSelected(k)).ToList(),
        _ => _cache is null ? _result?.Items ?? [] : LoadedItems(),
    };

    public string ToCsv(GridExportScope scope) =>
        GridExport.ToCsv(ExportColumns, RowsFor(scope), true, ValueOf);

    /// <summary>Ctrl+C: selected rows (or the active row) as TSV, including pending edits.</summary>
    public string CopyText(bool includeHeader = false)
    {
        var rows = Selection.Count > 0 ? RowsFor(GridExportScope.Selected) : RowAt(Active.Row) is { Item: { } item } ? [item] : [];
        return GridExport.ToTsv(ExportColumns, rows, includeHeader, ValueOf);
    }

    public string ToTsv(GridExportScope scope, bool includeHeader = true) => GridExport.ToTsv(ExportColumns, RowsFor(scope), includeHeader, ValueOf);

    private object? ValueOf(object item, GridColumn<object> column) => KeyOf(item) is { } key ? Edit.GetValue(item, key, column) : column.GetValue(item);

    private List<object> LoadedItems()
    {
        var list = new List<object>();
        for (var i = 0; i < RowCount; i++)
            if (RowAt(i) is { Item: { } item }) list.Add(item);
        return list;
    }

    // ---- live updates ----------------------------------------------------------------------------------------

    /// <summary>Marks an item as changed (the renderer flashes its row) and recomputes the view.</summary>
    public void MarkChanged(object item, long nowMs)
    {
        MarkFlash(item, nowMs);
        Invalidate();
    }

    /// <summary>Starts an item's row flash without recomputing (callers batch the <see cref="Invalidate"/>).</summary>
    public void MarkFlash(object item, long nowMs)
    {
        if (KeyOf(item) is { } key) _flash[key] = nowMs;
    }

    /// <summary>0..1 flash strength for a row key at <paramref name="nowMs"/> (fades out over <paramref name="durationMs"/>).</summary>
    public double FlashStrength(string rowKey, long nowMs, double durationMs = 1200)
    {
        if (!_flash.TryGetValue(rowKey, out var at)) return 0;
        var t = (nowMs - at) / durationMs;
        if (t >= 1)
        {
            _flash.Remove(rowKey);
            return 0;
        }
        return 1 - Math.Clamp(t, 0, 1);
    }

    public bool HasFlashes => _flash.Count > 0;

    // ---- internals -------------------------------------------------------------------------------------------

    private void RebuildPipeline()
    {
        _pipeline = new DataPipeline<object>(_columns, new DataPipelineOptions<object>
        {
            RowKey = _rowKey,
            ChildrenSelector = _children,
            Paginate = _pagination == GridPagination.Pages,
        });
        Refresh();
    }

    private GridQuery ServerQuery(GridState? state = null)
    {
        var s = state ?? State;
        return GridQuery.FromState(s, 0, BlockSize);
    }

    private int ServerIndex(int viewIndex) => Pagination == GridPagination.Pages ? State.PageIndex * State.PageSize + viewIndex : viewIndex;

    private string ServerKey(object item, int serverIndex) =>
        _rowKey?.Invoke(item) is { } k ? (k as string ?? Convert.ToString(k, CultureInfo.InvariantCulture) ?? "") : "#" + serverIndex.ToString(CultureInfo.InvariantCulture);

    private void RebuildKeyIndex()
    {
        if (_cache is not null)
        {
            for (var i = 0; i < RowCount; i++)
                if (RowAt(i) is { Item: { } item } r) _itemsByKey[r.Key] = item;
            return;
        }

        _itemsByKey.Clear();
        if (_result is null) return;
        for (var i = 0; i < _result.Items.Count; i++)
            _itemsByKey[_result.ItemKeys[i]] = _result.Items[i];
        foreach (var row in _result.Rows)
            if (row is { Kind: GridRowKind.Data, Item: { } item }) _itemsByKey[row.Key] = item;
        if (_rowKey is null && _children is null)
        {
            // Unfiltered rows keep their positional keys (#index), so selection can be set for filtered-out rows too.
            for (var i = 0; i < _items.Count; i++)
                _itemsByKey.TryAdd("#" + i.ToString(CultureInfo.InvariantCulture), _items[i]);
        }
        else if (_rowKey is not null && _children is null)
        {
            foreach (var item in _items)
                if (_rowKey(item) is { } k) _itemsByKey.TryAdd(k as string ?? Convert.ToString(k, CultureInfo.InvariantCulture) ?? "", item);
        }
    }

    private void RebuildOffsets()
    {
        var count = RowCount;
        var offsets = new double[count + 1];
        var hasDetails = _result is not null && State.ExpandedDetails.Count > 0;
        for (var i = 0; i < count; i++)
            offsets[i + 1] = offsets[i] + (hasDetails && _result!.Rows[i].Kind == GridRowKind.Detail ? DetailHeight : RowHeight);
        _offsets = offsets;
    }

    /// <summary>Recompute geometry after <see cref="RowHeight"/> or <see cref="DetailHeight"/> changed.</summary>
    public void Remeasure() => RebuildOffsets();

    private void ClampActive()
    {
        var rows = RowCount;
        Active = new GridCell(Math.Clamp(Active.Row, 0, Math.Max(0, rows - 1)), Math.Max(0, Active.Column));
    }

    private void HookEdit()
    {
        Edit.Changed += OnEditChanged;
        Edit.Committed += OnEditCommitted;
    }

    private void OnEditChanged(object? sender, EventArgs e) => EditChanged?.Invoke(this, EventArgs.Empty);
    private void OnEditCommitted(object? sender, GridCellChange<object> change) => CellCommitted?.Invoke(this, change);
    private void OnSelectionChanged(object? sender, EventArgs e) => SelectionChanged?.Invoke(this, EventArgs.Empty);

    private void OnCacheChanged(object? sender, EventArgs e)
    {
        RebuildOffsets();
        ViewChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Exposes a non-generic IList as a read-only list without copying (100k-row sources).</summary>
    private sealed class ListAdapter(IList list) : IReadOnlyList<object>
    {
        public object this[int index] => list[index]!;
        public int Count => list.Count;
        public IEnumerator<object> GetEnumerator() { foreach (var o in list) yield return o!; }
        IEnumerator IEnumerable.GetEnumerator() => list.GetEnumerator();
    }
}
