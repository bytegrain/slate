using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Slate.Data;

namespace Slate.Core.Tests.Data;

using Row = Dictionary<string, object?>;

/// <summary>
/// Writes/validates tests/fixtures/data-grid.json: a deterministic dataset, column specs and scenario results produced
/// by the C# engine. @bytegrain/slate-web's TypeScript port must reproduce every result exactly.
/// Regenerate with SLATE_UPDATE_FIXTURES=1 dotnet test.
/// </summary>
public class DataGridFixtureTests
{
    // ---- dataset ---------------------------------------------------------------------------------------------

    private static readonly string[] Names =
    [
        "Zoë", "zeta", "Zeta", "Ångström", "émile", "Émile", "alpha", "Alpha", "日本語", "bravo, inc.", "say \"hi\"", "  padded ",
        "Delta", "charlie", "Echo", "foxtrot", "Golf", "hotel", "India", "juliet",
    ];

    private static readonly string[] Teams = ["Platform", "Web", "Desktop", "Tokens"];
    private static readonly string?[] Statuses = ["Open", "In progress", "Done", null, "Blocked"];

    internal static JsonArray Dataset()
    {
        var rows = new JsonArray();
        uint seed = 20261006;
        uint Next() { seed = seed * 1664525 + 1013904223; return seed >> 8; }

        for (var i = 1; i <= 60; i++)
        {
            var r = Next();
            var amount = r % 7 == 0 ? (double?)null : Math.Round((r % 100000) / 100.0 - 200, 2);
            var created = r % 11 == 0 ? null
                : new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddDays(r % 280).AddHours(i % 3 == 0 ? (r % 24) : 0)
                    .ToString(i % 3 == 0 ? "yyyy-MM-dd'T'HH:mm:ss'Z'" : "yyyy-MM-dd", CultureInfo.InvariantCulture);
            var parent = i <= 10 ? (int?)null : (int)(Next() % (uint)(i - 1)) + 1;
            rows.Add(new JsonObject
            {
                ["id"] = (double)i,
                ["name"] = Names[(i - 1) % Names.Length] + (i > Names.Length ? " " + (i / Names.Length) : ""),
                ["team"] = Teams[r % 4],
                ["status"] = Statuses[(r / 7) % 5],
                ["amount"] = amount,
                ["qty"] = (double)(r % 9),
                ["score"] = (double)(r % 101),
                ["created"] = created,
                ["active"] = r % 3 != 0,
                ["parentId"] = parent is null ? null : (double)parent,
            });
        }
        return rows;
    }

    internal static JsonArray ColumnSpecs() =>
    [
        new JsonObject { ["field"] = "id", ["type"] = "number", ["searchable"] = false, ["width"] = 64.0 },
        new JsonObject { ["field"] = "name", ["title"] = "Name", ["type"] = "text", ["flex"] = 2.0, ["minWidth"] = 120.0 },
        new JsonObject { ["field"] = "team", ["type"] = "enum", ["enumOrder"] = new JsonArray("Platform", "Web", "Desktop", "Tokens") },
        new JsonObject { ["field"] = "status", ["type"] = "enum" },
        new JsonObject { ["field"] = "amount", ["title"] = "Amount", ["type"] = "number", ["format"] = "#,##0.00", ["aggregate"] = "sum", ["width"] = 120.0 },
        new JsonObject { ["field"] = "qty", ["type"] = "number", ["aggregate"] = "avg", ["width"] = 80.0 },
        new JsonObject { ["field"] = "score", ["type"] = "progress", ["aggregate"] = "max", ["pinned"] = "end", ["width"] = 100.0 },
        new JsonObject { ["field"] = "created", ["type"] = "date", ["aggregate"] = "min", ["width"] = 140.0 },
        new JsonObject { ["field"] = "active", ["type"] = "boolean", ["aggregate"] = "count", ["width"] = 90.0 },
        new JsonObject { ["field"] = "parentId", ["type"] = "number", ["hidden"] = true, ["searchable"] = false },
    ];

    internal static List<GridColumn<Row>> Columns(JsonArray specs) => specs.Select(n =>
    {
        var s = n!.AsObject();
        var field = s["field"]!.GetValue<string>();
        var type = GridState.Enum<GridColumnType>(s["type"]!.GetValue<string>());
        return new GridColumn<Row>
        {
            Field = field,
            Title = s["title"]?.GetValue<string>(),
            Type = type,
            Accessor = row => row.TryGetValue(field, out var v) ? type == GridColumnType.Date && v is string iso ? GridValues.ParseIsoDate(iso) : v : null,
            Width = s["width"]?.GetValue<double>(),
            Flex = s["flex"]?.GetValue<double>() ?? 0,
            MinWidth = s["minWidth"]?.GetValue<double>() ?? 48,
            Searchable = s["searchable"]?.GetValue<bool>() ?? true,
            Hidden = s["hidden"]?.GetValue<bool>() ?? false,
            Pinned = s["pinned"] is { } p ? GridState.Enum<GridPin>(p.GetValue<string>()) : GridPin.None,
            Format = s["format"]?.GetValue<string>(),
            Aggregate = s["aggregate"] is { } a ? GridState.Enum<GridAggregate>(a.GetValue<string>()) : GridAggregate.None,
            EnumOrder = s["enumOrder"] is JsonArray eo ? eo.Select(x => x!.GetValue<string>()).ToList() : null,
        };
    }).ToList();

    internal static List<Row> Rows(JsonArray data) => data.Select(n => n!.AsObject().ToDictionary(kv => kv.Key, kv => GridState.FromNode(kv.Value))).ToList();

    // ---- scenarios ---------------------------------------------------------------------------------------

    private static GridState S(Func<GridState, GridState> f) => f(new GridState());

    private static readonly (string Id, GridState State, bool Tree, bool Paginate)[] PipelineCases =
    [
        ("unsorted", new GridState(), false, false),
        ("sort-name-asc", S(s => s.ToggleSort("name")), false, false),
        ("sort-name-desc", S(s => s.ToggleSort("name").ToggleSort("name")), false, false),
        ("sort-team-enum-then-amount-desc", S(s => s.ToggleSort("team").ToggleSort("amount", true).ToggleSort("amount", true)), false, false),
        ("sort-created-nulls-last", S(s => s.ToggleSort("created").ToggleSort("created")), false, false),
        ("sort-active-then-score", S(s => s.ToggleSort("active").ToggleSort("score", true)), false, false),
        ("filter-name-contains", S(s => s.SetFilter(new("name", FilterOperator.Contains, "e"))), false, false),
        ("filter-name-equals-ci", S(s => s.SetFilter(new("name", FilterOperator.Equals, "zeta"))), false, false),
        ("filter-name-not-equals", S(s => s.SetFilter(new("name", FilterOperator.NotEquals, "zeta"))), false, false),
        ("filter-name-starts", S(s => s.SetFilter(new("name", FilterOperator.StartsWith, "é"))), false, false),
        ("filter-name-ends", S(s => s.SetFilter(new("name", FilterOperator.EndsWith, " 2"))), false, false),
        ("filter-status-empty", S(s => s.SetFilter(new("status", FilterOperator.IsEmpty))), false, false),
        ("filter-amount-not-empty", S(s => s.SetFilter(new("amount", FilterOperator.IsNotEmpty))), false, false),
        ("filter-amount-lt", S(s => s.SetFilter(new("amount", FilterOperator.LessThan, 0.0))), false, false),
        ("filter-amount-lte-string", S(s => s.SetFilter(new("amount", FilterOperator.LessThanOrEqual, "100.5"))), false, false),
        ("filter-qty-gt", S(s => s.SetFilter(new("qty", FilterOperator.GreaterThan, 5.0))), false, false),
        ("filter-qty-gte", S(s => s.SetFilter(new("qty", FilterOperator.GreaterThanOrEqual, 5.0))), false, false),
        ("filter-score-between-reversed", S(s => s.SetFilter(new("score", FilterOperator.Between, 80.0, 20.0))), false, false),
        ("filter-created-between", S(s => s.SetFilter(new("created", FilterOperator.Between, "2026-03-01", "2026-06-30"))), false, false),
        ("filter-created-gt", S(s => s.SetFilter(new("created", FilterOperator.GreaterThan, "2026-08-01T00:00:00Z"))), false, false),
        ("filter-active-equals", S(s => s.SetFilter(new("active", FilterOperator.Equals, false))), false, false),
        ("filter-team-anyof", S(s => s.SetFilter(new("team", FilterOperator.AnyOf, Values: ["web", "Tokens"]))), false, false),
        ("filter-text-lt", S(s => s.SetFilter(new("name", FilterOperator.LessThan, "d"))), false, false),
        ("filter-incomplete-ignored", S(s => s.SetFilter(new("amount", FilterOperator.GreaterThan))), false, false),
        ("filter-combined", S(s => s.SetFilter(new("team", FilterOperator.Equals, "web")).SetFilter(new("active", FilterOperator.Equals, true)).ToggleSort("id")), false, false),
        ("quick-filter-terms", S(s => s.SetQuickFilter("open  WEB")), false, false),
        ("quick-filter-number-text", S(s => s.SetQuickFilter(".5")), false, false),
        ("quick-filter-grouped-amount", S(s => s.SetFilter(new("amount", FilterOperator.GreaterThan, 700.0)).SetQuickFilter("1")), false, false),
        ("quick-filter-date", S(s => s.SetQuickFilter("2026-04")), false, false),
        ("group-team", S(s => s.SetGroupBy("team")), false, false),
        ("group-team-desc-sorted-amount", S(s => s.SetGroupBy("team").ToggleSort("team").ToggleSort("team").ToggleSort("amount", true)), false, false),
        ("group-status-nulls", S(s => s.SetGroupBy("status")), false, false),
        ("group-nested-team-status", S(s => s.SetGroupBy("team", "status")), false, false),
        ("group-collapse-one", S(s => s.SetGroupBy("team").ToggleGroup("team=Web")), false, false),
        ("group-collapsed-default-expand-one", S(s => s.SetGroupBy("team", "active").CollapseAll().ToggleGroup("team=Desktop")), false, false),
        ("group-filtered", S(s => s.SetGroupBy("active").SetFilter(new("qty", FilterOperator.LessThan, 3.0))), false, false),
        ("detail-rows", S(s => s.ToggleSort("id").ToggleDetail("3").ToggleDetail("10")), false, false),
        ("page-2", S(s => s.ToggleSort("id").SetPageSize(25).SetPage(1)), false, true),
        ("page-clamped", S(s => s.SetPageSize(25).SetPage(99)), false, true),
        ("page-grouped", S(s => s.SetGroupBy("team").SetPageSize(10).SetPage(1)), false, true),
        ("tree", new GridState(), true, false),
        ("tree-sorted", S(s => s.ToggleSort("name")), true, false),
        ("tree-collapse-node", S(s => s.ToggleGroup("row:1").ToggleGroup("row:2")), true, false),
        ("tree-collapsed-default", S(s => s.CollapseAll().ToggleGroup("row:3")), true, false),
        ("tree-filter-keeps-ancestors", S(s => s.SetFilter(new("team", FilterOperator.Equals, "Tokens"))), true, false),
        ("tree-quick-filter", S(s => s.SetQuickFilter("zeta")), true, false),
    ];

    private static JsonObject RunPipeline(List<GridColumn<Row>> cols, List<Row> rows, GridState state, bool tree, bool paginate)
    {
        var byParent = rows.ToLookup(r => r["parentId"] is double p ? (double?)p : null);
        var pipeline = new DataPipeline<Row>(cols, new DataPipelineOptions<Row>
        {
            RowKey = r => r["id"],
            ChildrenSelector = tree ? r => byParent[(double)r["id"]!] : null,
            Paginate = paginate,
        });
        var input = tree ? byParent[null].ToList() : rows;
        var result = pipeline.Run(input, state);
        var colByField = cols.ToDictionary(c => c.Field);

        JsonObject Aggs(IReadOnlyDictionary<string, object?> a) =>
            new(a.OrderBy(kv => kv.Key, StringComparer.Ordinal).Select(kv => new KeyValuePair<string, JsonNode?>(kv.Key,
                kv.Value is null ? null : JsonValue.Create(colByField[kv.Key].Aggregate == GridAggregate.Count
                    ? Convert.ToString(kv.Value, CultureInfo.InvariantCulture)
                    : colByField[kv.Key].DisplayText(kv.Value)))));

        return new JsonObject
        {
            ["rows"] = new JsonArray([.. result.Rows.Select(r => (JsonNode)(r.Kind switch
            {
                GridRowKind.Group => new JsonObject
                {
                    ["k"] = "g", ["key"] = r.Key, ["depth"] = r.Depth, ["field"] = r.GroupField, ["text"] = r.GroupKeyText,
                    ["count"] = r.RowCount, ["expanded"] = r.Expanded, ["agg"] = Aggs(r.Aggregates!),
                },
                GridRowKind.Detail => new JsonObject { ["k"] = "x", ["key"] = r.Key, ["depth"] = r.Depth },
                _ => new JsonObject { ["k"] = "d", ["key"] = r.Key, ["depth"] = r.Depth, ["children"] = r.HasChildren, ["expanded"] = r.Expanded },
            }))]),
            ["keys"] = new JsonArray([.. result.ItemKeys.Select(k => (JsonNode)JsonValue.Create(k))]),
            ["visibleKeys"] = new JsonArray([.. result.VisibleKeys.Select(k => (JsonNode)JsonValue.Create(k))]),
            ["total"] = result.TotalCount,
            ["filtered"] = result.FilteredCount,
            ["viewRows"] = result.ViewRowCount,
            ["pageIndex"] = result.PageIndex,
            ["pageCount"] = result.PageCount,
            ["totals"] = Aggs(result.Totals),
        };
    }

    internal static JsonObject Build()
    {
        var data = Dataset();
        var specs = ColumnSpecs();
        var cols = Columns(specs);
        var rows = Rows(data);

        var pipeline = new JsonArray();
        foreach (var (id, state, tree, paginate) in PipelineCases)
            pipeline.Add(new JsonObject { ["id"] = id, ["state"] = state.ToJsonNode(), ["tree"] = tree, ["paginate"] = paginate, ["expected"] = RunPipeline(cols, rows, state, tree, paginate) });

        return new JsonObject
        {
            ["$comment"] = "Generated by Slate.Core.Tests DataGridFixtureTests. Regenerate with SLATE_UPDATE_FIXTURES=1 dotnet test.",
            ["columns"] = specs,
            ["data"] = data,
            ["pipeline"] = pipeline,
            ["transitions"] = Transitions(),
            ["selection"] = SelectionCases(),
            ["navigation"] = NavigationCases(),
            ["export"] = ExportCases(cols, rows),
            ["viewport"] = ViewportCases(),
            ["layout"] = LayoutCases(cols),
            ["format"] = FormatCases(),
        };
    }

    private static JsonArray Transitions()
    {
        (string Op, Func<GridState, GridState> Apply, JsonObject Args)[] steps =
        [
            ("toggleSort", s => s.ToggleSort("name"), new() { ["field"] = "name", ["additive"] = false }),
            ("toggleSort", s => s.ToggleSort("amount", true), new() { ["field"] = "amount", ["additive"] = true }),
            ("toggleSort", s => s.ToggleSort("name", true), new() { ["field"] = "name", ["additive"] = true }),
            ("toggleSort", s => s.ToggleSort("name", true), new() { ["field"] = "name", ["additive"] = true }),
            ("toggleSort", s => s.ToggleSort("amount"), new() { ["field"] = "amount", ["additive"] = false }),
            ("setFilter", s => s.SetFilter(new("team", FilterOperator.AnyOf, Values: ["Web", 3.0, true])), new() { ["filter"] = new JsonObject { ["field"] = "team", ["operator"] = "anyOf", ["values"] = new JsonArray("Web", 3.0, true) } }),
            ("setFilter", s => s.SetFilter(new("qty", FilterOperator.Between, 1.0, "4")), new() { ["filter"] = new JsonObject { ["field"] = "qty", ["operator"] = "between", ["value"] = 1.0, ["value2"] = "4" } }),
            ("setFilter", s => s.SetFilter(new("team", FilterOperator.Equals, "Web")), new() { ["filter"] = new JsonObject { ["field"] = "team", ["operator"] = "equals", ["value"] = "Web" } }),
            ("clearFilter", s => s.ClearFilter("qty"), new() { ["field"] = "qty" }),
            ("setQuickFilter", s => s.SetQuickFilter("abc"), new() { ["text"] = "abc" }),
            ("moveColumn", s => s.MoveColumn(["id", "name", "team", "status"], "status", 0), new() { ["order"] = new JsonArray("id", "name", "team", "status"), ["field"] = "status", ["toIndex"] = 0 }),
            ("resizeColumn", s => s.ResizeColumn("name", 12, 48, 400), new() { ["field"] = "name", ["width"] = 12, ["min"] = 48, ["max"] = 400 }),
            ("resizeColumn", s => s.ResizeColumn("team", 233.333, 48, 400), new() { ["field"] = "team", ["width"] = 233.333, ["min"] = 48, ["max"] = 400 }),
            ("pinColumn", s => s.PinColumn("id", GridPin.Start), new() { ["field"] = "id", ["pin"] = "start" }),
            ("setColumnHidden", s => s.SetColumnHidden("qty", true), new() { ["field"] = "qty", ["hidden"] = true }),
            ("addGroupBy", s => s.AddGroupBy("team"), new() { ["field"] = "team" }),
            ("addGroupBy", s => s.AddGroupBy("status"), new() { ["field"] = "status" }),
            ("toggleGroup", s => s.ToggleGroup("team=Web"), new() { ["id"] = "team=Web" }),
            ("collapseAll", s => s.CollapseAll(), new()),
            ("toggleGroup", s => s.ToggleGroup("team=Web"), new() { ["id"] = "team=Web" }),
            ("setGroupExpanded", s => s.SetGroupExpanded("team=Web", true), new() { ["id"] = "team=Web", ["expanded"] = true }),
            ("removeGroupBy", s => s.RemoveGroupBy("team"), new() { ["field"] = "team" }),
            ("expandAll", s => s.ExpandAll(), new()),
            ("toggleDetail", s => s.ToggleDetail("7"), new() { ["key"] = "7" }),
            ("setPageSize", s => s.SetPageSize(0), new() { ["size"] = 0 }),
            ("setPage", s => s.SetPage(3), new() { ["index"] = 3 }),
            ("clearFilters", s => s.ClearFilters(), new()),
        ];

        var state = new GridState();
        var list = new JsonArray();
        foreach (var (op, apply, args) in steps)
        {
            state = apply(state);
            args["op"] = op;
            list.Add(new JsonObject { ["step"] = args, ["state"] = state.ToJsonNode() });
        }
        // Round trip through JSON must be lossless.
        Assert.Equal(state.ToJson(), GridState.FromJson(state.ToJson()).ToJson());
        return list;
    }

    private static JsonArray SelectionCases()
    {
        string[] order = ["a", "b", "c", "d", "e", "f", "g"];
        (string Id, GridSelectionMode Mode, (string Op, string? Key, bool Ctrl, bool Shift)[] Ops)[] cases =
        [
            ("multi-click-range", GridSelectionMode.Multi, [("click", "b", false, false), ("click", "e", false, true), ("click", "c", true, false), ("click", "g", true, true)]),
            ("multi-shift-backwards", GridSelectionMode.Multi, [("click", "f", false, false), ("click", "b", false, true), ("click", "d", false, true)]),
            ("multi-toggle-and-all", GridSelectionMode.Multi, [("toggle", "a", false, false), ("toggle", "c", false, false), ("toggle", "a", false, false), ("selectAll", null, false, false), ("toggle", "d", false, false), ("clear", null, false, false)]),
            ("multi-extend-without-anchor", GridSelectionMode.Multi, [("extendTo", "c", false, false), ("extendTo", "e", false, false)]),
            ("multi-all-matching", GridSelectionMode.Multi, [("selectAllMatching", "1000", false, false), ("toggle", "b", false, false), ("click", "c", true, false), ("click", "c", true, false)]),
            ("single", GridSelectionMode.Single, [("click", "b", false, false), ("click", "d", true, true), ("toggle", "d", false, false), ("toggle", "e", false, false)]),
            ("none", GridSelectionMode.None, [("click", "b", false, false), ("toggle", "c", false, false), ("selectAll", null, false, false)]),
            ("shift-anchor-missing-from-view", GridSelectionMode.Multi, [("click", "z", false, false), ("click", "c", false, true)]),
        ];

        var list = new JsonArray();
        foreach (var (id, mode, ops) in cases)
        {
            var model = new SelectionModel<string>(mode);
            var steps = new JsonArray();
            foreach (var (op, key, ctrl, shift) in ops)
            {
                switch (op)
                {
                    case "click": model.Click(key!, order, ctrl, shift); break;
                    case "toggle": model.Toggle(key!); break;
                    case "extendTo": model.ExtendTo(key!, order); break;
                    case "selectAll": model.SelectAll(order); break;
                    case "selectAllMatching": model.SelectAllMatching(int.Parse(key!, CultureInfo.InvariantCulture)); break;
                    case "clear": model.Clear(); break;
                }
                steps.Add(new JsonObject
                {
                    ["op"] = op, ["key"] = key, ["ctrl"] = ctrl, ["shift"] = shift,
                    ["selected"] = new JsonArray([.. model.SelectedIn(order).Select(k => (JsonNode)JsonValue.Create(k))]),
                    ["anchor"] = model.HasAnchor ? model.Anchor : null,
                    ["count"] = model.Count,
                    ["header"] = GridState.Camel(model.HeaderState(order)),
                });
            }
            list.Add(new JsonObject { ["id"] = id, ["mode"] = GridState.Camel(mode), ["order"] = new JsonArray([.. order.Select(k => (JsonNode)JsonValue.Create(k))]), ["steps"] = steps });
        }
        return list;
    }

    private static JsonArray NavigationCases()
    {
        // kinds: d = data, g = group, x = detail
        (string Id, string Kinds, int Cols, int Page, int[] Expandable, int[] Expanded, (int R, int C) Start, GridKey[] Keys)[] cases =
        [
            ("arrows-and-bounds", "dddddd", 4, 3, [], [], (0, 0), [GridKey.Up, GridKey.Left, GridKey.Right, GridKey.Right, GridKey.Down, GridKey.End, GridKey.Right, GridKey.Home, GridKey.CtrlEnd, GridKey.Down, GridKey.CtrlHome]),
            ("paging", "dddddddddddd", 3, 5, [], [], (1, 1), [GridKey.PageDown, GridKey.PageDown, GridKey.PageDown, GridKey.PageUp, GridKey.PageUp, GridKey.PageUp]),
            ("group-rows", "gdddgdd", 3, 4, [0, 4], [0], (0, 2), [GridKey.Left, GridKey.Left, GridKey.Right, GridKey.End, GridKey.Down, GridKey.End, GridKey.Down, GridKey.Down, GridKey.Down, GridKey.Right, GridKey.Minus, GridKey.Plus, GridKey.Home]),
            ("tree-first-column", "dddd", 3, 4, [0, 2], [0], (0, 0), [GridKey.Left, GridKey.Right, GridKey.Right, GridKey.Left, GridKey.Left, GridKey.Down, GridKey.Down, GridKey.Right, GridKey.Plus, GridKey.Minus]),
            ("detail-rows", "dxdx", 3, 4, [], [], (0, 2), [GridKey.Down, GridKey.Left, GridKey.Right, GridKey.End, GridKey.Down, GridKey.Left]),
            ("empty", "", 3, 4, [], [], (0, 0), [GridKey.Down]),
        ];

        var list = new JsonArray();
        foreach (var (id, kinds, cols, page, expandable, expanded, start, keys) in cases)
        {
            var exp = new HashSet<int>(expanded);
            var ctx = new GridNavContext
            {
                RowCount = kinds.Length, ColumnCount = cols, PageRows = page,
                Kind = i => kinds[i] switch { 'g' => GridRowKind.Group, 'x' => GridRowKind.Detail, _ => GridRowKind.Data },
                CanExpand = i => expandable.Contains(i),
                IsExpanded = i => exp.Contains(i),
            };
            var cell = new GridCell(start.R, start.C);
            var steps = new JsonArray();
            foreach (var key in keys)
            {
                var r = GridNavigator.Move(cell, key, ctx);
                if (r.Action == GridNavAction.Expand) exp.Add(r.Cell.Row);
                if (r.Action == GridNavAction.Collapse) exp.Remove(r.Cell.Row);
                cell = r.Cell;
                steps.Add(new JsonObject { ["key"] = GridState.Camel(key), ["row"] = cell.Row, ["column"] = cell.Column, ["action"] = GridState.Camel(r.Action) });
            }
            list.Add(new JsonObject
            {
                ["id"] = id, ["kinds"] = kinds, ["columns"] = cols, ["pageRows"] = page,
                ["expandable"] = new JsonArray([.. expandable.Select(i => (JsonNode)JsonValue.Create(i))]),
                ["expanded"] = new JsonArray([.. expanded.Select(i => (JsonNode)JsonValue.Create(i))]),
                ["start"] = new JsonObject { ["row"] = start.R, ["column"] = start.C },
                ["steps"] = steps,
            });
        }
        return list;
    }

    private static JsonArray ExportCases(List<GridColumn<Row>> cols, List<Row> rows)
    {
        var list = new JsonArray();
        foreach (var (id, state) in new[]
                 {
                     ("sorted-first-12", S(s => s.ToggleSort("id").SetColumnHidden("qty", true))),
                     ("filtered-quotes", S(s => s.SetFilter(new("name", FilterOperator.Contains, "\"")).ToggleSort("id"))),
                 })
        {
            var layout = GridLayout.Resolve(cols, state);
            var visible = layout.Columns.Select(c => c.Column).ToList();
            var items = new DataPipeline<Row>(cols, new() { RowKey = r => r["id"] }).Run(rows, state).Items.Take(12).ToList();
            list.Add(new JsonObject
            {
                ["id"] = id, ["state"] = state.ToJsonNode(), ["take"] = 12,
                ["columns"] = new JsonArray([.. visible.Select(c => (JsonNode)JsonValue.Create(c.Field))]),
                ["csv"] = GridExport.ToCsv(visible, items),
                ["tsv"] = GridExport.ToTsv(visible, items, includeHeader: true),
            });
        }
        list.Add(new JsonObject
        {
            ["id"] = "csv-quoting",
            ["fields"] = new JsonArray("plain", "a,b", "say \"hi\"", "line\nbreak", " lead", "trail ", "", "tab\there"),
            ["csv"] = new JsonArray("plain", "\"a,b\"", "\"say \"\"hi\"\"\"", "\"line\nbreak\"", "\" lead\"", "\"trail \"", "", "tab\there"),
            ["tsv"] = new JsonArray("plain", "a,b", "say \"hi\"", "line break", " lead", "trail ", "", "tab here"),
        });
        // Self-check the quoting case against the C# implementation.
        var q = list[^1]!;
        var fields = q["fields"]!.AsArray().Select(n => n!.GetValue<string>()).ToArray();
        Assert.Equal(q["csv"]!.AsArray().Select(n => n!.GetValue<string>()), fields.Select(GridExport.Csv));
        Assert.Equal(q["tsv"]!.AsArray().Select(n => n!.GetValue<string>()), fields.Select(GridExport.Tsv));
        return list;
    }

    private static JsonArray ViewportCases()
    {
        (double Top, double Height, double Row, int Count, int Overscan)[] cases =
        [(0, 400, 32, 1000, 6), (3200, 400, 32, 1000, 6), (31_600, 400, 32, 1000, 6), (99_999, 400, 32, 1000, 6), (-50, 400, 32, 1000, 0), (0, 400, 40, 5, 6), (0, 400, 32, 0, 6), (517, 333, 32, 100, 2)];
        var list = new JsonArray();
        foreach (var (top, height, row, count, over) in cases)
        {
            var r = GridViewport.Compute(top, height, row, count, over);
            list.Add(new JsonObject
            {
                ["scrollTop"] = top, ["viewportHeight"] = height, ["rowHeight"] = row, ["rowCount"] = count, ["overscan"] = over,
                ["first"] = r.First, ["count"] = r.Count, ["offsetTop"] = r.OffsetTop, ["totalHeight"] = r.TotalHeight,
                ["reveal"] = new JsonArray([.. new[] { 0, 5, 20, 50 }.Select(i => (JsonNode)JsonValue.Create(GridViewport.ScrollToReveal(i, top < 0 ? 0 : top, height, row)))]),
                ["pageRows"] = GridViewport.PageRows(height, row),
            });
        }
        return list;
    }

    private static JsonArray LayoutCases(List<GridColumn<Row>> cols)
    {
        var list = new JsonArray();
        foreach (var (id, state, width) in new[]
                 {
                     ("default-900", new GridState(), 900.0),
                     ("default-wide-flex", new GridState(), 2400.0),
                     ("reordered-pinned-hidden", S(s => s.MoveColumn(["id", "name", "team", "status", "amount", "qty", "score", "created", "active", "parentId"], "created", 0)
                         .PinColumn("created", GridPin.Start).PinColumn("id", GridPin.Start).SetColumnHidden("status", true).PinColumn("active", GridPin.End).ResizeColumn("team", 210)), 1200.0),
                     ("narrow", S(s => s.SetColumnHidden("parentId", false)), 300.0),
                 })
        {
            var layout = GridLayout.Resolve(cols, state, width);
            list.Add(new JsonObject
            {
                ["id"] = id, ["state"] = state.ToJsonNode(), ["availableWidth"] = width, ["totalWidth"] = layout.TotalWidth,
                ["order"] = new JsonArray([.. layout.Order.Select(f => (JsonNode)JsonValue.Create(f))]),
                ["columns"] = new JsonArray([.. layout.Columns.Select(c => (JsonNode)new JsonObject
                {
                    ["field"] = c.Field, ["width"] = c.Width, ["pin"] = GridState.Camel(c.Pin), ["index"] = c.Index, ["left"] = c.Left, ["sticky"] = c.StickyOffset,
                })]),
                ["scrollingRange"] = new JsonArray(layout.ScrollingRange(150, 400, 50).First, layout.ScrollingRange(150, 400, 50).Last),
                ["estimates"] = new JsonObject(cols.Select(c => new KeyValuePair<string, JsonNode?>(c.Field,
                    GridLayout.EstimateWidth(c, ["x", "medium text", "a considerably longer cell value"])))),
            });
        }
        return list;
    }

    private static JsonArray FormatCases()
    {
        (object? Value, GridColumnType Type, string? Format)[] cases =
        [
            (1234.5, GridColumnType.Number, null), (0.1, GridColumnType.Number, null), (-3.0, GridColumnType.Number, null),
            (1234567.891, GridColumnType.Number, "#,##0.00"), (-1234567.891, GridColumnType.Number, "#,##0"), (0.4567, GridColumnType.Number, "0.0%"),
            (42.0, GridColumnType.Progress, "0%"), (999.995, GridColumnType.Number, "0.0"), (12.0, GridColumnType.Number, "0.00"),
            ("2026-03-04", GridColumnType.Date, null), ("2026-03-04T09:05:00Z", GridColumnType.Date, null),
            ("2026-12-25T23:59:59Z", GridColumnType.Date, "dd MMM yyyy HH:mm:ss"), ("2026-01-02", GridColumnType.Date, "MM/dd/yyyy"),
            (true, GridColumnType.Boolean, null), ("no", GridColumnType.Boolean, null), (null, GridColumnType.Text, null),
            ("text", GridColumnType.Enum, null), (5.0, GridColumnType.Text, null),
        ];
        var list = new JsonArray();
        foreach (var (value, type, format) in cases)
        {
            var v = type == GridColumnType.Date && value is string iso ? GridValues.ParseIsoDate(iso) : value;
            list.Add(new JsonObject
            {
                ["value"] = GridState.ToNode(value), ["type"] = GridState.Camel(type), ["format"] = format,
                ["text"] = GridValues.Format(v, type, format),
            });
        }
        return list;
    }

    // ---- the test ---------------------------------------------------------------------------------------

    private static string FixturePath()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "global.json")))
                return Path.Combine(dir.FullName, "tests", "fixtures", "data-grid.json");
        throw new DirectoryNotFoundException("Repository root not found.");
    }

    [Fact]
    public void Fixture_matches_engine_output()
    {
        var path = FixturePath();
        var expected = Build().ToJsonString(new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping })
            .Replace("\r\n", "\n") + "\n";
        if (Environment.GetEnvironmentVariable("SLATE_UPDATE_FIXTURES") == "1" || !File.Exists(path))
        {
            File.WriteAllText(path, expected);
            return;
        }
        Assert.Equal(File.ReadAllText(path).Replace("\r\n", "\n"), expected);
    }
}
