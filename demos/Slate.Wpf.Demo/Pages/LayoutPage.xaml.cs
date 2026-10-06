using System.Windows;
using System.Windows.Controls;

namespace Slate.Wpf.Demo.Pages;

public partial class LayoutPage : UserControl
{
    public LayoutPage() => InitializeComponent();

    private void OnDrawerMode(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string mode } && Window.GetWindow(this) is MainWindow window)
        {
            window.AppShell.DrawerMode = Enum.Parse<DrawerMode>(mode);
            window.AppShell.IsDrawerOpen = true;
        }
    }
}
