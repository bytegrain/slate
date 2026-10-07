using System.Collections;
using System.Collections.Specialized;
using Avalonia;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Collections;
using Avalonia.Controls;
using Avalonia.Controls.Metadata;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Metadata;
using Avalonia.Threading;
using Slate.Data;

namespace Slate.Avalonia.Controls;

/// <summary>
/// Slate's data grid (docs/design/data-grid.md): virtualised rows and columns, pinning, multi-sort, typed filters,
/// grouping with aggregates, tree data, row detail, keyed selection, keyboard grid navigation, inline and batch
/// editing, export, and server-side data. All data logic lives in the Slate.Core engine (<see cref="DataPipeline{T}"/>,
/// <see cref="GridState"/>, <see cref="SelectionModel{TKey}"/>, <see cref="EditSession{T}"/>, <see cref="GridRowCache{T}"/>);
/// this control renders the view window and forwards input.
/// </summary>
[TemplatePart("PART_Toolbar", typeof(Border))]
[TemplatePart("PART_GroupBar", typeof(Border))]
[TemplatePart("PART_Header", typeof(DataGridHeaderRow))]
[TemplatePart("PART_Scroll", typeof(ScrollViewer))]
[TemplatePart("PART_Rows", typeof(DataGridRowsPresenter))]
[TemplatePart("PART_Footer", typeof(DataGridFooterRow))]
[TemplatePart("PART_BottomBar", typeof(Border))]
[TemplatePart("PART_Empty", typeof(ContentControl))]
[PseudoClasses(":striped", ":bordered", ":loading", ":empty", ":keyboard-focus")]
public partial class DataGrid : TemplatedControl
{
    // ---- canonical options (design/api/components.json "DataGrid") ----

    public static readonly StyledProperty<IEnumerable?> ItemsProperty = AvaloniaProperty.Register<DataGrid, IEnumerable?>(nameof(Items));

    /// <summary>Server mode: overrides <see cref="Items"/>. Wrap a typed source with <see cref="GridDataSourceAdapter.From{T}"/>.</summary>
    public static readonly StyledProperty<IGridDataSource<object>?> DataSourceProperty =
        AvaloniaProperty.Register<DataGrid, IGridDataSource<object>?>(nameof(DataSource));

    public static readonly StyledProperty<Func<object, object?>?> RowKeyProperty = AvaloniaProperty.Register<DataGrid, Func<object, object?>?>(nameof(RowKey));

    /// <summary>XAML convenience for <see cref="RowKey"/>: the property holding each row's stable identity.</summary>
    public static readonly StyledProperty<string?> RowKeyPathProperty = AvaloniaProperty.Register<DataGrid, string?>(nameof(RowKeyPath));

    public static readonly StyledProperty<GridSelectionMode> SelectionModeProperty =
        AvaloniaProperty.Register<DataGrid, GridSelectionMode>(nameof(SelectionMode), GridSelectionMode.None);

    public static readonly DirectProperty<DataGrid, IReadOnlyCollection<object>> SelectedItemsProperty =
        AvaloniaProperty.RegisterDirect<DataGrid, IReadOnlyCollection<object>>(nameof(SelectedItems), g => g.SelectedItems, (g, v) => g.SelectedItems = v,
            defaultBindingMode: global::Avalonia.Data.BindingMode.TwoWay);

    public static readonly StyledProperty<GridPagination> PaginationProperty = AvaloniaProperty.Register<DataGrid, GridPagination>(nameof(Pagination));
    public static readonly StyledProperty<int> PageSizeProperty = AvaloniaProperty.Register<DataGrid, int>(nameof(PageSize), 50);

    /// <summary>Null inherits; otherwise scopes Slate's density (control and row heights) to this grid.</summary>
    public static readonly StyledProperty<Density?> DensityProperty = AvaloniaProperty.Register<DataGrid, Density?>(nameof(Density));

    public static readonly StyledProperty<bool> StripedProperty = AvaloniaProperty.Register<DataGrid, bool>(nameof(Striped));
    public static readonly StyledProperty<bool> BorderedProperty = AvaloniaProperty.Register<DataGrid, bool>(nameof(Bordered));

    public static readonly StyledProperty<string> QuickFilterProperty =
        AvaloniaProperty.Register<DataGrid, string>(nameof(QuickFilter), "", defaultBindingMode: global::Avalonia.Data.BindingMode.TwoWay);

    public static readonly StyledProperty<bool> ShowToolbarProperty = AvaloniaProperty.Register<DataGrid, bool>(nameof(ShowToolbar));
    public static readonly StyledProperty<bool> ShowFooterProperty = AvaloniaProperty.Register<DataGrid, bool>(nameof(ShowFooter));
    public static readonly StyledProperty<bool> GroupableProperty = AvaloniaProperty.Register<DataGrid, bool>(nameof(Groupable));

    public static readonly StyledProperty<IReadOnlyList<string>?> GroupByProperty =
        AvaloniaProperty.Register<DataGrid, IReadOnlyList<string>?>(nameof(GroupBy), defaultBindingMode: global::Avalonia.Data.BindingMode.TwoWay);

    public static readonly StyledProperty<Func<object, IEnumerable<object>?>?> ChildrenSelectorProperty =
        AvaloniaProperty.Register<DataGrid, Func<object, IEnumerable<object>?>?>(nameof(ChildrenSelector));

    public static readonly StyledProperty<IDataTemplate?> RowDetailProperty = AvaloniaProperty.Register<DataGrid, IDataTemplate?>(nameof(RowDetail));

    /// <summary>Height of an expanded detail row, in row heights.</summary>
    public static readonly StyledProperty<int> DetailRowSpanProperty = AvaloniaProperty.Register<DataGrid, int>(nameof(DetailRowSpan), 4);

    public static readonly StyledProperty<Func<object, Tone?>?> RowToneProperty = AvaloniaProperty.Register<DataGrid, Func<object, Tone?>?>(nameof(RowTone));
    public static readonly StyledProperty<GridEditMode> EditModeProperty = AvaloniaProperty.Register<DataGrid, GridEditMode>(nameof(EditMode));
    public static readonly StyledProperty<bool> LoadingProperty = AvaloniaProperty.Register<DataGrid, bool>(nameof(Loading));
    public static readonly StyledProperty<object?> EmptyContentProperty = AvaloniaProperty.Register<DataGrid, object?>(nameof(EmptyContent));
    public static readonly StyledProperty<object?> ToolbarContentProperty = AvaloniaProperty.Register<DataGrid, object?>(nameof(ToolbarContent));

    /// <summary>Actions shown in the selection bar while rows are selected ("3 selected · Archive · Delete").</summary>
    public static readonly StyledProperty<object?> SelectionActionsProperty = AvaloniaProperty.Register<DataGrid, object?>(nameof(SelectionActions));

    public static readonly DirectProperty<DataGrid, GridState> StateProperty =
        AvaloniaProperty.RegisterDirect<DataGrid, GridState>(nameof(State), g => g.State, (g, v) => g.State = v,
            defaultBindingMode: global::Avalonia.Data.BindingMode.TwoWay);

    public static readonly RoutedEvent<RoutedEventArgs> SelectionChangedEvent =
        RoutedEvent.Register<DataGrid, RoutedEventArgs>(nameof(SelectionChanged), RoutingStrategies.Bubble);

    public static readonly RoutedEvent<RoutedEventArgs> StateChangedEvent =
        RoutedEvent.Register<DataGrid, RoutedEventArgs>(nameof(StateChanged), RoutingStrategies.Bubble);

    public static readonly RoutedEvent<GridRowEventArgs> RowActivatedEvent =
        RoutedEvent.Register<DataGrid, GridRowEventArgs>(nameof(RowActivated), RoutingStrategies.Bubble);

    public static readonly RoutedEvent<GridCellEditEventArgs> CellEditCommittedEvent =
        RoutedEvent.Register<DataGrid, GridCellEditEventArgs>(nameof(CellEditCommitted), RoutingStrategies.Bubble);

    // ---- engine ----

    private readonly AvaloniaList<GridColumn> _columns = [];
    private List<GridColumn<object>> _engineColumns = [];
    private Dictionary<string, GridColumn> _defs = new();
    private DataPipeline<object> _pipeline = new([]);
    private IReadOnlyList<object> _items = [];
    private INotifyCollectionChanged? _observed;
    private GridPipelineResult<object>? _result;
    private GridColumnLayout<object> _layout = new([], [], 0);
    private SelectionModel<string> _selection = new(GridSelectionMode.None);
    private EditSession<object> _edit = new(GridEditMode.Cell);
    private GridRowCache<object>? _cache;
    private GridState _state = GridState.Empty;
    private IReadOnlyCollection<object> _selectedItems = [];
    private readonly Dictionary<string, object> _itemsByKey = new(StringComparer.Ordinal);
    private bool _refreshQueued;
    private bool _syncingState;

    public DataGrid()
    {
        _columns.CollectionChanged += (_, _) => RebuildColumns();
        // Theme, density and accent changes swap resources: row height and the composited pinned-cell brushes follow.
        ActualThemeVariantChanged += (_, _) => QueueRefresh();
        ResourcesChanged += (_, _) => QueueRefresh();
        AddHandler(KeyDownEvent, OnGridKeyDown, RoutingStrategies.Tunnel);
    }

    static DataGrid()
    {
        FocusableProperty.OverrideDefaultValue<DataGrid>(true);
        ItemsProperty.Changed.AddClassHandler<DataGrid>((g, _) => g.OnItemsChanged());
        DataSourceProperty.Changed.AddClassHandler<DataGrid>((g, _) => g.OnDataSourceChanged());
        RowKeyProperty.Changed.AddClassHandler<DataGrid>((g, _) => g.RebuildPipeline());
        RowKeyPathProperty.Changed.AddClassHandler<DataGrid>((g, _) => g.RebuildPipeline());
        ChildrenSelectorProperty.Changed.AddClassHandler<DataGrid>((g, _) => g.RebuildPipeline());
        PaginationProperty.Changed.AddClassHandler<DataGrid>((g, _) => g.RebuildPipeline());
        PageSizeProperty.Changed.AddClassHandler<DataGrid>((g, e) => g.Apply(g.State.SetPageSize(e.GetNewValue<int>())));
        SelectionModeProperty.Changed.AddClassHandler<DataGrid>((g, _) => g.ResetSelection());
        EditModeProperty.Changed.AddClassHandler<DataGrid>((g, _) => g.ResetEditing());
        QuickFilterProperty.Changed.AddClassHandler<DataGrid>((g, e) =>
        {
            if (!g._syncingState) g.Apply(g.State.SetQuickFilter(e.GetNewValue<string>() ?? ""));
        });
        GroupByProperty.Changed.AddClassHandler<DataGrid>((g, e) =>
        {
            if (!g._syncingState) g.Apply(g.State.SetGroupBy([.. e.GetNewValue<IReadOnlyList<string>?>() ?? []]));
        });
        DensityProperty.Changed.AddClassHandler<DataGrid>((g, e) => Sl.SetDensity(g, e.GetNewValue<Density?>()));
        StripedProperty.Changed.AddClassHandler<DataGrid>((g, e) => { g.PseudoClasses.Set(":striped", e.GetNewValue<bool>()); g.QueueRefresh(); });
        BorderedProperty.Changed.AddClassHandler<DataGrid>((g, e) => { g.PseudoClasses.Set(":bordered", e.GetNewValue<bool>()); g.QueueRefresh(); });
        LoadingProperty.Changed.AddClassHandler<DataGrid>((g, e) => { g.PseudoClasses.Set(":loading", e.GetNewValue<bool>()); g.QueueRefresh(); });
        ShowToolbarProperty.Changed.AddClassHandler<DataGrid>((g, _) => g.QueueRefresh());
        ShowFooterProperty.Changed.AddClassHandler<DataGrid>((g, _) => g.QueueRefresh());
        GroupableProperty.Changed.AddClassHandler<DataGrid>((g, _) => g.QueueRefresh());
        RowToneProperty.Changed.AddClassHandler<DataGrid>((g, _) => g.QueueRefresh());
        RowDetailProperty.Changed.AddClassHandler<DataGrid>((g, _) => g.QueueRefresh());
        DetailRowSpanProperty.Changed.AddClassHandler<DataGrid>((g, _) => g.QueueRefresh());
        ToolbarContentProperty.Changed.AddClassHandler<DataGrid>((g, _) => g.QueueRefresh());
        SelectionActionsProperty.Changed.AddClassHandler<DataGrid>((g, _) => g.QueueRefresh());
    }

    public IEnumerable? Items { get => GetValue(ItemsProperty); set => SetValue(ItemsProperty, value); }
    public IGridDataSource<object>? DataSource { get => GetValue(DataSourceProperty); set => SetValue(DataSourceProperty, value); }

    /// <summary>Column definitions (XAML: <c>&lt;sl:DataGrid.Columns&gt;</c>).</summary>
    [Content]
    public AvaloniaList<GridColumn> Columns => _columns;

    public Func<object, object?>? RowKey { get => GetValue(RowKeyProperty); set => SetValue(RowKeyProperty, value); }
    public string? RowKeyPath { get => GetValue(RowKeyPathProperty); set => SetValue(RowKeyPathProperty, value); }
    public GridSelectionMode SelectionMode { get => GetValue(SelectionModeProperty); set => SetValue(SelectionModeProperty, value); }
    public GridPagination Pagination { get => GetValue(PaginationProperty); set => SetValue(PaginationProperty, value); }
    public int PageSize { get => GetValue(PageSizeProperty); set => SetValue(PageSizeProperty, value); }
    public Density? Density { get => GetValue(DensityProperty); set => SetValue(DensityProperty, value); }
    public bool Striped { get => GetValue(StripedProperty); set => SetValue(StripedProperty, value); }
    public bool Bordered { get => GetValue(BorderedProperty); set => SetValue(BorderedProperty, value); }
    public string QuickFilter { get => GetValue(QuickFilterProperty); set => SetValue(QuickFilterProperty, value); }
    public bool ShowToolbar { get => GetValue(ShowToolbarProperty); set => SetValue(ShowToolbarProperty, value); }
    public bool ShowFooter { get => GetValue(ShowFooterProperty); set => SetValue(ShowFooterProperty, value); }
    public bool Groupable { get => GetValue(GroupableProperty); set => SetValue(GroupableProperty, value); }
    public IReadOnlyList<string>? GroupBy { get => GetValue(GroupByProperty); set => SetValue(GroupByProperty, value); }
    public Func<object, IEnumerable<object>?>? ChildrenSelector { get => GetValue(ChildrenSelectorProperty); set => SetValue(ChildrenSelectorProperty, value); }
    public IDataTemplate? RowDetail { get => GetValue(RowDetailProperty); set => SetValue(RowDetailProperty, value); }
    public int DetailRowSpan { get => GetValue(DetailRowSpanProperty); set => SetValue(DetailRowSpanProperty, value); }
    public Func<object, Tone?>? RowTone { get => GetValue(RowToneProperty); set => SetValue(RowToneProperty, value); }
    public GridEditMode EditMode { get => GetValue(EditModeProperty); set => SetValue(EditModeProperty, value); }
    public bool Loading { get => GetValue(LoadingProperty); set => SetValue(LoadingProperty, value); }
    public object? EmptyContent { get => GetValue(EmptyContentProperty); set => SetValue(EmptyContentProperty, value); }
    public object? ToolbarContent { get => GetValue(ToolbarContentProperty); set => SetValue(ToolbarContentProperty, value); }
    public object? SelectionActions { get => GetValue(SelectionActionsProperty); set => SetValue(SelectionActionsProperty, value); }

    /// <summary>Selected rows. In server mode with "select all matching", only loaded rows are listed; see <see cref="Selection"/>.</summary>
    public IReadOnlyCollection<object> SelectedItems
    {
        get => _selectedItems;
        set
        {
            var keys = (value ?? []).Select(KeyOf).ToList();
            _selection.Set(keys);
        }
    }

    /// <summary>
    /// Layout, sort, filters, grouping, expansion and paging. Serialise with <see cref="GridState.ToJson"/> to persist
    /// and assign back to restore exactly.
    /// </summary>
    public GridState State
    {
        get => _state;
        set => Apply(value ?? GridState.Empty);
    }

    public event EventHandler<RoutedEventArgs>? SelectionChanged { add => AddHandler(SelectionChangedEvent, value); remove => RemoveHandler(SelectionChangedEvent, value); }
    public event EventHandler<RoutedEventArgs>? StateChanged { add => AddHandler(StateChangedEvent, value); remove => RemoveHandler(StateChangedEvent, value); }
    public event EventHandler<GridRowEventArgs>? RowActivated { add => AddHandler(RowActivatedEvent, value); remove => RemoveHandler(RowActivatedEvent, value); }
    public event EventHandler<GridCellEditEventArgs>? CellEditCommitted { add => AddHandler(CellEditCommittedEvent, value); remove => RemoveHandler(CellEditCommittedEvent, value); }

    // ---- read-only views for renderers, tests and apps ----

    /// <summary>The keyed selection model (select-all-matching, exclusions, anchor).</summary>
    public SelectionModel<string> Selection => _selection;

    /// <summary>The edit session (pending batch changes, current edit).</summary>
    public EditSession<object> Editing => _edit;

    /// <summary>The latest pipeline result (client mode).</summary>
    public GridPipelineResult<object>? Result => _result;

    public GridColumnLayout<object> Layout => _layout;
    public IReadOnlyList<GridColumn<object>> EngineColumns => _engineColumns;
    public bool IsServerMode => _cache is not null;
    internal GridRowCache<object>? Cache => _cache;

    /// <summary>The active (keyboard) cell, or null.</summary>
    public GridCell? ActiveCell { get; private set; }

    /// <summary>Rows in the current view (client: pipeline rows; server: total count or a skeleton page while unknown).</summary>
    public int ViewRowCount => _cache is { } c ? (Pagination == GridPagination.Pages ? PageSlice().Count : c.TotalCount ?? SkeletonRowCount) : _result?.Rows.Count ?? 0;

    /// <summary>Data rows matching the current filters (server: total count).</summary>
    public int MatchingCount => _cache is { } c ? c.TotalCount ?? 0 : _result?.FilteredCount ?? 0;

    internal const int SkeletonRowCount = 20;

    /// <summary>The id <see cref="GridState.ToggledGroups"/> uses for a group row or a tree node (the pipeline's "row:" prefix).</summary>
    internal static string? ExpandId(GridViewRow<object>? row) =>
        row is null ? null : row.Kind == GridRowKind.Group ? row.GroupId : row.HasChildren ? "row:" + row.Key : null;

    internal GridColumn? Definition(string field) => _defs.GetValueOrDefault(field);

    // ---- items & columns ----

    private void OnItemsChanged()
    {
        if (_observed is not null) _observed.CollectionChanged -= OnCollectionChanged;
        _observed = Items as INotifyCollectionChanged;
        if (_observed is not null) _observed.CollectionChanged += OnCollectionChanged;
        Materialize();
        _pipeline.Invalidate();
        QueueRefresh();
    }

    private void Materialize() =>
        _items = Items switch
        {
            null => [],
            IReadOnlyList<object> list => list,
            IList list => list.Cast<object>().ToList(),
            var e => e.Cast<object>().ToList(),
        };

    private void OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (Items is not IReadOnlyList<object>) Materialize();
        _pipeline.Invalidate();
        if (e.Action == NotifyCollectionChangedAction.Replace && e.NewItems is not null)
            foreach (var item in e.NewItems) Flash(item!);
        QueueRefresh();
    }

    /// <summary>Re-reads row values (call after mutating items in place) and flashes the given rows.</summary>
    public void NotifyItemsChanged(params object[] changed)
    {
        _pipeline.Invalidate();
        foreach (var item in changed) Flash(item);
        QueueRefresh();
    }

    /// <summary>
    /// Re-reads the <see cref="Columns"/> definitions. Adding or removing columns does this automatically; call it after
    /// changing a column's properties in place (columns are plain objects). Also runs when the grid is attached.
    /// </summary>
    public void RefreshColumns() => RebuildColumns();

    protected override void OnAttachedToVisualTree(global::Avalonia.VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        RebuildColumns();
    }

    private void RebuildColumns()
    {
        _engineColumns = _columns.Select(c => c.ToEngine()).ToList();
        _defs = _columns.ToDictionary(c => c.Field, StringComparer.Ordinal);
        RebuildPipeline();
    }

    private void RebuildPipeline()
    {
        Func<object, object?>? key = RowKey;
        if (key is null && RowKeyPath is { Length: > 0 } path)
            key = item => GridColumnAccess.Read(item, path);
        _pipeline = new DataPipeline<object>(_engineColumns, new DataPipelineOptions<object>
        {
            RowKey = key,
            ChildrenSelector = ChildrenSelector is { } children ? item => children(item) : null,
            Paginate = Pagination == GridPagination.Pages && _cache is null,
        });
        QueueRefresh();
    }

    private void OnDataSourceChanged()
    {
        if (_cache is not null) _cache.Changed -= OnCacheChanged;
        _cache = DataSource is { } source ? new GridRowCache<object>(source, blockSize: 100) : null;
        if (_cache is not null)
        {
            _cache.Changed += OnCacheChanged;
            _cache.SetQuery(GridQuery.FromState(_state));
        }
        RebuildPipeline();
    }

    private void OnCacheChanged(object? sender, EventArgs e)
    {
        if (Dispatcher.UIThread.CheckAccess()) QueueRefresh();
        else Dispatcher.UIThread.Post(QueueRefresh);
    }

    internal string KeyOf(object item)
    {
        var key = RowKey?.Invoke(item) ?? (RowKeyPath is { Length: > 0 } p ? GridColumnAccess.Read(item, p) : null);
        return key?.ToString() ?? (_items is IList list ? list.IndexOf(item).ToString(System.Globalization.CultureInfo.InvariantCulture) : item.GetHashCode().ToString(System.Globalization.CultureInfo.InvariantCulture));
    }

    // ---- state ----

    /// <summary>Applies a new state (every header/menu/toolbar action goes through here).</summary>
    public void Apply(GridState state)
    {
        if (state.Equals(_state))
            return;
        var old = _state;
        _state = state;
        _syncingState = true;
        try
        {
            SetCurrentValue(QuickFilterProperty, state.QuickFilter);
            SetCurrentValue(GroupByProperty, state.GroupBy);
        }
        finally { _syncingState = false; }

        if (_cache is not null && !GridQuery.FromState(old).SameResultSet(GridQuery.FromState(state)))
            _cache.SetQuery(GridQuery.FromState(state));

        RaisePropertyChanged(StateProperty, old, state);
        RaiseEvent(new RoutedEventArgs(StateChangedEvent));
        QueueRefresh();
    }

    public void ToggleSort(string field, bool additive = false) => Apply(_state.ToggleSort(field, additive));
    public void SetFilter(GridFilter filter) => Apply(_state.SetFilter(filter));
    public void ClearFilter(string field) => Apply(_state.ClearFilter(field));
    public void ClearFilters() => Apply(_state.ClearFilters());
    public void AddGroupBy(string field) => Apply(_state.AddGroupBy(field));
    public void RemoveGroupBy(string field) => Apply(_state.RemoveGroupBy(field));
    public void ToggleGroup(string groupId) => Apply(_state.ToggleGroup(groupId));
    public void ToggleDetail(string rowKey) => Apply(_state.ToggleDetail(rowKey));
    public void PinColumn(string field, GridPin pin) => Apply(_state.PinColumn(field, pin));
    public void SetColumnHidden(string field, bool hidden) => Apply(_state.SetColumnHidden(field, hidden));
    public void ResizeColumn(string field, double width)
    {
        var col = _engineColumns.FirstOrDefault(c => c.Field == field);
        Apply(_state.ResizeColumn(field, width, col?.MinWidth ?? 48, col?.MaxWidth ?? 2000));
    }

    public void MoveColumn(string field, int toIndex) => Apply(_state.MoveColumn(_layout.Order, field, toIndex));

    /// <summary>Sizes a column to fit its header and a sample of its cell texts (double-click on the resize grip).</summary>
    public void AutoSizeColumn(string field)
    {
        var col = _engineColumns.FirstOrDefault(c => c.Field == field);
        if (col is null) return;
        var sample = (_result?.Items ?? LoadedItems()).Take(200).Select(i => col.DisplayText(_edit.GetValue(i, KeyOf(i), col)));
        ResizeColumn(field, GridLayout.EstimateWidth(col, sample));
    }

    // ---- refresh ----

    internal void QueueRefresh()
    {
        if (_refreshQueued) return;
        _refreshQueued = true;
        Dispatcher.UIThread.Post(() => { _refreshQueued = false; Refresh(); }, DispatcherPriority.Render);
    }

    /// <summary>Re-runs the pipeline (client mode) and re-renders. Normally queued automatically.</summary>
    public void Refresh()
    {
        _refreshQueued = false;
        if (_cache is null)
            _result = _pipeline.Run(_items, _state);
        else
            _result = null;

        IndexItems();
        _layout = GridLayout.Resolve(_engineColumns, _state, Math.Max(0, ViewportWidth - LeadWidth));
        PublishSelection(raise: false);
        ClampActiveCell();
        PseudoClasses.Set(":empty", ViewRowCount == 0 && !Loading);
        RenderChrome();
        _header?.Rebuild();
        _footer?.Rebuild();
        _rows?.Realize(force: true);
    }

    private void IndexItems()
    {
        _itemsByKey.Clear();
        if (_result is { } r)
        {
            for (var i = 0; i < r.Items.Count; i++)
                _itemsByKey[r.ItemKeys[i]] = r.Items[i];
        }
    }

    private IEnumerable<object> LoadedItems()
    {
        if (_cache is null) return _result?.Items ?? [];
        var list = new List<object>();
        var total = _cache.TotalCount ?? 0;
        for (var i = 0; i < total; i++)
            if (_cache.TryGet(i, out var item)) list.Add(item);
        return list;
    }

    // ---- rows (renderer access) ----

    /// <summary>Paging (server mode): the indices of the current page.</summary>
    internal (int First, int Count) PageSlice()
    {
        var total = _cache?.TotalCount ?? 0;
        var first = Math.Min(_state.PageIndex * _state.PageSize, Math.Max(0, total));
        return (first, Math.Max(0, Math.Min(_state.PageSize, total - first)));
    }

    /// <summary>Describes view row <paramref name="index"/> for rendering.</summary>
    internal DataGridRowInfo RowAt(int index)
    {
        if (_cache is { } cache)
        {
            var absolute = Pagination == GridPagination.Pages ? PageSlice().First + index : index;
            if (cache.TryGet(absolute, out var item))
            {
                var key = KeyOf(item);
                _itemsByKey[key] = item;
                return new DataGridRowInfo(index, GridRowKind.Data, key, item, 0, null);
            }
            return new DataGridRowInfo(index, GridRowKind.Data, "skeleton:" + absolute, null, 0, null) { IsSkeleton = true };
        }

        var row = _result!.Rows[index];
        return new DataGridRowInfo(index, row.Kind, row.Key, row.Item, row.Depth, row);
    }

    /// <summary>Server mode: asks the cache for the rows about to be shown.</summary>
    internal void EnsureLoaded(int first, int count)
    {
        if (_cache is null) return;
        var offset = Pagination == GridPagination.Pages ? PageSlice().First : 0;
        _ = _cache.EnsureRangeAsync(offset + first, Math.Max(1, count));
    }

    internal double ViewportWidth => _scroll?.Viewport.Width is > 0 and var w ? w : Bounds.Width;

    /// <summary>Width of the built-in leading columns (selection checkbox, detail expander).</summary>
    internal double LeadWidth => (SelectionMode == GridSelectionMode.Multi ? SelectionColumnWidth : 0) + (RowDetail is not null ? DetailColumnWidth : 0);

    internal const double SelectionColumnWidth = 40;
    internal const double DetailColumnWidth = 32;

    /// <summary>Row height: the larger of the grid token and the active density's control height.</summary>
    internal double RowHeight => Math.Max(Resource("Sl.Component.Grid.RowHeight", 32), Resource("Sl.Size.Control.Md", 32));

    internal double HeaderHeight => Math.Max(Resource("Sl.Component.Grid.HeaderHeight", 40), Resource("Sl.Size.Control.Lg", 40));

    internal double Resource(string key, double fallback) =>
        this.TryFindResource(key, ActualThemeVariant, out var v) && v is double d ? d : fallback;

    internal object? ItemByKey(string key) => _itemsByKey.GetValueOrDefault(key);

    // ---- selection ----

    private void ResetSelection()
    {
        _selection.Changed -= OnSelectionChanged;
        _selection = new SelectionModel<string>(SelectionMode);
        _selection.Changed += OnSelectionChanged;
        PublishSelection(raise: false);
        QueueRefresh();
    }

    private void OnSelectionChanged(object? sender, EventArgs e) => PublishSelection(raise: true);

    private void PublishSelection(bool raise)
    {
        var old = _selectedItems;
        _selectedItems = _itemsByKey.Where(kv => _selection.IsSelected(kv.Key)).Select(kv => kv.Value).ToList();
        if (!raise) return;
        RaisePropertyChanged(SelectedItemsProperty, old, _selectedItems);
        RaiseEvent(new RoutedEventArgs(SelectionChangedEvent));
        RenderChrome();
        _header?.Rebuild();
        _rows?.Realize(force: false);
    }

    internal IReadOnlyList<string> VisibleKeys =>
        _result?.VisibleKeys ?? Enumerable.Range(0, ViewRowCount).Select(i => RowAt(i)).Where(r => !r.IsSkeleton).Select(r => r.Key).ToList();

    /// <summary>Pointer selection on a row (click / Ctrl+click / Shift+click).</summary>
    public void ClickRow(string key, bool ctrl = false, bool shift = false)
    {
        if (SelectionMode == GridSelectionMode.None) return;
        _selection.Click(key, VisibleKeys, ctrl, shift);
    }

    /// <summary>Header checkbox: none/some → all (all matching in server mode), all → none.</summary>
    public void ToggleSelectAll()
    {
        if (SelectionMode != GridSelectionMode.Multi) return;
        var keys = _result?.ItemKeys ?? VisibleKeys;
        if (HeaderSelectionState == SelectAllState.All) _selection.Clear();
        else if (_cache is not null) _selection.SelectAllMatching(_cache.TotalCount ?? 0);
        else _selection.SelectAll(keys);
    }

    internal SelectAllState HeaderSelectionState =>
        _cache is not null && _selection.AllMatching ? (_selection.Excluded.Count == 0 ? SelectAllState.All : SelectAllState.Some)
        : _selection.HeaderState((_result?.ItemKeys ?? VisibleKeys).ToList());

    // ---- editing ----

    private void ResetEditing()
    {
        _edit.Changed -= OnEditChanged;
        _edit.Committed -= OnEditCommitted;
        _edit = new EditSession<object>(EditMode == GridEditMode.None ? GridEditMode.Cell : EditMode);
        _edit.Changed += OnEditChanged;
        _edit.Committed += OnEditCommitted;
        QueueRefresh();
    }

    private void OnEditChanged(object? sender, EventArgs e) { RenderChrome(); _rows?.Realize(force: false); }

    private void OnEditCommitted(object? sender, GridCellChange<object> change)
    {
        if (_edit.Mode == GridEditMode.Cell) _pipeline.Invalidate();
        RaiseEvent(new GridCellEditEventArgs(CellEditCommittedEvent, change));
        if (_edit.Mode == GridEditMode.Cell) QueueRefresh();
    }

    /// <summary>Starts editing the active cell (or the given one). Returns false if the cell isn't editable.</summary>
    public bool BeginEdit(GridCell? cell = null, string? initialText = null)
    {
        if (EditMode == GridEditMode.None || (cell ?? ActiveCell) is not { } c) return false;
        var info = c.Row < ViewRowCount ? RowAt(c.Row) : default;
        if (info.Item is null || info.Kind != GridRowKind.Data || c.Column >= _layout.Columns.Count) return false;
        var col = _layout.Columns[c.Column].Column;
        if (!col.Editable) return false;
        ActiveCell = c;
        var started = _edit.Begin(info.Item, info.Key, col, initialText);
        _rows?.Realize(force: false);
        _rows?.FocusEditor();
        return started;
    }

    /// <summary>Commits the current edit; returns false (and keeps editing) while the value is invalid.</summary>
    public bool CommitEdit()
    {
        if (!_edit.IsEditing) return true;
        var ok = _edit.Commit();
        _rows?.Realize(force: false);
        if (ok) Focus();
        return ok;
    }

    public void CancelEdit()
    {
        _edit.Cancel();
        _rows?.Realize(force: false);
        Focus();
    }

    /// <summary>Batch mode: writes every pending change to its row.</summary>
    public void CommitAll()
    {
        if (_edit.IsEditing && !_edit.Commit()) return;
        _edit.CommitAll(_engineColumns);
        _pipeline.Invalidate();
        QueueRefresh();
    }

    public void DiscardAll()
    {
        _edit.Cancel();
        _edit.DiscardAll();
        QueueRefresh();
    }

    // ---- export & clipboard ----

    public enum ExportScope { Visible, Selected, All }

    /// <summary>Rows for an export: visible (current filters/sort), selected, or all.</summary>
    public IReadOnlyList<object> RowsFor(ExportScope scope) => scope switch
    {
        ExportScope.Selected => (_result?.Items ?? LoadedItems()).Where(i => _selection.IsSelected(KeyOf(i))).ToList(),
        ExportScope.All => _cache is null ? _items : LoadedItems().ToList(),
        _ => (_result?.Items ?? LoadedItems()).ToList(),
    };

    private Func<object, GridColumn<object>, object?> ValueWithEdits => (item, col) => _edit.GetValue(item, KeyOf(item), col);

    private IReadOnlyList<GridColumn<object>> ExportColumns => _layout.Columns.Select(c => c.Column).ToList();

    public string ExportCsv(ExportScope scope = ExportScope.Visible) => GridExport.ToCsv(ExportColumns, RowsFor(scope), true, ValueWithEdits);

    public string ExportTsv(ExportScope scope = ExportScope.Visible, bool includeHeader = true) => GridExport.ToTsv(ExportColumns, RowsFor(scope), includeHeader, ValueWithEdits);

    /// <summary>
    /// Ctrl+C: the selected rows (or the active row) as TSV, also written to the clipboard. Ctrl+Shift+C includes headers.
    /// </summary>
    public string CopySelection(bool includeHeader = false)
    {
        IReadOnlyList<object> rows = _selection.Count > 0 ? RowsFor(ExportScope.Selected)
            : ActiveCell is { } c && c.Row < ViewRowCount && RowAt(c.Row).Item is { } item ? [item] : [];
        var text = GridExport.ToTsv(ExportColumns, rows, includeHeader, ValueWithEdits);
        LastCopiedText = text;
        if (TopLevel.GetTopLevel(this)?.Clipboard is { } clipboard)
            _ = clipboard.SetTextAsync(text);
        return text;
    }

    /// <summary>The text most recently copied by <see cref="CopySelection"/>.</summary>
    public string? LastCopiedText { get; private set; }

    /// <summary>Raised by the export menu with the produced text; the default handler offers a save dialog.</summary>
    public event EventHandler<GridExportEventArgs>? Exported;

    internal async void RunExport(ExportScope scope, bool csv)
    {
        var text = csv ? ExportCsv(scope) : ExportTsv(scope);
        var args = new GridExportEventArgs(scope, csv ? "csv" : "tsv", text);
        Exported?.Invoke(this, args);
        if (args.Handled) return;
        if (TopLevel.GetTopLevel(this)?.StorageProvider is { CanSave: true } storage)
        {
            var file = await storage.SaveFilePickerAsync(new global::Avalonia.Platform.Storage.FilePickerSaveOptions
            {
                SuggestedFileName = $"export.{args.Format}",
            });
            if (file is null) return;
            await using var stream = await file.OpenWriteAsync();
            await using var writer = new StreamWriter(stream);
            await writer.WriteAsync(text);
        }
    }

    // ---- activation & flash ----

    internal void Activate(DataGridRowInfo row)
    {
        if (row.Item is not null && row.Kind == GridRowKind.Data)
            RaiseEvent(new GridRowEventArgs(RowActivatedEvent, row.Item, row.Key));
    }

    private readonly Dictionary<string, long> _flash = new(StringComparer.Ordinal);

    internal bool IsFlashing(string key) => _flash.TryGetValue(key, out var until) && Environment.TickCount64 < until;

    private void Flash(object item)
    {
        if (SlateTheme.Current?.ReduceMotion == true) return;
        var key = KeyOf(item);
        _flash[key] = Environment.TickCount64 + 900;
        DispatcherTimer.RunOnce(() => { _flash.Remove(key); _rows?.Realize(force: false); }, TimeSpan.FromMilliseconds(950));
    }

    protected override AutomationPeer OnCreateAutomationPeer() => new DataGridPeer(this);

    private sealed class DataGridPeer(DataGrid owner) : ControlAutomationPeer(owner), ISelectionProvider
    {
        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.DataGrid;
        protected override string? GetNameCore() => base.GetNameCore() ?? "Data grid";
        public bool CanSelectMultiple => owner.SelectionMode == GridSelectionMode.Multi;
        public bool IsSelectionRequired => false;

        public IReadOnlyList<AutomationPeer> GetSelection() =>
            owner._rows?.Children.OfType<DataGridRow>().Where(r => r.IsVisible && r.IsSelected).Select(r => (AutomationPeer)ControlAutomationPeer.CreatePeerForElement(r)).ToList() ?? [];
    }
}

/// <summary>What a rendered row shows.</summary>
internal readonly record struct DataGridRowInfo(int Index, GridRowKind Kind, string Key, object? Item, int Depth, GridViewRow<object>? Row)
{
    public bool IsSkeleton { get; init; }
}

public sealed class GridExportEventArgs(DataGrid.ExportScope scope, string format, string text) : EventArgs
{
    public DataGrid.ExportScope Scope { get; } = scope;
    public string Format { get; } = format;
    public string Text { get; } = text;
    public bool Handled { get; set; }
}

/// <summary>Adapts a typed server data source to the grid's object-typed one.</summary>
public static class GridDataSourceAdapter
{
    public static IGridDataSource<object> From<T>(IGridDataSource<T> source) where T : class => new Adapter<T>(source);

    private sealed class Adapter<T>(IGridDataSource<T> inner) : IGridDataSource<object> where T : class
    {
        public async Task<GridResult<object>> QueryAsync(GridQuery query, CancellationToken cancellationToken = default)
        {
            var r = await inner.QueryAsync(query, cancellationToken).ConfigureAwait(false);
            return new GridResult<object>(r.Items.Cast<object>().ToList(), r.TotalCount);
        }
    }
}

/// <summary>Reads a value by property name or dictionary key (the grid's default accessor for keys).</summary>
internal static class GridColumnAccess
{
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<(Type, string), System.Reflection.PropertyInfo?> Properties = new();

    public static object? Read(object item, string path) => item switch
    {
        IDictionary<string, object?> d => d.TryGetValue(path, out var v) ? v : null,
        IReadOnlyDictionary<string, object?> rd => rd.TryGetValue(path, out var v) ? v : null,
        _ => Property(item.GetType(), path)?.GetValue(item),
    };

    private static System.Reflection.PropertyInfo? Property(Type type, string path) =>
        Properties.GetOrAdd((type, path), static k =>
            k.Item1.GetProperty(k.Item2, System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.IgnoreCase));

    /// <summary>Default setter for editable columns declared without one: writes the property (converting the parsed value) or dictionary entry.</summary>
    public static void Write(object item, string path, object? value)
    {
        if (item is IDictionary<string, object?> d) { d[path] = value; return; }
        if (Property(item.GetType(), path) is not { CanWrite: true } prop) return;
        var target = Nullable.GetUnderlyingType(prop.PropertyType) ?? prop.PropertyType;
        if (value is not null && !target.IsInstanceOfType(value))
            value = target.IsEnum ? Enum.Parse(target, value.ToString()!, ignoreCase: true)
                : Convert.ChangeType(value, target, System.Globalization.CultureInfo.InvariantCulture);
        prop.SetValue(item, value);
    }
}
