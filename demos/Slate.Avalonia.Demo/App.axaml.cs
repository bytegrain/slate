using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Microsoft.Extensions.DependencyInjection;
using Slate.Avalonia.Services;

namespace Slate.Avalonia.Demo;

public partial class App : Application
{
    public static IServiceProvider Services { get; private set; } = null!;

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        // DI registration: SlateWindow picks the services up through SlateServices automatically.
        Services = new ServiceCollection().AddSlate().BuildServiceProvider();

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var window = new MainWindow();
            desktop.MainWindow = window;

            // `--screenshot <dir>`: render every page in light and dark to PNGs, then exit.
            // Renders the window's own visual tree, so it needs no screen-recording permission.
            var args = desktop.Args ?? [];
            var i = Array.IndexOf(args, "--screenshot");
            if (i >= 0)
            {
                var dir = i + 1 < args.Length ? args[i + 1] : "screenshots";
                window.Opened += async (_, _) =>
                {
                    await window.CaptureAllAsync(dir);
                    desktop.Shutdown();
                };
            }
        }
        base.OnFrameworkInitializationCompleted();
    }
}
