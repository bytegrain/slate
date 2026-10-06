using Avalonia.Controls;
using Slate.Dates;

namespace Slate.Avalonia.Demo.Pages;

public partial class InputsPage : UserControl
{
    private sealed record Runner(string Name, string Os);

    public InputsPage()
    {
        InitializeComponent();
        string[] regions = ["Frankfurt", "London", "N. Virginia", "Oregon", "São Paulo", "Singapore", "Sydney", "Tokyo"];
        Basic.Items = regions;
        Basic.Value = "London";
        Searchable.Items = new[] { "C#", "F#", "Go", "Java", "JavaScript", "Kotlin", "Python", "Ruby", "Rust", "Swift", "TypeScript", "Zig" };
        Grouped.Items = new Runner[]
        {
            new("ubuntu-22.04", "Linux"), new("ubuntu-24.04", "Linux"), new("macos-14", "macOS"),
            new("macos-15", "macOS"), new("windows-2022", "Windows"), new("windows-2025", "Windows"),
        };
        Grouped.ItemText = o => ((Runner)o!).Name;
        Grouped.GroupBy = o => ((Runner)o!).Os;
        Multi.Items = new[] { "bug", "design", "docs", "good first issue", "performance", "accessibility", "wontfix" };
        Multi.Values = new List<object> { "bug", "accessibility" };
        Invalid.Items = new[] { "Platform", "Design systems", "Infra" };
        Loading.Items = Array.Empty<string>();
        foreach (var s in new[] { Filled, Flushed, Large }) s.Items = regions;

        Due.DisabledDates = d => d.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday;
        Due.Min = DateOnly.FromDateTime(DateTime.Today);
        Window.Presets = CalendarModel.DefaultPresets;
        InlineCal.Presets = CalendarModel.DefaultPresets;
        InlineSingle.Value = DateOnly.FromDateTime(DateTime.Today);

        Range.Items = new[] { "Day", "Week", "Month", "Year" };
        Range.Value = "Week";
        Views.Items = new[] { "List", "Board", "Calendar" };
        Views.ItemIcon = o => (string)o! switch { "List" => "menu", "Board" => "columns", _ => "calendar" };
        Views.Value = "Board";
        Wide.Items = new[] { "Monthly", "Yearly" };
        Wide.Value = "Monthly";
        Range.ValueChanged += (_, _) => SegmentStatus.Text = $"Showing one {Range.Value?.ToString()?.ToLowerInvariant()}";
        SegmentStatus.Text = "Showing one week";
    }
}
