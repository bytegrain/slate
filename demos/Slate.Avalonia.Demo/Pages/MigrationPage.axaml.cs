using Avalonia.Controls;
using Avalonia.Layout;
using Slate.Avalonia.Controls;

namespace Slate.Avalonia.Demo.Pages;

public partial class MigrationPage : UserControl
{
    private static readonly (string Path, string Size, double Progress, string Speed, string State, Tone Tone)[] Rows =
    [
        (@"textures\env\cliffs_4k\albedo.png", "88.2 MB", 0.74, "112 MB/s", "Copying", Tone.Info),
        (@"builds\2026-10-03\Rust_Client.zip", "14.1 GB", 0.31, "406 MB/s", "Copying", Tone.Info),
        (@"audio\ambience\forest_night.wav", "1.2 GB", 1.0, "—", "Verified", Tone.Success),
        (@"models\vehicles\minicopter.fbx", "312 MB", 0.12, "38 MB/s", "Retrying", Tone.Warning),
        (@"source\Slate.Wpf\Slate.Wpf.csproj", "6 KB", 0, "—", "Queued", Tone.Neutral),
    ];

    public MigrationPage()
    {
        InitializeComponent();

        for (var i = 0; i < 20; i++)
        {
            var seg = new Border { Margin = new global::Avalonia.Thickness(1, 0) };
            seg.Classes.Add("seg");
            seg.Classes.Set("on", i < 12);
            Gauge.Children.Add(seg);
        }

        foreach (var (path, size, progress, speed, state, tone) in Rows)
        {
            var row = new Border();
            row.Classes.Add("row");
            var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,70,90,80,84"), VerticalAlignment = VerticalAlignment.Center };
            grid.Children.Add(Text(path, mono: true, trim: true));
            grid.Children.Add(Column(Text(size, mono: true), 1));
            grid.Children.Add(Column(new ProgressBar { Value = progress * 100, MinWidth = 60, Margin = new global::Avalonia.Thickness(0, 0, 12, 0) }, 2));
            grid.Children.Add(Column(Text(speed, mono: true), 3));
            grid.Children.Add(Column(new Badge { Content = state, Tone = tone }, 4));
            row.Child = grid;
            Transfers.Children.Add(row);
        }

        Log.Text = string.Join('\n',
            "14:02:11  INFO   worker-3  copied textures/env/cliffs_4k/normal.png (91.4 MB)",
            "14:02:11  INFO   worker-7  verified audio/ambience/forest_night.wav (sha256 ok)",
            "14:02:12  WARN   worker-1  retry 2/5 models/vehicles/minicopter.fbx: connection reset",
            "14:02:12  ERROR  scanner   access denied \\\\fs01\\projects\\_secure",
            "14:02:13  INFO   checkpoint saved (114,207 files)");
    }

    private static TextBlock Text(string text, bool mono = false, bool trim = false)
    {
        var t = new TextBlock
        {
            Text = text,
            FontSize = 12.5,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = trim ? global::Avalonia.Media.TextTrimming.CharacterEllipsis : global::Avalonia.Media.TextTrimming.None,
        };
        if (mono)
            t.Classes.Add("mono");
        return t;
    }

    private static Control Column(Control c, int column)
    {
        Grid.SetColumn(c, column);
        return c;
    }
}
