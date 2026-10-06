using Avalonia;
using Avalonia.Headless;
using Slate.Avalonia;
using Slate.Avalonia.Tests;

[assembly: AvaloniaTestApplication(typeof(TestAppBuilder))]

namespace Slate.Avalonia.Tests;

public class TestApp : Application
{
    public override void Initialize() => Styles.Add(new SlateTheme());
}

public static class TestAppBuilder
{
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<TestApp>()
        .UseHeadless(new AvaloniaHeadlessPlatformOptions());
}
