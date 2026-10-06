using Avalonia.Controls;

namespace Slate.Avalonia.Demo.Pages;

public partial class ComponentsPage : UserControl
{
    public ComponentsPage()
    {
        InitializeComponent();
        ToggleLoading.Click += (_, _) => Sl.SetIsLoading(LoadingButton, !Sl.GetIsLoading(LoadingButton));
        EmailField.PropertyChanged += (_, e) =>
        {
            if (e.Property == Controls.TextField.TextProperty)
                EmailField.Error = EmailField.Text is { } t && t.Contains('@') && t.Contains('.') ? null : "Enter a complete email address.";
        };
    }
}
