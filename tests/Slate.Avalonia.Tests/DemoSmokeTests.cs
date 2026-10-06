using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Styling;
using Avalonia.VisualTree;
using Slate.Avalonia.Controls;
using Slate.Avalonia.Demo;
using static Slate.Avalonia.Tests.TestHelpers;

namespace Slate.Avalonia.Tests;

/// <summary>Opens the demo app headlessly and visits every page in both themes, so runtime XAML errors fail CI.</summary>
public class DemoSmokeTests
{
    [AvaloniaTheory]
    [InlineData("Light")]
    [InlineData("Dark")]
    public void Every_demo_page_loads_and_lays_out(string variant)
    {
        var window = new MainWindow { RequestedThemeVariant = variant == "Dark" ? ThemeVariant.Dark : ThemeVariant.Light };
        window.Show();
        Pump(window);
        try
        {
            var nav = window.GetVisualDescendants().OfType<NavItem>().ToList();
            Assert.Equal(9, nav.Count); // components, inputs, navigation, overlays, layout, feedback, dialogs, theming, sample
            foreach (var item in nav)
            {
                window.Click(item);
                Assert.True(item.Active, item.Label);
                var page = window.Part<ContentControl>("Page");
                var content = Assert.IsType<ScrollViewer>(page.Content).Content as Control;
                Assert.NotNull(content);
                Assert.True(content!.Bounds.Height > 100, $"{item.Label} did not lay out");
            }
        }
        finally { window.Close(); }
    }
}
