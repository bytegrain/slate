using System.Collections;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Input;
using Slate.Dates;

namespace Slate.Wpf.Tests;

/// <summary>Wave-2 controls on a live WPF tree: overlays, navigation, pickers, collections and their automation.</summary>
public class Wave2Tests
{
    private sealed record Fruit(string Name, string Group);

    private static readonly Fruit[] Fruits =
    [
        new("Apple", "Pome"), new("Pear", "Pome"), new("Cherry", "Stone"), new("Plum", "Stone"), new("Banana", "Berry"),
    ];

    private static Select RealizeSelect(Action<Select>? configure = null)
    {
        var s = new Select { Items = Fruits, ItemText = o => ((Fruit)o).Name, Label = "Fruit" };
        configure?.Invoke(s);
        return Wpf.Realize(s);
    }

    private static void Press(UIElement target, Key key)
    {
        var source = PresentationSource.FromVisual(target)!;
        var preview = new KeyEventArgs(Keyboard.PrimaryDevice, source, 0, key) { RoutedEvent = Keyboard.PreviewKeyDownEvent };
        target.RaiseEvent(preview);
        if (preview.Handled) return;
        target.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, source, 0, key) { RoutedEvent = Keyboard.KeyDownEvent });
    }

    // ---- overlays ----

    [Fact]
    public void Popup_positions_follow_the_shared_positioner()
    {
        var below = PopupPositions.Locate(PopoverPlacement.BottomStart, new Size(100, 50), new Size(80, 20), 6);
        Assert.Equal(new Point(0, 26), below);
        var above = PopupPositions.Locate(PopoverPlacement.Top, new Size(100, 50), new Size(80, 20), 6);
        Assert.Equal(-56, above.Y);
        Assert.Equal(-10, above.X);
        Assert.Equal(PopoverPlacement.TopEnd, PopupPositions.Opposite(PopoverPlacement.BottomEnd));
    }

    [Fact]
    public void Popover_opens_closes_on_escape_and_raises_open_changed() => Wpf.Run(() =>
    {
        var anchor = new Button { Content = "Open" };
        var p = Wpf.Realize(new Popover { Anchor = anchor, Content = new TextBox() });
        var changes = new List<bool>();
        p.OpenChanged += (_, e) => changes.Add(e.NewValue);

        anchor.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent, anchor));
        Assert.True(p.Open);
        var popup = (System.Windows.Controls.Primitives.Popup)p.Template.FindName(Popover.PartPopup, p);
        Assert.True(popup.IsOpen);
        Assert.Equal(System.Windows.Controls.Primitives.PlacementMode.Custom, popup.Placement);

        Press((UIElement)popup.Child, Key.Escape);
        Assert.False(p.Open);
        Assert.Equal([true, false], changes);
    });

    [Fact]
    public void Tooltip_uses_custom_placement_and_delay() => Wpf.Run(() =>
    {
        var target = Wpf.Realize(new Button { Content = "Save" });
        var tip = new Tooltip { Text = "Save file", Shortcut = "Ctrl+S", TooltipPlacement = PopoverPlacement.Bottom, Delay = 200 };
        target.ToolTip = tip;
        target.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice, 0) { RoutedEvent = Mouse.MouseEnterEvent });
        Assert.Equal(System.Windows.Controls.Primitives.PlacementMode.Custom, tip.Placement);
        Assert.Equal(200, ToolTipService.GetInitialShowDelay(target));
        tip.IsEnabled = false;
        target.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice, 0) { RoutedEvent = Mouse.MouseEnterEvent });
        Assert.False(ToolTipService.GetIsEnabled(target));
    });

    [Fact]
    public void Menu_opens_from_its_trigger_and_can_be_a_context_menu() => Wpf.Run(() =>
    {
        var trigger = new Button { Content = "Actions" };
        var menu = new Menu { Trigger = trigger };
        menu.Items.Add(new MenuItem { Header = "Rename" });
        menu.Items.Add(new MenuItem { Header = "Delete" });
        Wpf.Realize(menu);

        trigger.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent, trigger));
        Assert.True(menu.Open);
        Assert.True(menu.Popup.IsOpen);
        menu.Open = false;
        Assert.False(menu.Popup.IsOpen);

        menu.AsContextMenu = true;
        Wpf.Pump();
        Assert.Same(menu.Popup, trigger.ContextMenu);
    });

    // ---- navigation ----

    private static Tabs RealizeTabs(Action<Tabs>? configure = null)
    {
        var tabs = new Tabs();
        tabs.Items.Add(new Tab { Key = "general", Label = "General", Content = new TextBox() });
        tabs.Items.Add(new Tab { Key = "billing", Label = "Billing", Badge = "2", Content = new TextBox() });
        tabs.Items.Add(new Tab { Key = "logs", Label = "Logs", Closable = true, Content = new TextBox() });
        configure?.Invoke(tabs);
        return Wpf.Realize(tabs);
    }

    [Fact]
    public void Tabs_value_tracks_the_selected_key() => Wpf.Run(() =>
    {
        var tabs = RealizeTabs();
        string? raised = null;
        tabs.ValueChanged += (_, e) => raised = e.NewValue;

        tabs.Value = "billing";
        Assert.Equal(1, tabs.SelectedIndex);
        tabs.SelectedIndex = 2;
        Assert.Equal("logs", tabs.Value);
        Assert.Equal("logs", raised);
    });

    [Fact]
    public void Tabs_push_variant_size_and_direction() => Wpf.Run(() =>
    {
        var tabs = RealizeTabs(t => { t.Variant = TabsVariant.Pills; t.Size = ControlSize.Small; t.Direction = Direction.Column; });
        var tab = (Tab)tabs.Items[0];
        Assert.Equal(TabsVariant.Pills, tab.ActualVariant);
        Assert.Equal(ControlSize.Small, Sl.GetActualSize(tab));
        Assert.Equal(Dock.Left, tabs.TabStripPlacement);
    });

    [Fact]
    public void Closing_a_tab_removes_it_and_keep_alive_keeps_visited_content() => Wpf.Run(() =>
    {
        var tabs = RealizeTabs(t => t.KeepAlive = true);
        var first = (TextBox)((Tab)tabs.Items[0]).Content;
        tabs.Value = "billing";
        Wpf.Pump();
        Assert.True(first.IsLoaded, "visited content stays in the tree");
        Assert.False(first.IsVisible, "but hidden while another tab is selected");

        ((Tab)tabs.Items[2]).Close();
        Assert.Equal(2, tabs.Items.Count);
    });

    [Fact]
    public void Breadcrumbs_collapse_the_middle_into_an_ellipsis()
    {
        var items = new[] { "Home", "Projects", "Slate", "Docs", "Tabs" }.Select(l => new BreadcrumbItem(l)).ToList();
        var entries = Breadcrumbs.Layout(items, 3);
        Assert.Equal(["Home", "…", "Docs", "Tabs"], entries.Select(e => e.Label));
        Assert.True(entries[1].IsEllipsis);
        Assert.Equal(["Projects", "Slate"], entries[1].Hidden.Select(i => i.Label));
        Assert.True(entries[^1].IsCurrent);
        Assert.False(entries[0].ShowSeparator);
    }

    [Fact]
    public void Pagination_computes_pages_range_text_and_keeps_the_first_item() => Wpf.Run(() =>
    {
        var p = Wpf.Realize(new Pagination { TotalCount = 1234, PageSize = 50, PageSizes = [25, 50, 100] });
        Assert.Equal(25, p.ActualPageCount);
        Assert.False(p.CanGoPrevious);
        p.Page = 10;
        Assert.Equal("451–500 of 1,234", p.RangeText);
        Assert.Contains(p.Pages, e => e.IsEllipsis);
        Assert.Single(p.Pages, e => e.IsCurrent);

        p.PageSize = 100;
        Assert.Equal(5, p.Page); // item 451 lives on page 5 of 100
        p.Page = 99;
        Assert.Equal(13, p.Page); // coerced
        Assert.False(p.CanGoNext);
    });

    [Fact]
    public void Segmented_control_selects_by_value_and_arrows() => Wpf.Run(() =>
    {
        var s = Wpf.Realize(new SegmentedControl { Items = new[] { "Day", "Week", "Month" }, Value = "Week" });
        Assert.Equal("Week", s.SelectedSegment?.Text);
        var list = (ListBox)s.Template.FindName("PART_List", s);
        Press(list, Key.Right);
        Assert.Equal("Month", s.Value);
        Press(list, Key.Right);
        Assert.Equal("Day", s.Value); // wraps
    });

    // ---- widgets ----

    [Fact]
    public void Slider_snaps_supports_ranges_and_reports_as_slider() => Wpf.Run(() =>
    {
        var s = Wpf.Realize(new Slider { Minimum = 0, Maximum = 100, Step = 5, Value = 20, Label = "Volume" });
        var thumb = (UIElement)s.Template.FindName("PART_Thumb", s);
        Press(thumb, Key.Right);
        Assert.Equal(25, s.Value);
        Press(thumb, Key.End);
        Assert.Equal(100, s.Value);
        Assert.False(s.IsRange);

        s.Value = 10;
        s.RangeEnd = 60;
        Assert.True(s.IsRange);
        Assert.Contains("60", s.ValueText);
        Assert.Equal(AutomationControlType.Slider, UIElementAutomationPeer.CreatePeerForElement(s).GetAutomationControlType());
    });

    [Fact]
    public void Avatar_derives_initials_tone_and_name() => Wpf.Run(() =>
    {
        var a = Wpf.Realize(new Avatar { DisplayName = "Ada Lovelace", Status = AvatarStatus.Online });
        Assert.Equal("AL", a.Initials);
        Assert.NotNull(Sl.GetChromeBackground(a));
        Assert.Contains("Ada Lovelace", AutomationProperties.GetName(a));
    });

    [Fact]
    public void Skeleton_lines_shorten_the_last_and_hide_from_automation() => Wpf.Run(() =>
    {
        var s = Wpf.Realize(new Skeleton { Lines = 3 });
        Assert.Equal([1.0, 1.0, 0.6], s.LineWidths);
        var peer = UIElementAutomationPeer.CreatePeerForElement(s);
        Assert.False(peer.IsControlElement());
        Assert.False(peer.IsContentElement());
    });

    // ---- select ----

    [Fact]
    public void Select_chooses_a_single_value() => Wpf.Run(() =>
    {
        var s = RealizeSelect();
        object? raised = null;
        s.ValueChanged += (_, e) => raised = e.NewValue;
        s.IsDropDownOpen = true;
        s.Choose(s.Options.First(o => o.Text == "Plum"));
        Assert.Equal("Plum", ((Fruit)s.Value!).Name);
        Assert.Same(s.Value, raised);
        Assert.False(s.IsDropDownOpen);
        Assert.Equal("Plum", s.DisplayText);
        Assert.True(s.Options.Single(o => o.Text == "Plum").IsSelected);
    });

    [Fact]
    public void Select_multiple_shows_chips_with_overflow() => Wpf.Run(() =>
    {
        var s = RealizeSelect(x => { x.Multiple = true; x.MaxVisibleChips = 2; });
        foreach (var name in new[] { "Apple", "Pear", "Plum" })
            s.Choose(s.Options.First(o => o.Text == name));
        Assert.Equal(3, s.Values!.Count);
        Assert.Equal(2, s.Chips.Count);
        Assert.Equal("+1", s.OverflowText);
        s.Choose(s.Options.First(o => o.Text == "Pear")); // toggles off
        Assert.Equal(2, s.Values!.Count);
    });

    [Fact]
    public void Select_searches_groups_and_creates() => Wpf.Run(() =>
    {
        var s = RealizeSelect(x => { x.Searchable = true; x.Creatable = true; x.GroupBy = o => ((Fruit)o).Group; });
        Assert.Equal(["Pome", "Stone", "Berry"], s.Options.Where(o => o.IsHeader).Select(o => o.Text));

        s.SearchText = "pl";
        Assert.Contains(s.Options, o => o.Text == "Plum");
        Assert.DoesNotContain(s.Options, o => o.Text == "Pear");
        Assert.True(s.IsDropDownOpen);

        s.SearchText = "Kiwi";
        var create = Assert.Single(s.Options, o => o.IsCreate);
        s.Choose(create);
        Assert.Equal("Kiwi", s.Value);
    });

    [Fact]
    public void Select_keyboard_and_automation() => Wpf.Run(() =>
    {
        var s = RealizeSelect(x => x.Clearable = true);
        Press(s, Key.Down);
        Assert.True(s.IsDropDownOpen);
        Press(s, Key.Down);
        Press(s, Key.Enter);
        Assert.Equal("Pear", s.DisplayText);
        Assert.True(s.ShowClear);

        var peer = UIElementAutomationPeer.CreatePeerForElement(s);
        Assert.Equal(AutomationControlType.ComboBox, peer.GetAutomationControlType());
        var expand = (IExpandCollapseProvider)peer.GetPattern(PatternInterface.ExpandCollapse);
        expand.Expand();
        Assert.True(s.IsDropDownOpen);
        Assert.Equal("Pear", ((IValueProvider)peer.GetPattern(PatternInterface.Value)).Value);

        s.Clear();
        Assert.Null(s.Value);
        s.Error = "Required";
        Assert.True(s.HasError);
        Assert.Equal("Required", AutomationProperties.GetHelpText(s));
    });

    // ---- dates ----

    private static readonly DateOnly Today = new(2026, 3, 18);

    [Fact]
    public void Date_picker_parses_typed_dates_and_rejects_disabled_ones() => Wpf.Run(() =>
    {
        var p = Wpf.Realize(new DatePicker { Format = "yyyy-MM-dd", Today = Today, Max = new DateOnly(2026, 12, 31), DisabledDates = d => d.DayOfWeek == DayOfWeek.Sunday });
        p.InputText = "2026-03-17";
        Assert.True(p.Commit());
        Assert.Equal(new DateOnly(2026, 3, 17), p.Value);

        p.InputText = "2026-03-22"; // a Sunday
        Assert.False(p.Commit());
        Assert.Equal("2026-03-17", p.InputText);

        p.InputText = "2027-01-01"; // past Max
        Assert.False(p.Commit());
        p.InputText = "nonsense";
        Assert.False(p.Commit());
        Assert.Equal(new DateOnly(2026, 3, 17), p.Value);
    });

    [Fact]
    public void Calendar_picks_ranges_and_presets_flow_to_the_picker() => Wpf.Run(() =>
    {
        var p = Wpf.Realize(new DatePicker { Selection = DateSelection.Range, Inline = true, Today = Today, Format = "yyyy-MM-dd" });
        var cal = Wpf.Find<CalendarView>(p, c => c.IsVisible);
        cal.Pick(new DateOnly(2026, 3, 10));
        Assert.Null(p.Range); // half a range isn't committed
        cal.Pick(new DateOnly(2026, 3, 14));
        Assert.Equal(new DateRange(new DateOnly(2026, 3, 10), new DateOnly(2026, 3, 14)), p.Range);
        Assert.Equal("2026-03-10 – 2026-03-14", p.InputText);

        cal.ApplyPreset(new DatePreset("Last 7 days", DatePresetKind.Last7Days));
        Assert.Equal(Today, p.Range!.Value.End);
        Assert.Equal(6, Today.DayNumber - p.Range!.Value.Start.DayNumber);
    });

    [Fact]
    public void Calendar_grid_marks_today_selected_and_disabled_days() => Wpf.Run(() =>
    {
        var cal = Wpf.Realize(new CalendarView { Today = Today, SelectedDate = Today, FocusedDate = Today, Min = new DateOnly(2026, 3, 5), FirstDayOfWeek = DayOfWeek.Monday });
        Assert.Equal(6, cal.Weeks.Count);
        Assert.Equal(7, cal.Weekdays.Count);
        var days = cal.Weeks.SelectMany(w => w.Days).ToList();
        var today = days.Single(d => d.Date == Today);
        Assert.True(today.Day.IsToday && today.Day.IsSelected && today.IsFocused);
        Assert.True(days.Single(d => d.Date == new DateOnly(2026, 3, 4)).Day.IsDisabled);
        Assert.Contains("selected", today.AccessibleName);
        Assert.Equal(DayOfWeek.Monday, days[0].Date.DayOfWeek);

        cal.ShowMonth(1);
        Assert.Equal(4, cal.FocusedDate.Month);
    });

    [Fact]
    public void Date_picker_automation_expands_and_sets_value() => Wpf.Run(() =>
    {
        var p = Wpf.Realize(new DatePicker { Format = "yyyy-MM-dd", Today = Today, Label = "Due" });
        var peer = UIElementAutomationPeer.CreatePeerForElement(p);
        ((IValueProvider)peer.GetPattern(PatternInterface.Value)).SetValue("2026-04-01");
        Assert.Equal(new DateOnly(2026, 4, 1), p.Value);
        ((IExpandCollapseProvider)peer.GetPattern(PatternInterface.ExpandCollapse)).Expand();
        Assert.True(p.IsDropDownOpen);
    });

    // ---- tree ----

    private sealed class Node(string name, params Node[] children)
    {
        public string Name { get; } = name;
        public Node[] Children { get; } = children;
        public override string ToString() => Name;
    }

    private static readonly Node Docs = new("docs", new Node("intro.md"), new Node("api", new Node("select.md"), new Node("tree.md")));
    private static readonly Node Src = new("src", new Node("app.cs"));

    private static TreeView RealizeTree(Action<TreeView>? configure = null)
    {
        var t = new TreeView { Items = new[] { Docs, Src }, ChildrenSelector = n => ((Node)n).Children, ItemText = n => ((Node)n).Name };
        configure?.Invoke(t);
        return Wpf.Realize(t);
    }

    [Fact]
    public void Tree_flattens_expands_and_reports_set_positions() => Wpf.Run(() =>
    {
        var t = RealizeTree();
        Assert.Equal(["docs", "src"], t.Rows.Select(r => r.Text));
        t.Toggle(Docs);
        Assert.Equal(["docs", "intro.md", "api", "src"], t.Rows.Select(r => r.Text));
        var api = t.Rows.Single(r => r.Text == "api");
        Assert.Equal(2, api.Level);
        Assert.Equal((2, 2), (api.PositionInSet, api.SetSize));
        Assert.Contains(Docs, t.Expanded!.Cast<object>());
    });

    [Fact]
    public void Tree_multi_selection_and_checkbox_tristate() => Wpf.Run(() =>
    {
        var t = RealizeTree(x => { x.SelectionMode = TreeSelectionMode.Multi; x.Expanded = new ArrayList { Docs }; });
        t.Click(Docs);
        t.Click(Src, ModifierKeys.Shift);
        Assert.Equal(4, t.SelectedItems!.Count);
        t.Click(Docs, ModifierKeys.Control);
        Assert.Equal(3, t.SelectedItems!.Count);

        t.SelectionMode = TreeSelectionMode.Checkbox;
        t.Toggle(Docs.Children[1]);
        t.Click(Docs.Children[1].Children[0]);
        Assert.Null(t.Rows.Single(r => r.Text == "api").IsChecked);
        Assert.Null(t.Rows.Single(r => r.Text == "docs").IsChecked);
    });

    [Fact]
    public void Tree_filter_keeps_ancestors_and_lazy_children_load() => Wpf.Run(() =>
    {
        var t = RealizeTree(x => x.Filter = "tree");
        Assert.Equal(["docs", "api", "tree.md"], t.Rows.Select(r => r.Text));

        var lazy = RealizeTree(x =>
        {
            x.ChildrenSelector = null;
            x.HasChildren = n => ((Node)n).Children.Length > 0;
            x.LoadChildren = n => Task.FromResult<IEnumerable>(((Node)n).Children);
        });
        lazy.Toggle(Src);
        Wpf.Pump();
        Assert.Equal(["docs", "src", "app.cs"], lazy.Rows.Select(r => r.Text));
    });

    [Fact]
    public void Tree_rows_are_tree_items_for_automation() => Wpf.Run(() =>
    {
        var t = RealizeTree();
        var list = (ListBox)t.Template.FindName("PART_List", t);
        var listPeer = UIElementAutomationPeer.CreatePeerForElement(list);
        Assert.Equal(AutomationControlType.Tree, listPeer.GetAutomationControlType());
        var rows = listPeer.GetChildren();
        var docs = rows.First(r => r.GetName() == "docs");
        Assert.Equal(AutomationControlType.TreeItem, docs.GetAutomationControlType());
        var expand = (IExpandCollapseProvider)docs.GetPattern(PatternInterface.ExpandCollapse);
        Assert.Equal(ExpandCollapseState.Collapsed, expand.ExpandCollapseState);
        expand.Expand();
        Assert.Equal(4, t.Rows.Count);
    });
}
