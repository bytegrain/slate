using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Input.Platform;
using Avalonia.Media;
using Avalonia.Threading;
using Slate.Avalonia.Controls;
using Slate.Avalonia.Services;
using Slate.Data;
using Slate.Snackbars;

namespace Slate.Avalonia.Demo.Pages;

public partial class DataGridPage : UserControl
{
    public sealed class Deployment
    {
        public string Id { get; set; } = "";
        public string Service { get; set; } = "";
        public string Status { get; set; } = "";
        public string Region { get; set; } = "";
        public string Branch { get; set; } = "";
        public double Progress { get; set; }
        public double Duration { get; set; }
        public double Cost { get; set; }
        public double[] Cpu { get; set; } = [];
        public DateTime Started { get; set; }
        public bool Healthy { get; set; }
    }

    public sealed class FileNode
    {
        public string Path { get; init; } = "";
        public string Name { get; init; } = "";
        public double Size { get; set; }
        public string Kind { get; init; } = "";
        public List<FileNode>? Children { get; init; }
    }

    private static readonly string[] Services = ["api-gateway", "billing", "auth", "search", "web", "notifications", "media", "analytics", "payments", "inventory"];
    private static readonly string[] Statuses = ["Ready", "Ready", "Ready", "Building", "Queued", "Failed", "Cancelled"];
    private static readonly string[] Regions = ["eu-west-1", "us-east-1", "us-west-2", "ap-south-1", "sa-east-1"];
    private static readonly string[] Branches = ["main", "release/2.4", "feat/grid", "fix/login-loop", "chore/deps", "feat/search-v2"];

    private readonly List<Deployment> _deployments;
    private readonly DispatcherTimer _live = new() { Interval = TimeSpan.FromSeconds(1.2) };
    private readonly Random _random = new(7);

    public DataGridPage()
    {
        InitializeComponent();
        _deployments = MakeDeployments(100_000, seed: 42);

        Deployments.Columns.Single(c => c.Field == "Actions").Actions = item =>
        {
            var d = (Deployment)item;
            return
            [
                new GridRowAction("Open logs", () => SlateServices.Snackbars.Add($"Opening logs for {d.Id}"), "external-link"),
                new GridRowAction("Redeploy", () => SlateServices.Snackbars.Add($"Redeploying {d.Service}", Severity.Info), "refresh"),
                new GridRowAction("Copy ID", () => TopLevel.GetTopLevel(this)?.Clipboard?.SetTextAsync(d.Id), "copy", Shortcut: "Ctrl+C"),
                new GridRowAction("Cancel", () => SlateServices.Snackbars.Add($"Cancelled {d.Id}", Severity.Warning), "x", Tone.Danger),
            ];
        };
        Deployments.Columns.Single(c => c.Field == "Duration").Validate = v => v is double d && d < 0 ? "Duration can't be negative" : null;
        Deployments.Columns.Single(c => c.Field == "Duration").CellTone = item => ((Deployment)item).Duration > 540 ? Tone.Warning : null;
        Deployments.Items = _deployments;
        Deployments.GroupBy = ["Status"];
        Deployments.RowTone = FailedTone;
        Deployments.StateChanged += (_, _) => StateText.Text = "State: " + Deployments.State.ToJson();
        Deployments.RowActivated += (_, e) => SlateServices.Snackbars.Add($"Activated {((Deployment)e.Item).Id}");
        StateText.Text = "State: " + Deployments.State.ToJson();

        Grouped.IsCheckedChanged += (_, _) => Deployments.GroupBy = Grouped.IsChecked == true ? ["Status"] : [];
        Striped.IsCheckedChanged += (_, _) => Deployments.Striped = Striped.IsChecked == true;
        Batch.IsCheckedChanged += (_, _) => Deployments.EditMode = Batch.IsChecked == true ? GridEditMode.Batch : GridEditMode.Cell;
        Toned.IsCheckedChanged += (_, _) => Deployments.RowTone = Toned.IsChecked == true ? FailedTone : null;
        Paged.IsCheckedChanged += (_, _) => Deployments.Pagination = Paged.IsChecked == true ? GridPagination.Pages : GridPagination.None;
        Detail.IsCheckedChanged += (_, _) => Deployments.RowDetail = Detail.IsChecked == true
            ? new FuncDataTemplate<object>((item, _) => DetailView((Deployment)item))
            : null;
        Live.IsCheckedChanged += (_, _) => _live.IsEnabled = Live.IsChecked == true;
        _live.Tick += (_, _) => LiveTick();
        DetachedFromVisualTree += (_, _) => _live.Stop();

        Redeploy.Click += (_, _) =>
        {
            var d = MakeDeployments(1, seed: _random.Next())[0];
            d.Id = $"dep-{_deployments.Count + 1:D6}";
            d.Status = "Queued";
            _deployments.Insert(0, d);
            Deployments.NotifyItemsChanged(d);
        };
        RetrySelected.Click += (_, _) => SlateServices.Snackbars.Add($"Retrying {Deployments.SelectedItems.Count} deployments", Severity.Info);
        CancelSelected.Click += (_, _) => SlateServices.Snackbars.Add($"Cancelled {Deployments.SelectedItems.Count} deployments", Severity.Warning);

        // Server mode: the same engine queries an IGridDataSource; 400 ms latency shows skeleton rows while blocks load.
        var serverColumns = new List<GridColumn<Deployment>>
        {
            new() { Field = "Id" }, new() { Field = "Service" }, new() { Field = "Status", Type = GridColumnType.Enum },
            new() { Field = "Region" }, new() { Field = "Duration", Type = GridColumnType.Number },
        };
        Server.DataSource = GridDataSourceAdapter.From(new InMemoryGridDataSource<Deployment>(MakeDeployments(25_000, seed: 3), serverColumns, TimeSpan.FromMilliseconds(400)));

        Files.ChildrenSelector = item => ((FileNode)item).Children;
        Files.Items = MakeFiles();
    }

    private static Tone? FailedTone(object item) => ((Deployment)item).Status == "Failed" ? Tone.Danger : null;

    private static Control DetailView(Deployment d) => new StackPanel
    {
        Margin = new Thickness(56, 8, 12, 8),
        Spacing = 4,
        Children =
        {
            new TextBlock { Text = $"{d.Service} · {d.Branch} → {d.Region}", FontWeight = FontWeight.SemiBold },
            new TextBlock { Text = $"Started {d.Started:yyyy-MM-dd HH:mm} · {d.Duration:0.0}s · ${d.Cost:0.00} · {(d.Healthy ? "healthy" : "unhealthy")}", Opacity = 0.75 },
        },
    };

    private void LiveTick()
    {
        // Mutates a few rows near the top (as a websocket feed would) and flashes them.
        var changed = new List<object>();
        for (var i = 0; i < 3; i++)
        {
            var d = _deployments[_random.Next(0, 60)];
            d.Progress = Math.Min(1, d.Progress + _random.NextDouble() * 0.3);
            d.Duration = Math.Round(d.Duration + _random.NextDouble() * 20, 1);
            d.Cpu = [.. d.Cpu.Skip(1), _random.NextDouble() * 100];
            if (d.Progress >= 1 && d.Status is "Building" or "Queued") d.Status = "Ready";
            changed.Add(d);
        }
        Deployments.NotifyItemsChanged([.. changed]);
    }

    private static List<Deployment> MakeDeployments(int count, int seed)
    {
        var random = new Random(seed);
        var start = new DateTime(2026, 10, 7, 9, 0, 0);
        var list = new List<Deployment>(count);
        for (var i = 0; i < count; i++)
        {
            var status = Statuses[random.Next(Statuses.Length)];
            var cpu = new double[16];
            var level = random.NextDouble() * 60 + 10;
            for (var k = 0; k < cpu.Length; k++)
                cpu[k] = level = Math.Clamp(level + (random.NextDouble() - 0.5) * 30, 2, 100);
            list.Add(new Deployment
            {
                Id = $"dep-{i + 1:D6}",
                Service = Services[random.Next(Services.Length)],
                Status = status,
                Region = Regions[random.Next(Regions.Length)],
                Branch = Branches[random.Next(Branches.Length)],
                Progress = status switch { "Ready" => 1, "Queued" => 0, "Cancelled" or "Failed" => random.NextDouble() * 0.9, _ => random.NextDouble() },
                Duration = Math.Round(20 + random.NextDouble() * 580, 1),
                Cost = Math.Round(random.NextDouble() * 4, 2),
                Cpu = cpu,
                Started = start.AddMinutes(-i * 3 - random.Next(3)),
                Healthy = status != "Failed" && random.NextDouble() > 0.08,
            });
        }
        return list;
    }

    private static List<FileNode> MakeFiles()
    {
        FileNode File(string dir, string name, double kb, string kind) => new() { Path = $"{dir}/{name}", Name = name, Size = kb, Kind = kind };
        FileNode Folder(string path, params FileNode[] children) => new()
        {
            Path = path, Name = path.Split('/')[^1], Kind = "Folder", Children = [.. children], Size = children.Sum(c => c.Size),
        };
        return
        [
            Folder("src",
                Folder("src/Slate.Core",
                    Folder("src/Slate.Core/Data", File("src/Slate.Core/Data", "DataPipeline.cs", 38, "Code"), File("src/Slate.Core/Data", "GridState.cs", 21, "Code"), File("src/Slate.Core/Data", "GridLayout.cs", 12, "Code")),
                    File("src/Slate.Core", "Theming.cs", 9, "Code")),
                Folder("src/Slate.Avalonia",
                    Folder("src/Slate.Avalonia/Controls", File("src/Slate.Avalonia/Controls", "DataGrid.cs", 31, "Code"), File("src/Slate.Avalonia/Controls", "Navigation.cs", 44, "Code")),
                    File("src/Slate.Avalonia", "SlateTheme.axaml", 6, "Code"))),
            Folder("assets", File("assets", "logo.png", 148, "Image"), File("assets", "hero@2x.png", 2310, "Image"), File("assets", "favicon.ico", 15, "Image")),
            Folder("docs", File("docs", "data-grid.md", 27, "Text"), File("docs", "tokens.md", 18, "Text")),
            File("", "README.md", 7, "Text"),
        ];
    }
}
