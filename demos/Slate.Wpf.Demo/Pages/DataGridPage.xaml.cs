using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Threading;
using Slate.Data;

namespace Slate.Wpf.Demo.Pages;

public sealed class Deployment
{
    public int Id { get; init; }
    public string Service { get; set; } = "";
    public string Status { get; set; } = "";
    public string Region { get; init; } = "";
    public string Branch { get; init; } = "";
    public string Commit { get; init; } = "";
    public string Author { get; init; } = "";
    public DateTime Started { get; init; }
    public double Duration { get; set; }
    public double Progress { get; set; }
    public double[] Cpu { get; set; } = [];
    public double Cost { get; init; }
    public bool Canary { get; init; }
    public int Instances { get; init; }
    public string Message => $"Deploy {Commit} of {Branch} to {Region} by {Author}.";
    public string Log => $"[{Started:HH:mm:ss}] build {Id} · {Instances} instances · {Status.ToLowerInvariant()}";
}

public sealed record Order(int Id, string Customer, string Status, DateTime Placed, double Total);

public sealed class FileNode
{
    public string Name { get; init; } = "";
    public string Kind { get; init; } = "";
    public double Size { get; init; }
    public DateTime Modified { get; init; }
    public List<FileNode> Children { get; } = [];
}

/// <summary>sl:DataGrid showcase: a 100k-row deployments table, a server-mode grid and tree data.</summary>
public partial class DataGridPage : UserControl
{
    private static readonly string[] Services = ["api-gateway", "auth", "billing", "catalog", "checkout", "edge-cache", "inventory", "notifications", "search", "web"];
    private static readonly string[] Regions = ["eu-west-1", "eu-central-1", "us-east-1", "us-west-2", "ap-south-1"];
    private static readonly string[] Authors = ["aiko", "bram", "carmen", "dev", "eli", "farah", "gus", "hana"];
    private static readonly string[] Branches = ["main", "release/2.4", "feat/grid", "fix/latency", "chore/deps"];

    private readonly List<Deployment> _deployments;
    private readonly DispatcherTimer _live = new() { Interval = TimeSpan.FromMilliseconds(400) };
    private readonly Random _random = new(7);
    private string? _savedLayout;

    public DataGridPage()
    {
        InitializeComponent();
        _deployments = Generate(100_000);
        Deployments.RowKey = d => ((Deployment)d).Id;
        Deployments.RowTone = d => ((Deployment)d).Status == "Failed" ? Tone.Danger : null;
        var duration = Deployments.Columns.First(c => c.Field == "Duration");
        duration.Validate = v => v is double d && d < 0 ? "Duration can't be negative" : null;
        var actions = Deployments.Columns.First(c => c.Field == "Actions");
        actions.Accessor = _ => null;
        actions.Actions = item =>
        {
            var d = (Deployment)item;
            return
            [
                new GridRowAction("Open logs", () => Deployments.ScrollIntoView(d), "file"),
                new GridRowAction("Redeploy", () => Restart(d), "refresh", Shortcut: "R"),
                new GridRowAction("Cancel", () => SetStatus(d, "Cancelled"), "x", Tone.Danger),
            ];
        };
        Deployments.Columns.First(c => c.Field == "Cost").CellTone = d => ((Deployment)d).Cost > 40 ? Tone.Warning : null;
        Deployments.InvalidateColumns();
        Deployments.Items = _deployments;
        Deployments.State = Deployments.State.ToggleSort("Started").ToggleSort("Started");
        UpdateSummary();

        _live.Tick += (_, _) => LiveTick();
        Unloaded += (_, _) => _live.Stop();

        SetUpServer();
        SetUpTree();
    }

    private static List<Deployment> Generate(int count)
    {
        var random = new Random(42);
        var start = new DateTime(2026, 10, 1, 8, 0, 0, DateTimeKind.Local);
        var statuses = new[] { "Ready", "Ready", "Ready", "Ready", "Building", "Queued", "Failed", "Cancelled" };
        var list = new List<Deployment>(count);
        for (var i = 0; i < count; i++)
        {
            var status = statuses[random.Next(statuses.Length)];
            list.Add(new Deployment
            {
                Id = 100_000 + i,
                Service = Services[random.Next(Services.Length)],
                Status = status,
                Region = Regions[random.Next(Regions.Length)],
                Branch = Branches[random.Next(Branches.Length)],
                Commit = random.Next(0x1000000, 0xFFFFFFF).ToString("x7", CultureInfo.InvariantCulture),
                Author = Authors[random.Next(Authors.Length)],
                Started = start.AddMinutes(-i * 3 - random.Next(3)),
                Duration = Math.Round(20 + random.NextDouble() * 400, 1),
                Progress = status == "Building" ? random.NextDouble() : status == "Queued" ? 0 : 1,
                Cpu = Enumerable.Range(0, 12).Select(_ => random.NextDouble() * 100).ToArray(),
                Cost = Math.Round(random.NextDouble() * 60, 2),
                Canary = random.Next(5) == 0,
                Instances = random.Next(1, 24),
            });
        }
        return list;
    }

    private void UpdateSummary()
    {
        var c = Deployments.Controller;
        Summary.Text = $"{c.MatchingCount:N0} of {c.TotalCount:N0} deployments · {c.Selection.Count:N0} selected";
    }

    private void OnGridStateChanged(object sender, RoutedPropertyChangedEventArgs<GridState> e) => UpdateSummary();

    private void OnSelectionChanged(object sender, RoutedEventArgs e) => UpdateSummary();

    private void OnRowActivated(object? sender, GridRowEventArgs e) => Deployments.Controller.ToggleDetail(e.Key);

    private void OnSaveLayout(object sender, RoutedEventArgs e) => _savedLayout = Deployments.State.ToJson();

    private void OnRestoreLayout(object sender, RoutedEventArgs e)
    {
        if (_savedLayout is not null) Deployments.State = GridState.FromJson(_savedLayout);
    }

    private void OnRedeploy(object sender, RoutedEventArgs e)
    {
        foreach (var d in Deployments.SelectedItems.Cast<Deployment>().ToList()) Restart(d);
    }

    private void OnCancelSelected(object sender, RoutedEventArgs e)
    {
        foreach (var d in Deployments.SelectedItems.Cast<Deployment>().ToList()) SetStatus(d, "Cancelled");
    }

    private void Restart(Deployment d)
    {
        d.Status = "Building";
        d.Progress = 0;
        Deployments.NotifyItemChanged(d);
    }

    private void SetStatus(Deployment d, string status)
    {
        d.Status = status;
        Deployments.NotifyItemChanged(d);
    }

    private void OnLiveToggled(object sender, RoutedEventArgs e)
    {
        if (((ToggleButton)sender).IsChecked == true) _live.Start();
        else _live.Stop();
    }

    /// <summary>Simulated live feed: advances a few builds and flashes their rows.</summary>
    private void LiveTick()
    {
        for (var n = 0; n < 3; n++)
        {
            var d = _deployments[_random.Next(200)];
            if (d.Status is "Building" or "Queued")
            {
                d.Status = "Building";
                d.Progress = Math.Min(1, d.Progress + 0.2 + _random.NextDouble() * 0.3);
                if (d.Progress >= 1) d.Status = _random.Next(6) == 0 ? "Failed" : "Ready";
            }
            else
            {
                d.Status = "Building";
                d.Progress = 0.05;
            }
            d.Cpu = [.. d.Cpu.Skip(1), _random.NextDouble() * 100];
            Deployments.NotifyItemChanged(d);
        }
    }

    // ---- server mode ----

    private void SetUpServer()
    {
        var random = new Random(3);
        var customers = new[] { "Acme", "Globex", "Initech", "Umbrella", "Hooli", "Stark", "Wayne", "Wonka", "Soylent", "Tyrell" };
        var statuses = new[] { "Pending", "Paid", "Shipped", "Refunded" };
        var orders = Enumerable.Range(1, 250_000)
            .Select(i => (object)new Order(i, customers[random.Next(customers.Length)] + " " + (i % 97), statuses[random.Next(statuses.Length)],
                new DateTime(2026, 1, 1).AddMinutes(i * 1.7), Math.Round(random.NextDouble() * 2000, 2)))
            .ToList();
        Server.RowKey = o => ((Order)o).Id;
        var columns = Server.Columns.Select(c => c.Engine).ToList();
        Server.DataSource = new InMemoryGridDataSource<object>(orders, columns, TimeSpan.FromMilliseconds(300));
    }

    private void OnPagesToggled(object sender, RoutedEventArgs e) =>
        Server.Pagination = PagesToggle.IsChecked == true ? GridPagination.Pages : GridPagination.Infinite;

    // ---- tree ----

    private void SetUpTree()
    {
        var random = new Random(11);
        var day = new DateTime(2026, 9, 1);

        FileNode Folder(string name, params FileNode[] children)
        {
            var node = new FileNode { Name = name, Kind = "Folder", Modified = day.AddDays(random.Next(30)) };
            node.Children.AddRange(children);
            return node;
        }

        FileNode File(string name, string kind) =>
            new() { Name = name, Kind = kind, Size = random.Next(1, 90_000), Modified = day.AddDays(random.Next(30)) };

        var roots = new List<FileNode>
        {
            Folder("src",
                Folder("Slate.Core",
                    Folder("Data", File("DataPipeline.cs", "C#"), File("GridState.cs", "C#"), File("GridLayout.cs", "C#")),
                    File("Slate.Core.csproj", "Project")),
                Folder("Slate.Wpf",
                    Folder("Controls", File("DataGrid.cs", "C#"), File("Select.cs", "C#"), File("Overlays.cs", "C#")),
                    Folder("Themes", File("Generic.xaml", "XAML"), File("Controls.xaml", "XAML")))),
            Folder("design", Folder("tokens", File("color.json", "JSON"), File("density.json", "JSON")), File("data-grid.md", "Markdown")),
            File("README.md", "Markdown"),
            File("global.json", "JSON"),
        };

        Tree.RowKey = n => ((FileNode)n).Name;
        Tree.ChildrenSelector = n => ((FileNode)n).Children;
        Tree.Items = roots;
    }
}
