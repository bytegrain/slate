using System.Collections;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Threading;
using Slate.Data;

namespace Slate.Wpf;

/// <summary>Event data for <see cref="DataGrid.RowActivated"/>.</summary>
public sealed class GridRowEventArgs(RoutedEvent routedEvent, object item, string key) : RoutedEventArgs(routedEvent)
{
    public object Item { get; } = item;
    public string Key { get; } = key;
}

/// <summary>Event data for <see cref="DataGrid.CellEditCommitted"/>.</summary>
public sealed class GridCellEditEventArgs(RoutedEvent routedEvent, GridCellChange<object> change) : RoutedEventArgs(routedEvent)
{
    public object Item => Change.Item;
    public string Field => Change.Field;
    public object? OldValue => Change.OldValue;
    public object? NewValue => Change.NewValue;
    public GridCellChange<object> Change { get; } = change;
}

/// <summary>
/// Slate's data grid (docs/design/data-grid.md): virtualised rows and columns, pinning, resize/reorder/hide, multi-sort,
/// typed filters with chips, quick filter, grouping with aggregates, tree data, row detail, keyed selection, the
/// WAI-ARIA grid keyboard model, inline and batch editing, clipboard and CSV export, server data with infinite
/// scroll or paging, and a serialisable <see cref="State"/>. All grid logic lives in Slate.Core's engine
/// (via <see cref="GridController"/>); this control draws the view window and forwards input.
/// </summary>
[ContentProperty(nameof(Columns))]
[TemplatePart(Name = PartRoot, Type = typeof(Decorator))]
public partial class DataGrid : Control
{
    public const string PartRoot = "PART_Root";

    // ---- data ------------------------------------------------------------------------------------------------

    /// <summary>Client-mode rows (any IEnumerable; INotifyCollectionChanged sources refresh automatically).</summary>
    public static readonly DependencyProperty ItemsProperty = DependencyProperty.Register(
        nameof(Items), typeof(IEnumerable), typeof(DataGrid), new FrameworkPropertyMetadata(null, (d, e) => ((DataGrid)d).OnItemsChanged((IEnumerable?)e.OldValue, (IEnumerable?)e.NewValue)));

    /// <summary>Server mode: an <see cref="IGridDataSource{T}"/> of object. Overrides <see cref="Items"/>.</summary>
    public static readonly DependencyProperty DataSourceProperty = DependencyProperty.Register(
        nameof(DataSource), typeof(IGridDataSource<object>), typeof(DataGrid), new FrameworkPropertyMetadata(null, (d, e) => ((DataGrid)d).Controller.SetDataSource((IGridDataSource<object>?)e.NewValue)));

    public static readonly DependencyProperty RowKeyProperty = DependencyProperty.Register(
        nameof(RowKey), typeof(Func<object, object?>), typeof(DataGrid), new FrameworkPropertyMetadata(null, (d, e) => ((DataGrid)d).Controller.SetRowKey((Func<object, object?>?)e.NewValue)));

    public static readonly DependencyProperty ChildrenSelectorProperty = DependencyProperty.Register(
        nameof(ChildrenSelector), typeof(Func<object, IEnumerable?>), typeof(DataGrid), new FrameworkPropertyMetadata(null, (d, e) =>
        {
            var f = (Func<object, IEnumerable?>?)e.NewValue;
            ((DataGrid)d).Controller.SetChildren(f is null ? null : item => f(item)?.Cast<object>());
        }));

    // ---- behaviour -------------------------------------------------------------------------------------------

    public static readonly DependencyProperty SelectionModeProperty = DependencyProperty.Register(
        nameof(SelectionMode), typeof(GridSelectionMode), typeof(DataGrid), new FrameworkPropertyMetadata(GridSelectionMode.None, (d, e) => ((DataGrid)d).OnSelectionModeChanged()));

    public static readonly DependencyProperty SelectedItemsProperty = DependencyProperty.Register(
        nameof(SelectedItems), typeof(IReadOnlyCollection<object>), typeof(DataGrid),
        new FrameworkPropertyMetadata(Array.Empty<object>(), FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, (d, e) => ((DataGrid)d).OnSelectedItemsChanged()));

    public static readonly DependencyProperty PaginationProperty = DependencyProperty.Register(
        nameof(Pagination), typeof(GridPagination), typeof(DataGrid), new FrameworkPropertyMetadata(GridPagination.None, (d, e) => ((DataGrid)d).OnPaginationChanged()));

    public static readonly DependencyProperty PageSizeProperty = DependencyProperty.Register(
        nameof(PageSize), typeof(int), typeof(DataGrid), new FrameworkPropertyMetadata(50, (d, e) => ((DataGrid)d).Controller.SetPageSize((int)e.NewValue)));

    public static readonly DependencyProperty EditModeProperty = DependencyProperty.Register(
        nameof(EditMode), typeof(GridEditMode), typeof(DataGrid), new FrameworkPropertyMetadata(GridEditMode.None, (d, e) =>
        {
            var g = (DataGrid)d;
            g.Controller.EditMode = (GridEditMode)e.NewValue;
            g.UpdateChrome();
        }));

    public static readonly DependencyProperty StateProperty = DependencyProperty.Register(
        nameof(State), typeof(GridState), typeof(DataGrid),
        new FrameworkPropertyMetadata(GridState.Empty, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, (d, e) => ((DataGrid)d).OnStatePropertyChanged((GridState)e.NewValue)));

    public static readonly DependencyProperty QuickFilterProperty = DependencyProperty.Register(
        nameof(QuickFilter), typeof(string), typeof(DataGrid),
        new FrameworkPropertyMetadata("", FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, (d, e) => ((DataGrid)d).OnQuickFilterPropertyChanged((string?)e.NewValue ?? "")));

    public static readonly DependencyProperty GroupByProperty = DependencyProperty.Register(
        nameof(GroupBy), typeof(IReadOnlyList<string>), typeof(DataGrid),
        new FrameworkPropertyMetadata(Array.Empty<string>(), FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, (d, e) => ((DataGrid)d).OnGroupByPropertyChanged((IReadOnlyList<string>?)e.NewValue ?? [])));

    public static readonly DependencyProperty RowToneProperty = DependencyProperty.Register(
        nameof(RowTone), typeof(Func<object, Tone?>), typeof(DataGrid), new FrameworkPropertyMetadata(null, (d, e) =>
        {
            var g = (DataGrid)d;
            g.Controller.RowTone = (Func<object, Tone?>?)e.NewValue;
            g.InvalidateRows();
        }));

    // ---- appearance ------------------------------------------------------------------------------------------

    /// <summary>Density for this grid (null = inherit). Rows follow the density's control height.</summary>
    public static readonly DependencyProperty DensityProperty = DependencyProperty.Register(
        nameof(Density), typeof(Density?), typeof(DataGrid), new FrameworkPropertyMetadata(null, (d, e) => Sl.SetDensity(d, (Density?)e.NewValue)));

    public static readonly DependencyProperty StripedProperty = DependencyProperty.Register(
        nameof(Striped), typeof(bool), typeof(DataGrid), new FrameworkPropertyMetadata(false, (d, _) => ((DataGrid)d).InvalidateRows()));

    public static readonly DependencyProperty BorderedProperty = DependencyProperty.Register(
        nameof(Bordered), typeof(bool), typeof(DataGrid), new FrameworkPropertyMetadata(false, (d, _) => ((DataGrid)d).InvalidateRows()));

    public static readonly DependencyProperty ShowToolbarProperty = DependencyProperty.Register(
        nameof(ShowToolbar), typeof(bool), typeof(DataGrid), new FrameworkPropertyMetadata(false, (d, _) => ((DataGrid)d).UpdateChrome()));

    public static readonly DependencyProperty ShowFooterProperty = DependencyProperty.Register(
        nameof(ShowFooter), typeof(bool), typeof(DataGrid), new FrameworkPropertyMetadata(false, (d, _) => ((DataGrid)d).UpdateChrome()));

    public static readonly DependencyProperty GroupableProperty = DependencyProperty.Register(
        nameof(Groupable), typeof(bool), typeof(DataGrid), new FrameworkPropertyMetadata(false, (d, _) => ((DataGrid)d).UpdateChrome()));

    public static readonly DependencyProperty LoadingProperty = DependencyProperty.Register(
        nameof(Loading), typeof(bool), typeof(DataGrid), new FrameworkPropertyMetadata(false, (d, _) => ((DataGrid)d).UpdateChrome()));

    public static readonly DependencyProperty RowDetailProperty = DependencyProperty.Register(
        nameof(RowDetail), typeof(DataTemplate), typeof(DataGrid), new FrameworkPropertyMetadata(null, (d, _) => ((DataGrid)d).InvalidateLayoutAndRows()));

    /// <summary>Height of an open detail row (px).</summary>
    public static readonly DependencyProperty DetailHeightProperty = DependencyProperty.Register(
        nameof(DetailHeight), typeof(double), typeof(DataGrid), new FrameworkPropertyMetadata(160.0, (d, e) =>
        {
            var g = (DataGrid)d;
            g.Controller.DetailHeight = (double)e.NewValue;
            g.Controller.Remeasure();
            g.InvalidateRows();
        }));

    public static readonly DependencyProperty EmptyContentProperty = DependencyProperty.Register(
        nameof(EmptyContent), typeof(object), typeof(DataGrid), new FrameworkPropertyMetadata("No rows", (d, _) => ((DataGrid)d).UpdateChrome()));

    public static readonly DependencyProperty ToolbarContentProperty = DependencyProperty.Register(
        nameof(ToolbarContent), typeof(object), typeof(DataGrid), new FrameworkPropertyMetadata(null, (d, _) => ((DataGrid)d).UpdateChrome()));

    /// <summary>Actions shown in the selection bar while rows are selected (e.g. Archive, Delete buttons).</summary>
    public static readonly DependencyProperty SelectionActionsProperty = DependencyProperty.Register(
        nameof(SelectionActions), typeof(object), typeof(DataGrid), new FrameworkPropertyMetadata(null, (d, _) => ((DataGrid)d).UpdateChrome()));

    // ---- events ----------------------------------------------------------------------------------------------

    public static readonly RoutedEvent SelectionChangedEvent = EventManager.RegisterRoutedEvent(
        nameof(SelectionChanged), RoutingStrategy.Bubble, typeof(RoutedEventHandler), typeof(DataGrid));

    public static readonly RoutedEvent StateChangedEvent = EventManager.RegisterRoutedEvent(
        nameof(StateChanged), RoutingStrategy.Bubble, typeof(RoutedPropertyChangedEventHandler<GridState>), typeof(DataGrid));

    public static readonly RoutedEvent RowActivatedEvent = EventManager.RegisterRoutedEvent(
        nameof(RowActivated), RoutingStrategy.Bubble, typeof(EventHandler<GridRowEventArgs>), typeof(DataGrid));

    public static readonly RoutedEvent CellEditCommittedEvent = EventManager.RegisterRoutedEvent(
        nameof(CellEditCommitted), RoutingStrategy.Bubble, typeof(EventHandler<GridCellEditEventArgs>), typeof(DataGrid));

    private bool _syncing;
    private INotifyCollectionChanged? _observed;
    private readonly DispatcherTimer _flashTimer;

    static DataGrid()
    {
        DefaultStyleKeyProperty.OverrideMetadata(typeof(DataGrid), new FrameworkPropertyMetadata(typeof(DataGrid)));
        FocusableProperty.OverrideMetadata(typeof(DataGrid), new FrameworkPropertyMetadata(true));
        KeyboardNavigation.TabNavigationProperty.OverrideMetadata(typeof(DataGrid), new FrameworkPropertyMetadata(KeyboardNavigationMode.Local));
    }

    public DataGrid()
    {
        Columns.CollectionChanged += (_, _) => RebuildColumns();
        Controller.ViewChanged += (_, _) => OnUi(OnViewChanged);
        Controller.StateChanged += (_, _) => OnUi(OnControllerStateChanged);
        Controller.SelectionChanged += (_, _) => OnUi(OnControllerSelectionChanged);
        Controller.EditChanged += (_, _) => OnUi(() => { UpdateChrome(); InvalidateRows(); });
        Controller.CellCommitted += (_, change) => OnUi(() => RaiseEvent(new GridCellEditEventArgs(CellEditCommittedEvent, change)));

        _flashTimer = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(40) };
        _flashTimer.Tick += (_, _) =>
        {
            _surface?.InvalidateRowVisuals();
            if (!Controller.HasFlashes) _flashTimer.Stop();
        };

        InitTokens();
        Unloaded += (_, _) => _flashTimer.Stop();
    }

    /// <summary>The engine wiring (exposed for tests and advanced scenarios).</summary>
    public GridController Controller { get; } = new();

    /// <summary>Column definitions (the XAML content of the grid).</summary>
    public ObservableCollection<GridColumn> Columns { get; } = [];

    public IEnumerable? Items { get => (IEnumerable?)GetValue(ItemsProperty); set => SetValue(ItemsProperty, value); }
    public IGridDataSource<object>? DataSource { get => (IGridDataSource<object>?)GetValue(DataSourceProperty); set => SetValue(DataSourceProperty, value); }
    public Func<object, object?>? RowKey { get => (Func<object, object?>?)GetValue(RowKeyProperty); set => SetValue(RowKeyProperty, value); }
    public Func<object, IEnumerable?>? ChildrenSelector { get => (Func<object, IEnumerable?>?)GetValue(ChildrenSelectorProperty); set => SetValue(ChildrenSelectorProperty, value); }
    public GridSelectionMode SelectionMode { get => (GridSelectionMode)GetValue(SelectionModeProperty); set => SetValue(SelectionModeProperty, value); }
    public IReadOnlyCollection<object> SelectedItems { get => (IReadOnlyCollection<object>)GetValue(SelectedItemsProperty); set => SetValue(SelectedItemsProperty, value); }
    public GridPagination Pagination { get => (GridPagination)GetValue(PaginationProperty); set => SetValue(PaginationProperty, value); }
    public int PageSize { get => (int)GetValue(PageSizeProperty); set => SetValue(PageSizeProperty, value); }
    public GridEditMode EditMode { get => (GridEditMode)GetValue(EditModeProperty); set => SetValue(EditModeProperty, value); }
    public GridState State { get => (GridState)GetValue(StateProperty); set => SetValue(StateProperty, value); }
    public string QuickFilter { get => (string)GetValue(QuickFilterProperty); set => SetValue(QuickFilterProperty, value); }
    public IReadOnlyList<string> GroupBy { get => (IReadOnlyList<string>)GetValue(GroupByProperty); set => SetValue(GroupByProperty, value); }
    public Func<object, Tone?>? RowTone { get => (Func<object, Tone?>?)GetValue(RowToneProperty); set => SetValue(RowToneProperty, value); }
    public Density? Density { get => (Density?)GetValue(DensityProperty); set => SetValue(DensityProperty, value); }
    public bool Striped { get => (bool)GetValue(StripedProperty); set => SetValue(StripedProperty, value); }
    public bool Bordered { get => (bool)GetValue(BorderedProperty); set => SetValue(BorderedProperty, value); }
    public bool ShowToolbar { get => (bool)GetValue(ShowToolbarProperty); set => SetValue(ShowToolbarProperty, value); }
    public bool ShowFooter { get => (bool)GetValue(ShowFooterProperty); set => SetValue(ShowFooterProperty, value); }
    public bool Groupable { get => (bool)GetValue(GroupableProperty); set => SetValue(GroupableProperty, value); }
    public bool Loading { get => (bool)GetValue(LoadingProperty); set => SetValue(LoadingProperty, value); }
    public DataTemplate? RowDetail { get => (DataTemplate?)GetValue(RowDetailProperty); set => SetValue(RowDetailProperty, value); }
    public double DetailHeight { get => (double)GetValue(DetailHeightProperty); set => SetValue(DetailHeightProperty, value); }
    public object? EmptyContent { get => GetValue(EmptyContentProperty); set => SetValue(EmptyContentProperty, value); }
    public object? ToolbarContent { get => GetValue(ToolbarContentProperty); set => SetValue(ToolbarContentProperty, value); }
    public object? SelectionActions { get => GetValue(SelectionActionsProperty); set => SetValue(SelectionActionsProperty, value); }

    public event RoutedEventHandler SelectionChanged { add => AddHandler(SelectionChangedEvent, value); remove => RemoveHandler(SelectionChangedEvent, value); }
    public event RoutedPropertyChangedEventHandler<GridState> StateChanged { add => AddHandler(StateChangedEvent, value); remove => RemoveHandler(StateChangedEvent, value); }
    public event EventHandler<GridRowEventArgs> RowActivated { add => AddHandler(RowActivatedEvent, value); remove => RemoveHandler(RowActivatedEvent, value); }
    public event EventHandler<GridCellEditEventArgs> CellEditCommitted { add => AddHandler(CellEditCommittedEvent, value); remove => RemoveHandler(CellEditCommittedEvent, value); }

    // ---- public operations -----------------------------------------------------------------------------------

    /// <summary>Re-reads the column definitions after their code hooks (Accessor, Validate, Formatter…) changed.</summary>
    public void InvalidateColumns() => RebuildColumns();

    /// <summary>Re-runs the pipeline after items were mutated in place.</summary>
    public void Refresh() => Controller.Invalidate();

    /// <summary>Live update: recompute and briefly flash the item's row (no flash under reduced motion).</summary>
    public void NotifyItemChanged(object item)
    {
        if (!SlateMotion.IsReduced)
        {
            Controller.MarkFlash(item, Environment.TickCount64);
            _flashTimer.Start();
        }
        // Coalesce bursts of updates into one pipeline run.
        if (_refreshQueued) return;
        _refreshQueued = true;
        Dispatcher.BeginInvoke(DispatcherPriority.Background, () =>
        {
            _refreshQueued = false;
            Controller.Invalidate();
        });
    }

    private bool _refreshQueued;

    /// <summary>CSV (RFC 4180) of visible, selected or all rows, using column formatting and pending edits.</summary>
    public string ExportCsv(GridExportScope scope = GridExportScope.Visible) => Controller.ToCsv(scope);

    public string ExportTsv(GridExportScope scope = GridExportScope.Visible, bool includeHeader = true) => Controller.ToTsv(scope, includeHeader);

    /// <summary>Copies the selected rows (or the active row) as TSV — what Ctrl+C does.</summary>
    public void CopySelection(bool includeHeader = false)
    {
        var text = Controller.CopyText(includeHeader);
        if (text.Length > 0) TrySetClipboard(text);
    }

    /// <summary>Scrolls an item's row into view (client mode).</summary>
    public void ScrollIntoView(object item)
    {
        if (Controller.KeyOf(item) is not { } key) return;
        for (var i = 0; i < Controller.RowCount; i++)
        {
            if (Controller.RowAt(i)?.Key == key)
            {
                RevealRow(i);
                return;
            }
        }
    }

    public IReadOnlyList<GridCellChange<object>> CommitAll() => Controller.CommitAll();

    public void DiscardAll() => Controller.DiscardAll();

    // ---- wiring ----------------------------------------------------------------------------------------------

    private void OnUi(Action action)
    {
        if (Dispatcher.CheckAccess()) action();
        else Dispatcher.BeginInvoke(action);
    }

    private void RebuildColumns()
    {
        foreach (var c in Columns) c.Invalidate();
        Controller.SetColumns(Columns.Select(c => c.Engine).ToList());
        _columnsByField = Columns.GroupBy(c => c.Field).ToDictionary(g => g.Key, g => g.First());
        InvalidateLayoutAndRows();
    }

    private Dictionary<string, GridColumn> _columnsByField = new();

    internal GridColumn? ColumnDefinition(string field) => _columnsByField.GetValueOrDefault(field);

    private void OnItemsChanged(IEnumerable? oldValue, IEnumerable? newValue)
    {
        if (_observed is not null) _observed.CollectionChanged -= OnSourceCollectionChanged;
        _observed = newValue as INotifyCollectionChanged;
        if (_observed is not null) _observed.CollectionChanged += OnSourceCollectionChanged;
        Controller.SetItems(newValue);
    }

    private void OnSourceCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) =>
        OnUi(() => Controller.SetItems(Items));

    private void OnSelectionModeChanged()
    {
        Controller.SelectionMode = SelectionMode;
        InvalidateLayoutAndRows();
        UpdateChrome();
    }

    private void OnPaginationChanged()
    {
        Controller.Pagination = Pagination;
        UpdateChrome();
    }

    private void OnStatePropertyChanged(GridState state)
    {
        if (_syncing) return;
        Controller.SetState(state, raise: false);
        SyncFromState();
    }

    private void OnQuickFilterPropertyChanged(string text)
    {
        if (_syncing || text == Controller.State.QuickFilter) return;
        Controller.SetQuickFilter(text);
    }

    private void OnGroupByPropertyChanged(IReadOnlyList<string> fields)
    {
        if (_syncing || fields.SequenceEqual(Controller.State.GroupBy)) return;
        Controller.SetGroupBy(fields);
    }

    private void OnControllerStateChanged()
    {
        var old = State;
        SyncFromState();
        RaiseEvent(new RoutedPropertyChangedEventArgs<GridState>(old, Controller.State, StateChangedEvent));
    }

    /// <summary>Pushes the controller's state into the two-way properties without echoing back.</summary>
    private void SyncFromState()
    {
        _syncing = true;
        try
        {
            SetCurrentValue(StateProperty, Controller.State);
            SetCurrentValue(QuickFilterProperty, Controller.State.QuickFilter);
            SetCurrentValue(GroupByProperty, Controller.State.GroupBy);
        }
        finally
        {
            _syncing = false;
        }
        UpdateChrome();
        InvalidateLayoutAndRows();
    }

    private void OnSelectedItemsChanged()
    {
        if (_syncing) return;
        Controller.SetSelectedItems(SelectedItems);
    }

    private void OnControllerSelectionChanged()
    {
        _syncing = true;
        try { SetCurrentValue(SelectedItemsProperty, Controller.SelectedItems); }
        finally { _syncing = false; }
        UpdateChrome();
        InvalidateRows();
        _header?.InvalidateVisuals();
        RaiseEvent(new RoutedEventArgs(SelectionChangedEvent));
        AnnounceSelection();
    }

    private void OnViewChanged()
    {
        UpdateChrome();
        _surface?.InvalidateView();
        _footer?.InvalidateVisuals();
    }

    internal void InvalidateRows() => _surface?.InvalidateRows();

    internal void InvalidateLayoutAndRows()
    {
        _header?.InvalidateVisuals();
        _surface?.InvalidateView();
        _footer?.InvalidateVisuals();
    }

    internal static void TrySetClipboard(string text)
    {
        try { Clipboard.SetText(text); }
        catch (System.Runtime.InteropServices.ExternalException) { /* clipboard busy: ignore */ }
    }

    protected override AutomationPeer OnCreateAutomationPeer() => new DataGridAutomationPeer(this);
}
