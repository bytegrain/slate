using System.Windows.Controls;

namespace Slate.Wpf.Demo.Pages;

public partial class ComponentsPage : UserControl
{
    public ComponentsPage()
    {
        InitializeComponent();
        IconGallery.ItemsSource = SlateIcons.All.Keys.Order().ToList();
    }
}
