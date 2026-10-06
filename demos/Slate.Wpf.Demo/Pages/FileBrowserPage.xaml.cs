using System.Windows;
using System.Windows.Controls;
using Slate.Snackbars;
using MessageBoxOptions = Slate.Dialogs.MessageBoxOptions;

namespace Slate.Wpf.Demo.Pages;

public sealed record FileRow(string Name, string Kind, long Bytes, string Modified)
{
    public bool IsFolder => Kind == "Folder";
    public string Icon => IsFolder ? "folder" : "file";

    public string SizeText => IsFolder ? "—" : Bytes switch
    {
        >= 1L << 30 => $"{Bytes / (double)(1L << 30):0.0} GB",
        >= 1L << 20 => $"{Bytes / (double)(1L << 20):0.0} MB",
        >= 1L << 10 => $"{Bytes / 1024.0:0} KB",
        _ => $"{Bytes} B",
    };
}

/// <summary>Realistic Alloy desktop screen: a studio asset browser built only from Slate parts.</summary>
public partial class FileBrowserPage : UserControl
{
    private static readonly FileRow[] Sample =
    [
        new("builds", "Folder", 0, "Today, 09:12"),
        new("textures", "Folder", 0, "Yesterday"),
        new("audio", "Folder", 0, "2 Oct"),
        new("hero_rifle_albedo.psd", "Photoshop", 412_000_000, "Today, 10:42"),
        new("hero_rifle.fbx", "FBX model", 38_400_000, "Today, 10:31"),
        new("hero_rifle_normal.png", "PNG image", 22_100_000, "Today, 10:30"),
        new("Slate.Wpf.csproj", "C# project", 2_048, "Today, 09:58"),
        new("trailer_v3.mp4", "MPEG-4 video", 1_920_000_000, "1 Oct"),
        new("level_harbor.umap", "Unreal map", 156_000_000, "30 Sep"),
        new("release_notes.md", "Markdown", 6_200, "29 Sep"),
        new("palette.json", "JSON", 12_800, "29 Sep"),
    ];

    public FileBrowserPage()
    {
        InitializeComponent();
        Files.ItemsSource = Sample;
        Files.SelectedItems.Add(Sample[3]);
        Files.SelectedItems.Add(Sample[4]);
        Files.SelectedItems.Add(Sample[5]);
        UpdateStatus();
    }

    private void OnSelectionChanged(object sender, SelectionChangedEventArgs e) => UpdateStatus();

    private void UpdateStatus()
    {
        var selected = Files.SelectedItems.Cast<FileRow>().ToList();
        var bytes = selected.Sum(f => f.Bytes);
        StatusText.Text = selected.Count == 0
            ? $"{Sample.Length} items"
            : $"{Sample.Length} items · {selected.Count} selected ({new FileRow("", "", bytes, "").SizeText})";

        var focus = selected.LastOrDefault() ?? Sample[3];
        InspectorName.Text = focus.Name;
        InspectorKind.Text = focus.Kind;
        InspectorSize.Text = focus.SizeText;
        InspectorModified.Text = focus.Modified;
        PreviewIcon.Kind = focus.Icon;
    }

    private void OnShare(object sender, RoutedEventArgs e) =>
        SlateServices.Snackbar.Add(new SnackbarOptions { Message = "Share link copied to clipboard", Severity = Severity.Success });

    private async void OnDelete(object sender, RoutedEventArgs e)
    {
        var count = Math.Max(1, Files.SelectedItems.Count);
        var confirmed = await SlateServices.Dialogs.ConfirmAsync(new MessageBoxOptions
        {
            Title = $"Delete {count} item{(count == 1 ? "" : "s")}?",
            Message = "They'll be moved to the studio bin for 30 days.",
            ConfirmText = "Delete",
            Destructive = true,
        });
        if (confirmed)
        {
            SlateServices.Snackbar.Add(new SnackbarOptions
            {
                Message = $"Moved {count} item{(count == 1 ? "" : "s")} to the bin",
                Action = SnackbarAction.Create("Undo", () => SlateServices.Snackbar.Add("Restored", Severity.Success)),
            });
        }
    }
}
