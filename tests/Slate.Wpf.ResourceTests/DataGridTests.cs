using System.Reflection;
using System.Text.RegularExpressions;
using Slate.Data;

namespace Slate.Wpf.ResourceTests;

/// <summary>
/// sl:DataGrid checks that run on every OS: its resource keys and icon names exist, and its engine wiring
/// (<see cref="GridController"/>, compiled into this project from Slate.Wpf's source) behaves — sort, filter, group,
/// selection, keyboard navigation, editing, export, server mode, geometry and live updates.
/// </summary>
public partial class DataGridTests
{
    private static readonly string Sources = Path.Combine(XamlResourceTests.RepoRoot, "src", "Slate.Wpf", "Controls", "DataGrid");

    // ---- resources -------------------------------------------------------------------------------------------

    [Fact]
    public void Grid_resource_keys_exist()
    {
        var defined = XamlResourceTests.AllDefinedKeys();
        var keys = typeof(GridKeys).GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(f => f.IsLiteral && f.FieldType == typeof(string))
            .Select(f => (string)f.GetRawConstantValue()!)
            .Concat(Enum.GetValues<Tone>().SelectMany(t => new[] { GridKeys.ToneBackground(t), GridKeys.ToneForeground(t), GridKeys.ToneSolid(t) }))
            .ToList();
        Assert.NotEmpty(keys);
        var missing = keys.Where(k => !defined.Contains(k)).ToList();
        Assert.True(missing.Count == 0, "Missing: " + string.Join(", ", missing));
    }

    [GeneratedRegex("\"(Sl\\.[A-Za-z0-9.]+)\"")]
    private static partial Regex KeyLiteral();

    [Fact]
    public void Resource_keys_named_in_grid_code_exist()
    {
        var defined = XamlResourceTests.AllDefinedKeys();
        var used = Directory.EnumerateFiles(Sources, "*.cs")
            .SelectMany(f => KeyLiteral().Matches(File.ReadAllText(f)).Select(m => (File: Path.GetFileName(f), Key: m.Groups[1].Value)))
            .ToList();
        Assert.NotEmpty(used);
        Assert.Empty(used.Where(u => !defined.Contains(u.Key)).Select(u => $"{u.File}: {u.Key}"));
    }

    [GeneratedRegex("(?<=Kind = |Icon\\(\\w+, |StartIcon = |IconButton\\(|ChromeButton\\(\"[^\"]*\", |Item\\(\"[^\"]*\", |(?:open|Expanded) \\? \"chevron-[a-z]+\" : |(?:open|Expanded) \\? (?=\"chevron))\"([a-z][a-z0-9]*(?:-[a-z0-9]+)*)\"")]
    private static partial Regex IconLiteral();

    [Fact]
    public void Icons_named_in_grid_code_exist()
    {
        var used = Directory.EnumerateFiles(Sources, "*.cs")
            .SelectMany(f => IconLiteral().Matches(File.ReadAllText(f)).Select(m => m.Groups[1].Value))
            .Distinct()
            .ToList();
        Assert.Contains("chevron-right", used);
        Assert.Contains("more-horizontal", used);
        var unknown = used.Where(i => !SlateIcons.All.ContainsKey(i)).ToList();
        Assert.True(unknown.Count == 0, "Unknown icons: " + string.Join(", ", unknown));
    }

    [Fact]
    public void DataGrid_style_is_defined_with_its_root_part()
    {
        var generic = File.ReadAllText(Path.Combine(XamlResourceTests.RepoRoot, "src", "Slate.Wpf", "Themes", "Generic.xaml"));
        Assert.Contains("<Style TargetType=\"sl:DataGrid\">", generic);
        Assert.Contains("x:Name=\"PART_Root\"", generic);
    }

    [Fact]
    public void DataGrid_metadata_matches_the_engine_contract()
    {
        var grid = WpfMetadata.Find("sl:DataGrid")!;
        Assert.NotNull(grid);
        Assert.Equal("GridState", WpfMetadata.PropertyType(grid, "State")!.Name);
        Assert.Equal("GridSelectionMode", WpfMetadata.PropertyType(grid, "SelectionMode")!.Name);
        Assert.Equal("GridEditMode", WpfMetadata.PropertyType(grid, "EditMode")!.Name);
        Assert.Equal("GridPagination", WpfMetadata.PropertyType(grid, "Pagination")!.Name);
        var column = WpfMetadata.Find("sl:GridColumn")!;
        foreach (var option in new[] { "Field", "Title", "Type", "Width", "MinWidth", "MaxWidth", "Flex", "Sortable", "Filterable", "Resizable", "Reorderable", "Hideable", "Pinned", "Hidden", "Align", "Format", "Aggregate", "Editable", "Validate", "Template", "CellTone" })
            Assert.True(WpfMetadata.HasMember(column, option), $"GridColumn.{option}");
    }

    // ---- engine wiring ---------------------------------------------------------------------------------------

    private sealed class Deploy
    {
        public int Id { get; set; }
        public string Service { get; set; } = "";
        public string Status { get; set; } = "";
        public double Duration { get; set; }
        public bool Canary { get; set; }
        public List<Deploy> Children { get; } = [];
    }

    private static readonly string[] Statuses = ["Queued", "Building", "Ready", "Failed"];

    private static List<Deploy> Deploys(int n) =>
        Enumerable.Range(0, n).Select(i => new Deploy
        {
            Id = i,
            Service = "svc-" + (i % 7).ToString(System.Globalization.CultureInfo.InvariantCulture),
            Status = Statuses[i % 4],
            Duration = i % 10 * 10,
            Canary = i % 3 == 0,
        }).ToList();

    private static List<GridColumn<object>> Columns(bool editable = false) =>
    [
        new() { Field = "Id", Type = GridColumnType.Number, Accessor = o => ((Deploy)o).Id, Width = 80 },
        new() { Field = "Service", Accessor = o => ((Deploy)o).Service, Setter = (o, v) => ((Deploy)o).Service = (string)v!, Editable = editable, Validate = v => string.IsNullOrWhiteSpace(v as string) ? "Required" : null, Width = 160 },
        new() { Field = "Status", Type = GridColumnType.Enum, EnumOrder = Statuses, Accessor = o => ((Deploy)o).Status, Width = 120 },
        new() { Field = "Duration", Type = GridColumnType.Number, Aggregate = GridAggregate.Sum, Accessor = o => ((Deploy)o).Duration, Setter = (o, v) => ((Deploy)o).Duration = Convert.ToDouble(v, System.Globalization.CultureInfo.InvariantCulture), Editable = editable, Width = 120 },
        new() { Field = "Canary", Type = GridColumnType.Boolean, Accessor = o => ((Deploy)o).Canary, Width = 90 },
    ];

    private static GridController Controller(int n = 100, bool editable = false, GridSelectionMode selection = GridSelectionMode.Multi)
    {
        var c = new GridController { SelectionMode = selection };
        c.SetColumns(Columns(editable));
        c.SetRowKey(o => ((Deploy)o).Id);
        c.SetItems(Deploys(n));
        return c;
    }

    private static int IdAt(GridController c, int row) => ((Deploy)c.RowAt(row)!.Item!).Id;

    [Fact]
    public void Sorting_toggles_and_stacks_with_shift()
    {
        var c = Controller(20);
        var raised = 0;
        c.StateChanged += (_, _) => raised++;
        Assert.True(c.ToggleSort("Duration"));
        Assert.Equal(0, ((Deploy)c.RowAt(0)!.Item!).Duration);
        c.ToggleSort("Duration");
        Assert.Equal(90, ((Deploy)c.RowAt(0)!.Item!).Duration);
        c.ToggleSort("Id", additive: true);
        Assert.Equal(["Duration", "Id"], c.State.Sorts.Select(s => s.Field));
        Assert.Equal(9, IdAt(c, 0)); // Duration desc (90: ids 9, 19), then Id asc
        Assert.Equal(19, IdAt(c, 1));
        Assert.Equal(3, raised);
    }

    [Fact]
    public void Filters_produce_chips_and_quick_filter_searches()
    {
        var c = Controller(100);
        c.SetFilter(new GridFilter("Status", FilterOperator.AnyOf, Values: ["Ready", "Failed"]));
        Assert.Equal(50, c.MatchingCount);
        Assert.Equal("Status is any of Ready, Failed", Assert.Single(c.FilterChips).Text);
        c.SetFilter(new GridFilter("Duration", FilterOperator.Between, 20.0, 40.0));
        Assert.Equal(2, c.FilterChips.Count);
        Assert.All(Enumerable.Range(0, c.RowCount), i => Assert.InRange(((Deploy)c.RowAt(i)!.Item!).Duration, 20, 40));
        c.ClearFilters();
        c.SetQuickFilter("svc-3");
        Assert.All(Enumerable.Range(0, c.RowCount), i => Assert.Equal("svc-3", ((Deploy)c.RowAt(i)!.Item!).Service));
        Assert.Equal(100, c.TotalCount);
    }

    [Fact]
    public void Operators_are_type_aware()
    {
        Assert.Contains(FilterOperator.Between, GridController.OperatorsFor(GridColumnType.Date));
        Assert.DoesNotContain(FilterOperator.Contains, GridController.OperatorsFor(GridColumnType.Number));
        Assert.Equal(FilterOperator.AnyOf, GridController.OperatorsFor(GridColumnType.Enum)[0]);
        Assert.Equal("≥", GridController.OperatorText(FilterOperator.GreaterThanOrEqual));
        Assert.Equal(Statuses, Controller().DistinctValues("Status"));
    }

    [Fact]
    public void Grouping_adds_group_rows_with_counts_aggregates_and_toggles()
    {
        var c = Controller(40);
        Assert.True(c.GroupBy("Status"));
        var group = c.RowAt(0)!;
        Assert.Equal(GridRowKind.Group, group.Kind);
        Assert.Equal(10, group.RowCount);
        Assert.NotNull(group.Aggregates);
        Assert.True(group.Aggregates!.ContainsKey("Duration"));
        Assert.Equal(44, c.RowCount); // 4 groups + 40 rows
        c.ToggleExpand(0);
        Assert.Equal(34, c.RowCount);
        c.CollapseAll();
        Assert.Equal(4, c.RowCount);
        c.ExpandAll();
        Assert.Equal(44, c.RowCount);
        c.Ungroup("Status");
        Assert.Equal(40, c.RowCount);
        Assert.Equal(Deploys(40).Sum(d => d.Duration), Convert.ToDouble(c.Totals["Duration"], System.Globalization.CultureInfo.InvariantCulture));
    }

    [Fact]
    public void Tree_rows_expand_and_collapse()
    {
        var root = new Deploy { Id = 1, Service = "root" };
        root.Children.Add(new Deploy { Id = 2, Service = "child" });
        root.Children.Add(new Deploy { Id = 3, Service = "child" });
        var c = new GridController();
        c.SetColumns(Columns());
        c.SetRowKey(o => ((Deploy)o).Id);
        c.SetChildren(o => ((Deploy)o).Children);
        c.SetItems(new List<Deploy> { root });
        Assert.Equal(3, c.RowCount);
        Assert.True(c.RowAt(0)!.HasChildren);
        Assert.Equal(1, c.RowAt(1)!.Depth);
        Assert.True(c.ToggleExpand(0));
        Assert.Equal(1, c.RowCount);
        var nav = c.Navigate(GridKey.Plus, 400, 5);
        Assert.Equal(GridNavAction.Expand, nav.Action);
        Assert.Equal(3, c.RowCount);
        Assert.False(c.GroupBy("Status")); // grouping is off for tree data
    }

    [Fact]
    public void Selection_click_ctrl_shift_and_select_all()
    {
        var c = Controller(20);
        var changes = 0;
        c.SelectionChanged += (_, _) => changes++;
        c.Click(2);
        c.Click(5, shift: true);
        Assert.Equal(4, c.Selection.Count);
        c.Click(9, ctrl: true);
        Assert.Equal(5, c.Selection.Count);
        Assert.Equal(SelectAllState.Some, c.HeaderState);
        c.ToggleAll();
        Assert.Equal(SelectAllState.All, c.HeaderState);
        c.ToggleAll();
        Assert.Equal(0, c.Selection.Count);
        Assert.True(changes >= 4);

        // Selection is keyed, so it survives sorting.
        c.Click(0);
        c.ToggleSort("Id");
        c.ToggleSort("Id");
        Assert.Equal(0, ((Deploy)Assert.Single(c.SelectedItems)).Id);
        Assert.False(c.IsSelected(c.RowAt(0)!.Key));
    }

    [Fact]
    public void Selected_items_round_trip_and_select_all_matching()
    {
        var c = Controller(30);
        var items = Deploys(30); // different instances: keys come from RowKey
        c.SetSelectedItems([c.ItemOf("3")!, c.ItemOf("4")!]);
        Assert.Equal([3, 4], c.SelectedItems.Cast<Deploy>().Select(d => d.Id).Order());
        c.SetFilter(new GridFilter("Status", FilterOperator.AnyOf, Values: ["Ready"]));
        c.SelectAllMatching();
        Assert.True(c.Selection.AllMatching);
        Assert.Equal(c.MatchingCount, c.Selection.Count);
        Assert.NotEmpty(items);
    }

    [Fact]
    public void Keyboard_navigation_moves_and_clamps_the_active_cell()
    {
        var c = Controller(100);
        c.Navigate(GridKey.Down, 320, 5);
        c.Navigate(GridKey.Right, 320, 5);
        Assert.Equal(new GridCell(1, 1), c.Active);
        c.Navigate(GridKey.End, 320, 5);
        Assert.Equal(4, c.Active.Column);
        c.Navigate(GridKey.Right, 320, 5);
        Assert.Equal(4, c.Active.Column);
        c.Navigate(GridKey.PageDown, 320, 5);
        Assert.Equal(11, c.Active.Row); // 320 / 32 = 10 rows per page
        c.Navigate(GridKey.CtrlEnd, 320, 5);
        Assert.Equal(new GridCell(99, 4), c.Active);
        c.Navigate(GridKey.CtrlHome, 320, 5);
        Assert.Equal(new GridCell(0, 0), c.Active);
        c.SetFilter(new GridFilter("Id", FilterOperator.LessThan, 3.0));
        c.Active = new GridCell(50, 0);
        c.Navigate(GridKey.Down, 320, 5);
        Assert.Equal(2, c.Active.Row);
    }

    [Fact]
    public void Cell_edits_validate_then_commit_and_move()
    {
        var c = Controller(10, editable: true);
        c.EditMode = GridEditMode.Cell;
        var committed = new List<GridCellChange<object>>();
        c.CellCommitted += (_, change) => committed.Add(change);
        var service = c.Column("Service")!;
        Assert.False(c.CanEdit(0, c.Column("Status")!));
        Assert.True(c.BeginEdit(0, service));
        c.Edit.SetDraftText("  ");
        Assert.Equal("Required", c.Edit.Current!.Error);
        Assert.False(c.CommitEdit());
        c.Edit.SetDraftText("api");
        c.Active = new GridCell(0, 1);
        Assert.True(c.CommitEdit(GridEditCommitKey.Enter, false, 5, 320));
        Assert.Equal("api", ((Deploy)c.RowAt(0)!.Item!).Service);
        Assert.Equal(new GridCell(1, 1), c.Active);
        Assert.Equal("api", Assert.Single(committed).NewValue);
    }

    [Fact]
    public void Batch_edits_stay_pending_until_committed()
    {
        var c = Controller(10, editable: true);
        c.EditMode = GridEditMode.Batch;
        var duration = c.Column("Duration")!;
        c.BeginEdit(1, duration);
        c.Edit.SetDraftText("123");
        Assert.True(c.CommitEdit());
        Assert.Equal(1, c.PendingCount);
        Assert.Equal(10, ((Deploy)c.RowAt(1)!.Item!).Duration);
        Assert.Equal("123", c.Text(c.RowAt(1)!.Item!, c.RowAt(1)!.Key, duration));
        Assert.Contains("123", c.ToCsv(GridExportScope.Visible)); // exports include pending edits
        c.DiscardAll();
        Assert.Equal(0, c.PendingCount);
        c.BeginEdit(1, duration);
        c.Edit.SetDraftText("77");
        c.CommitEdit();
        Assert.Single(c.CommitAll());
        Assert.Equal(77, ((Deploy)c.ItemOf("1")!).Duration);
    }

    [Fact]
    public void Export_and_copy_respect_columns_selection_and_quoting()
    {
        var c = Controller(5);
        ((Deploy)c.ItemOf("2")!).Service = "a, \"b\"";
        c.Invalidate();
        c.Hide("Canary");
        var csv = c.ToCsv(GridExportScope.All).Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal("Id,Service,Status,Duration", csv[0]);
        Assert.Contains("\"a, \"\"b\"\"\"", csv[3]);
        c.Click(1);
        c.Click(3, ctrl: true);
        var tsv = c.CopyText().Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(2, tsv.Length);
        Assert.StartsWith("1\t", tsv[0]);
        Assert.StartsWith("Id\tService", c.CopyText(includeHeader: true));
        Assert.Equal(6, c.ToTsv(GridExportScope.Visible).Split('\n', StringSplitOptions.RemoveEmptyEntries).Length);
    }

    [Fact]
    public void Column_operations_update_state_and_layout()
    {
        var c = Controller(10);
        Assert.True(c.Pin("Status", GridPin.Start));
        Assert.Equal("Status", c.Layout(800).Columns[0].Field);
        Assert.True(c.Resize("Service", 300));
        Assert.Equal(300, c.Layout(800).Find("Service")!.Width);
        Assert.True(c.Resize("Service", 1));
        Assert.Equal(48, c.Layout(800).Find("Service")!.Width); // clamped to MinWidth
        Assert.True(c.Hide("Canary"));
        Assert.Null(c.Layout(800).Find("Canary"));
        Assert.True(c.MoveBy("Duration", -1));
        var fields = c.Layout(800).Columns.Select(x => x.Field).ToList();
        Assert.True(fields.IndexOf("Duration") < fields.IndexOf("Service"));
        Assert.True(c.AutoFit("Service"));
        Assert.True(c.Layout(800).Find("Service")!.Width > 48);
        Assert.True(c.Pin("Id", GridPin.End));
        var layout = c.Layout(800);
        Assert.Equal("Id", layout.Columns[^1].Field);
        Assert.Equal(0, layout.Columns[^1].StickyOffset);
    }

    [Fact]
    public void Layout_only_changes_reuse_the_pipeline_result()
    {
        var c = Controller(1000);
        c.ToggleSort("Duration");
        var result = c.Result;
        c.Resize("Service", 222);
        c.Pin("Id", GridPin.Start);
        Assert.Same(result, c.Result);
        c.Hide("Service"); // hidden columns change the quick-filter scope, so this one recomputes
        Assert.NotSame(result, c.Result);
    }

    [Fact]
    public void State_round_trips_through_json()
    {
        var c = Controller(50);
        c.ToggleSort("Duration");
        c.SetFilter(new GridFilter("Status", FilterOperator.AnyOf, Values: ["Ready"]));
        c.Resize("Service", 240);
        c.GroupBy("Service");
        var json = c.State.ToJson();
        var d = Controller(50);
        Assert.True(d.SetState(GridState.FromJson(json)));
        Assert.Equal(json, d.State.ToJson());
        Assert.Equal(c.RowCount, d.RowCount);
        Assert.False(d.SetState(GridState.FromJson(json)));
    }

    [Fact]
    public void Row_geometry_handles_100k_rows_and_tall_detail_rows()
    {
        var c = Controller(100_000);
        Assert.Equal(100_000 * 32, c.TotalHeight);
        var window = c.Window(1_600_000, 640, overscan: 4);
        Assert.Equal(50_000 - 4, window.First);
        Assert.InRange(window.Count, 20, 30);
        Assert.Equal(50_000, c.IndexAt(1_600_000 + 5));

        c.ToggleDetail("0");
        Assert.Equal(GridRowKind.Detail, c.RowAt(1)!.Kind);
        Assert.Equal(c.DetailHeight, c.HeightOf(1));
        Assert.Equal(32 + c.DetailHeight, c.OffsetOf(2));
        Assert.Equal(1, c.IndexAt(40));
        Assert.Equal(100_001 * 32 - 32 + c.DetailHeight, c.TotalHeight);
        Assert.Equal(c.OffsetOf(2), c.ScrollToReveal(2, 0, 64) + 64 - 32);
    }

    [Fact]
    public void Pages_mode_pages_through_rows()
    {
        var c = Controller(120);
        c.Pagination = GridPagination.Pages;
        c.SetPageSize(50);
        Assert.Equal(3, c.PageCount);
        Assert.Equal(50, c.RowCount);
        c.SetPage(2);
        Assert.Equal(20, c.RowCount);
        Assert.Equal(100, IdAt(c, 0));
        c.SetPage(99);
        Assert.Equal(2, c.State.PageIndex);
    }

    [Fact]
    public async Task Server_mode_shows_placeholders_then_loads_blocks()
    {
        var items = Deploys(1000).Cast<object>().ToList();
        var c = new GridController { SelectionMode = GridSelectionMode.Multi };
        var columns = Columns();
        c.SetColumns(columns);
        c.SetRowKey(o => ((Deploy)o).Id);
        var changed = 0;
        c.ViewChanged += (_, _) => changed++;
        c.SetDataSource(new InMemoryGridDataSource<object>(items, columns));
        Assert.True(c.IsServer);
        Assert.Equal(c.SkeletonRows, c.RowCount);
        Assert.Null(c.RowAt(0)!.Item);
        Assert.StartsWith("#loading", c.RowAt(0)!.Key);

        await c.EnsureLoadedAsync(0, 40);
        Assert.Equal(1000, c.TotalCount);
        Assert.Equal(1000, c.RowCount);
        Assert.Equal(0, IdAt(c, 0));
        Assert.Null(c.RowAt(500)!.Item); // block not loaded yet
        Assert.True(changed > 0);

        c.ToggleSort("Id");
        c.ToggleSort("Id");
        await c.EnsureLoadedAsync(0, 10);
        Assert.Equal(999, IdAt(c, 0));

        c.Click(0);
        c.SelectAllMatching();
        Assert.Equal(1000, c.Selection.Count);
        Assert.False(c.GroupBy("Status")); // grouping is client-side only
    }

    [Fact]
    public void Live_updates_flash_then_fade()
    {
        var c = Controller(10);
        var item = (Deploy)c.ItemOf("4")!;
        item.Duration = 999;
        c.MarkChanged(item, nowMs: 1000);
        Assert.True(c.HasFlashes);
        Assert.Equal(1, c.FlashStrength("4", 1000));
        Assert.InRange(c.FlashStrength("4", 1600), 0.4, 0.6);
        Assert.Equal(0, c.FlashStrength("4", 2300));
        Assert.False(c.HasFlashes);
        Assert.Equal("999", c.Text(item, "4", c.Column("Duration")!));
    }
}
