using System.Windows;
using System.Windows.Controls;

namespace Slate.Wpf.Demo.Pages;

public partial class LayoutPage : UserControl
{
    public LayoutPage() => InitializeComponent();

    private void OnDrawerVariant(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string mode } && Window.GetWindow(this) is MainWindow window)
        {
            window.AppShell.DrawerVariant = Enum.Parse<DrawerVariant>(mode);
            window.AppShell.DrawerOpen = true;
        }
    }
}
