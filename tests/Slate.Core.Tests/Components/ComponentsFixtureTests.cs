using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Slate.Collections;
using Slate.Dates;
using Slate.Overlays;

namespace Slate.Core.Tests.Components;

/// <summary>
/// Writes/validates tests/fixtures/components.json: inputs and expected outputs for the wave-2 component logic
/// (positioning, calendar, list navigation, typeahead, filtering, tree, pagination, avatar, slider). @bytegrain/slate-web's
/// TypeScript port asserts it reproduces every result exactly. Regenerate with SLATE_UPDATE_FIXTURES=1 dotnet test.
/// </summary>
public class ComponentsFixtureTests
{
    // ---- helpers -------------------------------------------------------------------------------------------

    internal static string Kebab(Enum e) => Regex.Replace(e.ToString(), "(?<!^)([A-Z])", "-$1").ToLowerInvariant();

    private static JsonObject Rect(OverlayRect r) => new() { ["x"] = r.X, ["y"] = r.Y, ["width"] = r.Width, ["height"] = r.Height };

    private static string Iso(DateOnly d) => d.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);

    private static string? Iso(DateOnly? d) => d is { } v ? Iso(v) : null;

    private static DateOnly D(string iso) => DateOnly.ParseExact(iso, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);

    private static JsonArray Arr<TItem>(IEnumerable<TItem> items) => new(items.Select(i => JsonValue.Create(i)).ToArray<JsonNode?>());

    private static JsonNode? Range(DateRange? r) => r is { } v ? new JsonObject { ["start"] = Iso(v.Start), ["end"] = Iso(v.End) } : null;

    // ---- sections ------------------------------------------------------------------------------------------

    private static JsonArray Positioning()
    {
        var vp = new OverlayRect(0, 0, 1000, 700);
        var cases = new (string Id, OverlayRect Anchor, double W, double H, PopoverPlacement P, bool Flip, bool Shift)[]
        {
            ("bottom-fits", new(400, 100, 120, 32), 200, 150, PopoverPlacement.Bottom, true, true),
            ("bottom-start", new(400, 100, 120, 32), 200, 150, PopoverPlacement.BottomStart, true, true),
            ("bottom-end", new(400, 100, 120, 32), 200, 150, PopoverPlacement.BottomEnd, true, true),
            ("bottom-flips-to-top", new(400, 600, 120, 32), 200, 150, PopoverPlacement.Bottom, true, true),
            ("bottom-no-flip", new(400, 600, 120, 32), 200, 150, PopoverPlacement.Bottom, false, true),
            ("top-flips-to-bottom", new(400, 40, 120, 32), 200, 150, PopoverPlacement.Top, true, true),
            ("neither-fits-shrinks", new(400, 300, 120, 32), 200, 500, PopoverPlacement.Bottom, true, true),
            ("shift-left-edge", new(5, 100, 40, 32), 240, 100, PopoverPlacement.Bottom, true, true),
            ("shift-right-edge", new(960, 100, 30, 32), 240, 100, PopoverPlacement.BottomStart, true, true),
            ("no-shift", new(960, 100, 30, 32), 240, 100, PopoverPlacement.BottomStart, true, false),
            ("right-fits", new(100, 300, 120, 32), 220, 160, PopoverPlacement.Right, true, true),
            ("right-flips-left", new(800, 300, 120, 32), 220, 160, PopoverPlacement.RightStart, true, true),
            ("left-flips-right", new(30, 300, 120, 32), 220, 160, PopoverPlacement.Left, true, true),
            ("left-end", new(500, 300, 120, 32), 220, 160, PopoverPlacement.LeftEnd, true, true),
            ("tall-side-popup", new(500, 300, 120, 32), 220, 900, PopoverPlacement.Right, true, true),
            ("wider-than-viewport", new(400, 100, 120, 32), 1200, 100, PopoverPlacement.Bottom, true, true),
            ("top-start-near-top-right", new(900, 20, 80, 28), 160, 300, PopoverPlacement.TopStart, true, true),
            ("arrow-clamped", new(990, 300, 8, 8), 200, 120, PopoverPlacement.Bottom, true, true),
        };
        var arr = new JsonArray();
        foreach (var c in cases)
        {
            var req = new PositionRequest { Anchor = c.Anchor, PopupWidth = c.W, PopupHeight = c.H, Viewport = vp, Placement = c.P, Flip = c.Flip, Shift = c.Shift };
            var res = PopoverPositioner.Position(req);
            arr.Add(new JsonObject
            {
                ["id"] = c.Id, ["anchor"] = Rect(c.Anchor), ["popupWidth"] = c.W, ["popupHeight"] = c.H, ["viewport"] = Rect(vp),
                ["placement"] = Kebab(c.P), ["offset"] = req.Offset, ["padding"] = req.Padding, ["flip"] = c.Flip, ["shift"] = c.Shift, ["arrowPadding"] = req.ArrowPadding,
                ["result"] = new JsonObject { ["rect"] = Rect(res.Rect), ["placement"] = Kebab(res.Placement), ["maxHeight"] = res.MaxHeight, ["arrowOffset"] = res.ArrowOffset, ["flipped"] = res.Flipped },
            });
        }
        return arr;
    }

    private static JsonArray AtPoint()
    {
        var vp = new OverlayRect(0, 0, 1000, 700);
        var arr = new JsonArray();
        foreach (var (id, x, y, w, h) in new[] { ("plain", 300.0, 200.0, 200.0, 160.0), ("flip-x", 900.0, 200.0, 200.0, 160.0), ("flip-y", 300.0, 650.0, 200.0, 160.0), ("flip-both", 950.0, 680.0, 200.0, 160.0), ("too-tall", 300.0, 200.0, 200.0, 900.0), ("corner", 2.0, 3.0, 200.0, 160.0) })
        {
            var r = PopoverPositioner.PositionAtPoint(x, y, w, h, vp);
            arr.Add(new JsonObject
            {
                ["id"] = id, ["x"] = x, ["y"] = y, ["popupWidth"] = w, ["popupHeight"] = h, ["viewport"] = Rect(vp), ["padding"] = 8,
                ["result"] = new JsonObject { ["rect"] = Rect(r.Rect), ["placement"] = Kebab(r.Placement), ["maxHeight"] = r.MaxHeight, ["arrowOffset"] = r.ArrowOffset, ["flipped"] = r.Flipped },
            });
        }
        return arr;
    }

    internal static string DayFlags(CalendarDay d) =>
        Iso(d.Date) + ":" + (d.InMonth ? "m" : "") + (d.IsToday ? "t" : "") + (d.IsDisabled ? "x" : "") + (d.IsSelected ? "s" : "") +
        (d.IsRangeStart ? "a" : "") + (d.IsRangeEnd ? "b" : "") + (d.InRange ? "r" : "") + (d.InPreview ? "p" : "");

    private static JsonArray Calendar()
    {
        var arr = new JsonArray();
        var cases = new (string Id, int Y, int M, DayOfWeek First, string? Today, string? Min, string? Max, string[] Disabled, bool Weekends, string? Selected, (string S, string? E)? Range, string? Hover)[]
        {
            ("oct-2026-monday", 2026, 10, DayOfWeek.Monday, "2026-10-06", null, null, [], false, "2026-10-14", null, null),
            ("oct-2026-sunday", 2026, 10, DayOfWeek.Sunday, "2026-10-06", null, null, [], false, null, null, null),
            ("feb-2028-leap-saturday", 2028, 2, DayOfWeek.Saturday, null, null, null, [], false, null, null, null),
            ("jan-2027-iso-weeks", 2027, 1, DayOfWeek.Monday, null, null, null, [], false, null, null, null),
            ("dec-2026-iso-week-53", 2026, 12, DayOfWeek.Monday, null, null, null, [], false, null, null, null),
            ("min-max-disabled", 2026, 10, DayOfWeek.Monday, null, "2026-10-05", "2026-10-25", ["2026-10-13", "2026-10-14"], false, null, null, null),
            ("weekends-disabled", 2026, 10, DayOfWeek.Monday, null, null, null, [], true, null, null, null),
            ("range-complete", 2026, 10, DayOfWeek.Monday, null, null, null, [], false, null, ("2026-10-08", "2026-10-15"), null),
            ("range-across-months", 2026, 11, DayOfWeek.Monday, null, null, null, [], false, null, ("2026-10-28", "2026-11-03"), null),
            ("range-preview-forward", 2026, 10, DayOfWeek.Monday, null, null, null, [], false, null, ("2026-10-08", null), "2026-10-12"),
            ("range-preview-backward", 2026, 10, DayOfWeek.Monday, null, null, null, [], false, null, ("2026-10-08", null), "2026-10-03"),
        };
        foreach (var c in cases)
        {
            var disabled = c.Disabled.Select(D).ToHashSet();
            var opts = new CalendarOptions
            {
                FirstDayOfWeek = c.First,
                Today = c.Today is null ? null : D(c.Today),
                Min = c.Min is null ? null : D(c.Min),
                Max = c.Max is null ? null : D(c.Max),
                IsDateDisabled = d => disabled.Contains(d) || (c.Weekends && d.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday),
                Selected = c.Selected is null ? null : D(c.Selected),
                Range = c.Range is { } r ? new DateRange(D(r.S), r.E is null ? null : D(r.E)) : null,
                Hover = c.Hover is null ? null : D(c.Hover),
            };
            var m = CalendarModel.Build(c.Y, c.M, opts);
            arr.Add(new JsonObject
            {
                ["id"] = c.Id, ["year"] = c.Y, ["month"] = c.M, ["firstDayOfWeek"] = (int)c.First, ["today"] = c.Today, ["min"] = c.Min, ["max"] = c.Max,
                ["disabled"] = Arr(c.Disabled), ["disableWeekends"] = c.Weekends, ["selected"] = c.Selected,
                ["range"] = c.Range is { } rr ? new JsonObject { ["start"] = rr.S, ["end"] = rr.E } : null, ["hover"] = c.Hover,
                ["result"] = new JsonObject
                {
                    ["weekdays"] = Arr(m.Weekdays.Select(w => (int)w)),
                    ["weeks"] = new JsonArray(m.Weeks.Select(w => (JsonNode?)new JsonObject { ["isoWeek"] = w.IsoWeek, ["days"] = Arr(w.Days.Select(DayFlags)) }).ToArray()),
                },
            });
        }
        return arr;
    }

    private static JsonArray CalendarNav()
    {
        var arr = new JsonArray();
        var keys = Enum.GetValues<CalendarKey>();
        var scenarios = new (string Id, string Focus, DayOfWeek First, string? Min, string? Max, string[] Disabled, bool Weekends)[]
        {
            ("plain-mon", "2026-10-14", DayOfWeek.Monday, null, null, [], false),
            ("plain-sun", "2026-10-14", DayOfWeek.Sunday, null, null, [], false),
            ("month-end-clamp", "2026-01-31", DayOfWeek.Monday, null, null, [], false),
            ("leap-year", "2028-02-29", DayOfWeek.Monday, null, null, [], false),
            ("bounded", "2026-10-06", DayOfWeek.Monday, "2026-10-05", "2026-10-20", [], false),
            ("skip-disabled", "2026-10-14", DayOfWeek.Monday, null, null, ["2026-10-15", "2026-10-16", "2026-10-21", "2026-10-13"], false),
            ("skip-weekends", "2026-10-16", DayOfWeek.Monday, null, null, [], true),
        };
        foreach (var s in scenarios)
        {
            var disabled = s.Disabled.Select(D).ToHashSet();
            var opts = new CalendarOptions
            {
                FirstDayOfWeek = s.First, Min = s.Min is null ? null : D(s.Min), Max = s.Max is null ? null : D(s.Max),
                IsDateDisabled = d => disabled.Contains(d) || (s.Weekends && d.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday),
            };
            foreach (var key in keys)
            foreach (var shift in key is CalendarKey.PageUp or CalendarKey.PageDown ? new[] { false, true } : [false])
            {
                arr.Add(new JsonObject
                {
                    ["id"] = $"{s.Id}/{Kebab(key)}{(shift ? "+shift" : "")}", ["focused"] = s.Focus, ["key"] = Kebab(key), ["shift"] = shift,
                    ["firstDayOfWeek"] = (int)s.First, ["min"] = s.Min, ["max"] = s.Max, ["disabled"] = Arr(s.Disabled), ["disableWeekends"] = s.Weekends,
                    ["result"] = Iso(CalendarModel.Navigate(D(s.Focus), key, shift, opts)),
                });
            }
        }
        return arr;
    }

    private static JsonObject Dates()
    {
        var pick = new JsonArray();
        DateRange? current = null;
        foreach (var click in new[] { "2026-10-10", "2026-10-15", "2026-10-20", "2026-10-02", "2026-10-02", "2026-11-01" })
        {
            var next = CalendarModel.PickRange(current, D(click));
            pick.Add(new JsonObject { ["current"] = Range(current), ["clicked"] = click, ["result"] = Range(next) });
            current = next;
        }

        var presets = new JsonArray();
        foreach (var today in new[] { "2026-10-06", "2026-01-15", "2028-03-01", "2026-12-31" })
        foreach (var kind in Enum.GetValues<DatePresetKind>())
        {
            var r = CalendarModel.Resolve(kind, D(today));
            presets.Add(new JsonObject { ["kind"] = Kebab(kind), ["today"] = today, ["start"] = Iso(r.Start), ["end"] = Iso(r.End) });
        }

        var text = new JsonArray();
        foreach (var (pattern, date) in new[] { ("yyyy-MM-dd", "2026-10-06"), ("M/d/yyyy", "2026-01-05"), ("dd.MM.yyyy", "2026-12-31"), ("d/M/yy", "2007-03-09"), ("yyyy/MM/dd", "0999-02-03") })
            text.Add(new JsonObject { ["op"] = "format", ["pattern"] = pattern, ["input"] = date, ["result"] = DateText.Format(D(date), pattern) });
        foreach (var (pattern, input) in new[]
                 {
                     ("M/d/yyyy", "10/6/2026"), ("M/d/yyyy", "1/31/2026"), ("M/d/yyyy", "2/30/2026"), ("dd.MM.yyyy", "31.12.2026"), ("dd.MM.yyyy", "6 10 26"),
                     ("d/M/yy", "9/3/07"), ("d/M/yy", "9/3/75"), ("yyyy-MM-dd", "2026-1-5"), ("M/d/yyyy", "2026-10-06"), ("M/d/yyyy", ""), ("M/d/yyyy", "abc"),
                     ("M/d/yyyy", "10/6"), ("M/d/yyyy", "13/1/2026"), ("dd.MM.yyyy", "29.02.2028"), ("dd.MM.yyyy", "29.02.2027"), ("M/d/yyyy", "  4/5/2026  "),
                 })
            text.Add(new JsonObject { ["op"] = "parse", ["pattern"] = pattern, ["input"] = input, ["result"] = Iso(DateText.Parse(input, pattern)) });

        return new JsonObject { ["pickRange"] = pick, ["presets"] = presets, ["text"] = text };
    }

    private static JsonArray ListNav()
    {
        var arr = new JsonArray();
        var lists = new (string Id, bool[] Disabled)[]
        {
            ("all-enabled", [false, false, false, false, false, false]),
            ("some-disabled", [true, false, false, true, false, true]),
            ("all-disabled", [true, true, true]),
            ("long", Enumerable.Range(0, 30).Select(i => i % 7 == 3).ToArray()),
            ("empty", []),
        };
        foreach (var (id, disabled) in lists)
        foreach (var wrap in new[] { false, true })
        foreach (var current in new[] { -1, 0, 2, disabled.Length - 1 }.Distinct())
        foreach (var key in Enum.GetValues<ListKey>())
        {
            var opts = new ListNavigationOptions { Wrap = wrap, PageSize = 4 };
            arr.Add(new JsonObject
            {
                ["id"] = $"{id}/{(wrap ? "wrap" : "nowrap")}/{current}/{Kebab(key)}", ["current"] = current, ["key"] = Kebab(key), ["disabled"] = Arr(disabled),
                ["wrap"] = wrap, ["pageSize"] = 4, ["result"] = ListNavigator.Move(current, key, disabled, opts),
            });
        }
        return arr;
    }

    private static JsonArray TypeaheadCases()
    {
        string[] fruits = ["Apple", "Apricot", "Banana", "Blueberry", "Cherry", "Ångström", "Avocado", "Éclair", "banana split"];
        var scenarios = new (string Id, string[] Labels, bool[]? Disabled, int Current, (string Key, long T)[] Keys)[]
        {
            ("prefix", fruits, null, -1, [("a", 0), ("p", 100), ("r", 200)]),
            ("cycle-same-letter", fruits, null, -1, [("b", 0), ("b", 100), ("b", 200), ("b", 300)]),
            ("timeout-resets", fruits, null, 0, [("b", 0), ("l", 1000)]),
            ("diacritics", fruits, null, -1, [("a", 0), ("n", 50)]),
            ("accented-e", fruits, null, -1, [("e", 0), ("c", 60)]),
            ("skip-disabled", fruits, [false, true, false, true, false, false, false, false, false], -1, [("b", 0), ("b", 100)]),
            ("no-match-keeps-buffer", fruits, null, 2, [("z", 0), ("q", 100)]),
            ("from-current-wraps", fruits, null, 6, [("a", 0)]),
        };
        var arr = new JsonArray();
        foreach (var s in scenarios)
        {
            var t = new Typeahead();
            var current = s.Current;
            var results = new List<int>();
            foreach (var (key, time) in s.Keys)
            {
                var r = t.Search(key, time, s.Labels, current, s.Disabled);
                results.Add(r);
                if (r >= 0) current = r;
            }
            arr.Add(new JsonObject
            {
                ["id"] = s.Id, ["labels"] = Arr(s.Labels), ["disabled"] = s.Disabled is null ? null : Arr(s.Disabled), ["current"] = s.Current, ["timeout"] = t.Timeout,
                ["keys"] = new JsonArray(s.Keys.Select(k => (JsonNode?)new JsonObject { ["key"] = k.Key, ["t"] = k.T }).ToArray()), ["results"] = Arr(results),
            });
        }
        return arr;
    }

    private static JsonObject Filtering()
    {
        string[] labels =
        [
            "Slate.Avalonia", "Slate.Blazor", "Slate.Core", "Slate.Wpf", "@bytegrain/slate-web", "Avalonia Desktop", "Blazor WebAssembly",
            "Café Crème", "crème brûlée", "Zoë's Desktop Tools", "data-grid", "DataGrid Pro", "Grid", "Grind", "日本語 テキスト", "emoji 😀 icons", "",
        ];
        var filter = new JsonArray();
        foreach (var q in new[] { "", "  ", "slate", "ava", "grid", "data grid", "creme", "CRÈME", "desk", "zoe", "blaz web", "wpf", "テキスト", "😀", "icons emoji", "xyz", "g", "s.c" })
        {
            var r = OptionFilter.Filter(labels, q);
            filter.Add(new JsonObject
            {
                ["query"] = q,
                ["result"] = new JsonArray(r.Select(m => (JsonNode?)new JsonObject
                {
                    ["index"] = m.Index, ["score"] = m.Score,
                    ["ranges"] = new JsonArray(m.Ranges.Select(x => (JsonNode?)new JsonArray(x.Start, x.Length)).ToArray()),
                }).ToArray()),
            });
        }

        string[] keys = ["Fruit", "Veg", "Fruit", "Grain", "Veg", "Fruit", ""];
        var groups = OptionFilter.Group(keys);
        return new JsonObject
        {
            ["labels"] = Arr(labels),
            ["filter"] = filter,
            ["fold"] = new JsonArray(new[] { "Ångström", "CRÈME BRÛLÉE", "Ǆ", "ﬁ", "İstanbul", "Straße" }.Select(s => (JsonNode?)new JsonObject { ["input"] = s, ["result"] = TextFolding.Fold(s) }).ToArray()),
            ["group"] = new JsonObject
            {
                ["keys"] = Arr(keys),
                ["result"] = new JsonArray(groups.Select(g => (JsonNode?)new JsonObject { ["key"] = g.Key, ["indices"] = Arr(g.Indices) }).ToArray()),
            },
        };
    }

    internal sealed record Node(string Id, string Label, Node[]? Children = null, bool Lazy = false);

    internal static readonly Node[] TreeData =
    [
        new("src", "src", [
            new("core", "Slate.Core", [new("tokens", "SlateTokens.g.cs"), new("queue", "SnackbarQueue.cs"), new("stack", "DialogStack.cs")]),
            new("web", "@bytegrain/slate-web", [new("button", "button.ts"), new("grid", "data-grid.ts"), new("theme", "théme.ts")]),
            new("wpf", "Slate.Wpf", Lazy: true),
        ]),
        new("docs", "docs", [new("readme", "README.md"), new("design", "design", [new("color", "color.md"), new("grid-doc", "data-grid.md")])]),
        new("license", "LICENSE"),
    ];

    internal static TreeModel<Node> Tree() => new(TreeData, n => n.Id, n => n.Children, n => n.Lazy);

    private static JsonNode? NodeJson(Node n) => new JsonObject
    {
        ["id"] = n.Id, ["label"] = n.Label, ["lazy"] = n.Lazy,
        ["children"] = n.Children is null ? null : new JsonArray(n.Children.Select(NodeJson).ToArray()),
    };

    private static JsonArray Sorted(IEnumerable<string> ids) => Arr(ids.Order(StringComparer.Ordinal));

    private static JsonObject TreeCases()
    {
        var model = Tree();
        JsonArray Rows(IReadOnlyList<TreeRow> rows) => new(rows.Select(r => (JsonNode?)new JsonObject
        {
            ["id"] = r.Id, ["parentId"] = r.ParentId, ["depth"] = r.Depth, ["hasChildren"] = r.HasChildren, ["expanded"] = r.Expanded, ["pos"] = r.PositionInSet, ["size"] = r.SetSize,
        }).ToArray());

        var flatten = new JsonArray();
        foreach (var expanded in new[] { Array.Empty<string>(), ["src"], ["src", "core", "docs"], ["src", "web", "wpf", "docs", "design"], ["core"] })
            flatten.Add(new JsonObject { ["expanded"] = Arr(expanded), ["rows"] = Rows(model.Flatten(expanded.ToHashSet())) });

        var filter = new JsonArray();
        foreach (var q in new[] { "grid", "THEME", ".md", "slate", "zzz", "" })
        {
            var fq = TextFolding.Fold(q);
            var (visible, expand) = model.Filter(n => TextFolding.Fold(n.Label).Contains(fq, StringComparison.Ordinal));
            filter.Add(new JsonObject
            {
                ["query"] = q, ["visible"] = Sorted(visible), ["expand"] = Sorted(expand),
                ["rows"] = Rows(model.Flatten(expand, visible)),
            });
        }

        var nav = new JsonArray();
        var navCases = new (string? Focus, string[] Expanded)[] { (null, []), ("src", []), ("src", ["src"]), ("core", ["src"]), ("core", ["src", "core"]), ("tokens", ["src", "core"]), ("license", ["src", "docs"]), ("wpf", ["src"]), ("readme", ["docs"]), ("design", ["docs"]) };
        foreach (var (focus, expanded) in navCases)
        foreach (var key in Enum.GetValues<TreeKey>())
        {
            var r = model.Navigate(focus, key, expanded.ToHashSet());
            nav.Add(new JsonObject { ["focus"] = focus, ["key"] = Kebab(key), ["expanded"] = Arr(expanded), ["result"] = new JsonObject { ["focus"] = r.FocusId, ["expanded"] = Sorted(r.Expanded) } });
        }

        var check = new JsonArray();
        IReadOnlySet<string> state = new HashSet<string>();
        string[] all = ["src", "core", "tokens", "queue", "stack", "web", "button", "grid", "theme", "wpf", "docs", "readme", "design", "color", "grid-doc", "license"];
        foreach (var toggle in new[] { "tokens", "queue", "stack", "core", "web", "button", "src", "design", "color", "docs" })
        {
            var before = state;
            state = model.ToggleCheck(toggle, state);
            var states = new JsonObject();
            foreach (var id in all) states[id] = Kebab(model.GetCheckState(id, state));
            check.Add(new JsonObject { ["checked"] = Sorted(before), ["toggle"] = toggle, ["result"] = Sorted(state), ["states"] = states });
        }

        return new JsonObject { ["nodes"] = new JsonArray(TreeData.Select(NodeJson).ToArray()), ["flatten"] = flatten, ["filter"] = filter, ["nav"] = nav, ["check"] = check };
    }

    private static JsonObject Paging()
    {
        var ranges = new JsonArray();
        foreach (var count in new[] { 0, 1, 2, 5, 7, 8, 10, 20, 100 })
        foreach (var (sib, bound) in new[] { (1, 1), (0, 1), (2, 1), (1, 2), (1, 0) })
        foreach (var page in new[] { 1, 2, 3, 4, 5, count / 2, count - 3, count - 1, count, count + 5 }.Distinct())
            ranges.Add(new JsonObject
            {
                ["page"] = page, ["pageCount"] = count, ["siblings"] = sib, ["boundaries"] = bound,
                ["result"] = new JsonArray(PaginationRange.Compute(page, count, sib, bound).Select(i => (JsonNode?)(i.IsEllipsis ? JsonValue.Create("…") : JsonValue.Create(i.Page))).ToArray()),
            });

        var first = new JsonArray();
        foreach (var (page, oldSize, newSize) in new[] { (1, 10, 25), (3, 10, 25), (5, 25, 10), (4, 50, 100), (10, 20, 5), (0, 10, 10) })
            first.Add(new JsonObject { ["page"] = page, ["oldSize"] = oldSize, ["newSize"] = newSize, ["result"] = PaginationRange.PageForFirstItem(page, oldSize, newSize) });

        var counts = new JsonArray();
        foreach (var (total, size) in new[] { (0, 10), (1, 10), (10, 10), (11, 10), (999, 25), (-5, 10) })
            counts.Add(new JsonObject { ["total"] = total, ["size"] = size, ["result"] = PaginationRange.PageCount(total, size) });

        return new JsonObject { ["ranges"] = ranges, ["pageForFirstItem"] = first, ["pageCount"] = counts };
    }

    private static JsonArray Avatars()
    {
        var arr = new JsonArray();
        foreach (var name in new[] { "Aaron Griffin", "aaron", "Jo Marsh-Smith", "jo.marsh@slate.dev", "  rae   kim  ", "Éloïse Dubois", "", null, "@handle", "Zoë", "李 小龍", "😀 Party", "Mary Anne de la Cruz", "x_y_z" })
            arr.Add(new JsonObject { ["name"] = name, ["initials"] = AvatarText.Initials(name), ["tone"] = Kebab(AvatarText.ToneFor(name)) });
        return arr;
    }

    private static JsonObject Slider()
    {
        var snap = new JsonArray();
        foreach (var (v, min, max, step) in new[] { (37.0, 0.0, 100.0, 5.0), (37.5, 0, 100, 5), (-10, 0, 100, 1), (150, 0, 100, 1), (0.3, 0, 1, 0.1), (0.35, 0, 1, 0.1), (0.7, 0, 1, 0.05), (10, 0, 10, 3), (9.6, 0, 10, 3), (5, 2, 12, 4), (3.3, -5, 5, 0.25), (7, 0, 10, 0) })
            snap.Add(new JsonObject { ["value"] = v, ["min"] = min, ["max"] = max, ["step"] = step, ["result"] = SliderMath.Snap(v, min, max, step) });

        var keys = new JsonArray();
        foreach (var (v, min, max, step) in new[] { (50.0, 0.0, 100.0, 1.0), (0.0, 0.0, 100.0, 5.0), (100.0, 0.0, 100.0, 5.0), (0.5, 0.0, 1.0, 0.01), (3.0, 0.0, 7.0, 1.0) })
        foreach (var key in Enum.GetValues<SliderKey>())
            keys.Add(new JsonObject { ["value"] = v, ["min"] = min, ["max"] = max, ["step"] = step, ["key"] = Kebab(key), ["result"] = SliderMath.Key(v, key, min, max, step) });

        var fractions = new JsonArray();
        foreach (var (f, min, max, step) in new[] { (0.0, 0.0, 100.0, 1.0), (0.333, 0.0, 100.0, 1.0), (0.5, -50.0, 50.0, 10.0), (1.2, 0.0, 10.0, 1.0), (0.77, 0.0, 1.0, 0.1) })
            fractions.Add(new JsonObject { ["fraction"] = f, ["min"] = min, ["max"] = max, ["step"] = step, ["value"] = SliderMath.FromFraction(f, min, max, step), ["back"] = SliderMath.ToFraction(SliderMath.FromFraction(f, min, max, step), min, max) });

        var range = new JsonArray();
        foreach (var (start, end, thumb, v) in new[] { (20.0, 80.0, 0, 30.0), (20.0, 80.0, 0, 95.0), (20.0, 80.0, 1, 10.0), (20.0, 80.0, 1, 63.0) })
        {
            var r = SliderMath.SetRangeThumb((start, end), thumb, v, 0, 100, 5);
            range.Add(new JsonObject { ["start"] = start, ["end"] = end, ["thumb"] = thumb, ["value"] = v, ["min"] = 0, ["max"] = 100, ["step"] = 5, ["result"] = new JsonArray(r.Start, r.End) });
        }
        return new JsonObject { ["snap"] = snap, ["keys"] = keys, ["fractions"] = fractions, ["range"] = range };
    }

    // ---- fixture -------------------------------------------------------------------------------------------

    internal static string Render()
    {
        var root = new JsonObject
        {
            ["$comment"] = "Generated by tests/Slate.Core.Tests/Components/ComponentsFixtureTests.cs. Regenerate with SLATE_UPDATE_FIXTURES=1 dotnet test.",
            ["positioning"] = Positioning(),
            ["atPoint"] = AtPoint(),
            ["calendar"] = Calendar(),
            ["calendarNav"] = CalendarNav(),
            ["dates"] = Dates(),
            ["listNav"] = ListNav(),
            ["typeahead"] = TypeaheadCases(),
            ["filtering"] = Filtering(),
            ["tree"] = TreeCases(),
            ["paging"] = Paging(),
            ["avatar"] = Avatars(),
            ["slider"] = Slider(),
        };
        return root.ToJsonString(new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }).Replace("\r\n", "\n") + "\n";
    }

    private static string FixturePath()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "global.json")))
                return Path.Combine(dir.FullName, "tests", "fixtures", "components.json");
        throw new DirectoryNotFoundException("Repository root not found.");
    }

    [Fact]
    public void Fixture_matches_component_logic_output()
    {
        var path = FixturePath();
        var expected = Render();
        if (Environment.GetEnvironmentVariable("SLATE_UPDATE_FIXTURES") == "1" || !File.Exists(path))
        {
            File.WriteAllText(path, expected);
            return;
        }
        Assert.Equal(File.ReadAllText(path).Replace("\r\n", "\n"), expected);
    }
}
