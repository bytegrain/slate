using System.Diagnostics;
using Microsoft.Extensions.Time.Testing;
using Slate.Data;

namespace Slate.Core.Tests.Data;

public sealed class Person
{
    public int Id { get; set; }
    public string? Name { get; set; }
    public string? Team { get; set; }
    public double? Salary { get; set; }
    public DateTime? Joined { get; set; }
    public bool Active { get; set; }
    public List<Person> Reports { get; set; } = [];
}

internal static class People
{
    public static List<Person> Sample() =>
    [
        new() { Id = 1, Name = "Ada", Team = "Web", Salary = 120, Joined = new DateTime(2024, 1, 5), Active = true },
        new() { Id = 2, Name = "bob", Team = "Desktop", Salary = null, Joined = new DateTime(2023, 6, 1), Active = false },
        new() { Id = 3, Name = "Cy", Team = "Web", Salary = 90, Joined = null, Active = true },
        new() { Id = 4, Name = "ada", Team = "Tokens", Salary = 90, Joined = new DateTime(2025, 2, 2), Active = true },
        new() { Id = 5, Name = null, Team = "Desktop", Salary = 200, Joined = new DateTime(2022, 3, 3), Active = false },
    ];

    public static List<GridColumn<Person>> Columns() =>
    [
        new() { Field = "Id", Type = GridColumnType.Number, Searchable = false },
        new() { Field = "Name" },
        new() { Field = "Team", Type = GridColumnType.Enum, EnumOrder = ["Web", "Desktop", "Tokens"] },
        new() { Field = "Salary", Type = GridColumnType.Number, Aggregate = GridAggregate.Sum, Editable = true, Setter = (p, v) => p.Salary = (double?)v,
                Validate = v => v is double d && d < 0 ? "Must be positive." : null },
        new() { Field = "Joined", Type = GridColumnType.Date, Aggregate = GridAggregate.Min },
        new() { Field = "Active", Type = GridColumnType.Boolean, Aggregate = GridAggregate.Count },
    ];

    public static DataPipeline<Person> Pipeline(bool tree = false, bool paginate = false) =>
        new(Columns(), new DataPipelineOptions<Person> { RowKey = p => p.Id, ChildrenSelector = tree ? p => p.Reports : null, Paginate = paginate });

    public static int[] Ids(GridPipelineResult<Person> r) => [.. r.Rows.Where(x => x.Kind == GridRowKind.Data).Select(x => x.Item!.Id)];
}

public class DataPipelineTests
{
    private readonly List<Person> _data = People.Sample();

    [Fact]
    public void Unsorted_keeps_source_order_and_counts()
    {
        var r = People.Pipeline().Run(_data, new GridState());
        Assert.Equal([1, 2, 3, 4, 5], People.Ids(r));
        Assert.Equal(5, r.TotalCount);
        Assert.Equal(5, r.FilteredCount);
        Assert.Equal(["1", "2", "3", "4", "5"], r.VisibleKeys);
    }

    [Fact]
    public void Text_sort_is_case_insensitive_stable_and_nulls_last_in_both_directions()
    {
        var p = People.Pipeline();
        Assert.Equal([1, 4, 2, 3, 5], People.Ids(p.Run(_data, new GridState().ToggleSort("Name"))));          // Ada < ada (ordinal tie-break), null last
        Assert.Equal([3, 2, 4, 1, 5], People.Ids(p.Run(_data, new GridState().ToggleSort("Name").ToggleSort("Name"))));
    }

    [Fact]
    public void Multi_sort_uses_enum_order_then_secondary_key()
    {
        var state = new GridState().ToggleSort("Team").ToggleSort("Salary", additive: true).ToggleSort("Salary", additive: true);
        Assert.Equal([1, 3, 5, 2, 4], People.Ids(People.Pipeline().Run(_data, state)));
    }

    [Fact]
    public void Equal_keys_keep_source_order()
    {
        var r = People.Pipeline().Run(_data, new GridState().ToggleSort("Salary"));
        Assert.Equal([3, 4, 1, 5, 2], People.Ids(r)); // 90 (3 before 4), 120, 200, null
    }

    [Theory]
    [InlineData(FilterOperator.Contains, "a", null, new[] { 1, 4 })]
    [InlineData(FilterOperator.Equals, "ADA", null, new[] { 1, 4 })]
    [InlineData(FilterOperator.NotEquals, "ada", null, new[] { 2, 3, 5 })]
    [InlineData(FilterOperator.StartsWith, "b", null, new[] { 2 })]
    [InlineData(FilterOperator.EndsWith, "y", null, new[] { 3 })]
    [InlineData(FilterOperator.IsEmpty, null, null, new[] { 5 })]
    [InlineData(FilterOperator.IsNotEmpty, null, null, new[] { 1, 2, 3, 4 })]
    [InlineData(FilterOperator.LessThan, "b", null, new[] { 1, 4 })]
    public void Text_filters(FilterOperator op, string? value, string? value2, int[] expected) =>
        Assert.Equal(expected, People.Ids(People.Pipeline().Run(_data, new GridState().SetFilter(new GridFilter("Name", op, value, value2)))));

    [Theory]
    [InlineData(FilterOperator.Equals, 90.0, null, new[] { 3, 4 })]
    [InlineData(FilterOperator.GreaterThan, 100.0, null, new[] { 1, 5 })]
    [InlineData(FilterOperator.GreaterThanOrEqual, "120", null, new[] { 1, 5 })]
    [InlineData(FilterOperator.LessThanOrEqual, 90.0, null, new[] { 3, 4 })]
    [InlineData(FilterOperator.Between, 150.0, 90.0, new[] { 1, 3, 4 })]
    [InlineData(FilterOperator.NotEquals, 90.0, null, new[] { 1, 2, 5 })]
    [InlineData(FilterOperator.IsEmpty, null, null, new[] { 2 })]
    public void Number_filters(FilterOperator op, object? value, object? value2, int[] expected) =>
        Assert.Equal(expected, People.Ids(People.Pipeline().Run(_data, new GridState().SetFilter(new GridFilter("Salary", op, value, value2)))));

    [Fact]
    public void Date_filters_coerce_iso_strings()
    {
        var r = People.Pipeline().Run(_data, new GridState().SetFilter(new GridFilter("Joined", FilterOperator.Between, "2023-01-01", "2024-12-31")));
        Assert.Equal([1, 2], People.Ids(r));
    }

    [Fact]
    public void Boolean_and_any_of_filters()
    {
        Assert.Equal([2, 5], People.Ids(People.Pipeline().Run(_data, new GridState().SetFilter(new GridFilter("Active", FilterOperator.Equals, "false")))));
        Assert.Equal([2, 4, 5], People.Ids(People.Pipeline().Run(_data, new GridState().SetFilter(new GridFilter("Team", FilterOperator.AnyOf, Values: ["desktop", "TOKENS"])))));
    }

    [Fact]
    public void Incomplete_filters_are_ignored_and_unknown_fields_skipped()
    {
        var state = new GridState().SetFilter(new GridFilter("Salary", FilterOperator.GreaterThan)).SetFilter(new GridFilter("Nope", FilterOperator.Equals, "x"));
        Assert.Equal(5, People.Pipeline().Run(_data, state).FilteredCount);
    }

    [Fact]
    public void Quick_filter_requires_every_term_in_some_searchable_column()
    {
        var p = People.Pipeline();
        Assert.Equal([1, 3], People.Ids(p.Run(_data, new GridState().SetQuickFilter("web  TRUE"))));
        Assert.Equal([1, 4], People.Ids(p.Run(_data, new GridState().SetQuickFilter("ada"))));

        var idOnly = new DataPipeline<Person>([People.Columns()[0], People.Columns()[1]]);
        Assert.Empty(idOnly.Run(_data, new GridState().SetQuickFilter("3")).Items); // Id isn't searchable
    }

    [Fact]
    public void Quick_filter_skips_hidden_columns()
    {
        var state = new GridState().SetColumnHidden("Team", true).SetQuickFilter("tokens");
        Assert.Empty(People.Ids(People.Pipeline().Run(_data, state)));
    }

    [Fact]
    public void Grouping_orders_groups_counts_and_aggregates()
    {
        var r = People.Pipeline().Run(_data, new GridState().SetGroupBy("Team"));
        var groups = r.Rows.Where(x => x.Kind == GridRowKind.Group).ToList();
        Assert.Equal(["Web", "Desktop", "Tokens"], groups.Select(g => g.GroupKeyText));
        Assert.Equal([2, 2, 1], groups.Select(g => g.RowCount));
        Assert.Equal(210.0, groups[0].Aggregates!["Salary"]);
        Assert.Equal(2, groups[0].Aggregates!["Active"]); // count of non-null
        Assert.Equal(new DateTime(2022, 3, 3), groups[1].Aggregates!["Joined"]);
        Assert.Equal(500.0, r.Totals["Salary"]);
        Assert.All(r.Rows.Where(x => x.Kind == GridRowKind.Data), x => Assert.Equal(1, x.Depth));
    }

    [Fact]
    public void Collapsed_groups_hide_rows_but_keep_items_and_totals()
    {
        var r = People.Pipeline().Run(_data, new GridState().SetGroupBy("Team").ToggleGroup("Team=Desktop"));
        Assert.Equal(["Team=Web", "1", "3", "Team=Desktop", "Team=Tokens", "4"], r.Rows.Select(x => x.Key));
        Assert.False(r.Rows[3].Expanded);
        Assert.Equal(5, r.Items.Count);
        Assert.Equal(["1", "3", "4"], r.VisibleKeys);
    }

    [Fact]
    public void Nested_group_ids_escape_separators()
    {
        Assert.Equal("a=x\\|y|b=c\\\\d", DataPipeline<Person>.GroupId(DataPipeline<Person>.GroupId(null, "a", "x|y"), "b", "c\\d"));
    }

    [Fact]
    public void Null_group_sorts_last_with_empty_text()
    {
        var r = People.Pipeline().Run(_data, new GridState().SetGroupBy("Name"));
        var last = r.Rows.Last(x => x.Kind == GridRowKind.Group);
        Assert.Equal("", last.GroupKeyText);
        Assert.Null(last.GroupKey);
    }

    [Fact]
    public void Detail_rows_follow_their_data_row()
    {
        var r = People.Pipeline().Run(_data, new GridState().ToggleDetail("3"));
        Assert.Equal(GridRowKind.Detail, r.Rows[3].Kind);
        Assert.Equal("detail:3", r.Rows[3].Key);
        Assert.Same(r.Rows[2].Item, r.Rows[3].Item);
    }

    [Fact]
    public void Paging_slices_rows_and_clamps_the_page()
    {
        var p = People.Pipeline(paginate: true);
        var r = p.Run(_data, new GridState().SetPageSize(2).SetPage(1));
        Assert.Equal([3, 4], People.Ids(r));
        Assert.Equal(3, r.PageCount);
        Assert.Equal(0, r.Rows[0].Index);
        Assert.Equal(2, p.Run(_data, new GridState().SetPageSize(2).SetPage(10)).PageIndex);
    }

    [Fact]
    public void Tree_flattens_sorts_siblings_and_keeps_ancestors_of_matches()
    {
        var root = new Person { Id = 10, Name = "root", Team = "Web", Reports =
        [
            new Person { Id = 12, Name = "zed", Team = "Web" },
            new Person { Id = 11, Name = "amy", Team = "Tokens", Reports = [new Person { Id = 13, Name = "kid", Team = "Desktop" }] },
        ] };
        var p = People.Pipeline(tree: true);

        var all = p.Run([root], new GridState().ToggleSort("Name"));
        Assert.Equal([10, 11, 13, 12], People.Ids(all));
        Assert.Equal([0, 1, 2, 1], all.Rows.Select(r => r.Depth));
        Assert.True(all.Rows[0].HasChildren);

        var filtered = p.Run([root], new GridState().SetFilter(new GridFilter("Team", FilterOperator.Equals, "Desktop")));
        Assert.Equal([10, 11, 13], People.Ids(filtered)); // ancestors kept, forced open

        var collapsed = p.Run([root], new GridState().ToggleGroup("row:11"));
        Assert.Equal([10, 12, 11], People.Ids(collapsed));
        Assert.False(collapsed.Rows[2].Expanded);
        Assert.Equal(4, collapsed.Items.Count);
    }

    [Fact]
    public void Results_are_cached_until_state_items_or_version_change()
    {
        var p = People.Pipeline();
        var state = new GridState().ToggleSort("Name");
        var a = p.Run(_data, state);
        Assert.Same(a, p.Run(_data, state with { }));
        Assert.Same(a, p.Run(_data, GridState.FromJson(state.ToJson())));
        p.Invalidate();
        Assert.NotSame(a, p.Run(_data, state));
    }

    [Fact]
    public void Empty_data_is_fine()
    {
        var r = People.Pipeline().Run([], new GridState().SetGroupBy("Team").SetQuickFilter("x"));
        Assert.Empty(r.Rows);
        Assert.Equal(0.0, r.Totals["Salary"]);
        Assert.Null(r.Totals["Joined"]);
    }

    [Fact]
    public void Mixed_value_types_normalise()
    {
        var col = new GridColumn<object> { Field = "v", Type = GridColumnType.Number, Accessor = o => o };
        var items = new List<object> { "10", 2, 3.5m, 1L };
        var p = new DataPipeline<object>([col]);
        Assert.Equal([1L, 2, 3.5m, "10"], p.Run(items, new GridState().ToggleSort("v")).Items);
    }
}

public class GridStateTests
{
    [Fact]
    public void Sort_cycles_and_additive_sorts_append()
    {
        var s = new GridState().ToggleSort("a");
        Assert.Equal([new GridSort("a", SortDirection.Ascending)], s.Sorts);
        s = s.ToggleSort("b", additive: true);
        Assert.Equal(2, s.Sorts.Count);
        s = s.ToggleSort("a", additive: true);
        Assert.Equal(SortDirection.Descending, s.SortOf("a"));
        Assert.Equal("a", s.Sorts[0].Field);
        s = s.ToggleSort("a", additive: true);
        Assert.Equal(["b"], s.Sorts.Select(x => x.Field));
        s = s.ToggleSort("b").ToggleSort("b");
        Assert.Empty(s.Sorts);
    }

    [Fact]
    public void Transitions_reset_the_page()
    {
        var s = new GridState().SetPage(4);
        Assert.Equal(0, s.ToggleSort("a").PageIndex);
        Assert.Equal(0, s.SetFilter(new GridFilter("a", FilterOperator.Contains, "x")).PageIndex);
        Assert.Equal(0, s.SetQuickFilter("x").PageIndex);
    }

    [Fact]
    public void Columns_move_resize_with_clamping_pin_and_hide()
    {
        var s = new GridState()
            .MoveColumn(["a", "b", "c"], "c", 0)
            .ResizeColumn("a", 5, 40, 200)
            .ResizeColumn("b", 999, 40, 200)
            .PinColumn("c", GridPin.End)
            .SetColumnHidden("b", true);
        Assert.Equal(["c", "a", "b"], s.Order); // order lives apart from the overrides
        Assert.Equal(40, s.Column("a")!.Width);
        Assert.Equal(200, s.Column("b")!.Width);
        Assert.Equal(GridPin.End, s.Column("c")!.Pinned);
        Assert.True(s.Column("b")!.Hidden);
        Assert.Same(s, s.MoveColumn(["a"], "missing", 0));
    }

    [Fact]
    public void Group_expansion_xors_with_the_default()
    {
        var s = new GridState().ToggleGroup("g");
        Assert.True(s.IsCollapsed("g"));
        s = s.CollapseAll();
        Assert.True(s.IsCollapsed("g"));
        s = s.SetGroupExpanded("g", true);
        Assert.False(s.IsCollapsed("g"));
        Assert.Same(s, s.SetGroupExpanded("g", true));
    }

    [Fact]
    public void Json_round_trips_every_field()
    {
        var s = new GridState
        {
            Columns = [new GridColumnState("a", 120.5, true, GridPin.Start), new GridColumnState("b")],
            Sorts = [new GridSort("a", SortDirection.Descending)],
            Filters = [new GridFilter("a", FilterOperator.Between, 1.0, "2"), new GridFilter("b", FilterOperator.AnyOf, Values: ["x", true, null, 3.0])],
            QuickFilter = "q",
            GroupBy = ["b"],
            GroupsCollapsedByDefault = true,
            ToggledGroups = ["b=1"],
            ExpandedDetails = ["9"],
            PageIndex = 2,
            PageSize = 25,
        };
        var back = GridState.FromJson(s.ToJson());
        Assert.Equal(s, back);
        Assert.Equal(s.ToJson(), back.ToJson());
        Assert.Contains("\"operator\":\"anyOf\"", s.ToJson());
    }

    [Fact]
    public void Invalid_json_throws_format_exception()
    {
        Assert.Throws<FormatException>(() => GridState.FromJson("[]"));
        Assert.Throws<FormatException>(() => GridState.FromJson("""{"sorts":[{"field":"a","direction":"sideways"}]}"""));
    }
}

public class GridLayoutTests
{
    private static readonly List<GridColumn<Person>> Cols =
    [
        new() { Field = "a", Width = 100 },
        new() { Field = "b", Flex = 1, MinWidth = 50 },
        new() { Field = "c", Flex = 3, MinWidth = 50, MaxWidth = 120 },
        new() { Field = "d", Pinned = GridPin.End, Width = 80 },
        new() { Field = "e", Pinned = GridPin.Start, Width = 60 },
    ];

    [Fact]
    public void Resizing_hiding_or_unpinning_an_unseen_column_never_moves_it()
    {
        // Regression: overrides used to double as the order list, so touching column "c" first moved it ahead of "a".
        var base_ = GridLayout.Resolve(Cols, new GridState(), 0).Columns.Select(c => c.Field).ToList();
        var resized = GridLayout.Resolve(Cols, new GridState().ResizeColumn("c", 90), 0).Columns.Select(c => c.Field);
        Assert.Equal(base_, resized);

        var hiddenThenShown = new GridState().SetColumnHidden("b", true).SetColumnHidden("b", false);
        Assert.Equal(base_, GridLayout.Resolve(Cols, hiddenThenShown, 0).Columns.Select(c => c.Field));
    }

    [Fact]
    public void Moving_a_column_sets_an_explicit_order_that_survives_later_overrides()
    {
        var order = GridLayout.Resolve(Cols, new GridState(), 0).Columns.Select(c => c.Field).ToList();
        var moved = new GridState().MoveColumn(order, "c", 1).ResizeColumn("a", 140);
        Assert.Equal(["e", "c", "a", "b", "d"], GridLayout.Resolve(Cols, moved, 0).Columns.Select(c => c.Field));
        Assert.Equal(moved.Order, GridState.FromJson(moved.ToJson()).Order);
    }

    [Fact]
    public void Flex_columns_share_leftover_width_and_redistribute_clamps()
    {
        var l = GridLayout.Resolve(Cols, new GridState(), 600);
        Assert.Equal(["e", "a", "b", "c", "d"], l.Columns.Select(c => c.Field));
        Assert.Equal(120, l.Find("c")!.Width);
        Assert.Equal(600 - 100 - 80 - 60 - 120, l.Find("b")!.Width);
        Assert.Equal(600, l.TotalWidth);
    }

    [Fact]
    public void Offsets_and_sticky_offsets()
    {
        var l = GridLayout.Resolve(Cols, new GridState().PinColumn("a", GridPin.Start), 0);
        Assert.Equal(["a", "e", "b", "c", "d"], l.Columns.Select(c => c.Field)); // start pins keep column order
        Assert.Equal([0, 100], l.Columns.Take(2).Select(c => c.StickyOffset));
        Assert.Equal(0, l.Find("d")!.StickyOffset);
        Assert.Equal(160, l.Find("b")!.Left);
    }

    [Fact]
    public void State_overrides_order_width_and_visibility()
    {
        var s = new GridState().MoveColumn(["a", "b", "c", "d", "e"], "c", 0).ResizeColumn("c", 90).SetColumnHidden("a", true);
        var l = GridLayout.Resolve(Cols, s, 0);
        Assert.Equal(["e", "c", "b", "d"], l.Columns.Select(c => c.Field));
        Assert.Equal(90, l.Find("c")!.Width);
        Assert.Equal(50, l.Find("b")!.Width); // flex with no space → min
    }

    [Fact]
    public void Scrolling_range_ignores_pinned_columns()
    {
        var l = GridLayout.Resolve(Cols, new GridState(), 600);
        var (first, last) = l.ScrollingRange(0, 100, 0);
        Assert.Equal(1, first);
        Assert.Equal(1, last);
    }

    [Fact]
    public void Estimate_width_fits_header_and_content_within_limits()
    {
        var col = new GridColumn<Person> { Field = "Name", MinWidth = 48, MaxWidth = 300 };
        Assert.Equal(Math.Ceiling(4 * 7.4 + 24 + 24), GridLayout.EstimateWidth(col, ["ab"]));
        Assert.Equal(300, GridLayout.EstimateWidth(col, [new string('x', 500)]));
    }
}

public class GridViewportTests
{
    [Fact]
    public void Window_includes_overscan_and_clamps()
    {
        var r = GridViewport.Compute(3200, 400, 32, 1000, 6);
        Assert.Equal(94, r.First);
        Assert.Equal(119 - 94, r.Count);
        Assert.Equal(94 * 32, r.OffsetTop);
        Assert.Equal(32000, r.TotalHeight);
        Assert.Equal(0, GridViewport.Compute(0, 400, 32, 0).Count);
        Assert.Throws<ArgumentOutOfRangeException>(() => GridViewport.Compute(0, 400, 0, 10));
    }

    [Theory]
    [InlineData(5, 0, 400, 0)]
    [InlineData(20, 0, 400, 272)]
    [InlineData(2, 200, 400, 64)]
    public void Reveal_scrolls_minimally(int row, double top, double height, double expected) =>
        Assert.Equal(expected, GridViewport.ScrollToReveal(row, top, height, 32));
}

public class SelectionModelTests
{
    private static readonly string[] Order = ["a", "b", "c", "d", "e"];

    [Fact]
    public void Click_ctrl_and_shift_follow_desktop_conventions()
    {
        var m = new SelectionModel<string>();
        m.Click("b", Order);
        m.Click("d", Order, shift: true);
        Assert.Equal(["b", "c", "d"], m.SelectedIn(Order));
        m.Click("a", Order, ctrl: true);
        Assert.Equal(["a", "b", "c", "d"], m.SelectedIn(Order));
        m.Click("e", Order, ctrl: true, shift: true); // adds a..e range from anchor a
        Assert.Equal(Order, m.SelectedIn(Order));
        m.Click("c", Order);
        Assert.Equal(["c"], m.SelectedIn(Order));
    }

    [Fact]
    public void Selection_survives_reordering_because_it_is_keyed()
    {
        var m = new SelectionModel<string>();
        m.Click("a", Order);
        m.Click("c", Order, shift: true);
        Assert.Equal(["c", "b", "a"], m.SelectedIn(["e", "d", "c", "b", "a"]));
    }

    [Fact]
    public void Header_state_is_tristate()
    {
        var m = new SelectionModel<string>();
        Assert.Equal(SelectAllState.None, m.HeaderState(Order));
        m.Toggle("a");
        Assert.Equal(SelectAllState.Some, m.HeaderState(Order));
        m.SelectAll(Order);
        Assert.Equal(SelectAllState.All, m.HeaderState(Order));
        Assert.Equal(SelectAllState.None, m.HeaderState(Array.Empty<string>()));
    }

    [Fact]
    public void All_matching_tracks_exclusions()
    {
        var m = new SelectionModel<string>();
        m.SelectAllMatching(10_000);
        Assert.True(m.IsSelected("anything"));
        m.Toggle("x");
        Assert.False(m.IsSelected("x"));
        Assert.Equal(9_999, m.Count);
        m.Toggle("x");
        Assert.Equal(10_000, m.Count);
        m.Click("y", Order);
        Assert.False(m.AllMatching);
    }

    [Fact]
    public void Single_and_none_modes()
    {
        var single = new SelectionModel<string>(GridSelectionMode.Single);
        single.Click("a", Order);
        single.Click("c", Order, ctrl: true, shift: true);
        Assert.Equal(["c"], single.SelectedIn(Order));
        single.Toggle("c");
        Assert.Empty(single.SelectedIn(Order));

        var none = new SelectionModel<string>(GridSelectionMode.None);
        none.Click("a", Order);
        none.SelectAll(Order);
        Assert.Equal(0, none.Count);
    }

    [Fact]
    public void Value_type_keys_track_the_anchor_correctly()
    {
        var m = new SelectionModel<int>();
        int[] order = [0, 1, 2, 3];
        m.Click(0, order);
        m.Click(2, order, shift: true);
        Assert.Equal([0, 1, 2], m.SelectedIn(order));
        Assert.True(m.HasAnchor);
        m.Clear();
        Assert.False(m.HasAnchor);
    }

    [Fact]
    public void Changed_fires_on_changes_only()
    {
        var m = new SelectionModel<string>();
        var n = 0;
        m.Changed += (_, _) => n++;
        m.Clear();
        m.Toggle("a");
        m.Clear();
        Assert.Equal(2, n);
    }
}

public class GridNavigatorTests
{
    private static GridNavContext Ctx(int rows = 10, int cols = 4) => new() { RowCount = rows, ColumnCount = cols, PageRows = 4 };

    [Theory]
    [InlineData(GridKey.Down, 1, 1, 2, 1)]
    [InlineData(GridKey.Up, 0, 1, 0, 1)]
    [InlineData(GridKey.Right, 1, 3, 1, 3)]
    [InlineData(GridKey.Left, 1, 0, 1, 0)]
    [InlineData(GridKey.Home, 5, 2, 5, 0)]
    [InlineData(GridKey.End, 5, 0, 5, 3)]
    [InlineData(GridKey.CtrlHome, 5, 2, 0, 0)]
    [InlineData(GridKey.CtrlEnd, 0, 0, 9, 3)]
    [InlineData(GridKey.PageDown, 8, 1, 9, 1)]
    [InlineData(GridKey.PageUp, 3, 1, 0, 1)]
    public void Moves_and_clamps(GridKey key, int r, int c, int er, int ec) =>
        Assert.Equal(new GridCell(er, ec), GridNavigator.Move(new GridCell(r, c), key, Ctx()).Cell);

    [Fact]
    public void Group_rows_collapse_and_expand_instead_of_moving()
    {
        var expanded = true;
        var ctx = new GridNavContext
        {
            RowCount = 3, ColumnCount = 3, Kind = i => i == 0 ? GridRowKind.Group : GridRowKind.Data,
            CanExpand = i => i == 0, IsExpanded = _ => expanded,
        };
        Assert.Equal(GridNavAction.Collapse, GridNavigator.Move(new GridCell(0, 2), GridKey.Left, ctx).Action);
        expanded = false;
        var r = GridNavigator.Move(new GridCell(0, 2), GridKey.Right, ctx);
        Assert.Equal(GridNavAction.Expand, r.Action);
        Assert.Equal(new GridCell(0, 2), r.Cell); // column remembered on a single-cell row
    }

    [Fact]
    public void Empty_grid_stays_at_origin() =>
        Assert.Equal(new GridCell(0, 0), GridNavigator.Move(new GridCell(5, 5), GridKey.Down, Ctx(0, 0)).Cell);
}

public class EditSessionTests
{
    private static readonly List<GridColumn<Person>> Cols = People.Columns();
    private static GridColumn<Person> Salary => Cols.Single(c => c.Field == "Salary");

    [Fact]
    public void Cell_mode_parses_validates_and_writes_through()
    {
        var p = new Person { Id = 1, Salary = 10 };
        var s = new EditSession<Person>();
        GridCellChange<Person>? committed = null;
        s.Committed += (_, c) => committed = c;

        Assert.True(s.Begin(p, "1", Salary));
        s.SetDraftText("abc");
        Assert.Equal("Enter a number.", s.Current!.Error);
        Assert.False(s.Commit());
        s.SetDraftText("-5");
        Assert.Equal("Must be positive.", s.Current!.Error);
        Assert.False(s.Commit());
        s.SetDraftText(" 42.5 ");
        Assert.True(s.Commit());
        Assert.Equal(42.5, p.Salary);
        Assert.Equal(10.0, committed!.OldValue);
        Assert.False(s.IsEditing);
    }

    [Fact]
    public void Non_editable_columns_and_none_mode_refuse_to_begin()
    {
        var p = new Person();
        Assert.False(new EditSession<Person>().Begin(p, "1", Cols.Single(c => c.Field == "Name")));
        Assert.False(new EditSession<Person>(GridEditMode.None).Begin(p, "1", Salary));
    }

    [Fact]
    public void Batch_mode_collects_pending_changes_until_commit_all()
    {
        var p = new Person { Id = 1, Salary = 10 };
        var s = new EditSession<Person>(GridEditMode.Batch);
        s.Begin(p, "1", Salary, "20");
        s.Commit();
        s.Begin(p, "1", Salary);
        Assert.Equal(20.0, s.Current!.Original); // shows pending value
        s.SetDraft(30.0);
        s.Commit();
        Assert.Single(s.Pending);
        Assert.Equal(10.0, p.Salary);
        Assert.Equal(30.0, s.GetValue(p, "1", Salary));

        var applied = s.CommitAll(Cols);
        Assert.Single(applied);
        Assert.Equal(30.0, p.Salary);
        Assert.Empty(s.Pending);
    }

    [Fact]
    public void Batch_change_back_to_original_drops_the_pending_entry_and_discard_clears()
    {
        var p = new Person { Id = 1, Salary = 10 };
        var s = new EditSession<Person>(GridEditMode.Batch);
        s.Begin(p, "1", Salary, "20"); s.Commit();
        s.Begin(p, "1", Salary, "10"); s.Commit();
        Assert.Empty(s.Pending);
        s.Begin(p, "1", Salary, "15"); s.Commit();
        s.DiscardAll();
        Assert.Empty(s.Pending);
        Assert.Equal(10.0, p.Salary);
    }

    [Theory]
    [InlineData(GridEditCommitKey.Enter, false, GridKey.Down)]
    [InlineData(GridEditCommitKey.Enter, true, GridKey.Up)]
    [InlineData(GridEditCommitKey.Tab, false, GridKey.Right)]
    [InlineData(GridEditCommitKey.Tab, true, GridKey.Left)]
    public void Commit_movement(GridEditCommitKey key, bool shift, GridKey expected) =>
        Assert.Equal(expected, EditSession<Person>.MoveAfterCommit(key, shift));

    [Theory]
    [InlineData("2026-02-30")]
    [InlineData("tomorrow")]
    public void Date_parse_errors_are_user_facing(string text)
    {
        var col = new GridColumn<Person> { Field = "Joined", Type = GridColumnType.Date };
        Assert.Equal("Enter a date (YYYY-MM-DD).", Assert.Throws<FormatException>(() => col.Parse(text)).Message);
    }

    [Fact]
    public void Enum_parse_accepts_known_values_case_insensitively()
    {
        var col = Cols.Single(c => c.Field == "Team");
        Assert.Equal("Desktop", col.Parse("desktop"));
        Assert.Throws<FormatException>(() => col.Parse("Mobile"));
    }
}

public class GridExportTests
{
    [Fact]
    public void Csv_quotes_per_rfc_4180_and_uses_display_text()
    {
        var cols = new List<GridColumn<Person>>
        {
            new() { Field = "Name" },
            new() { Field = "Salary", Type = GridColumnType.Number, Format = "#,##0.00" },
            new() { Field = "Active", Type = GridColumnType.Boolean },
        };
        var rows = new[] { new Person { Name = "a,\"b\"", Salary = 1234.5, Active = true }, new Person { Name = null, Salary = null } };
        Assert.Equal("Name,Salary,Active\r\n\"a,\"\"b\"\"\",\"1,234.50\",true\r\n,,false", GridExport.ToCsv(cols, rows));
        Assert.Equal("a,\"b\"\t1,234.50\ttrue\n\t\tfalse", GridExport.ToTsv(cols, rows));
    }

    [Fact]
    public void Value_override_exports_pending_edits()
    {
        var col = new GridColumn<Person> { Field = "Name" };
        Assert.Equal("Name\r\nedited", GridExport.ToCsv([col], [new Person { Name = "x" }], valueOf: (_, _) => "edited"));
    }
}

public class GridDataSourceTests
{
    private static List<Person> Many(int n) => [.. Enumerable.Range(0, n).Select(i => new Person { Id = i, Name = "p" + i, Salary = n - i })];

    [Fact]
    public async Task In_memory_source_filters_sorts_and_windows()
    {
        var src = new InMemoryGridDataSource<Person>(Many(500), People.Columns());
        var q = GridQuery.FromState(new GridState().ToggleSort("Salary").SetFilter(new GridFilter("Salary", FilterOperator.LessThanOrEqual, 100.0)), 10, 5);
        var r = await src.QueryAsync(q);
        Assert.Equal(100, r.TotalCount);
        Assert.Equal([11.0, 12, 13, 14, 15], r.Items.Select(p => p.Salary!.Value));
    }

    [Fact]
    public async Task Simulated_latency_uses_the_time_provider_and_honours_cancellation()
    {
        var time = new FakeTimeProvider();
        var src = new InMemoryGridDataSource<Person>(Many(10), People.Columns(), TimeSpan.FromSeconds(1), time);
        using var cts = new CancellationTokenSource();
        var task = src.QueryAsync(new GridQuery(), cts.Token);
        Assert.False(task.IsCompleted);
        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task);

        var ok = src.QueryAsync(new GridQuery { Count = 3 });
        time.Advance(TimeSpan.FromSeconds(1));
        Assert.Equal(3, (await ok).Items.Count);
    }

    private sealed class ControlledSource : IGridDataSource<int>
    {
        public readonly List<(GridQuery Query, TaskCompletionSource<GridResult<int>> Tcs, CancellationToken Ct)> Calls = [];

        public Task<GridResult<int>> QueryAsync(GridQuery query, CancellationToken ct = default)
        {
            var tcs = new TaskCompletionSource<GridResult<int>>(TaskCreationOptions.RunContinuationsAsynchronously);
            ct.Register(() => tcs.TrySetCanceled(ct));
            Calls.Add((query, tcs, ct));
            return tcs.Task;
        }

        public void Complete(int call, int total) =>
            Calls[call].Tcs.SetResult(new GridResult<int>(Enumerable.Range(Calls[call].Query.Offset, Math.Min(Calls[call].Query.Count, total - Calls[call].Query.Offset)).ToList(), total));
    }

    [Fact]
    public async Task Row_cache_loads_blocks_once_and_serves_rows()
    {
        var src = new ControlledSource();
        var cache = new GridRowCache<int>(src, blockSize: 50);
        var changes = 0;
        cache.Changed += (_, _) => changes++;

        var a = cache.EnsureRangeAsync(30, 40);   // blocks 0 and 1
        var b = cache.EnsureRangeAsync(60, 10);   // block 1 again → de-duplicated
        Assert.Equal(2, src.Calls.Count);
        Assert.True(cache.IsLoading(75));
        src.Complete(0, 120);
        src.Complete(1, 120);
        await Task.WhenAll(a, b);

        Assert.Equal(120, cache.TotalCount);
        Assert.True(cache.TryGet(99, out var v));
        Assert.Equal(99, v);
        Assert.False(cache.TryGet(100, out _));
        Assert.Equal(2, changes);

        await cache.EnsureRangeAsync(0, 99);       // already loaded
        Assert.Equal(2, src.Calls.Count);
        _ = cache.EnsureRangeAsync(110, 500);      // clamps to total → only block 2
        Assert.Equal(3, src.Calls.Count);
        Assert.Equal(100, src.Calls[2].Query.Offset);
    }

    [Fact]
    public async Task Changing_the_query_cancels_and_discards_stale_results()
    {
        var src = new ControlledSource();
        var cache = new GridRowCache<int>(src, blockSize: 10);
        var stale = cache.EnsureRangeAsync(0, 5);
        cache.SetQuery(new GridQuery { QuickFilter = "x" });
        Assert.True(src.Calls[0].Ct.IsCancellationRequested);
        await stale; // cancellation is swallowed
        Assert.Null(cache.TotalCount);

        var fresh = cache.EnsureRangeAsync(0, 5);
        Assert.Equal("x", src.Calls[1].Query.QuickFilter);
        src.Complete(1, 3);
        await fresh;
        Assert.Equal(3, cache.TotalCount);
        Assert.Equal(1, cache.LoadedBlockCount);
    }

    [Fact]
    public async Task Load_errors_are_reported_and_retryable()
    {
        var src = new ControlledSource();
        var cache = new GridRowCache<int>(src, blockSize: 10);
        var t = cache.EnsureRangeAsync(0, 5);
        src.Calls[0].Tcs.SetException(new InvalidOperationException("boom"));
        await t;
        Assert.Equal("boom", cache.LastError!.Message);
        Assert.False(cache.IsLoading(0));
        _ = cache.EnsureRangeAsync(0, 5);
        Assert.Equal(2, src.Calls.Count);
    }

    [Fact]
    public void Query_equality_ignores_the_window()
    {
        var q = GridQuery.FromState(new GridState().ToggleSort("a").SetFilter(new GridFilter("b", FilterOperator.AnyOf, Values: ["x"])));
        Assert.True(q.SameResultSet(q.Window(500, 10)));
        Assert.False(q.SameResultSet(q with { QuickFilter = "z" }));
    }
}

[CollectionDefinition("perf", DisableParallelization = true)]
public class PerfCollection;

/// <summary>Performance budgets (best of three runs, serialised). SLATE_PERF_FACTOR=3 relaxes them on slow CI.</summary>
[Collection("perf")]
public class DataGridPerformanceTests
{
    private static double BestOf3(Action run)
    {
        var best = double.MaxValue;
        for (var i = 0; i < 3; i++)
        {
            var sw = Stopwatch.StartNew();
            run();
            best = Math.Min(best, sw.Elapsed.TotalMilliseconds);
        }
        return best;
    }

    private static double Budget(double ms) =>
        double.TryParse(Environment.GetEnvironmentVariable("SLATE_PERF_FACTOR"), out var f) && f > 0 ? ms * f : ms;

    private sealed record Rec(int Id, string Name, string Team, double Amount, DateTime When, bool Flag);

    private static (List<Rec>, List<GridColumn<Rec>>) Data(int n)
    {
        var rng = new Random(42);
        string[] teams = ["Platform", "Web", "Desktop", "Tokens", "Infra", "Data"];
        var rows = Enumerable.Range(0, n).Select(i => new Rec(i, "Item " + rng.Next(0, 50_000), teams[rng.Next(teams.Length)],
            Math.Round(rng.NextDouble() * 10_000, 2), new DateTime(2020, 1, 1).AddMinutes(rng.Next(0, 3_000_000)), rng.Next(2) == 0)).ToList();
        List<GridColumn<Rec>> cols =
        [
            new() { Field = "Id", Type = GridColumnType.Number, Accessor = r => r.Id, Searchable = false },
            new() { Field = "Name", Accessor = r => r.Name },
            new() { Field = "Team", Type = GridColumnType.Enum, Accessor = r => r.Team },
            new() { Field = "Amount", Type = GridColumnType.Number, Accessor = r => r.Amount, Aggregate = GridAggregate.Sum, Searchable = false },
            new() { Field = "When", Type = GridColumnType.Date, Accessor = r => r.When, Searchable = false },
            new() { Field = "Flag", Type = GridColumnType.Boolean, Accessor = r => r.Flag, Searchable = false },
        ];
        return (rows, cols);
    }

    [Fact]
    public void Sort_filter_and_quick_filter_100k_rows_within_budget()
    {
        var (rows, cols) = Data(100_000);
        var state = new GridState().ToggleSort("Team").ToggleSort("Amount", true)
            .SetFilter(new GridFilter("Amount", FilterOperator.GreaterThan, 1000.0)).SetQuickFilter("item 1");
        GridPipelineResult<Rec>? r = null;
        var ms = BestOf3(() => r = new DataPipeline<Rec>(cols).Run(rows, state));
        Assert.True(r!.FilteredCount > 0);
        Assert.True(ms < Budget(150), $"pipeline took {ms:0} ms");
    }

    [Fact]
    public void Grouping_100k_rows_within_budget()
    {
        var (rows, cols) = Data(100_000);
        var state = new GridState().SetGroupBy("Team");
        GridPipelineResult<Rec>? r = null;
        var ms = BestOf3(() => r = new DataPipeline<Rec>(cols).Run(rows, state));
        Assert.Equal(6, r!.Rows.Count(x => x.Kind == GridRowKind.Group));
        Assert.True(ms < Budget(200), $"grouping took {ms:0} ms");
    }
}
