using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Styling;
using static Slate.Avalonia.Tests.TestHelpers;

namespace Slate.Avalonia.Tests;

public class ThemeTests
{
    [AvaloniaTheory]
    [InlineData("Sl.Brush.Background.Canvas", SlateTokens.Themes.Light.Color.Background.Canvas, SlateTokens.Themes.Dark.Color.Background.Canvas)]
    [InlineData("Sl.Brush.Text.Primary", SlateTokens.Themes.Light.Color.Text.Primary, SlateTokens.Themes.Dark.Color.Text.Primary)]
    [InlineData("Sl.Brush.Accent.Default", SlateTokens.Themes.Light.Color.Accent.Default, SlateTokens.Themes.Dark.Color.Accent.Default)]
    [InlineData("Sl.Brush.Focus.Ring", SlateTokens.Themes.Light.Color.Focus.Ring, SlateTokens.Themes.Dark.Color.Focus.Ring)]
    public void Semantic_brushes_resolve_per_theme_variant(string key, string light, string dark)
    {
        Assert.Equal(Token(light), ColorOf((IBrush)Resource(key, ThemeVariant.Light)));
        Assert.Equal(Token(dark), ColorOf((IBrush)Resource(key, ThemeVariant.Dark)));
    }

    [AvaloniaFact]
    public void Shared_tokens_are_available_with_xaml_types()
    {
        Assert.Equal(16.0, Resource("Sl.Space.4"));
        Assert.Equal(new Thickness(16), Resource("Sl.Thickness.4"));
        Assert.Equal(new CornerRadius(6), Resource("Sl.CornerRadius.Md"));
        Assert.Equal(32.0, Resource("Sl.Size.Control.Md"));
        Assert.IsType<BoxShadows>(Resource("Sl.Shadow.E3"));
        Assert.IsType<BoxShadows>(Resource("Sl.Shadow.FocusRing", ThemeVariant.Dark));
    }

    [AvaloniaFact]
    public void Ui_font_is_the_embedded_instrument_sans()
    {
        var family = Assert.IsType<FontFamily>(Resource("Sl.Font.Family.Ui"));
        Assert.Contains("Instrument Sans", family.ToString());
        Assert.StartsWith("avares://Slate.Avalonia/Assets/Fonts", family.Key?.ToString() ?? family.ToString());
    }

    [AvaloniaFact]
    public void Window_uses_canvas_and_switches_with_requested_theme_variant()
    {
        var window = Show(new Border());
        try
        {
            window.RequestedThemeVariant = ThemeVariant.Light;
            Pump(window);
            Assert.Equal(Token(SlateTokens.Themes.Light.Color.Background.Canvas), ColorOf(window.Background));

            window.RequestedThemeVariant = ThemeVariant.Dark;
            Pump(window);
            Assert.Equal(Token(SlateTokens.Themes.Dark.Color.Background.Canvas), ColorOf(window.Background));
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void Mode_sets_the_application_theme_variant()
    {
        var theme = SlateTheme.Current!;
        var before = Application.Current!.RequestedThemeVariant;
        try
        {
            theme.Mode = ThemeMode.Dark;
            Assert.Equal(ThemeVariant.Dark, Application.Current.RequestedThemeVariant);
            theme.Mode = ThemeMode.System;
            Assert.Equal(ThemeVariant.Default, Application.Current.RequestedThemeVariant);
        }
        finally { Application.Current.RequestedThemeVariant = before; }
    }

    [AvaloniaFact]
    public void Density_changes_control_heights()
    {
        var button = new Button { Content = "Save" };
        var window = Show(button);
        try
        {
            Assert.Equal(32, button.Bounds.Height);
            WithTheme(t => t.Density = Density.Comfortable, t => t.Density = Density.Compact, () =>
            {
                Pump(window);
                Assert.Equal(40, button.Bounds.Height);
                Assert.Equal(44.0, Resource("Sl.Switch.TrackWidth"));
            });
            Pump(window);
            Assert.Equal(32, button.Bounds.Height);
            Assert.Equal(36.0, Resource("Sl.Switch.TrackWidth"));
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void Reduce_motion_removes_switch_transitions()
    {
        var toggle = new ToggleSwitch();
        var window = Show(toggle);
        try
        {
            Assert.NotNull(toggle.KnobTransitions);
            WithTheme(t => t.ReduceMotion = true, t => t.ReduceMotion = false, () =>
            {
                Pump(window);
                Assert.Null(toggle.KnobTransitions);
            });
            Pump(window);
            Assert.NotNull(toggle.KnobTransitions);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void Typography_themes_apply_token_metrics()
    {
        var text = new TextBlock { Text = "Title", Theme = (ControlTheme)Resource("Sl.Typography.H2") };
        var window = Show(text);
        try
        {
            Assert.Equal(SlateTokens.Typography.H2.FontSize, text.FontSize);
            Assert.Equal(FontWeight.SemiBold, text.FontWeight);
        }
        finally { window.Close(); }
    }
}
