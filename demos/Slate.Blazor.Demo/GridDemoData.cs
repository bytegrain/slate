using System.Globalization;
using Slate.Data;

namespace Slate.Blazor.Demo;

/// <summary>Deterministic data for the data grid demo — the same generator and seeds as the web demo (data-grid-demo.ts).</summary>
public static class GridDemoData
{
    /// <summary>mulberry32, bit-identical to the web demo's rng().</summary>
    public static Func<double> Rng(uint seed)
    {
        var s = seed;
        return () =>
        {
            s = unchecked(s + 0x6d2b79f5);
            var t = s;
            t = unchecked((t ^ (t >> 15)) * (t | 1));
            t ^= unchecked(t + (t ^ (t >> 7)) * (t | 61));
            return (t ^ (t >> 14)) / 4294967296.0;
        };
    }

    private static TItem Pick<TItem>(Func<double> r, IReadOnlyList<TItem> list) => list[(int)Math.Floor(r() * list.Count)];

    private static double JsRound(double x) => Math.Floor(x + 0.5);

    public static readonly string[] Services = ["api-gateway", "billing", "search", "auth", "ingest", "notifications", "web", "reports", "media", "scheduler", "ledger", "graph"];
    public static readonly string[] Regions = ["eu-west-1", "eu-central-1", "us-east-1", "us-west-2", "ap-southeast-2", "ap-northeast-1"];
    public static readonly string[] Envs = ["production", "staging", "preview"];
    public static readonly string[] Statuses = ["Running", "Succeeded", "Queued", "Failed", "Cancelled"];
    public static readonly string[] Owners = ["Ada Lovelace", "Grace Hopper", "Alan Turing", "Katherine Johnson", "Linus Torvalds", "Margaret Hamilton", "Dennis Ritchie", "Barbara Liskov", "Ken Thompson", "Frances Allen"];
    private static readonly double[] StatusWeights = [0.08, 0.72, 0.06, 0.09, 0.05];

    public static readonly IReadOnlyDictionary<string, Tone> StatusTones = new Dictionary<string, Tone>
    {
        ["Running"] = Tone.Accent, ["Succeeded"] = Tone.Success, ["Queued"] = Tone.Neutral, ["Failed"] = Tone.Danger, ["Cancelled"] = Tone.Warning,
    };

    public static readonly IReadOnlyDictionary<string, Tone> EnvTones = new Dictionary<string, Tone>
    {
        ["production"] = Tone.Danger, ["staging"] = Tone.Warning, ["preview"] = Tone.Info,
    };

    private static string StatusFor(double r)
    {
        var acc = 0.0;
        for (var i = 0; i < Statuses.Length; i++)
        {
            acc += StatusWeights[i];
            if (r < acc) return Statuses[i];
        }
        return "Succeeded";
    }

    public static Deployment[] Deployments(int count)
    {
        var r = Rng(42);
        var now = new DateTime(2026, 10, 6, 12, 0, 0, DateTimeKind.Utc);
        var output = new Deployment[count];
        for (var i = 0; i < count; i++)
        {
            var status = StatusFor(r());
            var progress = status switch
            {
                "Succeeded" => 100,
                "Queued" => 0,
                "Running" => JsRound(5 + r() * 90),
                _ => JsRound(r() * 80),
            };
            var latency = new double[12];
            for (var j = 0; j < 12; j++) latency[j] = JsRound(40 + r() * 160);
            output[i] = new Deployment
            {
                Id = $"DEP-{(count - i).ToString(CultureInfo.InvariantCulture).PadLeft(6, '0')}",
                Service = Pick(r, Services),
                Region = Pick(r, Regions),
                Env = Pick(r, Envs),
                Status = status,
                Owner = Pick(r, Owners),
                Commit = ((long)Math.Floor(r() * 0xfffffff)).ToString("x", CultureInfo.InvariantCulture).PadLeft(7, '0'),
                Progress = progress,
                Duration = JsRound(30 + r() * 900),
                Errors = status == "Failed" ? JsRound(1 + r() * 40) : r() < 0.1 ? JsRound(r() * 3) : 0,
                Cost = JsRound(r() * 4000) / 100,
                Started = now.AddMilliseconds(-Math.Floor(r() * 90 * 24 * 3600 * 1000)),
                Canary = r() < 0.2,
                Latency = latency,
            };
        }
        return output;
    }

    public static AuditEvent[] Audit(int count)
    {
        var r = Rng(7);
        var start = new DateTime(2026, 10, 6, 12, 0, 0, DateTimeKind.Utc);
        string[] actions = ["login", "logout", "deploy", "rollback", "invite", "rotate-key", "update-policy", "delete"];
        var output = new AuditEvent[count];
        for (var i = 0; i < count; i++)
        {
            output[i] = new AuditEvent
            {
                Seq = count - i,
                At = start.AddMilliseconds(-i * 37_000.0),
                Actor = Pick(r, Owners),
                Action = Pick(r, actions),
                Resource = $"{Pick(r, Services)}/{Pick(r, Envs)}",
                Ip = $"10.{Math.Floor(r() * 255)}.{Math.Floor(r() * 255)}.{Math.Floor(r() * 255)}",
                Severity = r() < 0.06 ? "High" : r() < 0.25 ? "Medium" : "Low",
            };
        }
        return output;
    }

    public static readonly IReadOnlyList<DataGridColumn<AuditEvent>> AuditColumns =
    [
        new GridColumn<AuditEvent> { Field = "Seq", Title = "#", Width = 90, Type = GridColumnType.Number, Format = "0" },
        new GridColumn<AuditEvent> { Field = "At", Title = "Time", Width = 184, Type = GridColumnType.Date, Format = "yyyy-MM-dd HH:mm:ss" },
        new GridColumn<AuditEvent> { Field = "Actor", Title = "Actor", Flex = 1, MinWidth = 130 },
        new GridColumn<AuditEvent> { Field = "Action", Title = "Action", Width = 130, Type = GridColumnType.Enum },
        new GridColumn<AuditEvent> { Field = "Resource", Title = "Resource", Flex = 1, MinWidth = 150 },
        new GridColumn<AuditEvent>
        {
            Field = "Severity", Title = "Severity", Width = 110, Type = GridColumnType.Enum, EnumOrder = ["Low", "Medium", "High"],
            EnumTones = new Dictionary<string, Tone> { ["Low"] = Tone.Neutral, ["Medium"] = Tone.Warning, ["High"] = Tone.Danger },
        },
    ];

    public static RepoNode[] Tree()
    {
        var r = Rng(3);
        DateTime Day(int n) => new DateTime(2026, 10, 6, 0, 0, 0, DateTimeKind.Utc).AddDays(-n);
        RepoNode File(string name, double kb) => new()
        {
            Name = name, Kind = "file", Size = JsRound(kb * 1024 * (0.6 + r() * 0.8)), Files = 1, Modified = Day((int)Math.Floor(r() * 60)),
        };
        RepoNode Folder(string name, params RepoNode[] children) => new()
        {
            Name = name, Kind = "folder", Children = children, Size = children.Sum(c => c.Size), Files = children.Sum(c => c.Files),
            Modified = children.Max(c => c.Modified),
        };
        static RepoNode[] WithPaths(RepoNode[] nodes, string parent = "")
        {
            foreach (var n in nodes)
            {
                n.Path = $"{parent}/{n.Name}";
                if (n.Children is not null) WithPaths(n.Children, n.Path);
            }
            return nodes;
        }
        return WithPaths(
        [
            Folder("src",
                Folder("Slate.Core", Folder("Data", File("DataPipeline.cs", 38), File("GridState.cs", 22), File("SelectionModel.cs", 11), File("EditSession.cs", 9)), File("Tokens.cs", 64), File("Positioning.cs", 18)),
                Folder("Slate.Blazor", File("SlDataGrid.razor", 41), File("SlButton.razor", 4), File("SlDialog.razor", 9)),
                Folder("Slate.Wpf", File("DataGrid.xaml", 52), File("Theme.xaml", 120))),
            Folder("packages",
                Folder("web",
                    Folder("src",
                        Folder("components", File("data-grid.ts", 74), File("data-grid-cells.ts", 21), File("select.ts", 26)),
                        Folder("core", Folder("grid", File("pipeline.ts", 34), File("layout.ts", 9), File("state.ts", 15))),
                        Folder("styles", File("datagrid.css", 18), File("tokens.css", 96))),
                    File("package.json", 2))),
            Folder("design", Folder("tokens", File("color.json", 44), File("typography.json", 12), File("icons.json", 31)), File("api.json", 58)),
            Folder("docs", File("data-grid.md", 14), File("css-classes.md", 36)),
            File("README.md", 7),
        ]);
    }

    public static string FormatBytes(object? value)
    {
        if (GridValues.ToDouble(value) is not { } n || !double.IsFinite(n)) return "";
        if (n < 1024) return $"{n.ToString(CultureInfo.InvariantCulture)} B";
        if (n < 1024 * 1024) return $"{(n / 1024).ToString("F1", CultureInfo.InvariantCulture)} KB";
        return $"{(n / 1024 / 1024).ToString("F2", CultureInfo.InvariantCulture)} MB";
    }
}

public sealed record Deployment
{
    public string Id { get; set; } = "";
    public string Service { get; set; } = "";
    public string Region { get; set; } = "";
    public string Env { get; set; } = "";
    public string Status { get; set; } = "";
    public string Owner { get; set; } = "";
    public string Commit { get; set; } = "";
    public double Progress { get; set; }
    public double Duration { get; set; }
    public double Errors { get; set; }
    public double Cost { get; set; }
    public DateTime Started { get; set; }
    public bool Canary { get; set; }
    public double[] Latency { get; set; } = [];
}

public sealed class AuditEvent
{
    public int Seq { get; init; }
    public DateTime At { get; init; }
    public string Actor { get; init; } = "";
    public string Action { get; init; } = "";
    public string Resource { get; init; } = "";
    public string Ip { get; init; } = "";
    public string Severity { get; init; } = "";
}

public sealed class RepoNode
{
    public string Path { get; set; } = "";
    public string Name { get; init; } = "";
    public string Kind { get; init; } = "file";
    public double Size { get; init; }
    public int Files { get; init; }
    public DateTime Modified { get; init; }
    public RepoNode[]? Children { get; init; }
}
