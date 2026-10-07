using System.Diagnostics;
using Avalonia;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Microsoft.Extensions.Time.Testing;
using Slate.Avalonia.Controls;
using Slate.Data;
using DataGrid = Slate.Avalonia.Controls.DataGrid;
using GridColumn = Slate.Avalonia.Controls.GridColumn;

namespace Slate.Avalonia.Tests;

/// <summary>
/// sl:DataGrid (docs/design/data-grid.md): the renderer wires the Slate.Core engine (pipeline, layout, selection,
/// navigator, edit session, row cache) to virtualised rows, header interactions and chrome.
/// </summary>
public class DataGridTests(ITestOutputHelper output)
{
    public sealed class Deployment
    {
        public string Id { get; set; } = "";
        public string Service { get; set; } = "";
        public string Status { get; set; } = "";
        public string Region { get; set; } = "";
        public double Duration { get; set; }
        public double Progress { get; set; }
        public bool Healthy { get; set; }
        public List<Deployment>? Children { get; set; }
    }

    private static readonly string[] Statuses = ["Queued", "Building", "Ready", "Failed"];
    private static readonly string[] Regions = ["eu-west", "us-east", "ap-south"];

    private static List<Deployment> Make(int count) =>
        Enumerable.Range(0, count).Select(i => new Deployment
        {
            Id = $"d{i:D6}",
            Service = $"svc-{i % 7}",
            Status = Statuses[i % 4],
            Region = Regions[i % 3],
            Duration = i % 100,
            Progress = (i % 10) / 10.0,
            Healthy = i % 2 == 0,
        }).ToList();

    private static DataGrid Grid(IEnumerable<object>? items = null, int count = 50)
    {
        var grid = new DataGrid { Items = (items ?? Make(count)).ToList(), RowKeyPath = "Id", SelectionMode = GridSelectionMode.Multi };
        grid.Columns.Add(new GridColumn { Field = "Id", Width = 110, Pinned = GridPin.Start });
        grid.Columns.Add(new GridColumn { Field = "Service", Width = 140 });
        grid.Columns.Add(new GridColumn { Field = "Status", Type = GridColumnType.Enum, EnumOrder = "Queued,Building,Ready,Failed", EnumTones = "Ready:Success,Failed:Danger" });
        grid.Columns.Add(new GridColumn { Field = "Region", Width = 120 });
        grid.Columns.Add(new GridColumn { Field = "Duration", Type = GridColumnType.Number, Width = 110, Aggregate = GridAggregate.Sum });
        grid.Columns.Add(new GridColumn { Field = "Progress", Type = GridColumnType.Progress, Width = 160 });
        grid.Columns.Add(new GridColumn { Field = "Healthy", Type = GridColumnType.Boolean, Width = 100 });
        return grid;
    }

    private static Deployment ItemAt(DataGrid grid, int viewIndex) => (Deployment)grid.RowAt(viewIndex).Item!;

    [AvaloniaFact]
    public void Only_visible_rows_are_realised_and_indices_follow_the_scroll()
    {
        var grid = Grid(count: 10_000);
        var window = TestHelpers.Show(grid, 900, 500);
        var rows = grid.RowsPresenter!;
        Assert.Equal(10_000, grid.ViewRowCount);
        Assert.InRange(rows.RealizedCount, 5, 30);
        Assert.Equal(0, rows.RealizedIndices[0]);

        grid.ScrollToRow(5000);
        TestHelpers.Pump(window);
        Assert.InRange(rows.RealizedCount, 5, 30);
        Assert.Contains(5000, rows.RealizedIndices);
        var row = rows.RowFor(5000)!;
        Assert.Equal("d005000", ((Deployment)row.Item!).Id);
        Assert.Equal("d005000", row.Cells["Id"].Text);
        // Containers are recycled: realised rows are a contiguous window.
        var idx = rows.RealizedIndices;
        Assert.Equal(idx.Count - 1, idx[^1] - idx[0]);
    }

    [AvaloniaFact]
    public void Row_height_follows_the_grid_token_and_density()
    {
        var grid = Grid();
        var window = TestHelpers.Show(grid, 900, 500);
        var h0 = grid.RowsPresenter!.RowFor(0)!.Bounds.Height;
        Assert.Equal(grid.RowHeight, h0);
        grid.Density = Density.Comfortable; // the test app runs compact
        TestHelpers.Pump(window);
        Assert.True(grid.RowHeight > h0, $"comfortable {grid.RowHeight} vs {h0}");
        Assert.Equal(grid.RowHeight, grid.RowsPresenter.RowFor(0)!.Bounds.Height);
    }

    [AvaloniaFact]
    public void Clicking_a_header_sorts_and_shift_click_adds_a_secondary_sort()
    {
        var grid = Grid();
        var window = TestHelpers.Show(grid, 1100, 500);
        var duration = grid.HeaderRow!.Cells["Duration"];

        window.Click(duration);
        Assert.Equal([new GridSort("Duration", SortDirection.Ascending)], grid.State.Sorts);
        Assert.Equal(0, ItemAt(grid, 0).Duration);

        window.Click(duration);
        Assert.Equal(SortDirection.Descending, grid.State.Sorts[0].Direction);
        Assert.Equal(49, ItemAt(grid, 0).Duration);

        var center = grid.HeaderRow.Cells["Region"].TranslatePoint(new Point(30, 20), window)!.Value;
        global::Avalonia.Headless.HeadlessWindowExtensions.MouseDown(window, center, MouseButton.Left, RawInputModifiers.Shift);
        global::Avalonia.Headless.HeadlessWindowExtensions.MouseUp(window, center, MouseButton.Left, RawInputModifiers.Shift);
        TestHelpers.Pump(window);
        Assert.Equal(2, grid.State.Sorts.Count);
        Assert.Equal("Region", grid.State.Sorts[1].Field);
        Assert.Contains("sorted", grid.HeaderRow.Cells["Region"].Classes);

        // Third click on the primary removes it.
        window.Click(duration);
        Assert.DoesNotContain(grid.State.Sorts, s => s.Field == "Duration");
    }

    [AvaloniaFact]
    public void Enum_columns_sort_by_their_declared_order()
    {
        var grid = Grid();
        TestHelpers.Show(grid, 1100, 500);
        grid.ToggleSort("Status");
        TestHelpers.Pump();
        Assert.Equal("Queued", ItemAt(grid, 0).Status);
        Assert.Equal("Failed", ItemAt(grid, grid.ViewRowCount - 1).Status);
    }

    [AvaloniaFact]
    public void Filters_quick_filter_and_filter_editor_narrow_the_rows()
    {
        var grid = Grid(count: 100);
        var window = TestHelpers.Show(grid, 1100, 500);

        grid.SetFilter(new GridFilter("Duration", FilterOperator.GreaterThanOrEqual, 90.0));
        TestHelpers.Pump(window);
        Assert.Equal(10, grid.ViewRowCount);
        Assert.Equal(10, grid.MatchingCount);

        grid.QuickFilter = "svc-3";
        TestHelpers.Pump(window);
        Assert.All(Enumerable.Range(0, grid.ViewRowCount), i => Assert.Equal("svc-3", ItemAt(grid, i).Service));
        Assert.Equal("svc-3", grid.State.QuickFilter);

        grid.QuickFilter = "";
        grid.ClearFilters();
        TestHelpers.Pump(window);
        Assert.Equal(100, grid.ViewRowCount);

        // The type-aware editor builds an AnyOf filter for enums and a boolean Equals for booleans.
        var editor = new DataGridFilterEditor(grid, "Healthy", () => { });
        Assert.Null(editor.CurrentFilter());
        var enumEditor = new DataGridFilterEditor(grid, "Status", () => { });
        Assert.Null(enumEditor.CurrentFilter());
        grid.SetFilter(new GridFilter("Status", FilterOperator.AnyOf, Values: ["Failed"]));
        TestHelpers.Pump(window);
        Assert.Equal(25, grid.ViewRowCount);
        var reopened = new DataGridFilterEditor(grid, "Status", () => { });
        Assert.Equal(FilterOperator.AnyOf, reopened.CurrentFilter()!.Operator);
        Assert.Equal("is Failed", DataGridFilterEditor.Describe(grid.State.Filters[0]));
        Assert.Equal(["=", "≠", "<", "≤", ">", "≥", "between", "is empty", "is not empty"],
            DataGridFilterEditor.Operators(GridColumnType.Number).Select(o => o.Label));
    }

    [AvaloniaFact]
    public void Header_menu_offers_sort_filter_group_pin_hide_and_autosize()
    {
        var grid = Grid();
        grid.Groupable = true;
        TestHelpers.Show(grid, 1100, 500);
        var menu = grid.HeaderRow!.Cells["Region"].ColumnMenu;
        var labels = menu.Items.OfType<MenuItem>().Select(i => i.Header?.ToString()).ToList();
        foreach (var expected in new[] { "Sort ascending", "Sort descending", "Filter…", "Group by this column", "Pin to start", "Pin to end", "Auto-size", "Hide column" })
            Assert.Contains(expected, labels);
    }

    [AvaloniaFact]
    public void Columns_can_be_pinned_resized_reordered_and_hidden()
    {
        var grid = Grid();
        var window = TestHelpers.Show(grid, 700, 400);

        // Pinned start column stays put while the body scrolls horizontally.
        var idCell = grid.RowsPresenter!.RowFor(0)!.Cells["Id"];
        var x0 = idCell.TranslatePoint(default, window)!.Value.X;
        grid.ScrollViewer!.Offset = new Vector(200, 0);
        TestHelpers.Pump(window);
        Assert.Equal(x0, idCell.TranslatePoint(default, window)!.Value.X, 1);
        Assert.Contains("pin-edge-start", idCell.Classes);
        var service = grid.RowsPresenter.RowFor(0)!.Cells["Service"];
        Assert.True(service.TranslatePoint(default, window)!.Value.X < x0 + 110, "unpinned column scrolled under the pinned one");

        var orderBefore = grid.Layout.Order.ToList();
        grid.ResizeColumn("Region", 220);
        TestHelpers.Pump(window);
        Assert.Equal(orderBefore, grid.Layout.Order); // resizing never reorders (state.Order is only set by moves)
        Assert.Equal(220, grid.Layout.Find("Region")!.Width);
        Assert.Equal(220, grid.HeaderRow!.Cells["Region"].Bounds.Width);

        grid.MoveColumn("Region", 1);
        TestHelpers.Pump(window);
        Assert.Equal("Region", grid.Layout.Order[1]);
        Assert.Equal(grid.Layout.Order, grid.State.Order);

        // Alt+Right on a focused header moves the column.
        var header = grid.HeaderRow.Cells["Region"];
        header.Focus(NavigationMethod.Tab);
        window.Press(Key.Right, RawInputModifiers.Alt);
        Assert.Equal("Region", grid.Layout.Order[2]);

        grid.SetColumnHidden("Healthy", true);
        TestHelpers.Pump(window);
        Assert.Null(grid.Layout.Find("Healthy"));
        Assert.False(grid.HeaderRow.Cells.ContainsKey("Healthy") && grid.HeaderRow.Cells["Healthy"].IsVisible);

        grid.PinColumn("Healthy", GridPin.End);
        grid.SetColumnHidden("Healthy", false);
        TestHelpers.Pump(window);
        Assert.Equal(GridPin.End, grid.Layout.Find("Healthy")!.Pin);
    }

    [AvaloniaFact]
    public void Double_clicking_the_resize_grip_autofits_the_column()
    {
        var grid = Grid();
        TestHelpers.Show(grid, 1100, 400);
        grid.ResizeColumn("Service", 300);
        grid.AutoSizeColumn("Service");
        TestHelpers.Pump();
        Assert.InRange(grid.Layout.Find("Service")!.Width, 48, 200);
    }

    [AvaloniaFact]
    public void Grouping_shows_group_rows_with_counts_aggregates_and_a_footer()
    {
        var grid = Grid(count: 100);
        grid.Groupable = true;
        grid.ShowFooter = true;
        grid.GroupBy = ["Status"];
        var window = TestHelpers.Show(grid, 1100, 600);

        var first = grid.RowAt(0);
        Assert.Equal(GridRowKind.Group, first.Kind);
        Assert.Equal(25, first.Row!.RowCount);
        Assert.Equal(GridRowKind.Data, grid.RowAt(1).Kind);
        Assert.Equal(104, grid.ViewRowCount);

        var groupRow = grid.RowsPresenter!.RowFor(0)!;
        Assert.Contains("group", groupRow.Classes);
        var header = groupRow.GetVisualDescendants().OfType<DataGridGroupHeader>();
        Assert.Contains("25", string.Join(" ", header.SelectMany(h => h.GetVisualDescendants().OfType<TextBlock>()).Select(t => t.Text)));

        // Collapse via the engine-backed toggle; clicking the group row does the same.
        grid.ToggleGroup(first.Row.GroupId!);
        TestHelpers.Pump(window);
        Assert.Equal(79, grid.ViewRowCount);

        Assert.True(grid.FooterRow!.IsVisible);
        var total = Make(100).Sum(d => d.Duration);
        Assert.Contains(total.ToString("N0", System.Globalization.CultureInfo.InvariantCulture), grid.FooterRow.Cells["Duration"].Text);

        grid.RemoveGroupBy("Status");
        TestHelpers.Pump(window);
        Assert.Equal(100, grid.ViewRowCount);
        Assert.Empty(grid.GroupBy ?? []);
    }

    [AvaloniaFact]
    public void Tree_data_indents_children_and_expands_with_the_keyboard()
    {
        var root = new Deployment { Id = "root", Service = "src", Duration = 30, Children = [new() { Id = "a", Service = "a.cs", Duration = 10 }, new() { Id = "b", Service = "b.cs", Duration = 20 }] };
        var grid = Grid([root, new Deployment { Id = "solo", Service = "readme" }]);
        grid.ChildrenSelector = item => ((Deployment)item).Children;
        var window = TestHelpers.Show(grid, 900, 400);

        // Tree nodes start expanded; Left collapses on the first column, Right expands again.
        Assert.Equal(4, grid.ViewRowCount);
        Assert.True(grid.RowAt(0).Row!.HasChildren);
        Assert.Equal(1, grid.RowAt(1).Depth);
        var toggle = grid.RowsPresenter!.RowFor(0)!.Cells["Id"].GetVisualDescendants().OfType<Button>().Single(b => b.Classes.Contains("sl-grid-tree-toggle"));
        Assert.True(toggle.IsVisible);
        Assert.True(grid.RowsPresenter.RowFor(1)!.Cells["Id"].GetVisualDescendants().OfType<TextBlock>().First(t => t.Text == "a").Margin.Left >= 40);
        grid.Focus();
        grid.MoveActive(GridKey.Down);
        grid.MoveActive(GridKey.Up);
        window.Press(Key.Left);
        Assert.Equal(2, grid.ViewRowCount);
        window.Press(Key.Right);
        Assert.Equal(4, grid.ViewRowCount);
        toggle = grid.RowsPresenter.RowFor(0)!.Cells["Id"].GetVisualDescendants().OfType<Button>().Single(b => b.Classes.Contains("sl-grid-tree-toggle"));
        window.Click(toggle);
        Assert.Equal(2, grid.ViewRowCount);
    }

    [AvaloniaFact]
    public void Row_detail_expands_below_the_row()
    {
        var grid = Grid(count: 20);
        grid.RowDetail = new FuncDataTemplate<object>((item, _) => new TextBlock { Text = "detail " + ((Deployment)item).Id });
        var window = TestHelpers.Show(grid, 900, 500);
        var before = grid.RowsPresenter!.RowFor(1)!.Bounds.Y;
        grid.ToggleDetail("d000000");
        TestHelpers.Pump(window);
        Assert.Contains("d000000", grid.State.ExpandedDetails);
        Assert.Equal(GridRowKind.Detail, grid.RowAt(1).Kind);
        Assert.True(grid.RowsPresenter.RowFor(2)!.Bounds.Y > before + 20, "the next row moved down below the detail");
        Assert.Contains(grid.RowsPresenter.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == "detail d000000");
    }

    [AvaloniaFact]
    public void Selection_supports_click_ctrl_shift_select_all_and_header_tri_state()
    {
        var grid = Grid(count: 20);
        var window = TestHelpers.Show(grid, 1100, 600);
        var changes = 0;
        grid.SelectionChanged += (_, _) => changes++;

        window.Click(grid.RowsPresenter!.RowFor(2)!.Cells["Service"]);
        Assert.Equal(["d000002"], grid.Selection.Selected);
        Assert.True(grid.RowsPresenter.RowFor(2)!.IsSelected);
        Assert.Contains("selected", grid.RowsPresenter.RowFor(2)!.Classes);

        grid.ClickRow("d000005", shift: true);
        TestHelpers.Pump(window);
        Assert.Equal(4, grid.Selection.Count);
        grid.ClickRow("d000010", ctrl: true);
        TestHelpers.Pump(window);
        Assert.Equal(5, grid.Selection.Count);
        Assert.Equal(5, grid.SelectedItems.Count);
        Assert.Equal(SelectAllState.Some, grid.HeaderSelectionState);
        Assert.Null(grid.HeaderRow!.SelectAll!.IsChecked);

        grid.ToggleSelectAll();
        TestHelpers.Pump(window);
        Assert.Equal(20, grid.Selection.Count);
        Assert.True(grid.HeaderRow.SelectAll.IsChecked);

        // The selection bar shows the count.
        var texts = grid.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text).ToList();
        Assert.Contains(texts, t => t?.Contains("20 selected") == true);

        window.Press(Key.Escape);
        grid.Focus();
        window.Press(Key.Escape);
        Assert.Equal(0, grid.Selection.Count);
        Assert.True(changes >= 4);
    }

    [AvaloniaFact]
    public void Keyboard_moves_the_active_cell_and_shows_it_only_for_keyboard_users()
    {
        var grid = Grid(count: 200);
        var window = TestHelpers.Show(grid, 900, 500);
        window.Click(grid.RowsPresenter!.RowFor(0)!.Cells["Service"]);
        Assert.Equal(new GridCell(0, 1), grid.ActiveCell);
        Assert.False(grid.KeyboardFocusVisible);
        Assert.DoesNotContain("active", grid.RowsPresenter.RowFor(0)!.Cells["Service"].Classes);

        window.Press(Key.Down);
        window.Press(Key.Right);
        Assert.Equal(new GridCell(1, 2), grid.ActiveCell);
        Assert.True(grid.KeyboardFocusVisible);
        Assert.Contains("active", grid.RowsPresenter.RowFor(1)!.Cells["Status"].Classes);

        window.Press(Key.End, RawInputModifiers.Control);
        Assert.Equal(199, grid.ActiveCell!.Value.Row);
        Assert.Contains(199, grid.RowsPresenter.RealizedIndices);

        window.Press(Key.PageUp);
        Assert.True(grid.ActiveCell!.Value.Row < 199);
        window.Press(Key.Home, RawInputModifiers.Control);
        Assert.Equal(new GridCell(0, 0), grid.ActiveCell);

        // The click selected row 0; Space toggles it.
        window.Press(Key.Space);
        Assert.False(grid.Selection.IsSelected("d000000"));
        window.Press(Key.Space);
        Assert.True(grid.Selection.IsSelected("d000000"));
        window.Press(Key.Down, RawInputModifiers.Shift);
        window.Press(Key.Down, RawInputModifiers.Shift);
        Assert.Equal(3, grid.Selection.Count);
        window.Press(Key.A, RawInputModifiers.Control);
        Assert.Equal(200, grid.Selection.Count);
    }

    [AvaloniaFact]
    public void Enter_activates_a_row_when_it_is_not_editable()
    {
        var grid = Grid(count: 5);
        var window = TestHelpers.Show(grid, 900, 400);
        object? activated = null;
        grid.RowActivated += (_, e) => activated = e.Item;
        grid.Focus();
        grid.MoveActive(GridKey.Down); // the first key shows the active cell at the top-left
        grid.MoveActive(GridKey.Down);
        window.Press(Key.Enter);
        Assert.Equal("d000001", ((Deployment)activated!).Id);
    }

    [AvaloniaFact]
    public void Cell_editing_commits_through_the_setter_and_validates()
    {
        var items = Make(10);
        var grid = Grid(items);
        grid.EditMode = GridEditMode.Cell;
        grid.Columns.Single(c => c.Field == "Duration").Editable = true;
        grid.Columns.Single(c => c.Field == "Duration").Validate = v => v is double d && d < 0 ? "Must be positive" : null;
        grid.Columns.Single(c => c.Field == "Service").Editable = true;
        var window = TestHelpers.Show(grid, 1100, 400);
        GridCellChange<object>? committed = null;
        grid.CellEditCommitted += (_, e) => committed = e.Change;

        // Typing on the active cell starts editing with that text; Enter commits and moves down.
        window.Click(grid.RowsPresenter!.RowFor(0)!.Cells["Service"]);
        Assert.True(grid.BeginEdit(initialText: "api"));
        TestHelpers.Pump(window);
        var editor = Assert.IsAssignableFrom<TextBox>(grid.RowsPresenter.RowFor(0)!.Cells["Service"].Editor);
        Assert.Equal("api", editor.Text);
        window.Press(Key.Enter);
        Assert.Equal("api", items[0].Service);
        Assert.Equal("Service", committed!.Field);
        Assert.Equal(new GridCell(1, 1), grid.ActiveCell);

        // Invalid values block the commit and mark the cell.
        var dur = new GridCell(1, grid.Layout.Order.ToList().IndexOf("Duration"));
        Assert.True(grid.BeginEdit(dur, "-5"));
        TestHelpers.Pump(window);
        grid.Editing.SetDraftText("-5");
        Assert.False(grid.CommitEdit());
        TestHelpers.Pump(window);
        Assert.Equal("Must be positive", grid.Editing.Current!.Error);
        Assert.Contains("invalid", grid.RowsPresenter.RowFor(1)!.Cells["Duration"].Classes);
        window.Press(Key.Escape);
        Assert.False(grid.Editing.IsEditing);
        Assert.Equal(1, items[1].Duration);

        // Non-editable columns refuse.
        Assert.False(grid.BeginEdit(new GridCell(0, grid.Layout.Order.ToList().IndexOf("Region"))));
    }

    [AvaloniaFact]
    public void Batch_editing_collects_changes_and_commits_or_discards_them()
    {
        var items = Make(10);
        var grid = Grid(items);
        grid.EditMode = GridEditMode.Batch;
        grid.Columns.Single(c => c.Field == "Service").Editable = true;
        var window = TestHelpers.Show(grid, 1100, 400);

        foreach (var (row, text) in new[] { (0, "one"), (1, "two") })
        {
            Assert.True(grid.BeginEdit(new GridCell(row, 1), text));
            Assert.True(grid.CommitEdit());
        }
        TestHelpers.Pump(window);
        Assert.Equal("svc-0", items[0].Service);
        Assert.Equal(2, grid.Editing.Pending.Count);
        Assert.Equal("one", grid.RowsPresenter!.RowFor(0)!.Cells["Service"].Text);
        Assert.Contains("pending", grid.RowsPresenter.RowFor(0)!.Cells["Service"].Classes);
        var texts = grid.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text).ToList();
        Assert.Contains("2 unsaved changes", texts);
        // Exports include pending values.
        Assert.Contains("one", grid.ExportCsv());

        grid.DiscardAll();
        TestHelpers.Pump(window);
        Assert.Empty(grid.Editing.Pending);
        Assert.Equal("svc-0", grid.RowsPresenter.RowFor(0)!.Cells["Service"].Text);

        Assert.True(grid.BeginEdit(new GridCell(2, 1), "three"));
        grid.CommitEdit();
        grid.CommitAll();
        TestHelpers.Pump(window);
        Assert.Equal("three", items[2].Service);
        Assert.Empty(grid.Editing.Pending);
    }

    [AvaloniaFact]
    public void Export_and_copy_produce_csv_and_tsv()
    {
        var grid = Grid(count: 5);
        var window = TestHelpers.Show(grid, 1100, 400);
        var csv = grid.ExportCsv();
        var lines = csv.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(6, lines.Length);
        Assert.StartsWith("Id,Service,Status,Region,Duration,Progress,Healthy", lines[0]);

        grid.ClickRow("d000001");
        grid.ClickRow("d000003", ctrl: true);
        grid.Focus();
        window.Press(Key.C, RawInputModifiers.Control);
        var tsv = grid.LastCopiedText!.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(2, tsv.Length);
        Assert.StartsWith("d000001\tsvc-1\t", tsv[0]);

        window.Press(Key.C, RawInputModifiers.Control | RawInputModifiers.Shift);
        Assert.StartsWith("Id\tService", grid.LastCopiedText);
        Assert.Equal(2, grid.RowsFor(DataGrid.ExportScope.Selected).Count);
    }

    [AvaloniaFact]
    public void State_round_trips_through_json_and_raises_state_changed()
    {
        var grid = Grid(count: 100);
        grid.Groupable = true;
        var window = TestHelpers.Show(grid, 1100, 500);
        var raised = 0;
        grid.StateChanged += (_, _) => raised++;

        grid.ToggleSort("Duration");
        grid.SetFilter(new GridFilter("Region", FilterOperator.Equals, "eu-west"));
        grid.ResizeColumn("Service", 222);
        grid.PinColumn("Region", GridPin.End);
        grid.AddGroupBy("Status");
        TestHelpers.Pump(window);
        Assert.True(raised >= 5);
        var json = grid.State.ToJson();

        var other = Grid(count: 100);
        other.Groupable = true;
        var w2 = TestHelpers.Show(other, 1100, 500);
        other.State = GridState.FromJson(json);
        TestHelpers.Pump(w2);
        Assert.Equal(grid.State, other.State);
        Assert.Equal(222, other.Layout.Find("Service")!.Width);
        Assert.Equal(GridPin.End, other.Layout.Find("Region")!.Pin);
        Assert.Equal(grid.ViewRowCount, other.ViewRowCount);
        Assert.Equal(["Status"], other.GroupBy);
        Assert.Equal(json, other.State.ToJson());
    }

    [AvaloniaFact]
    public void Server_mode_shows_skeletons_then_loads_and_selects_all_matching()
    {
        var time = new FakeTimeProvider();
        var data = Make(1000);
        var columns = new List<GridColumn<Deployment>>
        {
            new() { Field = "Id" }, new() { Field = "Service" }, new() { Field = "Status", Type = GridColumnType.Enum },
            new() { Field = "Region" }, new() { Field = "Duration", Type = GridColumnType.Number },
            new() { Field = "Progress", Type = GridColumnType.Progress }, new() { Field = "Healthy", Type = GridColumnType.Boolean },
        };
        var grid = Grid([]);
        grid.Items = null;
        grid.DataSource = GridDataSourceAdapter.From(new InMemoryGridDataSource<Deployment>(data, columns, TimeSpan.FromMilliseconds(300), time));
        var window = TestHelpers.Show(grid, 1100, 500);

        Assert.True(grid.IsServerMode);
        Assert.True(grid.RowAt(0).IsSkeleton);
        Assert.Contains("skeleton", grid.RowsPresenter!.RowFor(0)!.Classes);

        WaitFor(window, time, () => grid.MatchingCount == 1000 && !grid.RowAt(0).IsSkeleton);
        Assert.Equal(1000, grid.ViewRowCount);
        Assert.Equal("d000000", ItemAt(grid, 0).Id);
        Assert.Equal("d000000", grid.RowsPresenter.RowFor(0)!.Cells["Id"].Text);

        // Infinite scroll: far rows start as skeletons and load on demand.
        grid.ScrollToRow(900);
        TestHelpers.Pump(window);
        WaitFor(window, time, () => !grid.RowAt(900).IsSkeleton);
        Assert.Equal("d000900", ItemAt(grid, 900).Id);

        // Sorting re-queries the source.
        grid.ToggleSort("Duration", additive: false);
        grid.ToggleSort("Duration", additive: false);
        TestHelpers.Pump(window);
        grid.ScrollToRow(0);
        WaitFor(window, time, () => !grid.RowAt(0).IsSkeleton && ItemAt(grid, 0).Duration == 99);

        grid.ToggleSelectAll();
        TestHelpers.Pump(window);
        Assert.True(grid.Selection.AllMatching);
        Assert.Equal(SelectAllState.All, grid.HeaderSelectionState);
        Assert.Contains(grid.GetVisualDescendants().OfType<TextBlock>(), t => t.Text?.Contains("1,000 selected") == true || t.Text?.Contains("All 1,000") == true);
    }

    private static void WaitFor(TopLevel window, FakeTimeProvider time, Func<bool> condition)
    {
        var sw = Stopwatch.StartNew();
        while (!condition())
        {
            Assert.True(sw.Elapsed < TimeSpan.FromSeconds(10), "timed out waiting for the data source");
            time.Advance(TimeSpan.FromMilliseconds(100));
            Thread.Sleep(5);
            TestHelpers.Pump(window);
        }
    }

    [AvaloniaFact]
    public void Paging_shows_a_pagination_control_and_slices_rows()
    {
        var grid = Grid(count: 95);
        grid.Pagination = GridPagination.Pages;
        grid.PageSize = 20;
        var window = TestHelpers.Show(grid, 1100, 900);
        Assert.Equal(20, grid.ViewRowCount);
        var pager = grid.GetVisualDescendants().OfType<global::Slate.Avalonia.Controls.Pagination>().Single();
        Assert.Equal(5, pager.PageCount);
        pager.Page = 5;
        TestHelpers.Pump(window);
        Assert.Equal(4, grid.State.PageIndex);
        Assert.Equal(15, grid.ViewRowCount);
        Assert.Equal("d000080", ItemAt(grid, 0).Id);
    }

    [AvaloniaFact]
    public void Live_updates_flash_rows_and_row_tone_tints_them()
    {
        var items = Make(10);
        var grid = Grid(items);
        grid.RowTone = item => ((Deployment)item).Status == "Failed" ? Tone.Danger : null;
        var window = TestHelpers.Show(grid, 1100, 400);
        Assert.Equal(TestHelpers.ColorOf((global::Avalonia.Media.IBrush)TestHelpers.Resource("Sl.Brush.Status.Danger.Bg")), TestHelpers.ColorOf(grid.RowsPresenter!.RowFor(3)!.Background));

        items[0].Service = "changed";
        grid.NotifyItemsChanged(items[0]);
        TestHelpers.Pump(window);
        Assert.Equal("changed", grid.RowsPresenter.RowFor(0)!.Cells["Service"].Text);
        Assert.True(grid.IsFlashing("d000000"));
    }

    [AvaloniaFact]
    public void Empty_content_shows_when_no_rows_match()
    {
        var grid = Grid(count: 5);
        grid.EmptyContent = "Nothing here";
        var window = TestHelpers.Show(grid, 900, 400);
        var empty = grid.Part<ContentControl>("PART_Empty");
        Assert.False(empty.IsVisible);
        grid.QuickFilter = "zzz";
        TestHelpers.Pump(window);
        Assert.True(empty.IsVisible);
        Assert.Equal(0, grid.ViewRowCount);
    }

    [AvaloniaFact]
    public void Automation_peers_expose_grid_rows_headers_and_selection()
    {
        var grid = Grid(count: 10);
        grid.GroupBy = ["Status"];
        var window = TestHelpers.Show(grid, 1100, 500);

        var peer = ControlAutomationPeer.CreatePeerForElement(grid);
        Assert.Equal(AutomationControlType.DataGrid, peer.GetAutomationControlType());
        var selection = Assert.IsAssignableFrom<ISelectionProvider>(peer);
        Assert.True(selection.CanSelectMultiple);

        var header = ControlAutomationPeer.CreatePeerForElement(grid.HeaderRow!.Cells["Duration"]);
        Assert.Equal(AutomationControlType.HeaderItem, header.GetAutomationControlType());
        Assert.Equal("Duration", header.GetName());
        Assert.IsAssignableFrom<IInvokeProvider>(header).Invoke();
        Assert.Equal("Duration", grid.State.Sorts[0].Field);

        var groupPeer = ControlAutomationPeer.CreatePeerForElement(grid.RowsPresenter!.RowFor(0)!);
        Assert.Equal(AutomationControlType.Group, groupPeer.GetAutomationControlType());
        var expand = Assert.IsAssignableFrom<IExpandCollapseProvider>(groupPeer);
        Assert.Equal(global::Avalonia.Automation.ExpandCollapseState.Expanded, expand.ExpandCollapseState);

        var rowPeer = ControlAutomationPeer.CreatePeerForElement(grid.RowsPresenter.RowFor(1)!);
        Assert.Equal(AutomationControlType.DataItem, rowPeer.GetAutomationControlType());
        Assert.IsAssignableFrom<ISelectionItemProvider>(rowPeer).Select();
        TestHelpers.Pump(window);
        Assert.Single(selection.GetSelection());

        expand.Collapse();
        TestHelpers.Pump(window);
        Assert.Equal(11, grid.ViewRowCount); // 4 groups + 10 rows, minus the first group's 3
    }

    [AvaloniaFact]
    public void Theme_switch_recolours_the_grid()
    {
        var grid = Grid(count: 5);
        var window = TestHelpers.Show(grid, 900, 400);
        var light = TestHelpers.ColorOf(grid.Background);
        var lightHeader = TestHelpers.ColorOf(grid.HeaderRow!.Cells["Id"].Background);
        TestHelpers.WithTheme(t => t.Mode = ThemeMode.Dark, t => t.Mode = ThemeMode.System, () =>
        {
            TestHelpers.Pump(window);
            var dark = TestHelpers.ColorOf(grid.Background);
            Assert.NotEqual(light, dark);
            Assert.Equal(TestHelpers.ColorOf((global::Avalonia.Media.IBrush)TestHelpers.Resource("Sl.Component.Grid.Background.Brush", ThemeVariant.Dark)), dark);
            Assert.NotEqual(lightHeader, TestHelpers.ColorOf(grid.HeaderRow.Cells["Id"].Background));
            // Pinned cells composite over the dark background.
            Assert.Equal(dark, TestHelpers.ColorOf(grid.RowsPresenter!.RowFor(0)!.Cells["Id"].Background));
        });
    }

    [AvaloniaFact]
    public void Scrolling_100k_rows_keeps_the_realised_set_bounded_and_fast()
    {
        var grid = Grid(Make(100_000));
        var window = TestHelpers.Show(grid, 1280, 800);
        var scroll = grid.ScrollViewer!;
        var max = 0;
        var times = new List<double>();
        var sw = new Stopwatch();
        for (var y = 0.0; y < 100_000 * grid.RowHeight; y += 100_000 * grid.RowHeight / 400)
        {
            sw.Restart();
            scroll.Offset = new Vector(0, y);
            TestHelpers.Pump(window);
            sw.Stop();
            times.Add(sw.Elapsed.TotalMilliseconds);
            max = Math.Max(max, grid.RowsPresenter!.RealizedCount);
        }
        // Small steps (wheel-like) reuse most containers.
        for (var i = 0; i < 200; i++)
        {
            sw.Restart();
            scroll.Offset = new Vector(0, scroll.Offset.Y - 48);
            TestHelpers.Pump(window);
            sw.Stop();
            times.Add(sw.Elapsed.TotalMilliseconds);
            max = Math.Max(max, grid.RowsPresenter!.RealizedCount);
        }
        static string Stats(List<double> t)
        {
            t = t.Order().ToList();
            return $"median={t[t.Count / 2]:0.00}ms p95={t[(int)(t.Count * 0.95)]:0.00}ms max={t[^1]:0.00}ms";
        }
        output.WriteLine($"100k rows: maxRealised={max}; jumps(400): {Stats(times[..400])}; wheel(200): {Stats(times[400..])}");
        times.Sort();
        var median = times[times.Count / 2];
        Assert.InRange(max, 1, 60);
        Assert.True(median < 25, $"median step {median:0.00}ms");
    }
}
