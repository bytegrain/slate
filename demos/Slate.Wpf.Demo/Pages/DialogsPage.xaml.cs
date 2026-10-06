using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Slate.Dialogs;
using MessageBoxOptions = Slate.Dialogs.MessageBoxOptions;

namespace Slate.Wpf.Demo.Pages;

public partial class DialogsPage : UserControl
{
    private static IDialogService Dialogs => SlateServices.Dialogs;

    public DialogsPage() => InitializeComponent();

    private void Show(object value) => Result.Text = value switch
    {
        DialogResult { Canceled: true } => "Canceled",
        DialogResult r => $"Ok: {r.Data ?? "(no data)"}",
        _ => value.ToString() ?? "",
    };

    /// <summary>A form dialog built from Slate parts; the footer buttons use DialogCommands.</summary>
    private async void OnForm(object sender, RoutedEventArgs e)
    {
        var name = new TextField { Label = "Package name", Value = "Slate.Wpf", HelperText = "Shown on NuGet.", Counter = true, MaxLength = 40 };
        FocusManager.SetFocusedElement(name, name);
        var ok = new Button { Content = "Rename", IsDefault = true, Command = DialogCommands.Ok };
        Sl.SetVariant(ok, ButtonVariant.Solid);
        Sl.SetTone(ok, Tone.Accent);
        ok.SetBinding(System.Windows.Controls.Primitives.ButtonBase.CommandParameterProperty, new System.Windows.Data.Binding(nameof(TextField.Value)) { Source = name });

        var content = new DialogContent
        {
            // DialogContent options override the DialogOptions the dialog was opened with.
            Icon = "pencil",
            Tone = Tone.Accent,
            Description = "Renaming keeps the package's download history.",
            Content = new Stack { Spacing = 4, Children = { name, new TextField { Label = "Description", Multiline = true, Placeholder = "Optional" } } },
            Footer = new Stack
            {
                Direction = Direction.Row,
                Spacing = 2,
                Children = { new Button { Content = "Cancel", Command = DialogCommands.Cancel }, ok },
            },
        };

        Show(await Dialogs.ShowAsync(content, new DialogOptions { Title = "Rename package", CloseOnBackdropClick = false }));
    }

    private async void OnConfirm(object sender, RoutedEventArgs e) =>
        Show(await Dialogs.ConfirmAsync(new MessageBoxOptions
        {
            Title = "Publish release?",
            Message = "Slate 0.1.4 will be pushed to npm and NuGet.",
            ConfirmText = "Publish",
            Severity = Severity.Info,
        }) ? "Confirmed" : "Canceled");

    private async void OnDestructive(object sender, RoutedEventArgs e) =>
        Show(await Dialogs.ConfirmAsync(new MessageBoxOptions
        {
            Title = "Delete Slate.Wpf?",
            Message = "This removes the package and its 14 versions from the feed. This can't be undone.",
            ConfirmText = "Delete package",
            Destructive = true,
        }) ? "Deleted" : "Kept");

    private async void OnAlert(object sender, RoutedEventArgs e)
    {
        await Dialogs.AlertAsync("Export complete", "Saved 2,481 rows to exports/players.csv.", Severity.Success);
        Show("Acknowledged");
    }

    private async void OnStacked(object sender, RoutedEventArgs e)
    {
        var open = new Button { Content = "Open another dialog", HorizontalAlignment = HorizontalAlignment.Left };
        open.Click += async (_, _) =>
            await Dialogs.ConfirmAsync(new MessageBoxOptions { Title = "Second dialog", Message = "Escape closes only this one.", CancelText = null });

        var content = new DialogContent
        {
            Content = new Stack
            {
                Spacing = 3,
                Children = { new TextBlock { Text = "Dialogs stack. Only the top one is interactive.", TextWrapping = TextWrapping.Wrap }, open },
            },
            Footer = new Button { Content = "Done", Command = DialogCommands.Ok },
        };
        Show(await Dialogs.ShowAsync(content, new DialogOptions { Title = "First dialog" }));
    }

    private async void OnWide(object sender, RoutedEventArgs e) =>
        Show(await Dialogs.ShowAsync(
            new TextBlock { Text = "MaxWidth = Lg (880px), FullWidth = true.", TextWrapping = TextWrapping.Wrap },
            new DialogOptions { Title = "Wide dialog", MaxWidth = DialogWidth.Lg, FullWidth = true }));

    private async void OnTop(object sender, RoutedEventArgs e)
    {
        var search = new TextBox();
        Sl.SetPlaceholder(search, "Type a command…");
        FocusManager.SetFocusedElement(search, search);
        Show(await Dialogs.ShowAsync(search, new DialogOptions { Title = "Command palette", Placement = DialogPlacement.Top, MaxWidth = DialogWidth.Md, FullWidth = true }));
    }
}
