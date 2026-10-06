using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Slate.Avalonia.Demo.Pages;

public partial class OverlaysPage : UserControl
{
    public OverlaysPage()
    {
        InitializeComponent();
        MenuStatus.Text = "Pick a menu item";
        // Menu popups route outside the page, so listen on the menu itself.
        Actions.AddHandler(MenuItem.ClickEvent, (_, e) =>
        {
            if (e.Source is MenuItem { Header: string header }) MenuStatus.Text = $"Chose “{header}”";
        }, RoutingStrategies.Bubble);
    }
}
