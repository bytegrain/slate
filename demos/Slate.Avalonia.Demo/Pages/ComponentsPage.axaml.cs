using Avalonia.Controls;

namespace Slate.Avalonia.Demo.Pages;

public partial class ComponentsPage : UserControl
{
    public ComponentsPage()
    {
        InitializeComponent();
        ToggleLoading.Click += (_, _) => Sl.SetLoading(LoadingButton, !Sl.GetLoading(LoadingButton));
        EmailField.ValueChanged += (_, _) =>
            EmailField.Error = EmailField.Value is { } t && t.Contains('@') && t.Contains('.') ? null : "Enter a complete email address.";
    }
}
