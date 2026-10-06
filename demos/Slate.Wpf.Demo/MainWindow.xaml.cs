using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Slate.Wpf.Demo.Pages;

namespace Slate.Wpf.Demo;

public partial class MainWindow : SlateWindow
{
    private readonly Dictionary<string, Func<UserControl>> _pages = new()
    {
        ["Components"] = () => new ComponentsPage(),
        ["Pickers"] = () => new PickersPage(),
        ["Layout"] = () => new LayoutPage(),
        ["Feedback"] = () => new FeedbackPage(),
        ["Dialogs"] = () => new DialogsPage(),
        ["Theming"] = () => new ThemingPage(),
        ["Playground"] = () => new PlaygroundPage(),
        ["Files"] = () => new FileBrowserPage(),
    };

    private bool _syncingNav;

    public MainWindow()
    {
        InitializeComponent();

        var theme = SlateTheme.Current!;
        (theme.Mode switch
        {
            Slate.ThemeMode.Dark => ThemeDark,
            Slate.ThemeMode.Light => ThemeLight,
            _ => ThemeSystem,
        }).IsChecked = true;
        Comfortable.IsChecked = theme.Density == Density.Comfortable;

        Nav.SelectedIndex = 0;
        InputBindings.Add(new KeyBinding(new RelayCommand(() => Search.Focus()), Key.K, ModifierKeys.Control));
    }

    /// <summary>Lets pages (e.g. Layout) change the drawer mode.</summary>
    public AppShell AppShell => Shell;

    private void OnThemeChecked(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string mode } && SlateTheme.Current is { } theme)
            theme.Mode = Enum.Parse<Slate.ThemeMode>(mode);
    }

    private void OnDensityChanged(object sender, RoutedEventArgs e)
    {
        if (SlateTheme.Current is { } theme)
            theme.Density = Comfortable.IsChecked == true ? Density.Comfortable : Density.Compact;
    }

    private void OnNavigate(object sender, SelectionChangedEventArgs e)
    {
        if (_syncingNav || sender is not ListBox list || list.SelectedItem is not NavItem { Tag: string key })
            return;

        // Two nav lists act as one selection.
        _syncingNav = true;
        (ReferenceEquals(list, Nav) ? Examples : Nav).SelectedItem = null;
        _syncingNav = false;

        PageHost.Content = _pages[key]();
        if (Shell.Overlay)
            Shell.DrawerOpen = false;
    }
}

/// <summary>Minimal ICommand for the demo.</summary>
public sealed class RelayCommand(Action execute) : ICommand
{
    public event EventHandler? CanExecuteChanged { add { } remove { } }
    public bool CanExecute(object? parameter) => true;
    public void Execute(object? parameter) => execute();
}
