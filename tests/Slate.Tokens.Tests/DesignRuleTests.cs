using Slate.Tokens.Loading;
using Slate.Tokens.Model;
using Slate.Tokens.Validation;

namespace Slate.Tokens.Tests;

/// <summary>
/// Executable design rules for the real Alloy tokens. If a token change breaks accessibility or
/// consistency, these fail — see docs/design/color.md for the reasoning behind each pair.
/// </summary>
public class DesignRuleTests
{
    private static readonly TokenBuild Build = TestTokens.Real;

    public static TheoryData<string> Themes => new(Build.Themes.Select(t => t.Name));

    private static ColorValue Color(string theme, string path) =>
        Build.Theme(theme).Tokens.Single(t => t.Path == path).As<ColorValue>();

    private static void AssertContrast(string theme, string fg, string bg, double min)
    {
        var ratio = Contrast.Ratio(Color(theme, fg), Color(theme, bg));
        Assert.True(ratio >= min, $"[{theme}] {fg} on {bg} is {ratio:0.00}:1, needs {min}:1");
    }

    private static readonly string[] ReadingSurfaces =
        ["color.background.canvas", "color.background.surface", "color.background.sunken", "color.background.raised", "color.background.subtle"];

    [Theory, MemberData(nameof(Themes))]
    public void Body_text_is_readable_on_every_reading_surface(string theme)
    {
        foreach (var bg in ReadingSurfaces)
        foreach (var fg in new[] { "color.text.primary", "color.text.secondary", "color.text.tertiary", "color.text.placeholder", "color.text.link", "color.accent.text" })
            AssertContrast(theme, fg, bg, Contrast.Text);
    }

    [Theory, MemberData(nameof(Themes))]
    public void Primary_and_secondary_text_stay_readable_on_muted_and_hover_fills(string theme)
    {
        foreach (var bg in new[] { "color.background.muted", "color.background.hover", "color.background.pressed" })
        foreach (var fg in new[] { "color.text.primary", "color.text.secondary" })
            AssertContrast(theme, fg, bg, Contrast.Text);
    }

    [Theory, MemberData(nameof(Themes))]
    public void Text_on_accent_is_readable_in_every_interaction_state(string theme)
    {
        foreach (var bg in new[] { "color.accent.default", "color.accent.hover", "color.accent.pressed" })
            AssertContrast(theme, "color.text.onAccent", bg, Contrast.Text);
    }

    [Theory, MemberData(nameof(Themes))]
    public void Status_colours_are_readable_as_text_tints_and_solids(string theme)
    {
        foreach (var status in new[] { "success", "warning", "danger", "info" })
        {
            var p = $"color.status.{status}";
            AssertContrast(theme, $"{p}.fg", "color.background.surface", Contrast.Text);
            AssertContrast(theme, $"{p}.fg", "color.background.canvas", Contrast.Text);
            AssertContrast(theme, "color.text.primary", "color.background.surface", Contrast.Text);
            AssertContrast(theme, $"{p}.onSolid", $"{p}.solid", Contrast.Text);

            // Tinted backgrounds are translucent in dark mode: composite over the surface they sit on.
            var tint = Color(theme, $"{p}.bg").Over(Color(theme, "color.background.surface"));
            var ratio = Contrast.Ratio(Color(theme, $"{p}.fg"), tint);
            Assert.True(ratio >= Contrast.Text, $"[{theme}] {p}.fg on its tint is {ratio:0.00}:1");
        }
    }

    [Theory, MemberData(nameof(Themes))]
    public void Inverse_surfaces_are_readable(string theme)
    {
        AssertContrast(theme, "color.inverse.text", "color.inverse.background", Contrast.Text);
        AssertContrast(theme, "color.inverse.textMuted", "color.inverse.background", Contrast.Text);
    }

    [Theory, MemberData(nameof(Themes))]
    public void Focus_ring_is_visible_on_every_surface(string theme)
    {
        foreach (var bg in ReadingSurfaces.Append("color.background.muted"))
            AssertContrast(theme, "color.focus.ring", bg, Contrast.NonText);
    }

    [Theory, MemberData(nameof(Themes))]
    public void Selection_controls_have_visible_boundaries(string theme)
    {
        foreach (var bg in ReadingSurfaces)
        {
            AssertContrast(theme, "color.border.control", bg, Contrast.NonText);
            AssertContrast(theme, "color.accent.default", bg, Contrast.NonText); // checked state fill
        }
    }

    [Theory, MemberData(nameof(Themes))]
    public void Surfaces_step_in_a_consistent_direction(string theme)
    {
        // Light: canvas is darker than surface. Dark: surfaces get lighter as they rise.
        double L(string p) => Color(theme, p).RelativeLuminance();
        if (theme == "light")
        {
            Assert.True(L("color.background.canvas") < L("color.background.surface"));
            Assert.True(L("color.background.muted") < L("color.background.subtle"));
        }
        else
        {
            Assert.True(L("color.background.sunken") < L("color.background.surface"));
            Assert.True(L("color.background.canvas") < L("color.background.surface"));
            Assert.True(L("color.background.surface") < L("color.background.raised"));
        }
    }

    [Fact]
    public void Spacing_is_on_the_4px_grid_except_half_steps()
    {
        foreach (var t in Build.Shared.Where(t => t.Path.StartsWith("space.", StringComparison.Ordinal)))
        {
            var px = t.As<DimensionValue>().Value;
            Assert.True(px % 4 == 0 || px is 2 or 6 or 10, $"{t.Path} = {px}px is off-grid");
        }
    }

    [Fact]
    public void Control_heights_meet_minimum_target_sizes()
    {
        // WCAG 2.5.8 (AA) minimum target size is 24px; every control size must exceed it.
        foreach (var mode in Build.Config.Density!.Modes)
        foreach (var (key, token) in Build.DensityTokens(mode))
            Assert.True(token.As<DimensionValue>().Value >= 24, $"{mode}.{key} is below 24px");
    }

    [Fact]
    public void Typography_scale_is_monotonic()
    {
        string[] order = ["caption", "label", "body", "title", "h3", "h2", "h1", "display"];
        var sizes = order.Select(n => Build.Shared.Single(t => t.Path == $"typography.{n}").As<TypographyValue>().FontSize.Value).ToList();
        Assert.Equal(sizes.Order(), sizes);
    }
}

public class GeneratedOutputTests
{
    [Fact]
    public void Committed_generated_files_match_the_design_source()
    {
        var result = TokenPipeline.Run(TokenPipeline.DefaultConfig(TestTokens.RepoRoot), check: true);
        Assert.True(result.Stale.Count == 0,
            "Generated files are out of date — run `dotnet run --project tools/Slate.Tokens.Cli -- build`:\n" +
            string.Join("\n", result.Stale));
        Assert.NotEmpty(result.Unchanged);
    }
}
