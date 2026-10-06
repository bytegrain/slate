using Slate.Collections;
using Slate.Dates;
using Slate.Overlays;

namespace Slate.Core.Tests.Components;

public class PositioningTests
{
    private static readonly OverlayRect Viewport = new(0, 0, 1000, 700);

    private static PositionResult Place(OverlayRect anchor, double w, double h, PopoverPlacement p = PopoverPlacement.Bottom, bool flip = true, bool shift = true) =>
        PopoverPositioner.Position(new PositionRequest { Anchor = anchor, PopupWidth = w, PopupHeight = h, Viewport = Viewport, Placement = p, Flip = flip, Shift = shift });

    [Fact]
    public void Bottom_centers_below_the_anchor_with_the_offset()
    {
        var r = Place(new(400, 100, 120, 32), 200, 150);
        Assert.Equal(new OverlayRect(360, 138, 200, 150), r.Rect);
        Assert.Equal(PopoverPlacement.Bottom, r.Placement);
        Assert.False(r.Flipped);
        Assert.Null(r.MaxHeight);
        Assert.Equal(100, r.ArrowOffset); // anchor centre relative to popup start
    }

    [Theory]
    [InlineData(PopoverPlacement.BottomStart, 400)]
    [InlineData(PopoverPlacement.BottomEnd, 320)]
    public void Alignment_lines_up_with_the_anchor_edges(PopoverPlacement p, double x) =>
        Assert.Equal(x, Place(new(400, 100, 120, 32), 200, 150, p).Rect.X);

    [Fact]
    public void Flips_to_the_other_side_when_it_does_not_fit()
    {
        var r = Place(new(400, 600, 120, 32), 200, 150);
        Assert.Equal(PopoverPlacement.Top, r.Placement);
        Assert.True(r.Flipped);
        Assert.Equal(600 - 6 - 150, r.Rect.Y);
    }

    [Fact]
    public void Without_flip_it_stays_and_is_shortened()
    {
        var r = Place(new(400, 600, 120, 32), 200, 150, flip: false);
        Assert.Equal(PopoverPlacement.Bottom, r.Placement);
        Assert.Equal(700 - 8 - 638, r.MaxHeight);
    }

    [Fact]
    public void When_neither_side_fits_uses_the_larger_side_and_constrains_height()
    {
        var r = Place(new(400, 300, 120, 32), 200, 500);
        Assert.Equal(PopoverPlacement.Bottom, r.Placement); // 354 below vs 286 above
        Assert.Equal(700 - 8 - 338, r.MaxHeight);
        Assert.True(r.Rect.Bottom <= 692);
    }

    [Fact]
    public void Shift_keeps_the_popup_inside_the_viewport_and_the_arrow_on_the_anchor()
    {
        var r = Place(new(5, 100, 40, 32), 240, 100);
        Assert.Equal(8, r.Rect.X);
        Assert.Equal(25 - 8, r.ArrowOffset);
    }

    [Fact]
    public void Arrow_never_reaches_the_corners()
    {
        var r = Place(new(990, 300, 8, 8), 200, 120);
        Assert.Equal(200 - 12, r.ArrowOffset);
    }

    [Fact]
    public void Context_menu_flips_at_the_viewport_edges()
    {
        var plain = PopoverPositioner.PositionAtPoint(300, 200, 200, 160, Viewport);
        Assert.Equal(new OverlayRect(300, 200, 200, 160), plain.Rect);
        Assert.Equal(PopoverPlacement.BottomStart, plain.Placement);

        var corner = PopoverPositioner.PositionAtPoint(950, 680, 200, 160, Viewport);
        Assert.Equal(PopoverPlacement.TopEnd, corner.Placement);
        Assert.Equal(new OverlayRect(750, 520, 200, 160), corner.Rect);
    }

    [Fact]
    public void Negative_sizes_are_rejected() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => Place(new(0, 0, 10, 10), -1, 10));
}

public class CalendarTests
{
    private static DateOnly D(int y, int m, int d) => new(y, m, d);

    [Fact]
    public void Month_grid_is_six_weeks_starting_on_the_first_day_of_week()
    {
        var m = CalendarModel.Build(2026, 10, new CalendarOptions { FirstDayOfWeek = DayOfWeek.Monday });
        Assert.Equal(6, m.Weeks.Count);
        Assert.All(m.Weeks, w => Assert.Equal(7, w.Days.Count));
        Assert.Equal(D(2026, 9, 28), m.Weeks[0].Days[0].Date); // Oct 1 2026 is a Thursday
        Assert.Equal(DayOfWeek.Monday, m.Weekdays[0]);
        Assert.False(m.Weeks[0].Days[0].InMonth);
        Assert.Equal(40, m.Weeks[0].IsoWeek);
    }

    [Fact]
    public void Sunday_first_weeks_report_the_iso_week_of_their_thursday()
    {
        var m = CalendarModel.Build(2027, 1, new CalendarOptions { FirstDayOfWeek = DayOfWeek.Sunday });
        Assert.Equal(DayOfWeek.Sunday, m.Weekdays[0]);
        Assert.Equal(53, m.Weeks[0].IsoWeek); // Dec 27 2026 – Jan 2 2027: Thursday Dec 31 is in ISO week 53 of 2026
    }

    [Fact]
    public void Day_states_cover_today_selection_and_disabled()
    {
        var m = CalendarModel.Build(2026, 10, new CalendarOptions
        {
            Today = D(2026, 10, 6), Selected = D(2026, 10, 14), Min = D(2026, 10, 5), IsDateDisabled = d => d == D(2026, 10, 20),
        });
        var days = m.Weeks.SelectMany(w => w.Days).ToDictionary(d => d.Date);
        Assert.True(days[D(2026, 10, 6)].IsToday);
        Assert.True(days[D(2026, 10, 14)].IsSelected);
        Assert.True(days[D(2026, 10, 4)].IsDisabled);
        Assert.True(days[D(2026, 10, 20)].IsDisabled);
        Assert.False(days[D(2026, 10, 21)].IsDisabled);
    }

    [Fact]
    public void Range_and_hover_preview()
    {
        var pending = new CalendarOptions { Range = new DateRange(D(2026, 10, 8), null), Hover = D(2026, 10, 3) };
        var days = CalendarModel.Build(2026, 10, pending).Weeks.SelectMany(w => w.Days).ToDictionary(d => d.Date);
        Assert.True(days[D(2026, 10, 8)].IsRangeStart);
        Assert.True(days[D(2026, 10, 5)].InPreview);
        Assert.False(days[D(2026, 10, 5)].InRange);

        var done = CalendarModel.Build(2026, 10, new CalendarOptions { Range = new DateRange(D(2026, 10, 8), D(2026, 10, 15)) })
            .Weeks.SelectMany(w => w.Days).ToDictionary(d => d.Date);
        Assert.True(done[D(2026, 10, 15)].IsRangeEnd);
        Assert.True(done[D(2026, 10, 10)].InRange);
    }

    [Fact]
    public void Keyboard_moves_and_clamps_month_ends()
    {
        var o = new CalendarOptions();
        Assert.Equal(D(2026, 2, 28), CalendarModel.Navigate(D(2026, 1, 31), CalendarKey.PageDown, false, o));
        Assert.Equal(D(2027, 2, 28), CalendarModel.Navigate(D(2028, 2, 29), CalendarKey.PageUp, true, o));
        Assert.Equal(D(2026, 10, 12), CalendarModel.Navigate(D(2026, 10, 14), CalendarKey.Home, false, o)); // Monday
        Assert.Equal(D(2026, 10, 18), CalendarModel.Navigate(D(2026, 10, 14), CalendarKey.End, false, o));  // Sunday
    }

    [Fact]
    public void Keyboard_skips_disabled_days_and_respects_bounds()
    {
        var weekdaysOnly = new CalendarOptions { IsDateDisabled = d => d.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday };
        Assert.Equal(D(2026, 10, 19), CalendarModel.Navigate(D(2026, 10, 16), CalendarKey.Right, false, weekdaysOnly));

        var bounded = new CalendarOptions { Min = D(2026, 10, 5), Max = D(2026, 10, 20) };
        Assert.Equal(D(2026, 10, 5), CalendarModel.Navigate(D(2026, 10, 6), CalendarKey.Up, false, bounded));
        Assert.Equal(D(2026, 10, 20), CalendarModel.Navigate(D(2026, 10, 6), CalendarKey.PageDown, false, bounded));

        var nothing = new CalendarOptions { IsDateDisabled = _ => true };
        Assert.Equal(D(2026, 10, 6), CalendarModel.Navigate(D(2026, 10, 6), CalendarKey.Right, false, nothing));
    }

    [Fact]
    public void Range_picking_state_machine()
    {
        var r = CalendarModel.PickRange(null, D(2026, 10, 10));
        Assert.Equal(new DateRange(D(2026, 10, 10), null), r);
        r = CalendarModel.PickRange(r, D(2026, 10, 2));
        Assert.Equal(new DateRange(D(2026, 10, 2), D(2026, 10, 10)), r);
        r = CalendarModel.PickRange(r, D(2026, 11, 1));
        Assert.Equal(new DateRange(D(2026, 11, 1), null), r);
    }

    [Theory]
    [InlineData(DatePresetKind.Last7Days, "2026-09-30", "2026-10-06")]
    [InlineData(DatePresetKind.LastMonth, "2026-09-01", "2026-09-30")]
    [InlineData(DatePresetKind.ThisMonth, "2026-10-01", "2026-10-31")]
    public void Presets_resolve_relative_to_today(DatePresetKind kind, string start, string end)
    {
        var r = CalendarModel.Resolve(kind, D(2026, 10, 6));
        Assert.Equal(DateOnly.Parse(start), r.Start);
        Assert.Equal(DateOnly.Parse(end), r.End);
    }

    [Fact]
    public void Last_month_in_january_is_last_december() =>
        Assert.Equal(new DateRange(D(2025, 12, 1), D(2025, 12, 31)), CalendarModel.Resolve(DatePresetKind.LastMonth, D(2026, 1, 15)));

    [Theory]
    [InlineData("10/6/2026", "M/d/yyyy", "2026-10-06")]
    [InlineData("31.12.2026", "dd.MM.yyyy", "2026-12-31")]
    [InlineData("2026-10-06", "dd.MM.yyyy", "2026-10-06")]
    [InlineData("9/3/49", "d/M/yy", "2049-03-09")]
    [InlineData("9/3/50", "d/M/yy", "1950-03-09")]
    public void Parses_typed_dates(string input, string pattern, string expected) =>
        Assert.Equal(DateOnly.Parse(expected), DateText.Parse(input, pattern));

    [Theory]
    [InlineData("2/30/2026")]
    [InlineData("13/1/2026")]
    [InlineData("10/6")]
    [InlineData("")]
    [InlineData("tomorrow")]
    public void Rejects_invalid_dates(string input) => Assert.Null(DateText.Parse(input, "M/d/yyyy"));

    [Fact]
    public void Formats_with_patterns()
    {
        Assert.Equal("1/5/2026", DateText.Format(D(2026, 1, 5), "M/d/yyyy"));
        Assert.Equal("09/03/07", DateText.Format(D(2007, 3, 9), "dd/MM/yy"));
    }

    [Fact]
    public void Culture_helpers()
    {
        Assert.Equal(DayOfWeek.Sunday, CalendarModel.FirstDayOfWeek(new System.Globalization.CultureInfo("en-US")));
        Assert.Equal("M/d/yyyy", DateText.ShortPattern(new System.Globalization.CultureInfo("en-US")));
        Assert.Equal(DateOnly.Parse("2026-10-06"), DateText.Parse("6.10.2026", DateText.ShortPattern(new System.Globalization.CultureInfo("de-DE"))));
    }
}

public class ListNavigationTests
{
    private static readonly bool[] Mixed = [true, false, false, true, false, true];

    [Theory]
    [InlineData(-1, ListKey.Next, 1)]
    [InlineData(-1, ListKey.Previous, 4)]
    [InlineData(1, ListKey.Next, 2)]
    [InlineData(2, ListKey.Next, 4)]
    [InlineData(4, ListKey.Next, 4)]
    [InlineData(1, ListKey.Previous, 1)]
    [InlineData(2, ListKey.First, 1)]
    [InlineData(2, ListKey.Last, 4)]
    public void Skips_disabled_without_wrapping(int current, ListKey key, int expected) =>
        Assert.Equal(expected, ListNavigator.Move(current, key, Mixed));

    [Theory]
    [InlineData(4, ListKey.Next, 1)]
    [InlineData(1, ListKey.Previous, 4)]
    public void Wraps_when_enabled(int current, ListKey key, int expected) =>
        Assert.Equal(expected, ListNavigator.Move(current, key, Mixed, new ListNavigationOptions { Wrap = true }));

    [Fact]
    public void Paging_lands_on_the_nearest_enabled_item()
    {
        bool[] items = Enumerable.Range(0, 30).Select(i => i == 10).ToArray();
        Assert.Equal(11, ListNavigator.Move(0, ListKey.PageDown, items));
        Assert.Equal(29, ListNavigator.Move(25, ListKey.PageDown, items));
        Assert.Equal(0, ListNavigator.Move(5, ListKey.PageUp, items));
    }

    [Fact]
    public void Nothing_enabled_returns_minus_one()
    {
        Assert.Equal(-1, ListNavigator.Move(0, ListKey.Next, [true, true]));
        Assert.Equal(-1, ListNavigator.Move(0, ListKey.Next, []));
    }

    [Fact]
    public void Typeahead_extends_cycles_and_resets()
    {
        string[] labels = ["Apple", "Apricot", "Banana", "Blueberry", "Cherry"];
        var t = new Typeahead(500);
        Assert.Equal(0, t.Search("a", 0, labels, -1));
        Assert.Equal(0, t.Search("p", 100, labels, 0));
        Assert.Equal(1, t.Search("r", 200, labels, 0));
        Assert.Equal(2, t.Search("b", 1000, labels, 1)); // timed out: fresh search
        Assert.Equal(3, t.Search("b", 1100, labels, 2)); // same letter cycles
        Assert.Equal(2, t.Search("b", 1200, labels, 3));
        t.Reset();
        Assert.Equal("", t.Buffer);
    }

    [Fact]
    public void Folding_is_case_and_diacritic_insensitive()
    {
        Assert.Equal("angstrom", TextFolding.Fold("Ångström"));
        Assert.Equal("creme brulee", TextFolding.Fold("CRÈME BRÛLÉE"));
    }

    [Fact]
    public void Filter_ranks_label_start_then_word_start_then_substring()
    {
        string[] labels = ["Grind", "data-grid", "DataGrid Pro", "Grid"];
        var r = OptionFilter.Filter(labels, "grid");
        Assert.Equal([3, 1, 2], r.Select(m => m.Index));
        Assert.Equal([3, 2, 1], r.Select(m => m.Score));
        Assert.Equal(new TextRange(5, 4), Assert.Single(r[1].Ranges));
    }

    [Fact]
    public void Filter_requires_every_term_and_maps_ranges_to_the_original_text()
    {
        string[] labels = ["Café Crème", "crème brûlée", "Cafe"];
        var r = OptionFilter.Filter(labels, "cafe creme");
        var m = Assert.Single(r);
        Assert.Equal(0, m.Index);
        Assert.Equal([new TextRange(0, 4), new TextRange(5, 5)], m.Ranges);
    }

    [Fact]
    public void Filter_handles_surrogate_pairs()
    {
        var m = Assert.Single(OptionFilter.Filter(["emoji 😀 icons"], "😀"));
        Assert.Equal(new TextRange(6, 2), Assert.Single(m.Ranges));
    }

    [Fact]
    public void Empty_query_returns_everything_in_order() =>
        Assert.Equal([0, 1], OptionFilter.Filter(["b", "a"], "  ").Select(m => m.Index));

    [Fact]
    public void Group_keeps_first_appearance_order()
    {
        var g = OptionFilter.Group(["B", "A", "B"]);
        Assert.Equal(["B", "A"], g.Select(x => x.Key));
        Assert.Equal([0, 2], g[0].Indices);
    }
}

public class TreeModelTests
{
    private static TreeModel<ComponentsFixtureTests.Node> Tree() => ComponentsFixtureTests.Tree();

    private static HashSet<string> Set(params string[] ids) => new(ids);

    [Fact]
    public void Flatten_follows_expansion()
    {
        var t = Tree();
        Assert.Equal(["src", "docs", "license"], t.Flatten(Set()).Select(r => r.Id));
        var rows = t.Flatten(Set("src", "core"));
        Assert.Equal(["src", "core", "tokens", "queue", "stack", "web", "wpf", "docs", "license"], rows.Select(r => r.Id));
        var tokens = rows.Single(r => r.Id == "tokens");
        Assert.Equal((2, "core", 1, 3), (tokens.Depth, tokens.ParentId, tokens.PositionInSet, tokens.SetSize));
    }

    [Fact]
    public void Lazy_nodes_report_children_until_loaded()
    {
        var t = Tree();
        Assert.True(t.HasChildren("wpf"));
        Assert.Equal(TreeLoadState.NotLoaded, t.LoadState("wpf"));
        Assert.True(t.BeginLoad("wpf"));
        Assert.False(t.BeginLoad("wpf"));
        Assert.Equal(TreeLoadState.Loading, t.LoadState("wpf"));

        t.FailLoad("wpf");
        Assert.True(t.BeginLoad("wpf")); // retry after failure

        t.CompleteLoad("wpf", [new ComponentsFixtureTests.Node("theme-xaml", "SlateTheme.xaml")]);
        Assert.Equal(TreeLoadState.Loaded, t.LoadState("wpf"));
        Assert.Contains("theme-xaml", t.Flatten(Set("src", "wpf")).Select(r => r.Id));

        t.CompleteLoad("license", []);
        Assert.False(t.HasChildren("license"));
    }

    [Fact]
    public void Filter_keeps_ancestors_and_expands_them()
    {
        var (visible, expand) = Tree().Filter(n => n.Label.Contains("grid", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(Set("src", "web", "grid", "docs", "design", "grid-doc"), visible);
        Assert.Equal(Set("src", "web", "docs", "design"), expand);
    }

    [Fact]
    public void Right_expands_then_enters_and_left_collapses_then_exits()
    {
        var t = Tree();
        var a = t.Navigate("src", TreeKey.Right, Set());
        Assert.Equal(("src", true), (a.FocusId, a.Expanded.Contains("src")));
        var b = t.Navigate("src", TreeKey.Right, a.Expanded);
        Assert.Equal("core", b.FocusId);
        var c = t.Navigate("core", TreeKey.Left, b.Expanded);
        Assert.Equal("src", c.FocusId);
        var d = t.Navigate("src", TreeKey.Left, c.Expanded);
        Assert.False(d.Expanded.Contains("src"));
        Assert.Equal("license", t.Navigate("license", TreeKey.Right, Set()).FocusId);
    }

    [Fact]
    public void Asterisk_expands_siblings_with_children()
    {
        var r = Tree().Navigate("core", TreeKey.ExpandSiblings, Set("src"));
        Assert.Equal(Set("src", "core", "web", "wpf"), r.Expanded);
    }

    [Fact]
    public void Checkboxes_propagate_both_ways()
    {
        var t = Tree();
        var s = t.ToggleCheck("tokens", Set());
        Assert.Equal(CheckState.Indeterminate, t.GetCheckState("core", s));
        s = t.ToggleCheck("queue", s);
        s = t.ToggleCheck("stack", s);
        Assert.Equal(CheckState.Checked, t.GetCheckState("core", s));
        Assert.Contains("core", s);

        s = t.ToggleCheck("docs", s);
        Assert.Equal(CheckState.Checked, t.GetCheckState("color", s));
        s = t.ToggleCheck("color", s);
        Assert.Equal(CheckState.Indeterminate, t.GetCheckState("docs", s));
        Assert.DoesNotContain("docs", s);
    }

    [Fact]
    public void Duplicate_ids_are_rejected() =>
        Assert.Throws<ArgumentException>(() => new TreeModel<ComponentsFixtureTests.Node>(
            [new("a", "A"), new("a", "B")], n => n.Id, n => n.Children));
}

public class WidgetMathTests
{
    private static string Pages(int page, int count, int siblings = 1, int boundaries = 1) =>
        string.Join(" ", PaginationRange.Compute(page, count, siblings, boundaries));

    [Theory]
    [InlineData(1, 5, "1 2 3 4 5")]
    [InlineData(1, 10, "1 2 3 4 5 … 10")]
    [InlineData(5, 10, "1 … 4 5 6 … 10")]
    [InlineData(10, 10, "1 … 6 7 8 9 10")]
    [InlineData(4, 10, "1 2 3 4 5 … 10")]
    public void Pagination_slots(int page, int count, string expected) => Assert.Equal(expected, Pages(page, count));

    [Fact]
    public void Pagination_slot_count_is_constant()
    {
        var counts = Enumerable.Range(1, 40).Select(p => PaginationRange.Compute(p, 40).Count).Distinct();
        Assert.Equal([7], counts);
        Assert.Empty(PaginationRange.Compute(1, 0));
    }

    [Fact]
    public void Page_size_change_keeps_the_first_item_visible()
    {
        Assert.Equal(1, PaginationRange.PageForFirstItem(3, 10, 25)); // item 21 is on page 1 of 25
        Assert.Equal(2, PaginationRange.PageForFirstItem(5, 10, 25)); // item 41 is on page 2
        Assert.Equal(11, PaginationRange.PageForFirstItem(5, 25, 10));
        Assert.Equal(40, PaginationRange.PageCount(999, 25));
        Assert.Equal(1, PaginationRange.PageCount(0, 25));
    }

    [Theory]
    [InlineData("Aaron Griffin", "AG")]
    [InlineData("aaron", "A")]
    [InlineData("jo.marsh@slate.dev", "JM")]
    [InlineData("Mary Anne de la Cruz", "MC")]
    [InlineData("Jo Marsh-Smith", "JM")]
    [InlineData("x_y_z", "XZ")]
    [InlineData("@handle", "H")]
    [InlineData("", "?")]
    [InlineData(null, "?")]
    [InlineData("😀 Party", "😀P")]
    [InlineData("Éloïse Dubois", "ÉD")]
    public void Avatar_initials(string? name, string expected) => Assert.Equal(expected, AvatarText.Initials(name));

    [Fact]
    public void Avatar_tone_is_stable_and_case_insensitive()
    {
        var spread = Enumerable.Range(0, 500).Select(i => AvatarText.ToneFor($"user {i}")).GroupBy(t => t).ToDictionary(g => g.Key, g => g.Count());
        Assert.Equal(5, spread.Count);
        Assert.All(spread.Values, n => Assert.InRange(n, 60, 140)); // roughly even over 5 tones
        Assert.Equal(AvatarText.ToneFor("Aaron Griffin"), AvatarText.ToneFor("  aaron griffin "));
        Assert.NotEqual(Tone.Neutral, AvatarText.ToneFor("x"));
    }

    [Theory]
    [InlineData(37, 0, 100, 5, 35)]
    [InlineData(37.5, 0, 100, 5, 40)]
    [InlineData(0.3, 0, 1, 0.1, 0.3)]
    [InlineData(0.35, 0, 1, 0.1, 0.4)]
    [InlineData(150, 0, 100, 1, 100)]
    [InlineData(10, 0, 10, 3, 9)]
    [InlineData(7, 0, 10, 0, 7)]
    public void Slider_snaps(double v, double min, double max, double step, double expected) =>
        Assert.Equal(expected, SliderMath.Snap(v, min, max, step));

    [Fact]
    public void Slider_keys_and_range_thumbs()
    {
        Assert.Equal(60, SliderMath.Key(50, SliderKey.PageIncrease, 0, 100, 1));
        Assert.Equal(100, SliderMath.Key(50, SliderKey.End, 0, 100, 1));
        Assert.Equal(0.51, SliderMath.Key(0.5, SliderKey.Increase, 0, 1, 0.01));
        Assert.Equal((80, 80), SliderMath.SetRangeThumb((20, 80), 0, 95, 0, 100, 5));
        Assert.Equal((20, 20), SliderMath.SetRangeThumb((20, 80), 1, 10, 0, 100, 5));
        Assert.Equal(0.5, SliderMath.ToFraction(0, -50, 50));
    }
}
