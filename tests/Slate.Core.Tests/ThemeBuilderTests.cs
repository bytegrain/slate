using Slate.Theming;

namespace Slate.Core.Tests;

public class SlateColorTests
{
    [Theory]
    [InlineData("#0E5E6F", "#0E5E6F")]
    [InlineData("abc", "#AABBCC")]
    [InlineData("#2BD4A424", "#2BD4A424")]
    public void Parses_and_formats(string input, string expected) => Assert.Equal(expected, SlateColor.Parse(input).ToHex());

    [Theory]
    [InlineData("")]
    [InlineData("#12")]
    [InlineData("#GGHHII")]
    public void Rejects_bad_input(string input) => Assert.False(SlateColor.TryParse(input, out _));

    [Fact]
    public void Xaml_hex_puts_alpha_first() => Assert.Equal("#242BD4A4", SlateColor.Parse("#2BD4A424").ToArgbHex());

    [Fact]
    public void Contrast_matches_wcag_reference() =>
        Assert.Equal(4.54, SlateColor.Parse("#767676").ContrastWith(SlateColor.White), 2);

    [Theory]
    [InlineData("#0E5E6F")]
    [InlineData("#FF5A1F")]
    [InlineData("#808080")]
    [InlineData("#000000")]
    public void Hsl_round_trips(string hex)
    {
        var c = SlateColor.Parse(hex);
        var (h, s, l) = c.ToHsl();
        var back = SlateColor.FromHsl(h, s, l);
        Assert.InRange(Math.Abs(back.R - c.R) + Math.Abs(back.G - c.G) + Math.Abs(back.B - c.B), 0, 3);
    }

    [Fact]
    public void Lightness_adjustment_is_clamped()
    {
        Assert.Equal(SlateColor.White, SlateColor.Parse("#EEEEEE").AdjustLightness(1));
        Assert.Equal(SlateColor.Black, SlateColor.Parse("#111111").AdjustLightness(-1));
    }
}

public class ThemeBuilderTests
{
    private static readonly string[] Surfaces =
        ["color.background.canvas", "color.background.surface", "color.background.sunken", "color.background.raised", "color.background.subtle"];

    [Theory]
    [InlineData("light")]
    [InlineData("dark")]
    public void Empty_options_reproduce_the_built_in_theme(string baseTheme)
    {
        var theme = ThemeBuilder.Build(new SlateThemeOptions { Base = baseTheme });
        var expected = baseTheme == "light" ? SlateTokens.Themes.Light.Values : SlateTokens.Themes.Dark.Values;

        Assert.Empty(theme.ChangedPaths);
        Assert.All(expected, kv => Assert.Equal(kv.Value, theme.Values[kv.Key]));
        Assert.Equal(baseTheme == "dark", theme.IsDark);
    }

    [Fact]
    public void The_alloy_teal_is_kept_unchanged_when_it_already_passes()
    {
        var theme = ThemeBuilder.Build(new SlateThemeOptions { Accent = SlateTokens.Themes.Light.Color.Accent.Default });
        Assert.Equal(SlateTokens.Themes.Light.Color.Accent.Default, theme.Values["color.accent.default"]);
        Assert.Equal("#FFFFFF", theme.Values["color.text.onAccent"]);
    }

    public static TheoryData<string, string> Brands()
    {
        var data = new TheoryData<string, string>();
        string[] named =
        [
            "#FF0000", "#00FF00", "#0000FF", "#FFFF00", "#00FFFF", "#FF00FF", "#FFFFFF", "#000000", "#808080",
            "#FF5A1F", "#E8590C", "#5B3DF5", "#2F4FD8", "#D4FF3A", "#3EF0D0", "#8B5CF6", "#0E5E6F", "#FFB020", "#E5231B", "#1F3B2D",
        ];
        var rng = new Random(20261006);
        var random = Enumerable.Range(0, 60).Select(_ => $"#{rng.Next(0x1000000):X6}");
        foreach (var hex in named.Concat(random))
        {
            data.Add(hex, "light");
            data.Add(hex, "dark");
        }
        return data;
    }

    [Theory, MemberData(nameof(Brands))]
    public void Any_brand_colour_yields_an_accessible_accent(string brand, string baseTheme)
    {
        var t = ThemeBuilder.Build(new SlateThemeOptions { Base = baseTheme, Accent = brand });
        var onAccent = t.Color("color.text.onAccent");

        foreach (var state in new[] { "color.accent.default", "color.accent.hover", "color.accent.pressed" })
        {
            var ratio = onAccent.ContrastWith(t.Color(state));
            Assert.True(ratio >= 4.5, $"{brand}/{baseTheme}: on-accent on {state} is {ratio:0.00}");
        }

        foreach (var surface in Surfaces)
        {
            var bg = t.Color(surface);
            Assert.True(t.Color("color.accent.default").ContrastWith(bg) >= 3, $"{brand}/{baseTheme}: fill vs {surface}");
            Assert.True(t.Color("color.accent.text").ContrastWith(bg) >= 4.5, $"{brand}/{baseTheme}: accent text vs {surface}");
            Assert.True(t.Color("color.text.link").ContrastWith(bg) >= 4.5, $"{brand}/{baseTheme}: link vs {surface}");
            Assert.True(t.Color("color.text.linkHover").ContrastWith(bg) >= 4.5, $"{brand}/{baseTheme}: link hover vs {surface}");
        }
    }

    [Theory, MemberData(nameof(Brands))]
    public void States_are_visibly_different_from_the_fill(string brand, string baseTheme)
    {
        var t = ThemeBuilder.Build(new SlateThemeOptions { Base = baseTheme, Accent = brand });
        Assert.NotEqual(t.Values["color.accent.default"], t.Values["color.accent.hover"]);
        Assert.NotEqual(t.Values["color.accent.default"], t.Values["color.accent.pressed"]);
    }

    [Fact]
    public void Brand_is_only_adjusted_in_lightness()
    {
        var brand = SlateColor.Parse("#FFB020"); // amber: too light for white text
        var fill = ThemeBuilder.Build(new SlateThemeOptions { Accent = "#FFB020" }).Color("color.accent.default");
        Assert.Equal(brand.ToHsl().H, fill.ToHsl().H, 2);
    }

    [Fact]
    public void Radius_scale_multiplies_radii_but_not_full()
    {
        var square = ThemeBuilder.Build(new SlateThemeOptions { RadiusScale = 0 });
        var soft = ThemeBuilder.Build(new SlateThemeOptions { RadiusScale = 1.5 });

        Assert.Equal("0px", square.Values["radius.md"]);
        Assert.Equal("9px", soft.Values["radius.md"]);
        Assert.False(soft.Values.ContainsKey("radius.full") && soft.ChangedPaths.Contains("radius.full"));
        Assert.Equal(9, soft.Pixels("radius.md"));
    }

    [Fact]
    public void Fonts_and_overrides_apply_and_are_reported_as_changed()
    {
        var t = ThemeBuilder.Build(new SlateThemeOptions
        {
            Name = "acme",
            FontFamily = "Inter Display, sans-serif",
            Overrides = new Dictionary<string, string> { ["color.background.canvas"] = "#FAFAFA" },
        });

        Assert.Equal("acme", t.Name);
        Assert.Equal("Inter Display, sans-serif", t.Values["font.family.ui"]);
        Assert.Equal("#FAFAFA", t.Values["color.background.canvas"]);
        Assert.Equal(["color.background.canvas", "font.family.ui"], t.ChangedPaths.Order());
    }

    [Fact]
    public void Invalid_options_fail_loudly()
    {
        Assert.Throws<ArgumentException>(() => ThemeBuilder.Build(new SlateThemeOptions { Base = "sepia" }));
        Assert.Throws<ArgumentException>(() => ThemeBuilder.Build(new SlateThemeOptions { Accent = "teal" }));
        Assert.Throws<ArgumentException>(() => ThemeBuilder.Build(new SlateThemeOptions { Accent = "#0E5E6F80" }));
        Assert.Throws<ArgumentOutOfRangeException>(() => ThemeBuilder.Build(new SlateThemeOptions { RadiusScale = -1 }));
        Assert.Throws<ArgumentException>(() => ThemeBuilder.Build(new SlateThemeOptions { Overrides = new Dictionary<string, string> { ["color.nope"] = "#000" } }));
        Assert.Throws<ArgumentException>(() => ThemeBuilder.Build(new SlateThemeOptions { Overrides = new Dictionary<string, string> { ["color.background.canvas"] = "blue" } }));
    }
}
