using System.Collections;
using Avalonia.Controls;
using Slate.Avalonia.Controls;

namespace Slate.Avalonia.Demo.Pages;

public partial class NavComponentsPage : UserControl
{
    private sealed record Node(string Name, Node[]? Children = null, bool Lazy = false);

    public NavComponentsPage()
    {
        InitializeComponent();

        Short.Items = [new("Workspace", Icon: "home"), new("slate"), new("Settings")];
        Long.Items = [new("Workspace", Icon: "home"), new("slate"), new("src"), new("Slate.Avalonia"), new("Themes"), new("Controls"), new("Pickers.axaml")];
        Slashed.Items = [new("Docs"), new("Components"), new("Breadcrumbs")];
        foreach (var crumbs in new[] { Short, Long, Slashed })
            crumbs.ItemClick += (_, e) => CrumbStatus.Text = $"Navigate to {e.Item.Label}";
        CrumbStatus.Text = "Click a crumb";
        Sized.PageSizes = [25, 50, 100];
        Sized.Page = 3;

        foreach (var tab in Editors.Items.OfType<Tab>())
            tab.Closed += (s, _) => Editors.Items.Remove(s);

        Node[] roots =
        [
            new("src",
            [
                new("Slate.Core", [new("Dates", [new("CalendarModel.cs"), new("DateText.cs")]), new("Collections", [new("TreeModel.cs"), new("Typeahead.cs")])]),
                new("Slate.Avalonia", [new("Controls", [new("Select.cs"), new("Tree.cs")]), new("Themes", [new("Pickers.axaml")])]),
            ]),
            new("remote (loads lazily)", Lazy: true),
            new("tests", [new("Slate.Avalonia.Tests", [new("Wave2Tests.cs")])]),
            new("README.md"),
        ];
        Files.Items = roots;
        Files.ChildrenSelector = n => ((Node)n).Children;
        Files.HasChildren = n => ((Node)n).Children is { Length: > 0 } || ((Node)n).Lazy;
        Files.LoadChildren = async n =>
        {
            await Task.Delay(800);
            return (IEnumerable)new[] { new Node("artifact-1.zip"), new Node("artifact-2.zip") };
        };
        Files.ItemText = n => ((Node)n).Name;
        Files.ItemIcon = n => ((Node)n).Children is not null || ((Node)n).Lazy ? "folder" : "file";
        Files.Expanded = new List<object> { roots[0] };
        Files.ItemActivated += (_, _) => TreeStatus.Text = $"Opened {((Node)Files.ActivatedItem!).Name}";
        TreeStatus.Text = "Enter or double-click opens a file";
        TreeFilter.TextChanged += (_, _) => Files.Filter = TreeFilter.Text;

        Checks.Items = roots;
        Checks.ChildrenSelector = Files.ChildrenSelector;
        Checks.ItemText = Files.ItemText;
        Checks.Expanded = new List<object> { roots[0], roots[0].Children![0], roots[0].Children![1] };
    }
}
