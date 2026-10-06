using Avalonia;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;
using Slate.Avalonia.Controls;
using Slate.Collections;
using Slate.Dates;
using static Slate.Avalonia.Tests.TestHelpers;
using AvSlider = Avalonia.Controls.Slider;

namespace Slate.Avalonia.Tests;

internal static class Wave2Helpers
{
    public static void Type(this TopLevel top, string text)
    {
        global::Avalonia.Headless.HeadlessWindowExtensions.KeyTextInput(top, text);
        Pump(top);
    }

    public static Window Host(Control content, double width = 800, double height = 600)
    {
        var window = Show(new Panel { Children = { content } }, width, height);
        return window;
    }

    public static readonly string[] Fruits = ["Apple", "Apricot", "Banana", "Blueberry", "Cherry", "Clémentine", "Date"];
}

public class SelectTests
{
    private static StackPanel Options(Select select) =>
        ((Control)select.Part<global::Avalonia.Controls.Primitives.Popup>("PART_Popup").Child!).Part<StackPanel>("PART_Options");

    private static Select Fruit(bool searchable = false) => new() { Items = Wave2Helpers.Fruits, Label = "Fruit", Placeholder = "Pick one", Searchable = searchable };

    [AvaloniaFact]
    public void Placeholder_shows_until_a_value_is_chosen()
    {
        var select = Fruit();
        var w = Wave2Helpers.Host(select);
        Assert.True(select.Part<TextBlock>("PART_Placeholder").IsEffectivelyVisible);
        select.Value = "Cherry";
        Pump(w);
        Assert.Equal("Cherry", select.DisplayText);
        Assert.False(select.Part<TextBlock>("PART_Placeholder").IsVisible);
        w.Close();
    }

    [AvaloniaFact]
    public void Keyboard_opens_moves_and_selects()
    {
        var select = Fruit();
        var w = Wave2Helpers.Host(select);
        var changed = 0;
        select.ValueChanged += (_, _) => changed++;
        select.Focus();

        w.Press(Key.Down);
        Assert.True(select.IsDropDownOpen);
        Assert.Equal(0, select.HighlightedIndex);

        w.Press(Key.Down);
        w.Press(Key.Down);
        w.Press(Key.Enter);
        Assert.Equal("Banana", select.Value);
        Assert.False(select.IsDropDownOpen);
        Assert.Equal(1, changed);

        w.Press(Key.Enter);
        Assert.Equal(2, select.HighlightedIndex); // reopening highlights the current value
        w.Press(Key.End);
        Assert.Equal(Wave2Helpers.Fruits.Length - 1, select.HighlightedIndex);
        w.Press(Key.Escape);
        Assert.False(select.IsDropDownOpen);
        Assert.Equal("Banana", select.Value);
        w.Close();
    }

    [AvaloniaFact]
    public void Typeahead_selects_while_closed_and_cycles_on_repeat()
    {
        var select = Fruit();
        var w = Wave2Helpers.Host(select);
        select.Focus();
        w.Type("b");
        Assert.Equal("Banana", select.Value);
        w.Type("b");
        Assert.Equal("Blueberry", select.Value);
        w.Close();
    }

    [AvaloniaFact]
    public void Searchable_filters_folds_accents_and_highlights_matches()
    {
        var select = Fruit(searchable: true);
        var w = Wave2Helpers.Host(select);
        select.IsDropDownOpen = true;
        select.SearchText = "clem";
        Pump(w);

        var option = Assert.Single(select.VisibleOptions);
        Assert.Equal("Clémentine", option.Text);
        var block = Assert.IsType<TextBlock>(option.Content);
        Assert.Contains(block.Inlines!.OfType<Run>(), r => r.FontWeight == FontWeight.Bold && r.Text == "Clém");
        w.Close();
    }

    [AvaloniaFact]
    public void Search_shows_empty_content_when_nothing_matches()
    {
        var select = Fruit(searchable: true);
        var w = Wave2Helpers.Host(select);
        select.IsDropDownOpen = true;
        select.SearchText = "zzz";
        Pump(w);
        Assert.Empty(select.VisibleOptions);
        Assert.Contains(Options(select).Children, c => c.Classes.Contains("select-empty"));
        w.Close();
    }

    [AvaloniaFact]
    public void Creatable_offers_to_create_a_new_value()
    {
        var select = new Select { Items = Wave2Helpers.Fruits, Searchable = true, Creatable = true, Multiple = true };
        var w = Wave2Helpers.Host(select);
        select.IsDropDownOpen = true;
        select.SearchText = "Kiwi";
        Pump(w);

        var create = Assert.Single(select.VisibleOptions);
        Assert.True(create.IsCreate);
        Assert.Equal("Create “Kiwi”", create.Text);
        select.Focus();
        w.Press(Key.Enter);
        Assert.Equal(["Kiwi"], select.Values!.Cast<object>());
        w.Close();
    }

    [AvaloniaFact]
    public void Multiple_toggles_values_shows_chips_and_backspace_removes_the_last()
    {
        var select = new Select { Items = Wave2Helpers.Fruits, Multiple = true, MaxVisibleChips = 2 };
        var w = Wave2Helpers.Host(select);
        var changed = 0;
        select.ValuesChanged += (_, _) => changed++;
        select.Focus();
        w.Press(Key.Down);   // open, highlight Apple
        w.Press(Key.Enter);  // Apple
        w.Press(Key.Down);
        w.Press(Key.Enter);  // Apricot
        w.Press(Key.Down);
        w.Press(Key.Enter);  // Banana
        Assert.True(select.IsDropDownOpen); // multiple stays open
        Assert.Equal(["Apple", "Apricot", "Banana"], select.Values!.Cast<object>());
        Assert.Equal(3, changed);

        var chips = select.Part<WrapPanel>("PART_Chips").Children;
        Assert.Equal(3, chips.Count); // two chips + "+1"
        Assert.Equal("+1", Assert.IsType<Badge>(chips[^1]).Content);

        w.Press(Key.Back);
        Assert.Equal(["Apple", "Apricot"], select.Values!.Cast<object>());
        w.Close();
    }

    [AvaloniaFact]
    public void GroupBy_inserts_group_headers_in_first_appearance_order()
    {
        var select = new Select { Items = Wave2Helpers.Fruits, GroupBy = o => o!.ToString()![..1] };
        var w = Wave2Helpers.Host(select);
        select.IsDropDownOpen = true;
        Pump(w);
        var headers = Options(select).Children.OfType<TextBlock>().Where(t => t.Classes.Contains("select-group")).Select(t => t.Text);
        Assert.Equal(["A", "B", "C", "D"], headers);
        w.Close();
    }

    [AvaloniaFact]
    public void Clear_button_appears_with_a_value_and_clears_it()
    {
        var select = new Select { Items = Wave2Helpers.Fruits, Clearable = true };
        var w = Wave2Helpers.Host(select);
        var clear = select.Part<Button>("PART_Clear");
        Assert.False(clear.IsVisible);
        select.Value = "Date";
        Pump(w);
        Assert.True(clear.IsVisible);
        clear.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.Null(select.Value);
        w.Close();
    }

    [AvaloniaFact]
    public void Error_replaces_helper_and_marks_invalid()
    {
        var select = new Select { Items = Wave2Helpers.Fruits, HelperText = "Required", Error = "Pick a fruit" };
        var w = Wave2Helpers.Host(select);
        Assert.True(select.Classes.Contains(":invalid") || ((IPseudoClasses)select.Classes).Contains(":invalid"));
        Assert.False(select.Part<TextBlock>("PART_Helper").IsVisible);
        Assert.True(select.Part<StackPanel>("PART_ErrorRow").IsVisible);
        w.Close();
    }

    [AvaloniaFact]
    public void Automation_reports_a_combobox_with_expand_state()
    {
        var select = Fruit();
        var w = Wave2Helpers.Host(select);
        var peer = ControlAutomationPeer.CreatePeerForElement(select);
        Assert.Equal(AutomationControlType.ComboBox, peer.GetAutomationControlType());
        Assert.Equal("Fruit", peer.GetName());
        var expand = Assert.IsAssignableFrom<IExpandCollapseProvider>(peer);
        expand.Expand();
        Assert.True(select.IsDropDownOpen);
        w.Close();
    }

    [AvaloniaFact]
    public void Variant_size_and_radius_follow_field_defaults()
    {
        var select = new Select { Variant = FieldVariant.Filled, Size = ControlSize.Small, Radius = Radius.Full };
        var w = Wave2Helpers.Host(select);
        Assert.Contains("filled", select.Classes);
        Assert.Contains("small", select.Classes);
        Assert.Contains("radius-full", select.Classes);
        Assert.Equal(SlateTokens.Size.Control.Compact.Sm, select.Part<Border>("PART_Control").MinHeight);
        w.Close();
    }
}

public class PopoverAndMenuTests
{
    [AvaloniaFact]
    public void Anchor_click_opens_and_escape_closes_returning_focus()
    {
        var anchor = new Button { Content = "Filters" };
        var field = new TextBox();
        var popover = new Popover { Anchor = anchor, Content = field, Modal = true };
        var w = Wave2Helpers.Host(popover);
        var opened = 0;
        popover.OpenChanged += (_, _) => opened++;

        w.Click(anchor);
        Assert.True(popover.Open);
        Assert.True(popover.Part<global::Avalonia.Controls.Primitives.Popup>("PART_Popup").IsOpen);

        popover.Panel!.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Escape, Source = popover.Panel });
        Pump(w);
        Assert.False(popover.Open);
        Assert.Equal(anchor, w.Focused());
        Assert.Equal(2, opened);
        w.Close();
    }

    [AvaloniaFact]
    public void Slate_positioning_flips_when_there_is_no_room_below()
    {
        var anchor = new Button { Content = "Open" };
        var popover = new Popover
        {
            Anchor = anchor, Placement = PopoverPlacement.Bottom,
            Content = new Border { Width = 200, Height = 220 },
            VerticalAlignment = VerticalAlignment.Bottom, HorizontalAlignment = HorizontalAlignment.Center,
        };
        var w = Wave2Helpers.Host(popover, 600, 400);
        popover.Open = true;
        Pump(w);
        Pump(w);
        Assert.Equal(PopoverPlacement.Top, popover.ActualPlacement);
        w.Close();
    }

    [AvaloniaFact]
    public void Outside_click_dismissal_is_configurable()
    {
        var popover = new Popover { Anchor = new Button(), Content = "Hi", CloseOnOutsideClick = false };
        var w = Wave2Helpers.Host(popover);
        popover.Open = true;
        Pump(w);
        Assert.False(popover.Part<global::Avalonia.Controls.Primitives.Popup>("PART_Popup").IsLightDismissEnabled);
        w.Close();
    }

    [AvaloniaFact]
    public void Dropdown_menu_opens_from_its_trigger_and_syncs_open()
    {
        var trigger = new Button { Content = "Actions" };
        var rename = new MenuItem { Header = "Rename" };
        Slate.Avalonia.Sl.SetStartIcon(rename, "pencil");
        Slate.Avalonia.Sl.SetShortcut(rename, "F2");
        var delete = new MenuItem { Header = "Delete" };
        Slate.Avalonia.Sl.SetTone(delete, Tone.Danger);
        var menu = new DropdownMenu { Trigger = trigger, Items = { rename, new Separator(), delete } };
        var w = Wave2Helpers.Host(menu);

        w.Click(trigger);
        Assert.True(menu.Open);
        Assert.True(menu.Menu.IsOpen);
        Assert.IsType<Icon>(rename.Icon);
        Assert.Contains("tone-danger", delete.Classes);

        menu.Open = false;
        Pump(w);
        Assert.False(menu.Menu.IsOpen);

        trigger.Focus();
        w.Press(Key.Down);
        Assert.True(menu.Open);
        menu.Open = false;
        w.Close();
    }

    [AvaloniaFact]
    public void Context_menu_mode_attaches_to_the_trigger()
    {
        var trigger = new Border { Width = 100, Height = 40 };
        var menu = new DropdownMenu { Trigger = trigger, AsContextMenu = true, Items = { new MenuItem { Header = "Copy" } } };
        var w = Wave2Helpers.Host(menu);
        var presenter = menu.Part<global::Avalonia.Controls.Presenters.ContentPresenter>("PART_Trigger");
        Assert.Same(menu.Menu, presenter.ContextMenu);
        menu.AsContextMenu = false;
        Pump(w);
        Assert.Null(presenter.ContextMenu);
        w.Close();
    }

    [AvaloniaFact]
    public void Tooltip_sets_tip_with_shortcut_and_can_be_disabled()
    {
        var button = new Button { Content = "Copy" };
        var tip = new Tooltip { Text = "Copy link", Shortcut = "⌘C", Delay = 200, Child = button };
        var w = Wave2Helpers.Host(tip);
        var native = Assert.IsType<ToolTip>(ToolTip.GetTip(button));
        Assert.Equal("Copy link", native.Content);
        Assert.Equal("⌘C", Slate.Avalonia.Sl.GetShortcut(native));
        Assert.Equal(200, ToolTip.GetShowDelay(button));
        Assert.Equal(PlacementMode.Custom, ToolTip.GetPlacement(button));

        tip.Disabled = true;
        Assert.Null(ToolTip.GetTip(button));
        w.Close();
    }
}

public class TabsTests
{
    private static Tabs Build(out Tab overview, out Tab activity)
    {
        overview = new Tab { Key = "overview", Label = "Overview", Content = new TextBlock { Text = "A" } };
        activity = new Tab { Key = "activity", Label = "Activity", Badge = "3", Closable = true, Content = new TextBlock { Text = "B" } };
        return new Tabs { Items = { overview, activity } };
    }

    [AvaloniaFact]
    public void Value_selects_by_key_and_selection_updates_value()
    {
        var tabs = Build(out _, out var activity);
        var w = Wave2Helpers.Host(tabs);
        var changed = 0;
        tabs.ValueChanged += (_, _) => changed++;

        tabs.Value = "activity";
        Pump(w);
        Assert.Same(activity, tabs.SelectedItem);

        tabs.SelectedIndex = 0;
        Pump(w);
        Assert.Equal("overview", tabs.Value);
        Assert.True(changed >= 1);
        w.Close();
    }

    [AvaloniaFact]
    public void Variant_direction_and_size_drive_pseudo_classes_and_strip_placement()
    {
        var tabs = Build(out _, out _);
        tabs.Variant = TabsVariant.Pills;
        tabs.Direction = Direction.Column;
        tabs.Size = ControlSize.Small;
        var w = Wave2Helpers.Host(tabs);
        Assert.True(((IPseudoClasses)tabs.Classes).Contains(":pills"));
        Assert.True(((IPseudoClasses)tabs.Classes).Contains(":vertical"));
        Assert.Equal(Dock.Left, tabs.TabStripPlacement);
        Assert.Contains("small", tabs.Classes);
        w.Close();
    }

    [AvaloniaFact]
    public void Closable_tab_shows_a_close_button_and_delete_raises_closed()
    {
        var tabs = Build(out _, out var activity);
        var w = Wave2Helpers.Host(tabs);
        var closed = 0;
        activity.Closed += (_, _) => closed++;
        Assert.True(activity.Part<Button>("PART_Close").IsVisible);
        Assert.True(activity.Part<Badge>("PART_Badge").IsVisible);

        activity.Focus();
        w.Press(Key.Delete);
        activity.Part<Button>("PART_Close").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.Equal(2, closed);
        w.Close();
    }

    [AvaloniaFact]
    public void Keep_alive_keeps_visited_panels_mounted()
    {
        var tabs = Build(out var overview, out var activity);
        tabs.KeepAlive = true;
        var w = Wave2Helpers.Host(tabs);
        tabs.SelectedItem = activity;
        Pump(w);
        tabs.SelectedItem = overview;
        Pump(w);

        var host = tabs.Part<Panel>("PART_KeepAliveHost");
        Assert.Equal(2, host.Children.Count);
        Assert.Single(host.Children, c => c.IsVisible);
        Assert.NotNull(((TextBlock)activity.Content!).GetVisualParent()); // still in the tree
        w.Close();
    }

    [AvaloniaFact]
    public void Value_declared_before_tabs_are_added_wins_over_auto_selection()
    {
        var tabs = new Tabs();
        tabs.BeginInit();
        tabs.Value = "b";
        tabs.Items.Add(new Tab { Key = "a", Label = "A" });
        tabs.Items.Add(new Tab { Key = "b", Label = "B" });
        tabs.EndInit();
        var w = Wave2Helpers.Host(tabs);
        Assert.Equal("b", tabs.Value);
        Assert.Equal(1, tabs.SelectedIndex);
        w.Close();
    }

    [AvaloniaFact]
    public void Icon_names_become_slate_icons()
    {
        var tab = new Tab { Label = "Files", Icon = "folder" };
        Assert.IsType<Icon>(tab.Icon);
    }
}

public class SegmentedControlTests
{
    [AvaloniaFact]
    public void Arrow_keys_move_selection_with_wrap_and_click_selects()
    {
        var seg = new SegmentedControl { Items = new[] { "Day", "Week", "Month" }, Value = "Day" };
        var w = Wave2Helpers.Host(seg);
        var changes = 0;
        seg.ValueChanged += (_, _) => changes++;
        seg.Segments[0].Focus();

        w.Press(Key.Right);
        Assert.Equal("Week", seg.Value);
        w.Press(Key.End);
        Assert.Equal("Month", seg.Value);
        w.Press(Key.Right);
        Assert.Equal("Day", seg.Value); // wraps

        w.Click(seg.Segments[1]);
        Assert.Equal("Week", seg.Value);
        Assert.True(seg.Segments[1].IsChecked);
        Assert.False(seg.Segments[0].IsChecked);
        Assert.Equal(4, changes);
        w.Close();
    }
}

public class DateTests
{
    private static readonly DateOnly Today = new(2026, 10, 6);

    [AvaloniaFact]
    public void Calendar_renders_42_days_and_marks_today()
    {
        var cal = new MonthCalendar { Today = Today, FirstDayOfWeek = DayOfWeek.Monday };
        var w = Wave2Helpers.Host(cal);
        cal.ShowMonth(2026, 10);
        Pump(w);
        Assert.Equal(42, cal.Days.Count);
        Assert.Equal(new DateOnly(2026, 9, 28), cal.Days[0].Date);
        var today = cal.Days.Single(d => d.Date == Today);
        Assert.True(((IPseudoClasses)today.Classes).Contains(":today"));
        Assert.Equal("October 2026", cal.Part<TextBlock>("PART_Title").Text);
        w.Close();
    }

    [AvaloniaFact]
    public void Keyboard_navigation_moves_days_and_pages_months()
    {
        var cal = new MonthCalendar { Today = Today, Value = Today };
        var w = Wave2Helpers.Host(cal);
        cal.FocusDay();
        Pump(w);

        w.Press(Key.Right);
        Assert.Equal(Today.AddDays(1), cal.FocusedDate);
        w.Press(Key.Down);
        Assert.Equal(Today.AddDays(8), cal.FocusedDate);
        w.Press(Key.PageDown);
        Assert.Equal(11, cal.DisplayMonth);
        w.Press(Key.PageUp, RawInputModifiers.Shift);
        Assert.Equal(2025, cal.DisplayYear);

        w.Press(Key.Enter); // activates the focused day button
        Assert.Equal(cal.FocusedDate, cal.Value);
        w.Close();
    }

    [AvaloniaFact]
    public void Min_and_disabled_dates_are_not_selectable()
    {
        var cal = new MonthCalendar { Today = Today, Min = Today, DisabledDates = d => d.DayOfWeek == DayOfWeek.Sunday };
        var w = Wave2Helpers.Host(cal);
        cal.ShowMonth(2026, 10);
        Pump(w);
        Assert.False(cal.Days.Single(d => d.Date == Today.AddDays(-1)).IsEnabled);
        Assert.False(cal.Days.Single(d => d.Date == new DateOnly(2026, 10, 11)).IsEnabled);
        cal.Pick(Today.AddDays(-1));
        Assert.Null(cal.Value);
        w.Close();
    }

    [AvaloniaFact]
    public void Range_selection_picks_start_then_end()
    {
        var cal = new MonthCalendar { Today = Today, Selection = DateSelection.Range };
        var w = Wave2Helpers.Host(cal);
        cal.Pick(new DateOnly(2026, 10, 12));
        Assert.Equal(new DateRange(new DateOnly(2026, 10, 12), null), cal.Range);
        cal.Pick(new DateOnly(2026, 10, 15));
        Assert.Equal(new DateRange(new DateOnly(2026, 10, 12), new DateOnly(2026, 10, 15)), cal.Range);
        Pump(w);
        Assert.True(((IPseudoClasses)cal.Days.Single(d => d.Date == new DateOnly(2026, 10, 13)).Classes).Contains(":in-range"));
        w.Close();
    }

    [AvaloniaFact]
    public void Date_field_parses_typed_dates_and_formats_values()
    {
        var field = new DateField { Format = "yyyy-MM-dd", Label = "Due" };
        var w = Wave2Helpers.Host(field);
        DateOnly? last = null;
        field.ValueChanged += (_, _) => last = field.Value;

        field.Text = "2026-10-20";
        field.Commit();
        Assert.Equal(new DateOnly(2026, 10, 20), field.Value);
        Assert.Equal(field.Value, last);

        field.Text = "not a date";
        field.Commit();
        Assert.Equal("Enter a date as yyyy-MM-dd.", field.ParseError);
        Assert.Equal(field.ParseError, field.DisplayError);

        field.Value = new DateOnly(2027, 1, 2);
        Assert.Equal("2027-01-02", field.Text);
        Assert.Null(field.ParseError);
        w.Close();
    }

    [AvaloniaFact]
    public void Date_field_ranges_parse_and_respect_bounds()
    {
        var field = new DateField { Format = "yyyy-MM-dd", Selection = DateSelection.Range, Max = new DateOnly(2026, 12, 31) };
        var w = Wave2Helpers.Host(field);
        field.Text = "2026-10-01 – 2026-10-10";
        field.Commit();
        Assert.Equal(new DateRange(new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 10)), field.Range);

        field.Text = "2026-10-01 – 2027-02-01";
        field.Commit();
        Assert.NotNull(field.ParseError);
        w.Close();
    }

    [AvaloniaFact]
    public void Presets_resolve_relative_to_today()
    {
        var cal = new MonthCalendar { Today = Today, Selection = DateSelection.Range, Presets = CalendarModel.DefaultPresets };
        var w = Wave2Helpers.Host(cal);
        Assert.Equal(CalendarModel.DefaultPresets.Count, cal.Part<StackPanel>("PART_Presets").Children.Count);
        cal.ApplyPreset(CalendarModel.DefaultPresets.First(p => p.Kind == DatePresetKind.Last7Days));
        Assert.Equal(CalendarModel.Resolve(DatePresetKind.Last7Days, Today), cal.Range);
        w.Close();
    }

    [AvaloniaFact]
    public void Picking_in_the_popup_updates_the_field_and_closes()
    {
        var field = new DateField { Format = "yyyy-MM-dd", Today = Today };
        var w = Wave2Helpers.Host(field);
        field.IsDropDownOpen = true;
        Pump(w);
        field.Calendar!.Pick(new DateOnly(2026, 10, 9));
        Pump(w);
        Assert.Equal(new DateOnly(2026, 10, 9), field.Value);
        Assert.Equal("2026-10-09", field.Text);
        Assert.False(field.IsDropDownOpen);
        w.Close();
    }

    [AvaloniaFact]
    public void Inline_renders_the_calendar_without_a_field()
    {
        var field = new DateField { Inline = true, Today = Today };
        var w = Wave2Helpers.Host(field);
        Assert.NotNull(field.Calendar);
        Assert.Null(field.FindPart<TextField>("PART_Field"));
        w.Close();
    }
}

public class TreeListTests
{
    private sealed record Node(string Name, params Node[] Children);

    private static readonly Node[] Roots =
    [
        new("src", new Node("Slate.Core", new Node("Dates"), new Node("Collections")), new Node("Slate.Avalonia")),
        new("tests", new Node("Core.Tests")),
        new("README.md"),
    ];

    private static TreeList Build(TreeSelectionMode mode = TreeSelectionMode.Single) => new()
    {
        Items = Roots,
        ChildrenSelector = n => ((Node)n).Children,
        ItemText = n => ((Node)n).Name,
        SelectionMode = mode,
        Height = 400,
    };

    private static string[] Visible(TreeList t) => t.Rows.Select(r => t.TextOf(r.Item)).ToArray();

    [AvaloniaFact]
    public void Keyboard_expands_moves_and_collapses()
    {
        var tree = Build();
        var w = Wave2Helpers.Host(tree);
        tree.Focus();
        Assert.Equal(["src", "tests", "README.md"], Visible(tree));

        w.Press(Key.Right); // expand src
        Assert.Equal(["src", "Slate.Core", "Slate.Avalonia", "tests", "README.md"], Visible(tree));
        w.Press(Key.Right); // move into Slate.Core
        Assert.Equal("Slate.Core", tree.TextOf(tree.FocusedItem!));
        w.Press(Key.Left);  // to parent
        Assert.Equal("src", tree.TextOf(tree.FocusedItem!));
        w.Press(Key.Left);  // collapse
        Assert.Equal(["src", "tests", "README.md"], Visible(tree));
        w.Press(Key.End);
        Assert.Equal("README.md", tree.TextOf(tree.FocusedItem!));
        w.Close();
    }

    [AvaloniaFact]
    public void Single_selection_follows_focus_and_enter_activates()
    {
        var tree = Build();
        var w = Wave2Helpers.Host(tree);
        object? activated = null;
        tree.ItemActivated += (_, _) => activated = tree.ActivatedItem;
        tree.Focus();
        w.Press(Key.Down);
        Assert.Equal("tests", tree.TextOf(tree.SelectedItems!.Cast<object>().Single()));
        w.Press(Key.Enter);
        Assert.Equal("tests", tree.TextOf(activated!));
        w.Close();
    }

    [AvaloniaFact]
    public void Checkbox_mode_propagates_tri_state()
    {
        var tree = Build(TreeSelectionMode.Checkbox);
        tree.Expanded = new List<object> { Roots[0], Roots[0].Children[0] };
        var w = Wave2Helpers.Host(tree);
        tree.Focus();
        w.Press(Key.Down);  // Slate.Core
        w.Press(Key.Down);  // Dates
        w.Press(Key.Space);

        var row = tree.GetVisualDescendants().OfType<TreeListRow>().Single(r => r.Label as string == "Slate.Core");
        Assert.Null(row.Checked); // indeterminate: one of two children checked
        Assert.Contains(tree.SelectedItems!.Cast<object>(), n => ((Node)n).Name == "Dates");
        w.Close();
    }

    [AvaloniaFact]
    public void Filter_keeps_ancestors_and_expands_them()
    {
        var tree = Build();
        var w = Wave2Helpers.Host(tree);
        tree.Filter = "coll";
        Pump(w);
        Assert.Equal(["src", "Slate.Core", "Collections"], Visible(tree));
        tree.Filter = null;
        Pump(w);
        Assert.Contains("tests", Visible(tree));
        w.Close();
    }

    [AvaloniaFact]
    public async Task Lazy_children_load_on_expand()
    {
        var tree = new TreeList
        {
            Items = new[] { new Node("remote") },
            HasChildren = _ => true,
            ChildrenSelector = _ => null,
            LoadChildren = _ => Task.FromResult<System.Collections.IEnumerable>(new[] { new Node("a"), new Node("b") }),
            ItemText = n => ((Node)n).Name,
            Height = 300,
        };
        var w = Wave2Helpers.Host(tree);
        tree.Focus();
        w.Press(Key.Right);
        await Task.Delay(10);
        Pump(w);
        Assert.Equal(["remote", "a", "b"], Visible(tree));
        w.Close();
    }

    [AvaloniaFact]
    public void Typeahead_moves_focus()
    {
        var tree = Build();
        var w = Wave2Helpers.Host(tree);
        tree.Focus();
        w.Type("r");
        Assert.Equal("README.md", tree.TextOf(tree.FocusedItem!));
        w.Close();
    }

    [AvaloniaFact]
    public void Rows_expose_tree_item_automation()
    {
        var tree = Build();
        var w = Wave2Helpers.Host(tree);
        var row = tree.GetVisualDescendants().OfType<TreeListRow>().First();
        var peer = ControlAutomationPeer.CreatePeerForElement(row);
        Assert.Equal(AutomationControlType.TreeItem, peer.GetAutomationControlType());
        Assert.Equal(global::Avalonia.Automation.ExpandCollapseState.Collapsed, Assert.IsAssignableFrom<IExpandCollapseProvider>(peer).ExpandCollapseState);
        w.Close();
    }
}

public class SliderTests
{
    [AvaloniaFact]
    public void Step_snaps_keyboard_increments()
    {
        var slider = new AvSlider { Minimum = 0, Maximum = 100, Value = 20 };
        Slate.Avalonia.Sl.SetStep(slider, 5);
        var w = Wave2Helpers.Host(slider);
        slider.Focus();
        w.Press(Key.Right);
        Assert.Equal(25, slider.Value);
        w.Press(Key.PageUp);
        Assert.Equal(35, slider.Value);
        w.Press(Key.End);
        Assert.Equal(100, slider.Value);
        w.Press(Key.Home);
        Assert.Equal(0, slider.Value);
        Assert.True(slider.IsSnapToTickEnabled);
        w.Close();
    }

    [AvaloniaFact]
    public void Range_thumbs_never_cross_and_value_text_shows_both()
    {
        var slider = new AvSlider { Minimum = 0, Maximum = 100, Value = 40, Width = 300 };
        Slate.Avalonia.Sl.SetRangeEnd(slider, 50);
        Slate.Avalonia.Sl.SetShowValue(slider, true);
        var w = Wave2Helpers.Host(slider);
        slider.Focus();
        for (var i = 0; i < 20; i++) w.Press(Key.Right);
        Assert.Equal(50, slider.Value);
        Assert.Equal(50, Slate.Avalonia.Sl.GetRangeEnd(slider));
        Assert.Contains("range", slider.Classes);
        Assert.Equal("50 – 50", slider.Part<TextBlock>("PART_ValueText").Text);
        Assert.True(slider.Part<Canvas>("PART_RangeCanvas").IsVisible);
        w.Close();
    }

    [AvaloniaFact]
    public void Tone_and_ticks_map_to_classes()
    {
        var slider = new AvSlider();
        Slate.Avalonia.Sl.SetTone(slider, Tone.Success);
        Slate.Avalonia.Sl.SetShowTicks(slider, true);
        var w = Wave2Helpers.Host(slider);
        Assert.Contains("tone-success", slider.Classes);
        Assert.Contains("ticks", slider.Classes);
        w.Close();
    }
}

public class IdentityAndPlaceholderTests
{
    [AvaloniaFact]
    public void Avatar_shows_initials_tone_and_status()
    {
        var avatar = new Avatar { DisplayName = "Aaron Griffin", Status = AvatarStatus.Busy, Size = ControlSize.Large };
        var w = Wave2Helpers.Host(avatar);
        Assert.Equal("AG", avatar.Initials);
        Assert.Contains(Slate.Avalonia.Sl.ClassFor(Slate.Collections.AvatarText.ToneFor("Aaron Griffin")), avatar.Classes);
        Assert.Contains("status-busy", avatar.Classes);
        Assert.Equal(40, avatar.Bounds.Width);
        Assert.False(avatar.Part<Image>("PART_Image").IsVisible);

        avatar.Tone = Tone.Info;
        Assert.Contains("tone-info", avatar.Classes);
        w.Close();
    }

    [AvaloniaFact]
    public void Skeleton_draws_lines_with_a_short_last_line()
    {
        var skeleton = new Skeleton { Lines = 3, Width = 300 };
        var w = Wave2Helpers.Host(skeleton);
        var lines = skeleton.Part<SkeletonLinesPanel>("PART_Lines").Children;
        Assert.Equal(3, lines.Count);
        Assert.Equal(300, lines[0].Bounds.Width);
        Assert.Equal(180, lines[2].Bounds.Width);
        Assert.True(((IPseudoClasses)skeleton.Classes).Contains(":animated"));

        skeleton.Animated = false;
        Assert.False(((IPseudoClasses)skeleton.Classes).Contains(":animated"));
        w.Close();
    }

    [AvaloniaFact]
    public void Skeleton_stops_animating_under_reduced_motion()
    {
        WithTheme(t => t.ReduceMotion = true, t => t.ReduceMotion = false, () =>
        {
            var skeleton = new Skeleton { Shape = SkeletonShape.Circle, Width = 40, Height = 40 };
            var w = Wave2Helpers.Host(skeleton);
            Assert.False(((IPseudoClasses)skeleton.Classes).Contains(":animated"));
            Assert.True(skeleton.Part<global::Avalonia.Controls.Shapes.Ellipse>("PART_Circle").IsVisible);
            w.Close();
        });
    }
}

public class BreadcrumbsAndPaginationTests
{
    [AvaloniaFact]
    public void Breadcrumbs_mark_the_current_page_and_raise_item_click()
    {
        var items = new[] { new BreadcrumbItem("Workspace"), new BreadcrumbItem("slate"), new BreadcrumbItem("Button.axaml") };
        var crumbs = new Breadcrumbs { Items = items };
        var w = Wave2Helpers.Host(crumbs);
        BreadcrumbItem? clicked = null;
        crumbs.ItemClick += (_, e) => clicked = e.Item;

        var panel = crumbs.Part<StackPanel>("PART_Items");
        Assert.Equal("Button.axaml", Assert.IsType<TextBlock>(panel.Children[^1]).Text);
        w.Click((Button)panel.Children[2]);
        Assert.Equal("slate", clicked!.Label);
        w.Close();
    }

    [AvaloniaFact]
    public void Breadcrumbs_collapse_the_middle_beyond_max_items()
    {
        var items = Enumerable.Range(1, 6).Select(i => new BreadcrumbItem($"Level {i}")).ToArray();
        var crumbs = new Breadcrumbs { Items = items, MaxItems = 3 };
        var w = Wave2Helpers.Host(crumbs);
        Assert.Equal(["Level 2", "Level 3", "Level 4"], crumbs.Collapsed.Select(c => c.Label));
        Assert.Single(crumbs.Part<StackPanel>("PART_Items").Children.OfType<DropdownMenu>());
        w.Close();
    }

    [AvaloniaFact]
    public void Pagination_shows_ellipses_and_navigates()
    {
        var pager = new Pagination { PageCount = 20, Page = 10 };
        var w = Wave2Helpers.Host(pager);
        Assert.Equal("1 … 9 10 11 … 20", string.Join(" ", pager.Items));
        var pages = pager.Part<StackPanel>("PART_Pages").Children;
        w.Click((Button)pages[^1]); // next
        Assert.Equal(11, pager.Page);
        Assert.Contains(pages.OfType<Button>(), b => b.Classes.Contains("current") && (string?)b.Content == "11");
        w.Close();
    }

    [AvaloniaFact]
    public void Page_size_change_keeps_the_first_item_and_summary_updates()
    {
        var pager = new Pagination { TotalCount = 1240, PageSize = 50, PageSizes = [25, 50, 100], Page = 3 };
        var w = Wave2Helpers.Host(pager);
        Assert.Equal(25, pager.EffectivePageCount);
        Assert.Equal("101–150 of 1,240", pager.Part<TextBlock>("PART_Summary").Text);

        pager.PageSize = 25;
        Pump(w);
        Assert.Equal(5, pager.Page); // item 101 is on page 5 at 25 per page
        Assert.Equal("101–125 of 1,240", pager.Part<TextBlock>("PART_Summary").Text);
        w.Close();
    }
}
