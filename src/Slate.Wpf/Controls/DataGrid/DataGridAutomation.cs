using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Threading;
using Slate.Data;

namespace Slate.Wpf;

/// <summary>UI Automation for <see cref="DataGrid"/>: Grid, Table and Selection patterns. Row and cell peers exist for
/// realised (virtualised) rows; <see cref="IGridProvider.GetItem"/> scrolls a cell into view on demand.</summary>
public sealed class DataGridAutomationPeer(DataGrid owner) : FrameworkElementAutomationPeer(owner), IGridProvider, ITableProvider, ISelectionProvider
{
    private DataGrid Grid => (DataGrid)Owner;

    protected override string GetClassNameCore() => nameof(DataGrid);

    protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.DataGrid;

    public override object? GetPattern(PatternInterface patternInterface) => patternInterface switch
    {
        PatternInterface.Grid or PatternInterface.Table or PatternInterface.Selection => this,
        _ => base.GetPattern(patternInterface),
    };

    protected override List<AutomationPeer>? GetChildrenCore()
    {
        var children = base.GetChildrenCore();
        if (children is null) return null;
        // Flatten the scroll viewer so rows are direct children of the grid.
        var flat = new List<AutomationPeer>();
        foreach (var child in children)
        {
            if (child is ScrollViewerAutomationPeer scroller)
                flat.AddRange(scroller.GetChildren() ?? []);
            else
                flat.Add(child);
        }
        return flat;
    }

    // ---- IGridProvider ----

    public int RowCount => Grid.Controller.RowCount;

    public int ColumnCount => Grid.CurrentLayout.Columns.Count;

    public IRawElementProviderSimple? GetItem(int row, int column)
    {
        if (row < 0 || row >= RowCount || column < 0 || column >= ColumnCount) return null;
        var field = Grid.CurrentLayout.Columns[column].Field;
        var view = Grid.Surface?.RowView(row);
        if (view?.Cell(field) is null)
        {
            Grid.RevealRow(row);
            Grid.RevealColumn(column);
        }
        // UIA asks for an off-screen cell synchronously; process the queued WPF layout/load work before
        // asking the row for its recycled cell peer, otherwise ProviderFromPeer can return null.
        Grid.Dispatcher.Invoke(() =>
        {
            Grid.UpdateLayout();
            Grid.Surface?.UpdateLayout();
            var frame = new DispatcherFrame();
            Grid.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() => frame.Continue = false));
            Dispatcher.PushFrame(frame);
        });
        view = Grid.Surface?.RowView(row);
        return view?.Cell(field) is { } cell && CreatePeerForElement(cell) is { } peer ? ProviderFromPeer(peer) : null;
    }

    // ---- ITableProvider ----

    public RowOrColumnMajor RowOrColumnMajor => RowOrColumnMajor.RowMajor;

    public IRawElementProviderSimple[] GetColumnHeaders() =>
        Grid.Header?.HeaderCells.Select(CreatePeerForElement).OfType<AutomationPeer>().Select(ProviderFromPeer).ToArray() ?? [];

    public IRawElementProviderSimple[] GetRowHeaders() => [];

    // ---- ISelectionProvider ----

    public bool CanSelectMultiple => Grid.SelectionMode == GridSelectionMode.Multi;

    public bool IsSelectionRequired => false;

    public IRawElementProviderSimple[] GetSelection() =>
        Grid.Surface?.RealizedRows
            .Where(r => r.Row is { Kind: GridRowKind.Data } row && Grid.Controller.IsSelected(row.Key))
            .Select(CreatePeerForElement).OfType<AutomationPeer>().Select(ProviderFromPeer).ToArray() ?? [];

    internal IRawElementProviderSimple Provider(AutomationPeer peer) => ProviderFromPeer(peer);
}

/// <summary>A realised row: SelectionItem for data rows, ExpandCollapse for group and tree rows.</summary>
internal sealed class GridRowAutomationPeer(GridRowView owner, DataGrid grid) : FrameworkElementAutomationPeer(owner), ISelectionItemProvider, IExpandCollapseProvider
{
    private GridRowView Row => (GridRowView)Owner;

    protected override string GetClassNameCore() => "DataGridRow";

    protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.DataItem;

    protected override List<AutomationPeer>? GetChildrenCore()
    {
        var children = base.GetChildrenCore() ?? [];
        foreach (var cell in Row.Cells)
            if (UIElementAutomationPeer.CreatePeerForElement(cell) is { } peer && !children.Contains(peer))
                children.Add(peer);
        return children;
    }

    protected override string GetNameCore()
    {
        if (Row.Row is not { } row) return "";
        if (row.Kind == GridRowKind.Group) return $"{row.GroupKeyText}, {row.RowCount} rows";
        if (row.Item is null) return "Loading";
        return Row.Cells.FirstOrDefault()?.Text ?? row.Key;
    }

    public override object? GetPattern(PatternInterface patternInterface)
    {
        if (patternInterface == PatternInterface.SelectionItem && Row.Row is { Kind: GridRowKind.Data, Item: not null } && grid.SelectionMode != GridSelectionMode.None)
            return this;
        if (patternInterface == PatternInterface.ExpandCollapse && Row.Row is { } r && (r.Kind == GridRowKind.Group || r.HasChildren))
            return this;
        return base.GetPattern(patternInterface);
    }

    public bool IsSelected => Row.Row is { } row && grid.Controller.IsSelected(row.Key);

    public IRawElementProviderSimple? SelectionContainer =>
        CreatePeerForElement(grid) is { } peer ? ProviderFromPeer(peer) : null;

    public void AddToSelection()
    {
        if (!IsSelected && Row.Index >= 0) grid.Controller.Toggle(Row.Index);
    }

    public void RemoveFromSelection()
    {
        if (IsSelected && Row.Index >= 0) grid.Controller.Toggle(Row.Index);
    }

    public void Select()
    {
        if (Row.Index >= 0) grid.Controller.Click(Row.Index);
    }

    public ExpandCollapseState ExpandCollapseState =>
        Row.Row is { } row && (row.Kind == GridRowKind.Group || row.HasChildren)
            ? row.Expanded ? ExpandCollapseState.Expanded : ExpandCollapseState.Collapsed
            : ExpandCollapseState.LeafNode;

    public void Expand()
    {
        if (ExpandCollapseState == ExpandCollapseState.Collapsed) grid.Controller.ToggleExpand(Row.Index);
    }

    public void Collapse()
    {
        if (ExpandCollapseState == ExpandCollapseState.Expanded) grid.Controller.ToggleExpand(Row.Index);
    }
}

/// <summary>A realised cell: GridItem, TableItem and Value.</summary>
internal sealed class GridCellAutomationPeer(GridCellView owner, DataGrid grid) : FrameworkElementAutomationPeer(owner), IGridItemProvider, ITableItemProvider, IValueProvider
{
    private GridCellView Cell => (GridCellView)Owner;

    private GridRowView? RowView => System.Windows.Media.VisualTreeHelper.GetParent(Cell) switch
    {
        GridRowView r => r,
        GridLane lane => System.Windows.Media.VisualTreeHelper.GetParent(lane) as GridRowView,
        _ => null,
    };

    protected override string GetClassNameCore() => "DataGridCell";

    protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Custom;

    protected override string GetLocalizedControlTypeCore() => "cell";

    protected override string GetNameCore() => Cell.Text;

    protected override string GetItemStatusCore() => Cell.Error ?? "";

    public override object? GetPattern(PatternInterface patternInterface) => patternInterface switch
    {
        PatternInterface.GridItem or PatternInterface.TableItem or PatternInterface.Value => this,
        _ => base.GetPattern(patternInterface),
    };

    public int Row => RowView?.Index ?? -1;

    public int Column => grid.CurrentLayout.Find(Cell.Column.Field)?.Index ?? -1;

    public int RowSpan => 1;

    public int ColumnSpan => 1;

    public IRawElementProviderSimple? ContainingGrid => CreatePeerForElement(grid) is { } peer ? ProviderFromPeer(peer) : null;

    public IRawElementProviderSimple[] GetRowHeaderItems() => [];

    public IRawElementProviderSimple[] GetColumnHeaderItems() =>
        grid.Header?.Cell(Cell.Column.Field) is { } header && CreatePeerForElement(header) is { } peer ? [ProviderFromPeer(peer)] : [];

    public string Value => Cell.Text;

    public bool IsReadOnly => Row < 0 || !grid.Controller.CanEdit(Row, Cell.Column);

    public void SetValue(string value)
    {
        if (IsReadOnly) throw new ElementNotEnabledException();
        var c = grid.Controller;
        if (!c.BeginEdit(Row, Cell.Column)) throw new ElementNotEnabledException();
        c.Edit.SetDraftText(value);
        if (!c.CommitEdit())
        {
            var error = c.Edit.Current?.Error;
            c.CancelEdit();
            throw new ArgumentException(error ?? "Invalid value", nameof(value));
        }
    }
}

/// <summary>A column header: HeaderItem with Invoke (sort).</summary>
internal sealed class GridHeaderCellAutomationPeer(GridHeaderCell owner) : FrameworkElementAutomationPeer(owner), IInvokeProvider
{
    private GridHeaderCell Cell => (GridHeaderCell)Owner;

    protected override string GetClassNameCore() => "DataGridColumnHeader";

    protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.HeaderItem;

    protected override string GetNameCore() => Cell.Column.DisplayTitle;

    protected override string GetItemStatusCore() => Cell.Sort switch
    {
        SortDirection.Ascending => "Sorted ascending",
        SortDirection.Descending => "Sorted descending",
        _ => "",
    };

    public override object? GetPattern(PatternInterface patternInterface) =>
        patternInterface == PatternInterface.Invoke && Cell.Column.Sortable ? this : base.GetPattern(patternInterface);

    public void Invoke()
    {
        if (System.Windows.Media.VisualTreeHelper.GetParent(Cell) is not null)
            FindGrid(Cell)?.Controller.ToggleSort(Cell.Field);
    }

    private static DataGrid? FindGrid(DependencyObject d)
    {
        while (d is not null and not DataGrid) d = System.Windows.Media.VisualTreeHelper.GetParent(d);
        return d as DataGrid;
    }
}
