using System.Collections;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using Slate.Dates;

namespace Slate.Wpf.Demo.Pages;

public partial class PickersPage : UserControl
{
    private sealed record Fruit(string Name, string Family);

    private sealed record Folder(string Name, string? Icon, IReadOnlyList<Folder> Children, bool Remote = false)
    {
        public Folder(string name, params Folder[] children) : this(name, children.Length > 0 ? "folder" : "file", children) { }
    }

    private static readonly DatePreset[] Presets =
    [
        new("Today", DatePresetKind.Today),
        new("Yesterday", DatePresetKind.Yesterday),
        new("Last 7 days", DatePresetKind.Last7Days),
        new("Last 30 days", DatePresetKind.Last30Days),
        new("This month", DatePresetKind.ThisMonth),
        new("Last month", DatePresetKind.LastMonth),
        new("This year", DatePresetKind.ThisYear),
    ];

    public PickersPage()
    {
        InitializeComponent();

        // Breadcrumbs
        Crumbs.Items = new[] { new BreadcrumbItem("Home", "home"), new BreadcrumbItem("Projects"), new BreadcrumbItem("Slate") };
        LongCrumbs.Items = new[] { "Home", "Workspace", "Projects", "Slate", "Design", "Components", "Tabs" }.Select(l => new BreadcrumbItem(l)).ToList();

        // Pagination & segmented
        Pager.PageSizes = [25, 50, 100];
        Views.Items = new[] { ("List", "menu"), ("Board", "layers"), ("Files", "folder") };
        Views.ItemText = o => (((string, string))o).Item1;
        Views.ItemIcon = o => (((string, string))o).Item2;
        Views.Value = Views.Items.Cast<object>().First();
        Ranges.Items = new[] { "1D", "1W", "1M", "1Y", "All" };
        Ranges.Value = "1M";
        Disabled.Items = new[] { "On", "Off" };
        Disabled.Value = "On";
        Wide.Items = new[] { "Monthly", "Yearly (save 20%)" };
        Wide.Value = "Monthly";

        // Select
        var countries = CultureInfo.GetCultures(CultureTypes.SpecificCultures)
            .Select(c => new RegionInfo(c.Name).EnglishName).Distinct().Order().ToList();
        Country.Items = countries;
        City.Items = new[] { "Amsterdam", "Berlin", "Birmingham", "København", "Kraków", "Lisbon", "London", "Málaga", "Manchester", "Paris", "Reykjavík", "São Paulo", "Zürich" };
        Tags.Items = new List<string> { "bug", "design", "docs", "feature", "performance", "a11y", "wpf", "web", "avalonia" };
        Tags.Values = new List<object> { "design", "wpf" };
        Grouped.Items = new[]
        {
            new Fruit("Apple", "Pome"), new Fruit("Pear", "Pome"), new Fruit("Quince", "Pome"),
            new Fruit("Cherry", "Stone fruit"), new Fruit("Peach", "Stone fruit"), new Fruit("Plum", "Stone fruit"),
            new Fruit("Blueberry", "Berry"), new Fruit("Raspberry", "Berry"),
        };
        Grouped.ItemText = o => ((Fruit)o).Name;
        Grouped.GroupBy = o => ((Fruit)o).Family;
        foreach (var s in new[] { Filled, Underlined, Errored, Off })
            s.Items = new[] { "North", "South", "East", "West" };
        Filled.Value = "North";
        Off.Value = "East";
        Empty.Items = Array.Empty<string>();
        People.Items = new[] { "Ada Lovelace", "Grace Hopper", "Alan Turing", "Katherine Johnson" };

        // Dates
        Stay.Presets = Presets;
        InlineRange.Presets = Presets;
        Weekdays.DisabledDates = d => d.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday;
        Weekdays.Min = DateOnly.FromDateTime(DateTime.Today);
        Weekdays.Max = Weekdays.Min.Value.AddMonths(3);

        // Trees
        var docs = new Folder("docs", new Folder("getting-started.md"), new Folder("components",
            new Folder("select.md"), new Folder("tabs.md"), new Folder("tree-view.md"), new Folder("date-picker.md")), new Folder("theming.md"));
        var src = new Folder("src", new Folder("Slate.Core", new Folder("Overlays"), new Folder("Dates"), new Folder("Collections")),
            new Folder("Slate.Wpf", new Folder("Controls", new Folder("Select.cs"), new Folder("TreeView.cs"))));
        Files.Items = new[] { docs, src, new Folder("README.md") };
        Files.ChildrenSelector = o => ((Folder)o).Children;
        Files.ItemText = o => ((Folder)o).Name;
        Files.ItemIcon = o => ((Folder)o).Icon;
        Files.Expanded = new ArrayList { docs };

        var read = new Folder("Read", new Folder("Projects"), new Folder("Issues"), new Folder("Wiki"));
        var write = new Folder("Write", new Folder("Projects"), new Folder("Issues"));
        var admin = new Folder("Admin", new Folder("Billing"), new Folder("Members"), new Folder("Audit log"));
        Permissions.Items = new[] { read, write, admin };
        Permissions.ChildrenSelector = o => ((Folder)o).Children;
        Permissions.ItemText = o => ((Folder)o).Name;
        Permissions.Expanded = new ArrayList { read, write, admin };
        Permissions.SelectedItems = new ArrayList { read.Children[0], read.Children[1] };

        Lazy.Items = new[] { RemoteFolder("Shared drive"), RemoteFolder("Team"), new Folder("Empty", "folder", [], Remote: true) };
        Lazy.HasChildren = o => ((Folder)o) is { Remote: true, Name: not "Empty" } || ((Folder)o).Children.Count > 0;
        Lazy.LoadChildren = async o =>
        {
            await Task.Delay(700); // simulate a network round trip
            var f = (Folder)o;
            return (IEnumerable)Enumerable.Range(1, 4).Select(i => i == 1 ? RemoteFolder($"{f.Name} / sub {i}") : new Folder($"{f.Name} item {i}.txt")).ToList();
        };
        Lazy.ItemText = o => ((Folder)o).Name;
        Lazy.ItemIcon = o => ((Folder)o).Icon;
    }

    private static Folder RemoteFolder(string name) => new(name, "folder", [], Remote: true);

    private void Log(string message) => LastEvent.Text = message;

    private void OnOpenChanged(object sender, RoutedPropertyChangedEventArgs<bool> e) => Log($"{sender.GetType().Name} open: {e.NewValue}");

    private void OnApplyFilters(object sender, RoutedEventArgs e)
    {
        FilterPopover.Open = false;
        Log("Filters applied");
    }

    private void OnMenuItem(object sender, RoutedEventArgs e) => Log($"Menu: {((MenuItem)sender).Header}");

    private void OnTabChanged(object sender, RoutedPropertyChangedEventArgs<string?> e) => Log($"Tab: {e.NewValue}");

    private void OnCrumb(object? sender, BreadcrumbClickEventArgs e) => Log($"Breadcrumb: {e.Item.Label}");

    private void OnPageChanged(object sender, RoutedPropertyChangedEventArgs<int> e) => Log($"Page {e.NewValue} ({Pager.RangeText})");

    private void OnPageSizeChanged(object sender, RoutedPropertyChangedEventArgs<int?> e) => Log($"Page size {e.NewValue}, now on page {Pager.Page}");

    private void OnSegmentChanged(object sender, RoutedPropertyChangedEventArgs<object?> e) => Log($"View: {Views.SelectedSegment?.Text}");

    private void OnSelectValue(object sender, RoutedPropertyChangedEventArgs<object?> e) => Log($"{((Select)sender).Label}: {e.NewValue ?? "(none)"}");

    private void OnSelectValues(object sender, RoutedEventArgs e) =>
        Log($"Tags: {string.Join(", ", Tags.Values?.Cast<object>() ?? [])}");

    private void OnDateChanged(object sender, RoutedPropertyChangedEventArgs<DateOnly?> e) => Log($"Due: {e.NewValue?.ToString("D", CultureInfo.CurrentCulture) ?? "(none)"}");

    private void OnRangeChanged(object sender, RoutedPropertyChangedEventArgs<DateRange?> e) =>
        Log(e.NewValue is { } r ? $"Stay: {r.Start:d} – {r.End:d}" : "Stay: (none)");

    private void OnTreeFilter(object sender, TextChangedEventArgs e) => Files.Filter = TreeFilter.Text;

    private void OnTreeActivated(object? sender, TreeItemEventArgs e) => Log($"Opened {((Folder)e.Item).Name}");

    private void OnTreeSelection(object sender, RoutedEventArgs e) =>
        Log($"Selected: {string.Join(", ", Files.SelectedItems?.Cast<Folder>().Select(f => f.Name) ?? [])}");

    private void OnSlider(object sender, RoutedPropertyChangedEventArgs<double> e) => Log($"Volume: {e.NewValue:0}");
}
