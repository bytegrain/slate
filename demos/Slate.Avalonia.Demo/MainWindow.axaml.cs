using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Slate.Avalonia.Controls;
using Slate.Avalonia.Demo.Pages;

namespace Slate.Avalonia.Demo;

public partial class MainWindow : SlateWindow
{
    private readonly Dictionary<string, Func<Control>> _pages = new()
    {
        ["components"] = () => new ComponentsPage(),
        ["layout"] = () => new LayoutPage(),
        ["feedback"] = () => new FeedbackPage(),
        ["dialogs"] = () => new DialogsPage(),
        ["sample"] = () => new MigrationPage(),
    };

    public MainWindow()
    {
        InitializeComponent();

        foreach (var item in Nav.Children.OfType<NavItem>())
            item.Click += (_, _) => Navigate(item);
        Navigate(Nav.Children.OfType<NavItem>().First());

        LightButton.Click += (_, _) => SetMode(ThemeMode.Light);
        DarkButton.Click += (_, _) => SetMode(ThemeMode.Dark);
        SystemButton.Click += (_, _) => SetMode(ThemeMode.System);
        SetMode(ThemeMode.System);

        DensitySwitch.IsCheckedChanged += (_, _) =>
        {
            if (SlateTheme.Current is { } theme)
                theme.Density = DensitySwitch.IsChecked == true ? Density.Comfortable : Density.Compact;
        };
    }

    private void SetMode(ThemeMode mode)
    {
        if (SlateTheme.Current is { } theme)
            theme.Mode = mode;
        LightButton.IsChecked = mode == ThemeMode.Light;
        DarkButton.IsChecked = mode == ThemeMode.Dark;
        SystemButton.IsChecked = mode == ThemeMode.System;
    }

    private void Navigate(NavItem item)
    {
        foreach (var n in Nav.Children.OfType<NavItem>())
            n.IsActive = n == item;
        Bar.Title = item.Label;
        Page.Content = new ScrollViewer { Content = _pages[(string)item.Tag!]() };
        if (Shell.DrawerMode == DrawerMode.Temporary)
            Shell.IsDrawerOpen = false;
    }
}
