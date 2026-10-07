using System.Globalization;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;
using Slate.Blazor.Internal;
using Slate.Data;

namespace Slate.Blazor;

/// <summary>
/// Virtualised, keyboard-first data grid (docs/design/data-grid.md; markup in docs/design/css-classes.md#datagrid).
/// All behaviour — filtering, sorting, grouping, tree data, selection, navigation, editing, server paging, export —
/// comes from the Slate.Core engine (<see cref="DataPipeline{T}"/>, <see cref="GridState"/>, <see cref="SelectionModel{TKey}"/>,
/// <see cref="EditSession{T}"/>, <see cref="GridRowCache{T}"/>); this component renders the visible window and forwards input.
/// <para>
/// Blazor Server payloads: slate.js reports scrolling only when the rendered window (8 rows of overscan) stops covering
/// the viewport (rAF-throttled, ~1 call per 5 rows scrolled); toolbar, bars and header are memoised; rows are keyed by
/// row key so a window shift sends only the rows that entered; plain cells render as one markup string each and row/cell
/// clicks, checkboxes and expanders are delegated by slate.js, so rows carry no per-cell handlers. Measured on the
/// 100k demo: a 6-row shift ≈ 44 KB render batch (≈ 5 KB on the wire with the default permessage-deflate).
/// Works unchanged on WebAssembly (same interop, no circuit).
/// </para>
/// </summary>
[CascadingTypeParameter(nameof(T))]
public partial class SlDataGrid<T> : SlComponentBase, IAsyncDisposable, IHandleEvent
{
    private const double SelectWidth = 44;
    private const double DetailToggleWidth = 36;
    private const int SkeletonRows = 30;
    private const int Overscan = 8;
    private const int FlashMs = 900;
    private const int ColumnVirtualizationThreshold = 40;
    private static readonly int[] PageSizes = [25, 50, 100, 250];

    [Inject] private SlateJs Js { get; set; } = default!;
    [Inject] private SlateOptions Options { get; set; } = default!;

    // ---- parameters (design/api/components.json "DataGrid") ----

    /// <summary>Rows (client mode).</summary>
    [Parameter] public IEnumerable<T>? Items { get; set; }

    /// <summary>Server mode: rows load in blocks on demand; overrides <see cref="Items"/>.</summary>
    [Parameter] public IGridDataSource<T>? DataSource { get; set; }

    /// <summary><see cref="SlGridColumn{T}"/> declarations.</summary>
    [Parameter] public RenderFragment? Columns { get; set; }

    /// <summary>Columns as a list (used instead of <see cref="Columns"/> when non-empty).</summary>
    [Parameter] public IReadOnlyList<DataGridColumn<T>>? ColumnDefinitions { get; set; }

    /// <summary>Stable identity for selection, edits and live updates (default: the row index).</summary>
    [Parameter] public Func<T, object>? RowKey { get; set; }

    [Parameter] public GridSelectionMode SelectionMode { get; set; } = GridSelectionMode.None;

    /// <summary>Selected rows (two-way).</summary>
    [Parameter] public IReadOnlyCollection<T>? SelectedItems { get; set; }
    [Parameter] public EventCallback<IReadOnlyCollection<T>> SelectedItemsChanged { get; set; }

    [Parameter] public GridPagination Pagination { get; set; } = GridPagination.None;

    [Parameter] public int PageSize { get; set; } = 50;

    /// <summary>Null inherits the surrounding <c>data-sl-density</c>.</summary>
    [Parameter] public Density? Density { get; set; }

    [Parameter] public bool Striped { get; set; }

    /// <summary>Vertical cell borders.</summary>
    [Parameter] public bool Bordered { get; set; }

    /// <summary>Quick-filter text (two-way).</summary>
    [Parameter] public string? QuickFilter { get; set; }
    [Parameter] public EventCallback<string> QuickFilterChanged { get; set; }

    [Parameter] public bool ShowToolbar { get; set; }

    /// <summary>Aggregate footer row.</summary>
    [Parameter] public bool ShowFooter { get; set; }

    /// <summary>Shows the group-by bar.</summary>
    [Parameter] public bool Groupable { get; set; }

    /// <summary>Grouping fields (two-way).</summary>
    [Parameter] public string[]? GroupBy { get; set; }
    [Parameter] public EventCallback<string[]> GroupByChanged { get; set; }

    /// <summary>Tree data.</summary>
    [Parameter] public Func<T, IEnumerable<T>?>? ChildrenSelector { get; set; }

    /// <summary>Expandable detail row content.</summary>
    [Parameter] public RenderFragment<T>? RowDetail { get; set; }

    /// <summary>Height of detail rows, px.</summary>
    [Parameter] public double DetailHeight { get; set; } = 160;

    [Parameter] public Func<T, Tone?>? RowTone { get; set; }

    [Parameter] public GridEditMode EditMode { get; set; } = GridEditMode.None;

    [Parameter] public bool Loading { get; set; }

    [Parameter] public RenderFragment? EmptyContent { get; set; }

    /// <summary>Extra toolbar items.</summary>
    [Parameter] public RenderFragment? ToolbarContent { get; set; }

    /// <summary>Buttons in the selection bar (shown while rows are selected).</summary>
    [Parameter] public RenderFragment? SelectionActions { get; set; }

    /// <summary>Serialisable layout/sort/filter/group state (two-way).</summary>
    [Parameter] public GridState? State { get; set; }
    [Parameter] public EventCallback<GridState> StateChanged { get; set; }

    [Parameter] public EventCallback<GridSelectionChangedEventArgs<T>> SelectionChanged { get; set; }

    /// <summary>Enter or double-click on a row that isn't edited.</summary>
    [Parameter] public EventCallback<T> RowActivated { get; set; }

    [Parameter] public EventCallback<GridCellChange<T>> CellEditCommitted { get; set; }

    /// <summary>Ctrl+C copied rows (TSV).</summary>
    [Parameter] public EventCallback<GridCopyEventArgs> Copied { get; set; }

    /// <summary>Before an export downloads; set Cancel to handle the text yourself.</summary>
    [Parameter] public EventCallback<GridExportEventArgs> Exporting { get; set; }

    /// <summary>Accessible name of the grid.</summary>
    [Parameter] public string? Label { get; set; }

    /// <summary>Rows replaced by <see cref="UpsertAsync"/> flash briefly.</summary>
    [Parameter] public bool FlashUpdates { get; set; } = true;

    // ---- engine state ----

    private GridState _state = GridState.Empty;
    private GridState? _lastStateParam;
    private string[]? _lastGroupByParam;
    private string? _lastQuickFilterParam;
    private int _lastPageSizeParam = 50;
    private bool _initialized;

    private readonly List<SlGridColumn<T>> _registered = [];
    private int _columnsVersion;
    private int _builtColumnsVersion = -1;
    private IReadOnlyList<DataGridColumn<T>>? _builtDefinitions;
    private IReadOnlyList<DataGridColumn<T>> _defs = [];
    private List<GridColumn<T>> _engineColumns = [];
    private Dictionary<string, DataGridColumn<T>> _defByField = new();

    private DataPipeline<T>? _pipeline;
    private Func<T, object>? _pipelineRowKey;
    private Func<T, IEnumerable<T>?>? _pipelineChildren;
    private GridPagination _pipelinePagination;
    private GridPipelineResult<T>? _result;
    private IEnumerable<T>? _itemsParam;
    private IReadOnlyList<T> _items = [];

    private IGridDataSource<T>? _source;
    private GridRowCache<T>? _cache;
    private Dictionary<string, T> _serverItems = new();

    private SelectionModel<string> _selection = new(GridSelectionMode.None);
    private EditSession<T> _edit = new(GridEditMode.None);
    private IReadOnlyCollection<T>? _lastSelectedParam;
    private IReadOnlyCollection<T>? _lastEmittedSelection;
    private bool _syncingSelection;
    private SelectAllState? _headerState;
    private Dictionary<string, T>? _keyToItem;
    private GridPipelineResult<T>? _keyToItemFor;

    private GridColumnLayout<T> _layout = new([], [], 0);
    private double[]? _offsets;

    // ---- view state ----

    private GridCell _active = new(0, 0);
    private bool _keyboard;
    private double _vTop;
    private double _vLeft;
    private double _viewportHeight;
    private double _viewportWidth;
    private double _rowHeight = 32;
    private double _headerHeight = 40;
    private bool _inheritedComfortable;
    private Density? _densityOverride;
    private bool _scrolledX;
    private bool _scrollableEnd;
    private string _renderedWindow = "";
    private (int First, int Last)? _renderedColumns;
    private string? _openFilter;
    private GridCells.FilterDraft? _filterDraft;
    private string? _openMenu;
    private string? _openActionsKey;
    private bool _chooserOpen;
    private (string Field, string? Over, bool After, bool OverGroupBar)? _drag;
    private (string Field, double StartWidth)? _resizing;
    private string _announcement = "";
    private readonly Dictionary<string, long> _flashing = new();
    private int _lastPageKey = int.MinValue;

    // ---- render bookkeeping ----

    private ElementReference _viewport;
    private DotNetObjectReference<SlDataGrid<T>>? _dotnet;
    private bool _jsReady;
    private bool _chromeDirty = true;
    private int _chromeVersion;
    private double? _pendingScrollTop;
    private double? _pendingScrollLeft;
    private string? _pendingFocus;
    private string _lastSync = "";
    private string _lastNoReorder = "";
    private bool _disposed;

    // ---- public API ----

    /// <summary>The current state (also via <see cref="State"/>/<see cref="StateChanged"/>).</summary>
    public GridState CurrentState => _state;

    /// <summary>Engine result of the last run (client mode).</summary>
    public GridPipelineResult<T>? View => _result;

    /// <summary>Number of selected rows (including "all matching" in server mode).</summary>
    public int SelectedCount => _selection.Count;

    /// <summary>Pending batch edits.</summary>
    public IReadOnlyList<GridCellChange<T>> PendingChanges => _edit.Pending;

    /// <summary>The active cell (view row, layout column).</summary>
    public GridCell ActiveCell => _active;

    /// <summary>Replaces items by key (live updates); replaced rows flash.</summary>
    public Task UpsertAsync(IEnumerable<T> changed) => InvokeAsync(() =>
    {
        var byKey = new Dictionary<string, T>();
        foreach (var item in changed) byKey[KeyOf(item, -1)] = item;
        if (byKey.Count == 0) return;
        var next = new T[_items.Count];
        var now = Options.TimeProvider.GetTimestamp();
        for (var i = 0; i < next.Length; i++)
        {
            var item = _items[i];
            if (byKey.Count > 0 && byKey.TryGetValue(KeyOf(item, i), out var replacement))
            {
                item = replacement;
                if (FlashUpdates && RowKey is not null) _flashing[KeyOf(item, i)] = now;
            }
            next[i] = item;
        }
        _items = next;
        if (_flashing.Count > 0) _ = ExpireFlashAsync();
        Prepare();
        Invalidate();
    });

    /// <summary>Re-runs the pipeline (or re-queries the data source) after items changed in place.</summary>
    public Task RefreshAsync() => InvokeAsync(() =>
    {
        _pipeline?.Invalidate();
        _cache?.SetQuery(GridQuery.FromState(_state));
        _result = null;
        Prepare();
        Invalidate();
    });

    /// <summary>Selects rows by engine key (RowKey text).</summary>
    public Task SelectKeysAsync(IEnumerable<string> keys) => InvokeAsync(() => _selection.Set(keys));

    public Task ClearSelectionAsync() => InvokeAsync(() => _selection.Clear());

    /// <summary>Commits pending batch edits.</summary>
    public Task CommitEditsAsync() => InvokeAsync(() =>
    {
        var applied = _edit.CommitAll(_engineColumns);
        if (applied.Count > 0)
        {
            _pipeline?.Invalidate();
            Prepare();
            Announce($"{applied.Count} change{(applied.Count == 1 ? "" : "s")} committed");
        }
        Invalidate();
    });

    public Task DiscardEditsAsync() => InvokeAsync(() =>
    {
        _edit.DiscardAll();
        Invalidate();
    });

    /// <summary>Builds an export (does not download).</summary>
    public async Task<string> ExportTextAsync(GridExportFormat format, GridExportScope scope = GridExportScope.Visible)
    {
        var rows = await ExportRowsAsync(scope);
        var cols = ExportColumns();
        object? ValueOf(T item, GridColumn<T> c) => _edit.GetValue(item, KeyOf(item, -1), c);
        return format == GridExportFormat.Csv ? GridExport.ToCsv(cols, rows, true, ValueOf) : GridExport.ToTsv(cols, rows, true, ValueOf);
    }

    /// <summary>Exports and downloads (unless an <see cref="Exporting"/> handler cancels).</summary>
    public async Task ExportAsync(GridExportFormat format, GridExportScope scope = GridExportScope.Visible)
    {
        var text = await ExportTextAsync(format, scope);
        var args = new GridExportEventArgs(format, scope, text);
        await Exporting.InvokeAsync(args);
        if (args.Cancel) return;
        var ext = format == GridExportFormat.Csv ? "csv" : "tsv";
        await JsSafe.Run(() => Js.DownloadTextAsync($"{Label ?? "export"}.{ext}", format == GridExportFormat.Csv ? "text/csv" : "text/tab-separated-values", text));
    }

    /// <summary>Scrolls a view row into view and makes it active.</summary>
    public Task ScrollToRowAsync(int index) => InvokeAsync(() =>
    {
        _active = new GridCell(Math.Max(0, Math.Min(index, SlotCount - 1)), _active.Column);
        Reveal();
        Invalidate();
    });

    // ---- columns ----

    internal void RegisterColumn(SlGridColumn<T> column)
    {
        if (!_registered.Contains(column)) _registered.Add(column);
        _columnsVersion++;
        if (_initialized)
        {
            Prepare();
            Invalidate();
        }
    }

    internal void UnregisterColumn(SlGridColumn<T> column)
    {
        if (_disposed || !_registered.Remove(column)) return;
        _columnsVersion++;
        Prepare();
        Invalidate();
    }

    private void BuildColumns()
    {
        var useList = ColumnDefinitions is { Count: > 0 };
        if (_builtColumnsVersion == _columnsVersion && ReferenceEquals(_builtDefinitions, useList ? ColumnDefinitions : null)) return;
        _builtColumnsVersion = _columnsVersion;
        _builtDefinitions = useList ? ColumnDefinitions : null;
        _defs = useList ? ColumnDefinitions! : _registered.Select(c => c.Definition).OfType<DataGridColumn<T>>().ToList();
        _defByField = new Dictionary<string, DataGridColumn<T>>();
        _engineColumns = new List<GridColumn<T>>(_defs.Count);
        foreach (var d in _defs)
        {
            _defByField[d.Field] = d;
            var c = d.Column;
            // Editable plain-field columns write back to the same property unless a setter is given.
            if (c.Editable && c.Setter is null && c.Accessor is null && GridCells.AutoSetter<T>(c.Field) is { } setter) c = GridCells.WithSetter(c, setter);
            _engineColumns.Add(c);
        }
        _pipeline = null;
    }

    private DataGridColumn<T>? Def(string field) => _defByField.GetValueOrDefault(field);

    private GridColumn<T>? EngineColumn(string field) => _engineColumns.FirstOrDefault(c => c.Field == field);

    // ---- lifecycle ----

    protected override void OnParametersSet()
    {
        _chromeDirty = true;
        if (_selection.Mode != SelectionMode) WireSelection();
        if (_edit.Mode != EditMode) WireEdit();

        var first = !_initialized;
        var previous = _state;
        var s = _state;
        if (State is not null && !ReferenceEquals(State, _lastStateParam))
        {
            s = State;
        }
        else
        {
            if (!SameList(GroupBy, _lastGroupByParam) && (!first || GroupBy is { Length: > 0 }) && !SameList(GroupBy ?? [], s.GroupBy)) s = s.SetGroupBy(GroupBy ?? []);
            if (QuickFilter != _lastQuickFilterParam && (!first || !string.IsNullOrEmpty(QuickFilter)) && (QuickFilter ?? "") != s.QuickFilter) s = s.SetQuickFilter(QuickFilter ?? "");
            if (PageSize != _lastPageSizeParam && (!first || PageSize != 50) && PageSize != s.PageSize) s = s.SetPageSize(PageSize);
        }
        _lastStateParam = State;
        _lastGroupByParam = GroupBy;
        _lastQuickFilterParam = QuickFilter;
        _lastPageSizeParam = PageSize;
        _state = s;

        if (!ReferenceEquals(Items, _itemsParam))
        {
            _itemsParam = Items;
            _items = Items switch
            {
                null => [],
                IReadOnlyList<T> list => list,
                _ => Items.ToList(),
            };
        }

        if (!ReferenceEquals(DataSource, _source))
        {
            if (_cache is not null) _cache.Changed -= OnCacheChanged;
            _source = DataSource;
            _cache = DataSource is null ? null : new GridRowCache<T>(DataSource, 100);
            _serverItems = new Dictionary<string, T>();
            if (_cache is not null)
            {
                _cache.Changed += OnCacheChanged;
                _cache.SetQuery(GridQuery.FromState(_state));
            }
        }

        _initialized = true;
        Prepare();
        SyncSelectedItems();
        if (!ReferenceEquals(previous, _state) && !first) _ = NotifyStateAsync(previous, _state, external: State is not null && ReferenceEquals(State, _state));
    }

    /// <summary>Brings columns, pipeline, cache query, layout and offsets up to date (idempotent; not on the scroll path).</summary>
    private void Prepare()
    {
        BuildColumns();
        if (_pipeline is null || !Equals(_pipelineRowKey, RowKey) || !Equals(_pipelineChildren, ChildrenSelector) || _pipelinePagination != Pagination)
        {
            _pipelineRowKey = RowKey;
            _pipelineChildren = ChildrenSelector;
            _pipelinePagination = Pagination;
            var rowKey = RowKey;
            _pipeline = new DataPipeline<T>(_engineColumns, new DataPipelineOptions<T>
            {
                RowKey = rowKey is null ? null : item => rowKey(item),
                ChildrenSelector = ChildrenSelector,
                Paginate = Pagination == GridPagination.Pages,
            });
            _result = null;
        }

        if (_cache is not null)
        {
            _result = null;
            var query = GridQuery.FromState(_state);
            if (!_cache.Query.SameResultSet(query))
            {
                _cache.SetQuery(query);
                _serverItems = new Dictionary<string, T>();
                _selection.Clear();
            }
        }
        else
        {
            var result = _pipeline.Run(_items, _state);
            if (!ReferenceEquals(result, _result))
            {
                _result = result;
                _headerState = null;
            }
        }

        _layout = GridLayout.Resolve(_engineColumns, _state, Math.Max(0, (_viewportWidth > 0 ? _viewportWidth : 960) - LeadingWidth));
        BuildOffsets();
        var count = SlotCount;
        if (_active.Row >= count) _active = _active with { Row = Math.Max(0, count - 1) };
        if (_active.Column >= _layout.Columns.Count) _active = _active with { Column = Math.Max(0, _layout.Columns.Count - 1) };

        // A new page (or switching paging on/off) starts at the top.
        var pageKey = HashCode.Combine(Pagination, _state.PageIndex, _state.PageSize);
        if (pageKey != _lastPageKey)
        {
            var firstTime = _lastPageKey == int.MinValue;
            _lastPageKey = pageKey;
            if (!firstTime && _vTop != 0)
            {
                _vTop = 0;
                _pendingScrollTop = 0;
            }
        }
    }

    private void WireSelection()
    {
        _selection.Changed -= OnSelectionModelChanged;
        _selection = new SelectionModel<string>(SelectionMode);
        _selection.Changed += OnSelectionModelChanged;
        _headerState = null;
    }

    private void WireEdit()
    {
        _edit.Committed -= OnEditCommitted;
        _edit = new EditSession<T>(EditMode);
        _edit.Committed += OnEditCommitted;
    }

    private void OnEditCommitted(object? sender, GridCellChange<T> change)
    {
        _pipeline?.Invalidate();
        _ = InvokeAsync(async () =>
        {
            Prepare();
            await CellEditCommitted.InvokeAsync(change);
            Invalidate();
        });
    }

    private void OnCacheChanged(object? sender, EventArgs e)
    {
        if (_disposed) return;
        _ = InvokeAsync(() =>
        {
            _headerState = null;
            var count = SlotCount;
            if (_active.Row >= count) _active = _active with { Row = Math.Max(0, count - 1) };
            Invalidate();
        });
    }

    async Task IHandleEvent.HandleEventAsync(EventCallbackWorkItem callback, object? arg)
    {
        _chromeDirty = true;
        var task = callback.InvokeAsync(arg);
        var shouldAwait = task.Status != TaskStatus.RanToCompletion && task.Status != TaskStatus.Canceled;
        StateHasChanged();
        if (shouldAwait)
        {
            await task;
            _chromeDirty = true;
            StateHasChanged();
        }
    }

    /// <summary>Full re-render (toolbar, bars and header included).</summary>
    private void Invalidate()
    {
        _chromeDirty = true;
        StateHasChanged();
    }

    private int ChromeVersion()
    {
        if (_chromeDirty)
        {
            _chromeDirty = false;
            _chromeVersion++;
        }
        return _chromeVersion;
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (_disposed) return;
        if (firstRender)
        {
            _dotnet = DotNetObjectReference.Create(this);
            await JsSafe.Run(() => Js.GridInitAsync(_viewport, _dotnet));
            _jsReady = true;
        }
        if (_jsReady) await SyncJsAsync();
        if (_cache is not null)
        {
            var (first, count) = CacheRange();
            _ = EnsureRangeAsync(_cache, first, count);
        }
    }

    private static async Task EnsureRangeAsync(GridRowCache<T> cache, int first, int count)
    {
        try { await cache.EnsureRangeAsync(first, count); }
        catch (OperationCanceledException) { }
        catch (Exception) { /* surfaced through cache.LastError */ }
    }

    private async Task SyncJsAsync()
    {
        var w = Window();
        var count = SlotCount;
        double? top = w.First > 0 ? RowTop(Math.Min(w.First + 3, count)) : null;
        double? bottom = w.First + w.Count < count ? RowTop(Math.Max(w.First + w.Count - 3, 0)) : null;
        var body = Math.Max(0, (_viewportHeight > 0 ? _viewportHeight : 480) - _headerHeight);
        double? left = null, right = null;
        if (_layout.Columns.Count > ColumnVirtualizationThreshold)
        {
            left = Math.Max(0, _vLeft - 150);
            right = _vLeft + 150;
        }
        var noReorder = string.Join('\u001f', _layout.Columns.Where(c => !c.Column.Reorderable).Select(c => c.Field));
        var focus = _pendingFocus;
        if (focus is null && _edit.IsEditing && !_editorFocused)
        {
            focus = "editor";
        }
        if (focus == "editor") _editorFocused = true;
        var measure = _chromeVersion != _syncedChrome;
        _syncedChrome = _chromeVersion;
        var key = FormattableString.Invariant($"{top}:{bottom}:{body}:{left}:{right}");
        if (key == _lastSync && !_syncRequested && _pendingScrollTop is null && _pendingScrollLeft is null && focus is null && noReorder == _lastNoReorder && !measure) return;
        _lastSync = key;
        _syncRequested = false;
        var options = new GridSyncOptions(top, bottom, body, left, right, _pendingScrollTop, _pendingScrollLeft, focus,
            noReorder == _lastNoReorder ? null : _layout.Columns.Where(c => !c.Column.Reorderable).Select(c => c.Field).ToArray(), measure);
        _lastNoReorder = noReorder;
        _pendingScrollTop = null;
        _pendingScrollLeft = null;
        _pendingFocus = null;
        await JsSafe.Run(() => Js.GridSyncAsync(_viewport, options));
    }

    private bool _editorFocused;
    private bool _syncRequested;
    private int _syncedChrome = -1;

    private sealed record GridSyncOptions(double? Top, double? Bottom, double Body, double? Left, double? Right, double? ScrollTop, double? ScrollLeft,
        string? Focus, string[]? NoReorder, bool Measure);

    // ---- JS callbacks ----

    [JSInvokable]
    public Task OnGridMeasure(double rowHeight, double headerHeight, double viewportWidth, double viewportHeight, bool comfortable) => InvokeAsync(() =>
    {
        var changed = false;
        if (rowHeight > 0 && Math.Abs(rowHeight - _rowHeight) > 0.01) { _rowHeight = rowHeight; changed = true; }
        if (headerHeight > 0 && Math.Abs(headerHeight - _headerHeight) > 0.01) { _headerHeight = headerHeight; changed = true; }
        if (Math.Abs(viewportWidth - _viewportWidth) > 0.01) { _viewportWidth = viewportWidth; changed = true; }
        if (Math.Abs(viewportHeight - _viewportHeight) > 0.01) { _viewportHeight = viewportHeight; changed = true; }
        if (comfortable != _inheritedComfortable) { _inheritedComfortable = comfortable; changed = true; }
        if (!changed) return;
        Prepare();
        Invalidate();
    });

    /// <summary>Scroll report from slate.js (only when the rendered window stops covering the viewport). Returns whether it re-renders.</summary>
    [JSInvokable]
    public async Task<bool> OnGridScroll(double top, double left, bool scrolledX, bool scrollableEnd)
    {
        var rendering = false;
        await InvokeAsync(() =>
        {
            _vTop = top;
            _vLeft = left;
            var flags = scrolledX != _scrolledX || scrollableEnd != _scrollableEnd;
            _scrolledX = scrolledX;
            _scrollableEnd = scrollableEnd;
            var w = Window();
            var columns = _layout.Columns.Count > ColumnVirtualizationThreshold ? ScrollingColumns() : ((int, int)?)null;
            if ($"{w.First}:{w.Count}" == _renderedWindow && columns == _renderedColumns && !flags) return;
            if (columns != _renderedColumns) _chromeDirty = true; // the header shows the same column window
            rendering = true;
            _syncRequested = true; // slate.js waits for the new band before reporting again
            StateHasChanged();
        });
        return rendering;
    }

    /// <summary>
    /// Clicks on rows/cells (not on their buttons or inputs) are delegated by slate.js from the cell ids
    /// (<c>{gridId}-r{row}-c{col}</c>), so rows carry no per-cell event handlers — rows entering the window stay small.
    /// </summary>
    [JSInvokable]
    public Task OnGridCellClick(int row, int column, bool ctrl, bool shift, bool dbl) => InvokeAsync(async () =>
    {
        _chromeDirty = true;
        if (dbl) await OnRowDoubleClick(row, column);
        else await OnRowClick(new MouseEventArgs { CtrlKey = ctrl, ShiftKey = shift }, row, column);
        StateHasChanged();
    });

    /// <summary>Row checkbox, detail/tree/group expander and row-menu trigger clicks, delegated by slate.js like cell clicks.</summary>
    [JSInvokable]
    public Task OnGridRowCommand(int row, string command, bool shift) => InvokeAsync(() =>
    {
        _chromeDirty = true;
        switch (command)
        {
            case "check": OnRowCheckboxClick(new MouseEventArgs { ShiftKey = shift }, row); break;
            case "detail": OnDetailToggle(row); break;
            case "toggle": ToggleSlot(row); break;
            case "actions" when row < SlotCount: _openActionsKey = GetSlot(row).Key; break;
        }
        StateHasChanged();
    });

    [JSInvokable]
    public Task OnGridResize(string field, double dx, bool done) => InvokeAsync(() =>
    {
        var c = _layout.Find(field);
        if (c is null) return;
        _resizing ??= (field, c.Width);
        var start = _resizing.Value.StartWidth;
        _state = _state.ResizeColumn(field, start + dx, c.Column.MinWidth, c.Column.MaxWidth);
        if (done)
        {
            _resizing = null;
            CommitState(_state, "Column resized", force: true);
        }
        else
        {
            Prepare();
            Invalidate();
        }
    });

    [JSInvokable]
    public Task OnGridDrag(string field, string? over, bool after, bool overGroupBar, bool done) => InvokeAsync(() =>
    {
        if (!done)
        {
            _drag = (field, over, after, overGroupBar);
            Invalidate();
            return;
        }
        _drag = null;
        var c = _layout.Find(field);
        var title = c?.Column.DisplayTitle ?? field;
        if (overGroupBar && Groupable && ChildrenSelector is null)
        {
            CommitState(_state.AddGroupBy(field), $"Grouped by {title}");
        }
        else if (over is not null && over != field)
        {
            var order = _layout.Order.Where(f => f != field).ToList();
            var to = order.IndexOf(over) + (after ? 1 : 0);
            if (to < 0) to = order.Count;
            CommitState(_state.MoveColumn(_layout.Order, field, to), $"{title} moved");
        }
        else
        {
            Invalidate();
        }
    });

    // ---- state transitions ----

    /// <summary>Applies a state transition and notifies (StateChanged, GroupByChanged, QuickFilterChanged).</summary>
    private void CommitState(GridState next, string? message = null, bool force = false)
    {
        if (!force && ReferenceEquals(next, _state)) return;
        var previous = _state;
        _state = next;
        _lastStateParam = next;
        if (message is not null) _announcement = message;
        Prepare();
        Invalidate();
        _ = NotifyStateAsync(previous, next, external: false);
    }

    private async Task NotifyStateAsync(GridState previous, GridState next, bool external)
    {
        _lastStateParam = next;
        if (!external) await StateChanged.InvokeAsync(next);
        if (!previous.GroupBy.SequenceEqual(next.GroupBy))
        {
            var groupBy = next.GroupBy.ToArray();
            _lastGroupByParam = groupBy;
            await GroupByChanged.InvokeAsync(groupBy);
        }
        if (previous.QuickFilter != next.QuickFilter)
        {
            _lastQuickFilterParam = next.QuickFilter;
            await QuickFilterChanged.InvokeAsync(next.QuickFilter);
        }
    }

    private void Announce(string message)
    {
        _announcement = message;
        Invalidate();
    }

    // ---- selection ----

    private void OnSelectionModelChanged(object? sender, EventArgs e)
    {
        _headerState = null;
        if (_syncingSelection) return;
        _ = InvokeAsync(EmitSelectionAsync);
    }

    private async Task EmitSelectionAsync()
    {
        var map = KeyToItem();
        IReadOnlyList<string> keys = _selection.AllMatching ? map.Keys.Where(_selection.IsSelected).ToList() : _selection.Selected.ToList();
        var items = new List<T>(keys.Count);
        foreach (var k in keys)
            if (map.TryGetValue(k, out var it)) items.Add(it);
        _lastSelectedParam = items; // a bound parent echoes this back; it isn't a new selection
        _lastEmittedSelection = items;
        _announcement = _selection.Count > 0 ? $"{_selection.Count} selected" : "Selection cleared";
        Invalidate();
        await SelectedItemsChanged.InvokeAsync(items);
        await SelectionChanged.InvokeAsync(new GridSelectionChangedEventArgs<T>(keys, items, _selection.Count, _selection.AllMatching));
    }

    /// <summary>A <see cref="SelectedItems"/> value from the parent (not one we emitted) selects those rows.</summary>
    private void SyncSelectedItems()
    {
        if (ReferenceEquals(SelectedItems, _lastSelectedParam)) return;
        _lastSelectedParam = SelectedItems;
        // Null means "not bound" (or not yet): it never clears a selection made in the grid.
        if (SelectionMode == GridSelectionMode.None || SelectedItems is null || ReferenceEquals(SelectedItems, _lastEmittedSelection)) return;
        var keys = new List<string>();
        if (SelectedItems.Count > 0)
        {
            var set = new HashSet<T>(SelectedItems);
            var map = KeyToItem();
            if (RowKey is not null) keys.AddRange(SelectedItems.Select(i => KeyOf(i, -1)));
            else keys.AddRange(map.Where(kv => set.Contains(kv.Value)).Select(kv => kv.Key));
        }
        _syncingSelection = true;
        try { _selection.Set(keys); }
        finally { _syncingSelection = false; }
    }

    private Dictionary<string, T> KeyToItem()
    {
        if (_cache is not null) return _serverItems;
        if (_result is null) return new Dictionary<string, T>();
        if (!ReferenceEquals(_keyToItemFor, _result) || _keyToItem is null)
        {
            _keyToItemFor = _result;
            _keyToItem = new Dictionary<string, T>(_result.Items.Count);
            for (var i = 0; i < _result.Items.Count; i++) _keyToItem[_result.ItemKeys[i]] = _result.Items[i];
        }
        return _keyToItem;
    }

    private SelectAllState HeaderSelectState()
    {
        if (_cache is not null)
            return _selection.AllMatching ? (_selection.Excluded.Count > 0 ? SelectAllState.Some : SelectAllState.All) : _selection.Count > 0 ? SelectAllState.Some : SelectAllState.None;
        return _headerState ??= _selection.HeaderState(_result is null ? [] : (IReadOnlyCollection<string>)_result.ItemKeys);
    }

    private IReadOnlyList<string> VisibleKeys()
    {
        if (_result is not null) return _result.VisibleKeys;
        var keys = new List<string>();
        for (var i = 0; i < SlotCount; i++)
        {
            var s = GetSlot(i);
            if (s.Kind == SlotKind.Data) keys.Add(s.Key);
        }
        return keys;
    }

    private void SelectAll()
    {
        if (_cache is not null) _selection.SelectAllMatching(_cache.TotalCount ?? SlotCount);
        else _selection.SelectAll(_result?.ItemKeys ?? []);
    }

    private void ToggleHeaderSelection()
    {
        if (HeaderSelectState() == SelectAllState.All) _selection.Clear();
        else SelectAll();
    }

    // ---- rows ----

    private enum SlotKind { Data, Group, Detail, Skeleton }

    private readonly record struct Slot(SlotKind Kind, int Index, string Key, int Depth, T? Item, GridViewRow<T>? View);

    private int ServerFirst => Pagination == GridPagination.Pages ? _state.PageIndex * _state.PageSize : 0;

    private int SlotCount
    {
        get
        {
            if (_cache is not null)
            {
                var count = _cache.TotalCount ?? SkeletonRows;
                return Pagination == GridPagination.Pages ? Math.Max(0, Math.Min(_state.PageSize, count - ServerFirst)) : count;
            }
            return _result?.Rows.Count ?? 0;
        }
    }

    private Slot GetSlot(int i)
    {
        if (_cache is not null)
        {
            var index = ServerFirst + i;
            if (!_cache.TryGet(index, out var item)) return new Slot(SlotKind.Skeleton, i, "skeleton:" + index.ToString(CultureInfo.InvariantCulture), 0, default, null);
            var key = KeyOf(item, index);
            _serverItems[key] = item;
            return new Slot(SlotKind.Data, i, key, 0, item, null);
        }
        var r = _result!.Rows[i];
        var kind = r.Kind switch { GridRowKind.Group => SlotKind.Group, GridRowKind.Detail => SlotKind.Detail, _ => SlotKind.Data };
        return new Slot(kind, r.Index, r.Key, r.Depth, r.Item, r);
    }

    private string KeyOf(T item, int index)
    {
        var k = RowKey?.Invoke(item);
        return k is null ? "#" + index.ToString(CultureInfo.InvariantCulture) : k as string ?? Convert.ToString(k, CultureInfo.InvariantCulture) ?? "";
    }

    private bool HasDetail => RowDetail is not null;

    private double LeadingWidth => (SelectionMode == GridSelectionMode.Multi ? SelectWidth : 0) + (HasDetail ? DetailToggleWidth : 0);

    private bool IsComfortable => (_densityOverride ?? Density) is { } d ? d == Slate.Density.Comfortable : _inheritedComfortable;

    private int HeaderRowCount => _layout.Columns.Any(c => Def(c.Field)?.HeaderGroup is not null) ? 2 : 1;

    private void BuildOffsets()
    {
        _offsets = null;
        if (_state.ExpandedDetails.Count == 0 || _result is null) return;
        var rows = _result.Rows;
        if (!rows.Any(r => r.Kind == GridRowKind.Detail)) return;
        var o = new double[rows.Count + 1];
        for (var i = 0; i < rows.Count; i++) o[i + 1] = o[i] + (rows[i].Kind == GridRowKind.Detail ? DetailHeight : _rowHeight);
        _offsets = o;
    }

    private double RowTop(int i) => _offsets is not null ? _offsets[Math.Min(i, _offsets.Length - 1)] : i * _rowHeight;

    private double RowHeightOf(int i) => i < SlotCount && _result is not null && _result.Rows[i].Kind == GridRowKind.Detail ? DetailHeight : _rowHeight;

    private double TotalHeight => _offsets is not null ? _offsets[^1] : SlotCount * _rowHeight;

    private (int First, int Count, double OffsetTop) Window()
    {
        var body = Math.Max(0, (_viewportHeight > 0 ? _viewportHeight : 480) - _headerHeight);
        var count = SlotCount;
        if (_offsets is null)
        {
            var v = GridViewport.Compute(_vTop, body, _rowHeight, count, Overscan);
            return (v.First, v.Count, v.OffsetTop);
        }
        var o = _offsets;
        int lo = 0, hi = count;
        while (lo < hi)
        {
            var mid = (lo + hi) >> 1;
            if (o[mid + 1] <= _vTop) lo = mid + 1;
            else hi = mid;
        }
        var first = Math.Max(0, lo - Overscan);
        var last = lo;
        while (last < count && o[last] < _vTop + body) last++;
        last = Math.Min(count, last + Overscan);
        return (first, last - first, o[first]);
    }

    private (int First, int Count) CacheRange()
    {
        var w = Window();
        return (ServerFirst + w.First, Math.Max(w.Count, 1));
    }

    private (int First, int Last) ScrollingColumns() =>
        _layout.ScrollingRange(Math.Max(0, _vLeft - LeadingWidth), _viewportWidth > 0 ? _viewportWidth : 960, 300);

    /// <summary>Visible columns: pinned + scrolling range (column virtualisation for wide grids). A null cell is the leading spacer.</summary>
    private (List<ResolvedColumn<T>?> Cells, double SpacerBefore, double SpacerAfter) VisibleColumnSet()
    {
        var all = _layout.Columns;
        if (all.Count <= ColumnVirtualizationThreshold) return ([.. all], 0, 0);
        var (first, last) = ScrollingColumns();
        _renderedColumns = (first, last);
        var start = all.Where(c => c.Pin == GridPin.Start);
        var end = all.Where(c => c.Pin == GridPin.End);
        var scrolling = all.Where(c => c.Pin == GridPin.None).ToList();
        if (first < 0) return ([.. start, .. end], 0, 0);
        var firstScroll = all[first];
        var lastScroll = all[last];
        var scrollStart = scrolling.Count > 0 ? scrolling[0].Left : 0;
        var scrollEnd = scrolling.Count > 0 ? scrolling[^1].Left + scrolling[^1].Width : 0;
        var before = firstScroll.Left - scrollStart;
        var after = scrollEnd - (lastScroll.Left + lastScroll.Width);
        var cells = new List<ResolvedColumn<T>?>(start);
        if (before > 0) cells.Add(null);
        for (var i = first; i <= last; i++) cells.Add(all[i]);
        cells.AddRange(end);
        return (cells, before, after);
    }

    private string PinStyle(ResolvedColumn<T> c)
    {
        var style = "width:" + GridCells.Px(c.Width);
        if (c.Pin == GridPin.Start) style += ";left:" + GridCells.Px(c.StickyOffset + LeadingWidth);
        else if (c.Pin == GridPin.End) style += ";right:" + GridCells.Px(c.StickyOffset);
        return style;
    }

    private Css PinClasses(Css css, ResolvedColumn<T> c)
    {
        ResolvedColumn<T>? lastStart = null, firstEnd = null;
        foreach (var x in _layout.Columns)
        {
            if (x.Pin == GridPin.Start) lastStart = x;
            if (x.Pin == GridPin.End && firstEnd is null) firstEnd = x;
        }
        return css.Add("is-pinned-start", c.Pin == GridPin.Start).Add("is-pinned-end", c.Pin == GridPin.End)
            .Add("is-pinned-edge-start", ReferenceEquals(c, lastStart)).Add("is-pinned-edge-end", ReferenceEquals(c, firstEnd));
    }

    private bool SelectIsEdge => !_layout.Columns.Any(c => c.Pin == GridPin.Start) && !HasDetail;

    private int ColIndex(ResolvedColumn<T> c) => c.Index + 1 + (SelectionMode == GridSelectionMode.Multi ? 1 : 0) + (HasDetail ? 1 : 0);

    private int RowAriaIndex(int row) => (Pagination == GridPagination.Pages ? _state.PageIndex * _state.PageSize : 0) + row + 1 + HeaderRowCount;

    private string CellId(int row, int column) => $"{UniqueId}-r{row.ToString(CultureInfo.InvariantCulture)}-c{column.ToString(CultureInfo.InvariantCulture)}";

    private bool IsFlashing(string key) => _flashing.ContainsKey(key);

    private async Task ExpireFlashAsync()
    {
        await Task.Delay(TimeSpan.FromMilliseconds(FlashMs), Options.TimeProvider);
        await InvokeAsync(() =>
        {
            if (_disposed) return;
            var now = Options.TimeProvider.GetTimestamp();
            foreach (var (k, t) in _flashing.ToList())
                if (Options.TimeProvider.GetElapsedTime(t, now).TotalMilliseconds >= FlashMs - 20) _flashing.Remove(k);
            StateHasChanged();
        });
    }

    // ---- navigation ----

    private GridNavContext NavContext()
    {
        var body = Math.Max(1, (_viewportHeight > 0 ? _viewportHeight : 480) - _headerHeight);
        return new GridNavContext
        {
            RowCount = SlotCount,
            ColumnCount = Math.Max(1, _layout.Columns.Count),
            PageRows = GridViewport.PageRows(body, _rowHeight),
            Kind = r => r < SlotCount ? GetSlot(r).Kind switch { SlotKind.Group => GridRowKind.Group, SlotKind.Detail => GridRowKind.Detail, _ => GridRowKind.Data } : GridRowKind.Data,
            CanExpand = r => r < SlotCount && GetSlot(r) is var s && (s.Kind == SlotKind.Group || s.View?.HasChildren == true),
            IsExpanded = r => r < SlotCount && GetSlot(r).View?.Expanded == true,
        };
    }

    private void Reveal()
    {
        var body = Math.Max(0, (_viewportHeight > 0 ? _viewportHeight : 480) - _headerHeight);
        var top = RowTop(_active.Row);
        var h = RowHeightOf(_active.Row);
        var next = _vTop;
        if (_offsets is not null)
        {
            if (top < next) next = top;
            else if (top + h > next + body) next = top + h - body;
        }
        else
        {
            next = GridViewport.ScrollToReveal(_active.Row, _vTop, body, _rowHeight);
        }
        if (Math.Abs(next - _vTop) > 0.01)
        {
            _vTop = next;
            _pendingScrollTop = next;
        }
        RevealColumn(_active.Column < _layout.Columns.Count ? _layout.Columns[_active.Column] : null);
    }

    /// <summary>Scrolls horizontally so a scrolling (unpinned) column is fully visible between the pinned sections.</summary>
    private void RevealColumn(ResolvedColumn<T>? c)
    {
        if (c is null || c.Pin != GridPin.None) return;
        var startPinned = _layout.Columns.Where(x => x.Pin == GridPin.Start).Sum(x => x.Width) + LeadingWidth;
        var endPinned = _layout.Columns.Where(x => x.Pin == GridPin.End).Sum(x => x.Width);
        var left = c.Left + LeadingWidth;
        var width = _viewportWidth > 0 ? _viewportWidth : 960;
        var next = _vLeft;
        if (left - startPinned < next) next = left - startPinned;
        else if (left + c.Width > next + width - endPinned) next = left + c.Width - width + endPinned;
        if (Math.Abs(next - _vLeft) > 0.01)
        {
            _vLeft = Math.Max(0, next);
            _pendingScrollLeft = _vLeft;
        }
    }

    private async Task OnKeyDown(KeyboardEventArgs e)
    {
        if (_edit.IsEditing) return;
        _keyboard = true;
        var mod = e.CtrlKey || e.MetaKey;
        var slot = _active.Row < SlotCount ? GetSlot(_active.Row) : (Slot?)null;

        if (e.AltKey && e.Key is "ArrowLeft" or "ArrowRight")
        {
            var c = _active.Column < _layout.Columns.Count ? _layout.Columns[_active.Column] : null;
            if (c is not null && c.Column.Reorderable)
            {
                var delta = e.Key == "ArrowLeft" ? -1 : 1;
                _active = _active with { Column = Math.Max(0, Math.Min(_active.Column + delta, _layout.Columns.Count - 1)) };
                CommitState(_state.MoveColumn(_layout.Order, c.Field, IndexOf(_layout.Order, c.Field) + delta), $"{c.Column.DisplayTitle} moved");
            }
            return;
        }

        if (GridCells.KeyFrom(e.Key, mod) is { } gk && !(gk is GridKey.Plus or GridKey.Minus && mod))
        {
            var before = _active;
            var r = GridNavigator.Move(_active, gk, NavContext());
            if (r.Action != GridNavAction.None) ToggleSlot(_active.Row, r.Action == GridNavAction.Expand);
            _active = r.Cell;
            if (e.ShiftKey && SelectionMode == GridSelectionMode.Multi && gk is GridKey.Up or GridKey.Down or GridKey.PageUp or GridKey.PageDown && before.Row != r.Cell.Row)
            {
                var k = GetSlot(r.Cell.Row);
                if (k.Kind == SlotKind.Data)
                {
                    if (!_selection.HasAnchor && GetSlot(before.Row) is { Kind: SlotKind.Data } prev) _selection.Click(prev.Key, VisibleKeys());
                    _selection.ExtendTo(k.Key, VisibleKeys());
                }
            }
            Reveal();
            return;
        }

        switch (e.Key)
        {
            case " ":
                if (slot is { Kind: SlotKind.Group } || (slot?.View?.HasChildren == true && SelectionMode == GridSelectionMode.None)) ToggleSlot(_active.Row);
                else if (slot is { Kind: SlotKind.Data } s && SelectionMode != GridSelectionMode.None)
                {
                    if (SelectionMode == GridSelectionMode.Single) _selection.Click(s.Key, VisibleKeys());
                    else _selection.Toggle(s.Key);
                }
                return;
            case "Enter":
                if (slot is { Kind: SlotKind.Group }) ToggleSlot(_active.Row);
                else if (slot is { Kind: SlotKind.Data } d && !BeginEdit()) await RowActivated.InvokeAsync(d.Item!);
                return;
            case "F2":
                BeginEdit();
                return;
            case "Escape":
                if (_selection.Count > 0 && SelectionMode != GridSelectionMode.None) _selection.Clear();
                return;
        }

        if (mod && e.Key is "a" or "A" && SelectionMode == GridSelectionMode.Multi)
        {
            SelectAll();
            return;
        }
        if (mod && e.Key is "c" or "C")
        {
            await CopyAsync(e.ShiftKey);
            return;
        }
        if (!mod && !e.AltKey && e.Key.Length == 1 && slot is { Kind: SlotKind.Data }) BeginEdit(e.Key);
    }

    private static int IndexOf(IReadOnlyList<string> list, string value)
    {
        for (var i = 0; i < list.Count; i++)
            if (list[i] == value) return i;
        return -1;
    }

    private void ToggleSlot(int row, bool? expand = null)
    {
        if (row >= SlotCount) return;
        var s = GetSlot(row);
        if (s.View is null) return;
        if (s.Kind != SlotKind.Group && !s.View.HasChildren) return;
        var id = s.Kind == SlotKind.Group ? s.View.GroupId! : "row:" + s.Key;
        var next = expand is null ? _state.ToggleGroup(id) : _state.SetGroupExpanded(id, expand.Value);
        CommitState(next, $"{(s.View.Expanded ? "Collapsed" : "Expanded")} {s.View.GroupKeyText ?? ""}".Trim());
    }

    private async Task OnRowClick(MouseEventArgs e, int row, int column)
    {
        if (row >= SlotCount) return;
        var slot = GetSlot(row);
        _keyboard = false;
        if (_edit.IsEditing && !_edit.Commit()) return;
        _active = new GridCell(row, column < 0 ? _active.Column : column);
        _pendingFocus = "viewport";
        if (slot.Kind == SlotKind.Group) ToggleSlot(row);
        else if (slot.Kind == SlotKind.Data && SelectionMode != GridSelectionMode.None)
            _selection.Click(slot.Key, VisibleKeys(), SelectionMode == GridSelectionMode.Multi && (e.CtrlKey || e.MetaKey), SelectionMode == GridSelectionMode.Multi && e.ShiftKey);
        await Task.CompletedTask;
    }

    private async Task OnRowDoubleClick(int row, int column)
    {
        if (row >= SlotCount) return;
        var slot = GetSlot(row);
        if (slot.Kind != SlotKind.Data) return;
        _active = new GridCell(row, column);
        if (!BeginEdit()) await RowActivated.InvokeAsync(slot.Item!);
    }

    private void OnRowCheckboxClick(MouseEventArgs e, int row)
    {
        if (row >= SlotCount) return;
        var slot = GetSlot(row);
        if (slot.Kind != SlotKind.Data) return;
        if (e.ShiftKey) _selection.Click(slot.Key, VisibleKeys(), false, true);
        else _selection.Toggle(slot.Key);
        _active = _active with { Row = row };
    }

    private void OnDetailToggle(int row)
    {
        if (row >= SlotCount) return;
        CommitState(_state.ToggleDetail(GetSlot(row).Key));
    }

    // ---- editing ----

    private bool BeginEdit(string? initialText = null)
    {
        if (_active.Row >= SlotCount || _active.Column >= _layout.Columns.Count) return false;
        var slot = GetSlot(_active.Row);
        var c = _layout.Columns[_active.Column];
        if (slot.Kind != SlotKind.Data || slot.Item is null) return false;
        if (_edit.Mode == GridEditMode.None || !c.Column.Editable || c.Column.Setter is null) return false;
        var ok = _edit.Begin(slot.Item, slot.Key, c.Column);
        if (ok && initialText is not null) _edit.SetDraftText(initialText);
        _editorFocused = false;
        return ok;
    }

    private void FinishEditNavigation(GridEditCommitKey key, bool shift)
    {
        var move = EditSession<T>.MoveAfterCommit(key, shift);
        _active = GridNavigator.Move(_active, move, NavContext()).Cell;
        _keyboard = true;
        Reveal();
        _pendingFocus = "viewport";
    }

    private void OnEditorKeyDown(KeyboardEventArgs e)
    {
        if (e.Key == "Escape")
        {
            _edit.Cancel();
            _keyboard = true;
            _pendingFocus = "viewport";
            return;
        }
        if (e.Key is "Enter" or "Tab")
        {
            if (!_edit.Commit()) return;
            FinishEditNavigation(e.Key == "Enter" ? GridEditCommitKey.Enter : GridEditCommitKey.Tab, e.ShiftKey);
        }
    }

    private void OnEditorInput(ChangeEventArgs e) => _edit.SetDraftText(e.Value as string ?? Convert.ToString(e.Value, CultureInfo.InvariantCulture) ?? "");

    private void OnEditorChecked(ChangeEventArgs e) => _edit.SetDraft(e.Value is true || e.Value is "true" || e.Value is "on");

    /// <summary>Leaving the cell commits when valid (spreadsheet behaviour); invalid edits stay open.</summary>
    private void OnEditorBlur()
    {
        if (_edit.IsEditing) _edit.Commit();
    }

    // ---- clipboard & export ----

    private List<GridColumn<T>> ExportColumns() => _layout.Columns.Select(c => c.Column).Where(c => c.Type != GridColumnType.Actions).ToList();

    private async Task<IReadOnlyList<T>> ExportRowsAsync(GridExportScope scope)
    {
        if (_cache is not null && _source is not null)
        {
            if (scope == GridExportScope.Selected && !_selection.AllMatching)
                return _selection.Selected.Select(k => _serverItems.TryGetValue(k, out var it) ? (true, it) : (false, default!)).Where(x => x.Item1).Select(x => x.Item2).ToList();
            var total = _cache.TotalCount ?? 0;
            var res = await _source.QueryAsync(GridQuery.FromState(_state, 0, total));
            var items = res.Items.ToList();
            return scope == GridExportScope.Selected ? items.Where((it, i) => _selection.IsSelected(KeyOf(it, i))).ToList() : items;
        }
        if (_result is null) return [];
        if (scope == GridExportScope.All) return _items;
        if (scope == GridExportScope.Selected)
        {
            var map = KeyToItem();
            return _selection.SelectedIn(_result.ItemKeys).Select(k => map[k]).ToList();
        }
        return _result.Items;
    }

    private async Task CopyAsync(bool withHeader)
    {
        IReadOnlyList<T> rows;
        if (_selection.Count > 0) rows = await ExportRowsAsync(GridExportScope.Selected);
        else
        {
            var s = _active.Row < SlotCount ? GetSlot(_active.Row) : default;
            rows = s.Kind == SlotKind.Data && s.Item is not null ? [s.Item] : [];
        }
        if (rows.Count == 0) return;
        var text = GridExport.ToTsv(ExportColumns(), rows, withHeader, (item, c) => _edit.GetValue(item, KeyOf(item, -1), c));
        await JsSafe.Run(async () => await Js.CopyTextAsync(text));
        await Copied.InvokeAsync(new GridCopyEventArgs(text, rows.Count));
        _announcement = $"Copied {rows.Count} row{(rows.Count == 1 ? "" : "s")}";
    }

    // ---- header interactions ----

    private void OnSortClick(MouseEventArgs e, ResolvedColumn<T> c)
    {
        if (!c.Column.Sortable) return;
        var next = _state.ToggleSort(c.Field, e.ShiftKey);
        var dir = next.SortOf(c.Field);
        CommitState(next, dir is { } d ? $"Sorted by {c.Column.DisplayTitle}, {Names.Kebab(d.ToString())}" : $"Sort removed from {c.Column.DisplayTitle}");
    }

    private async Task OnSortKeyDown(KeyboardEventArgs e, ResolvedColumn<T> c)
    {
        if (!e.AltKey || e.Key is not ("ArrowLeft" or "ArrowRight") || !c.Column.Reorderable) return;
        var order = _layout.Order;
        CommitState(_state.MoveColumn(order, c.Field, IndexOf(order, c.Field) + (e.Key == "ArrowLeft" ? -1 : 1)), $"{c.Column.DisplayTitle} moved");
        await Task.Yield();
        await JsSafe.Run(() => Js.FocusSelectorAsync(_viewport, $".sl-data-grid__header-cell[data-field=\"{c.Field}\"] .sl-data-grid__sort"));
    }

    private void Autosize(ResolvedColumn<T> c)
    {
        var sample = new List<string>();
        for (var i = 0; i < SlotCount && sample.Count < 200; i++)
        {
            var s = GetSlot(i);
            if (s.Kind != SlotKind.Data || s.Item is null) continue;
            sample.Add(c.Column.DisplayText(c.Column.GetValue(s.Item)));
        }
        var width = GridLayout.EstimateWidth(c.Column, sample);
        CommitState(_state.ResizeColumn(c.Field, width, c.Column.MinWidth, c.Column.MaxWidth), $"{c.Column.DisplayTitle} resized to fit");
    }

    private void OnMenuSelect(MenuSelectEventArgs args, ResolvedColumn<T> c)
    {
        var title = c.Column.DisplayTitle;
        switch (args.Value)
        {
            case "asc": CommitState(_state.SetSorts(new GridSort(c.Field, SortDirection.Ascending)), $"Sorted by {title}, ascending"); break;
            case "desc": CommitState(_state.SetSorts(new GridSort(c.Field, SortDirection.Descending)), $"Sorted by {title}, descending"); break;
            case "unsort": CommitState(_state with { Sorts = [.. _state.Sorts.Where(s => s.Field != c.Field)] }, $"Sort removed from {title}"); break;
            case "filter": OpenFilterFor(c.Field); break;
            case "group": CommitState(_state.AddGroupBy(c.Field), $"Grouped by {title}"); break;
            case "ungroup": CommitState(_state.RemoveGroupBy(c.Field), $"Ungrouped {title}"); break;
            case "pin-start": CommitState(_state.PinColumn(c.Field, GridPin.Start), $"{title} pinned"); break;
            case "pin-end": CommitState(_state.PinColumn(c.Field, GridPin.End), $"{title} pinned"); break;
            case "unpin": CommitState(_state.PinColumn(c.Field, GridPin.None), $"{title} unpinned"); break;
            case "autosize": Autosize(c); break;
            case "hide": CommitState(_state.SetColumnHidden(c.Field, true), $"{title} hidden"); break;
            case "columns": _chooserOpen = true; break;
        }
    }

    private void OpenFilterFor(string field)
    {
        var c = EngineColumn(field);
        if (c is null) return;
        RevealColumn(_layout.Find(field));
        _openFilter = field;
        _filterDraft = GridCells.DraftFrom(field, c.Type, _state.Filters.FirstOrDefault(f => f.Field == field));
    }

    private void OnFilterOpenChanged(string field, bool open)
    {
        if (open) OpenFilterFor(field);
        else if (_openFilter == field)
        {
            _openFilter = null;
            _filterDraft = null;
        }
    }

    private void ApplyFilter(GridColumn<T> c)
    {
        var d = _filterDraft;
        if (d is null) return;
        var f = GridCells.FilterFromDraft(c.Type, d);
        var next = f is not null ? _state.SetFilter(f) : _state.ClearFilter(c.Field);
        _openFilter = null;
        _filterDraft = null;
        CommitState(next, f is not null ? $"Filtered {c.DisplayTitle}" : $"Filter cleared from {c.DisplayTitle}");
    }

    private void ClearFilterDraft(GridColumn<T> c)
    {
        _filterDraft = GridCells.DraftFrom(c.Field, c.Type, null);
        ApplyFilter(c);
    }

    private IReadOnlyList<string> FilterOptionsFor(DataGridColumn<T>? def, GridColumn<T> c)
    {
        if (def?.FilterOptions is { } options) return options;
        if (c.EnumOrder is { } order) return order;
        var seen = new HashSet<string>();
        foreach (var it in _cache is not null ? (IEnumerable<T>)_serverItems.Values : _items)
        {
            var v = c.GetValue(it);
            if (v is not null) seen.Add(c.DisplayText(v));
            if (seen.Count >= 200) break;
        }
        return seen.Order(StringComparer.OrdinalIgnoreCase).ToList();
    }

    private void OnMenuOpenChanged(string field, bool open)
    {
        if (open) _openMenu = field;
        else if (_openMenu == field) _openMenu = null;
    }

    private void OnActionsOpenChanged(string key, bool open)
    {
        if (open) _openActionsKey = key;
        else if (_openActionsKey == key)
        {
            _openActionsKey = null;
            _pendingFocus = "viewport"; // the trigger is swapped back to static markup
        }
    }

    private async Task OnActionSelected(MenuSelectEventArgs args, DataGridColumn<T>? def, T item)
    {
        if (def?.Actions is not { } actions || !int.TryParse(args.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var i) || i < 0 || i >= actions.Count) return;
        if (actions[i].Run is { } run) await run(item);
    }

    private Task OnExportSelected(MenuSelectEventArgs args)
    {
        var parts = (args.Value ?? "csv:visible").Split(':');
        var format = parts[0] == "tsv" ? GridExportFormat.Tsv : GridExportFormat.Csv;
        var scope = parts.Length > 1 ? parts[1] switch { "selected" => GridExportScope.Selected, "all" => GridExportScope.All, _ => GridExportScope.Visible } : GridExportScope.Visible;
        return ExportAsync(format, scope);
    }

    private void ToggleDensity() => _densityOverride = IsComfortable ? Slate.Density.Compact : Slate.Density.Comfortable;

    private void OnPageChanged(int page)
    {
        var current = (_cache is not null ? _state.PageIndex : _result?.PageIndex ?? 0) + 1;
        if (page != current) CommitState(_state.SetPage(page - 1));
    }

    private void OnPageSizeChanged(int? size)
    {
        if (size is { } s) CommitState(_state.SetPageSize(s));
    }

    private static bool SameList(IReadOnlyList<string>? a, IReadOnlyList<string>? b) =>
        ReferenceEquals(a, b) || (a is not null && b is not null && a.SequenceEqual(b));

    public async ValueTask DisposeAsync()
    {
        _disposed = true;
        if (_cache is not null) _cache.Changed -= OnCacheChanged;
        _selection.Changed -= OnSelectionModelChanged;
        _edit.Committed -= OnEditCommitted;
        if (_jsReady) await JsSafe.Run(() => Js.GridDisposeAsync(_viewport));
        _dotnet?.Dispose();
        GC.SuppressFinalize(this);
    }
}
