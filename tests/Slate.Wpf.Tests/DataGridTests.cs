using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Input;
using Slate.Data;

namespace Slate.Wpf.Tests;

/// <summary>sl:DataGrid on a live WPF tree: virtualisation bounds, pinning, keyboard, editing, selection, export and
/// automation. (The engine wiring itself is covered cross-platform in Slate.Wpf.ResourceTests.)</summary>
public class DataGridTests
{
    public sealed class Deploy
    {
        public int Id { get; set; }
        public string Service { get; set; } = "";
        public string Status { get; set; } = "";
        public double Duration { get; set; }
        public double Progress { get; set; }
        public bool Canary { get; set; }
    }

    private static readonly string[] Statuses = ["Queued", "Building", "Ready", "Failed"];

    private static List<Deploy> Deploys(int n) =>
        Enumerable.Range(0, n).Select(i => new Deploy
        {
            Id = i,
            Service = "svc-" + (i % 7),
            Status = Statuses[i % 4],
            Duration = i % 10 * 10,
            Progress = i % 101 / 100.0,
            Canary = i % 3 == 0,
        }).ToList();

    private static DataGrid Grid(int rows = 100_000, Action<DataGrid>? configure = null, double width = 900, double height = 480)
    {
        var grid = new DataGrid
        {
            Items = Deploys(rows),
            RowKey = o => ((Deploy)o).Id,
            SelectionMode = GridSelectionMode.Multi,
        };
        grid.Columns.Add(new GridColumn { Field = "Id", Type = GridColumnType.Number, Width = 80, Pinned = GridPin.Start });
        grid.Columns.Add(new GridColumn { Field = "Service", Width = 180, Editable = true, Validate = v => string.IsNullOrWhiteSpace(v as string) ? "Required" : null });
        grid.Columns.Add(new GridColumn { Field = "Status", Type = GridColumnType.Enum, Width = 140, EnumOrder = "Queued, Building, Ready, Failed", EnumTones = "Ready=Success, Failed=Danger" });
        grid.Columns.Add(new GridColumn { Field = "Duration", Type = GridColumnType.Number, Width = 140, Aggregate = GridAggregate.Sum, Editable = true });
        grid.Columns.Add(new GridColumn { Field = "Progress", Type = GridColumnType.Progress, Width = 200, Format = "0%" });
        for (var i = 0; i < 30; i++)
        {
            var factor = i;
            grid.Columns.Add(new GridColumn { Field = "Extra" + i, Title = "Extra " + i, Type = GridColumnType.Number, Width = 120, Accessor = o => ((Deploy)o).Id * factor });
        }
        grid.Columns.Add(new GridColumn { Field = "Canary", Type = GridColumnType.Boolean, Width = 90, Pinned = GridPin.End });
        configure?.Invoke(grid);
        return Wpf.Realize(grid, width, height);
    }

    private static int RealizedRowCount(DataGrid g) => g.Surface!.RealizedRows.Count(r => r.Index >= 0);

    [Fact]
    public void Only_the_viewport_rows_and_columns_are_realised() => Wpf.Run(() =>
    {
        var g = Grid();
        Assert.Equal(100_000, g.Controller.RowCount);
        var visibleRows = (int)Math.Ceiling(g.Surface!.ViewportHeight / g.Controller.RowHeight);
        Assert.InRange(RealizedRowCount(g), visibleRows, visibleRows + 2 * 4 + 1);

        var row = g.Surface.RealizedRows.First(r => r.Index == 0);
        Assert.True(row.Cells.Count() < g.CurrentLayout.Columns.Count, "columns should be virtualised");
        Assert.NotNull(row.Cell("Id"));      // start pin always realised
        Assert.NotNull(row.Cell("Canary"));  // end pin always realised

        g.Surface.SetVerticalOffset(50_000 * g.Controller.RowHeight);
        Wpf.Pump();
        Assert.Contains(g.Surface.RealizedRows, r => r.Index == 50_000);
        Assert.InRange(RealizedRowCount(g), visibleRows, visibleRows + 2 * 4 + 1);

        g.Surface.SetHorizontalOffset(2000);
        Wpf.Pump();
        var scrolled = g.Surface.RealizedRows.First(r => r.Index >= 0);
        Assert.NotNull(scrolled.Cell("Id"));
        Assert.Null(scrolled.Cell("Service")); // scrolled past
        Assert.Equal((true, true), g.ShadowEdges(g.BodyWidth));
    });

    [Fact]
    public void Pinned_columns_stay_put_while_scrolling() => Wpf.Run(() =>
    {
        var g = Grid(1000);
        var id = g.CurrentLayout.Find("Id")!;
        var canary = g.CurrentLayout.Find("Canary")!;
        var before = (g.ColumnX(id, g.BodyWidth), g.ColumnX(canary, g.BodyWidth));
        g.Surface!.SetHorizontalOffset(500);
        Wpf.Pump();
        Assert.Equal(before, (g.ColumnX(id, g.BodyWidth), g.ColumnX(canary, g.BodyWidth)));
        Assert.Equal(g.BodyWidth - canary.Width, g.ColumnX(canary, g.BodyWidth), 3);
        Assert.Equal(g.Prefix, g.ColumnX(id, g.BodyWidth), 3);
    });

    [Fact]
    public void Header_click_sorts_and_shift_click_adds_sorts() => Wpf.Run(() =>
    {
        var g = Grid(200);
        var states = new List<GridState>();
        g.StateChanged += (_, e) => states.Add(e.NewValue);
        g.Controller.ToggleSort("Duration");
        g.Controller.ToggleSort("Id", additive: true);
        Wpf.Pump();
        Assert.Equal(2, g.State.Sorts.Count);
        Assert.Equal(SortDirection.Ascending, g.Header!.Cell("Duration")!.Sort);
        Assert.Equal(2, states.Count);
        Assert.Same(g.Controller.State, g.State);
    });

    [Fact]
    public void Keyboard_moves_the_active_cell_and_selects() => Wpf.Run(() =>
    {
        var g = Grid(500);
        g.Focus();
        Assert.True(g.HandleKey(Key.Down, ModifierKeys.None));
        Assert.True(g.HandleKey(Key.Right, ModifierKeys.None));
        Assert.Equal(new GridCell(1, 1), g.Controller.Active);
        g.HandleKey(Key.Space, ModifierKeys.None);
        Assert.True(g.Controller.IsSelected("1"));
        g.HandleKey(Key.Down, ModifierKeys.Shift);
        g.HandleKey(Key.Down, ModifierKeys.Shift);
        Assert.Equal(3, g.Controller.Selection.Count);
        g.HandleKey(Key.End, ModifierKeys.Control);
        Wpf.Pump();
        Assert.Equal(499, g.Controller.Active.Row);
        Assert.Contains(g.Surface!.RealizedRows, r => r.Index == 499);
        g.HandleKey(Key.A, ModifierKeys.Control);
        Assert.Equal(500, g.SelectedItems.Count);
        g.HandleKey(Key.Escape, ModifierKeys.None);
        Assert.Empty(g.SelectedItems);
    });

    [Fact]
    public void Alt_arrows_reorder_columns() => Wpf.Run(() =>
    {
        var g = Grid(10);
        g.Controller.Active = new GridCell(0, 1); // Service
        g.HandleKey(Key.Right, ModifierKeys.Alt);
        Wpf.Pump();
        Assert.Equal("Service", g.CurrentLayout.Columns[2].Field);
        Assert.Equal(2, g.Controller.Active.Column);
    });

    [Fact]
    public void Mouse_style_selection_raises_events_and_syncs_selected_items() => Wpf.Run(() =>
    {
        var g = Grid(50);
        var raised = 0;
        g.SelectionChanged += (_, _) => raised++;
        g.Controller.Click(2);
        g.Controller.Click(4, shift: true);
        Wpf.Pump();
        Assert.Equal([2, 3, 4], g.SelectedItems.Cast<Deploy>().Select(d => d.Id).Order());
        Assert.True(raised > 0);
        g.SelectedItems = [g.Controller.ItemOf("7")!];
        Assert.True(g.Controller.IsSelected("7"));
        Assert.Equal(1, g.Controller.Selection.Count);
    });

    [Fact]
    public void Inline_editing_validates_commits_and_raises_events() => Wpf.Run(() =>
    {
        var g = Grid(20, grid => grid.EditMode = GridEditMode.Cell);
        var commits = new List<GridCellEditEventArgs>();
        g.CellEditCommitted += (_, e) => commits.Add(e);
        Assert.True(g.BeginEditAt(0, 1, "x"));
        Wpf.Pump();
        Assert.True(g.IsEditingCell);
        var box = Wpf.Find<TextBox>(g.Surface!);
        box.Text = "";
        Assert.False(g.FinishEdit(GridEditCommitKey.Enter, false)); // blocked: Required
        box.Text = "api";
        Assert.True(g.FinishEdit(GridEditCommitKey.Enter, false));
        Wpf.Pump();
        Assert.False(g.IsEditingCell);
        Assert.Equal("api", ((Deploy)g.Controller.ItemOf("0")!).Service);
        Assert.Equal("api", Assert.Single(commits).NewValue);
        Assert.Equal(1, g.Controller.Active.Row); // Enter moves down
    });

    [Fact]
    public void Batch_mode_shows_pending_edits_until_commit() => Wpf.Run(() =>
    {
        var g = Grid(20, grid => grid.EditMode = GridEditMode.Batch);
        g.BeginEditAt(3, 3, "55");
        Wpf.Pump();
        Assert.True(g.FinishEdit(null, false));
        Assert.Equal(1, g.Controller.PendingCount);
        Assert.Equal(30, ((Deploy)g.Controller.ItemOf("3")!).Duration);
        Assert.Single(g.CommitAll());
        Assert.Equal(55, ((Deploy)g.Controller.ItemOf("3")!).Duration);
    });

    [Fact]
    public void Export_uses_visible_columns_and_copy_uses_selection() => Wpf.Run(() =>
    {
        var g = Grid(5);
        foreach (var c in g.Columns.Where(c => c.Field.StartsWith("Extra", StringComparison.Ordinal) || c.Field == "Progress")) g.Controller.Hide(c.Field);
        var csv = g.ExportCsv(GridExportScope.All);
        Assert.StartsWith("Id,Service,Status,Duration,Canary\r\n", csv);
        Assert.DoesNotContain("Progress", csv);
        g.Controller.Click(1);
        Assert.StartsWith("1\tsvc-1\tBuilding", g.Controller.CopyText());
        Assert.Equal(6, g.ExportTsv().Split('\n').Length);
    });

    [Fact]
    public void Grouping_footer_and_detail_rows_render() => Wpf.Run(() =>
    {
        var g = Grid(40, grid =>
        {
            grid.Groupable = true;
            grid.ShowFooter = true;
            grid.RowDetail = new DataTemplate();
        });
        g.Controller.GroupBy("Status");
        Wpf.Pump();
        var groupRow = g.Surface!.RealizedRows.First(r => r.Index == 0);
        Assert.Equal(GridRowKind.Group, groupRow.Row!.Kind);
        Assert.NotNull(groupRow.Cell("Duration")); // aggregate cell
        g.Controller.Ungroup("Status");
        g.Controller.ToggleDetail("0");
        Wpf.Pump();
        Assert.Equal(GridRowKind.Detail, g.Controller.RowAt(1)!.Kind);
        Assert.Equal(g.DetailHeight, g.Surface.RealizedRows.First(r => r.Index == 1).ActualHeight, 1);
    });

    [Fact]
    public async Task Server_mode_shows_skeletons_then_rows()
    {
        var items = Deploys(5000).Cast<object>().ToList();
        DataGrid g = null!;
        Wpf.Run(() =>
        {
            g = Grid(0);
            var columns = g.Columns.Select(c => c.Engine).ToList();
            g.DataSource = new InMemoryGridDataSource<object>(items, columns, TimeSpan.FromMilliseconds(20));
            Wpf.Pump();
            Assert.Null(g.Controller.RowAt(0)!.Item);
        });
        for (var i = 0; i < 100 && Wpf.Run(() => g.Controller.TotalCount) == 0; i++) await Task.Delay(20);
        Wpf.Run(() =>
        {
            Wpf.Pump();
            Assert.Equal(5000, g.Controller.RowCount);
            Assert.NotNull(g.Controller.RowAt(0)!.Item);
        });
    }

    [Fact]
    public void State_property_restores_layout_two_way() => Wpf.Run(() =>
    {
        var g = Grid(30);
        var json = g.Controller.State.SetSorts(new GridSort("Duration", SortDirection.Descending)).SetQuickFilter("svc-2").ToJson();
        g.State = GridState.FromJson(json);
        Wpf.Pump();
        Assert.Equal("svc-2", g.QuickFilter);
        Assert.All(Enumerable.Range(0, g.Controller.RowCount), i => Assert.Equal("svc-2", ((Deploy)g.Controller.RowAt(i)!.Item!).Service));
        g.QuickFilter = "";
        Assert.Equal("", g.State.QuickFilter);
    });

    [Fact]
    public void Automation_exposes_grid_table_and_selection_patterns() => Wpf.Run(() =>
    {
        var g = Grid(100);
        var peer = (DataGridAutomationPeer)UIElementAutomationPeer.CreatePeerForElement(g);
        Assert.Equal(AutomationControlType.DataGrid, peer.GetAutomationControlType());
        var grid = (IGridProvider)peer.GetPattern(PatternInterface.Grid)!;
        Assert.Equal(100, grid.RowCount);
        Assert.Equal(g.CurrentLayout.Columns.Count, grid.ColumnCount);
        var cellProvider = grid.GetItem(80, 2); // scrolls into view on demand
        var surface = g.Surface;
        var row = surface?.RowView(80);
        Assert.True(cellProvider is not null,
            $"row={row?.Index.ToString() ?? "not realized"}, cell={row?.Cell(g.CurrentLayout.Columns[2].Field) is not null}, offset={surface?.VerticalOffset}, window={surface?.WindowRange}");
        var table = (ITableProvider)peer.GetPattern(PatternInterface.Table)!;
        Assert.Equal(g.CurrentLayout.Columns.Count, table.GetColumnHeaders().Length);

        var rowView = g.Surface!.RealizedRows.First(r => r.Index >= 0);
        var rowPeer = UIElementAutomationPeer.CreatePeerForElement(rowView);
        var item = (ISelectionItemProvider)rowPeer.GetPattern(PatternInterface.SelectionItem)!;
        item.AddToSelection();
        Assert.True(item.IsSelected);
        Assert.Single(((ISelectionProvider)peer.GetPattern(PatternInterface.Selection)!).GetSelection());

        var cellPeer = UIElementAutomationPeer.CreatePeerForElement(rowView.Cell("Service")!);
        Assert.Equal(rowView.Index, ((IGridItemProvider)cellPeer.GetPattern(PatternInterface.GridItem)!).Row);
        Assert.StartsWith("svc-", cellPeer.GetName());
    });

    [Fact]
    public void Columns_declared_in_xaml_parse() => Wpf.Run(() =>
    {
        const string xaml = """
            <sl:DataGrid xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" xmlns:sl="https://slate.dev/wpf"
                         SelectionMode="Multi" Pagination="Pages" PageSize="25" EditMode="Batch" Striped="True" ShowToolbar="True">
              <sl:GridColumn Field="Service" Title="Service" Width="200" Pinned="Start" />
              <sl:GridColumn Field="Status" Type="Enum" EnumTones="Ready=Success, Failed=Danger" />
              <sl:GridColumn Field="Duration" Type="Number" Format="#,##0" Aggregate="Avg" Align="End" />
            </sl:DataGrid>
            """;
        var g = (DataGrid)System.Windows.Markup.XamlReader.Parse(xaml);
        g.Items = new ObservableCollection<Deploy>(Deploys(60));
        Wpf.Realize(g);
        Assert.Equal(3, g.Columns.Count);
        Assert.Equal(GridPin.Start, g.CurrentLayout.Find("Service")!.Pin);
        Assert.Equal(25, g.Controller.RowCount);
        Assert.Equal(3, g.Controller.PageCount);
        ((ObservableCollection<Deploy>)g.Items).Add(new Deploy { Id = 999 });
        Wpf.Pump();
        Assert.Equal(61, g.Controller.TotalCount);
    });
}
