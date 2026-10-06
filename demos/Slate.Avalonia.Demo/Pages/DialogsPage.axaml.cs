using Avalonia.Controls;
using Slate.Avalonia.Controls;
using Slate.Avalonia.Services;
using Slate.Dialogs;

namespace Slate.Avalonia.Demo.Pages;

public partial class DialogsPage : UserControl
{
    public DialogsPage()
    {
        InitializeComponent();
        var dialogs = SlateServices.Dialogs;

        Form.Click += async (_, _) =>
        {
            var name = new TextField { Label = "Package name", Text = "Slate.Wpf", HelperText = "Shown on NuGet." };
            DialogHost.SetAutoFocus(name, true);
            var save = new Button { Content = "Rename", Classes = { "primary" }, IsDefault = true };
            save.Click += (_, _) => DialogHost.Close(save, DialogResult.Ok(name.Text));
            var cancel = new Button { Content = "Cancel" };
            DialogHost.SetCloseWith(cancel, DialogCloseAction.Cancel);

            var result = await dialogs.ShowAsync(new DialogContent
            {
                Description = "Renaming keeps all published versions.",
                Content = new StackPanel { Spacing = 12, Children = { name, new CheckBox { Content = "Redirect the old ID" } } },
                Footer = new StackPanel { Orientation = global::Avalonia.Layout.Orientation.Horizontal, Spacing = 8, Children = { cancel, save } },
            }, new DialogOptions { Title = "Rename package", CloseOnBackdropClick = false });
            Show(result.Canceled ? "cancelled" : $"renamed to {result.Data}");
        };

        Confirm.Click += async (_, _) => Show($"confirmed = {await dialogs.ConfirmAsync(new MessageBoxOptions
        {
            Title = "Publish 0.1.4?",
            Message = "This pushes Slate.Avalonia to NuGet.",
            ConfirmText = "Publish",
        })}");

        Destructive.Click += async (_, _) => Show($"deleted = {await dialogs.ConfirmAsync(new MessageBoxOptions
        {
            Title = "Delete Slate.Wpf?",
            Message = "This removes the package and its 14 versions from the feed. This can't be undone.",
            ConfirmText = "Delete package",
            Destructive = true,
        })}");

        Alert.Click += async (_, _) =>
        {
            await dialogs.AlertAsync("Your export is ready in Downloads.", "Export complete");
            Show("acknowledged");
        };

        Stacked.Click += async (_, _) =>
        {
            var open = new Button { Content = "Open another dialog", Classes = { "primary" } };
            open.Click += async (_, _) => await dialogs.ConfirmAsync(new MessageBoxOptions { Title = "Second dialog", Message = "Only the top dialog is interactive. Escape closes this one first." });
            var result = await dialogs.ShowAsync(new StackPanel
            {
                Spacing = 12,
                Children = { new TextBlock { Text = "The first dialog.", TextWrapping = global::Avalonia.Media.TextWrapping.Wrap }, open },
            }, new DialogOptions { Title = "First dialog", MaxWidth = DialogWidth.Md });
            Show($"stack closed (cancelled = {result.Canceled})");
        };
    }

    private void Show(string text) => Result.Text = $"Result: {text}";
}
