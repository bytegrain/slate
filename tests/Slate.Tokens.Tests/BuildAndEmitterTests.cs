using Slate.Tokens.Emitters;
using Slate.Tokens.Loading;
using Slate.Tokens.Model;
using static Slate.Tokens.Tests.TestTokens;

namespace Slate.Tokens.Tests;

public class TokenBuildTests
{
    private const string Primitives = """
        { "gray": { "$type": "color", "light": { "$value": "#F6F7F9" }, "dark": { "$value": "#0D1014" } },
          "space": { "$type": "dimension", "2": { "$value": "8px" } } }
        """;

    [Fact]
    public void Themes_layer_semantic_tokens_over_primitives()
    {
        var build = Build(Primitives,
            ("light", """{ "color": { "$type": "color", "bg": { "$value": "{gray.light}" } } }"""),
            ("dark", """{ "color": { "$type": "color", "bg": { "$value": "{gray.dark}" } } }"""));

        Assert.Equal(["gray.light", "gray.dark", "space.2"], build.Shared.Select(t => t.Path));
        Assert.Equal(ColorValue.Parse("#0D1014", "x"), build.Theme("dark").Tokens.Single().Value);
        Assert.Equal("light", build.DefaultTheme.Name);
    }

    [Fact]
    public void Themes_must_define_the_same_paths()
    {
        var ex = Assert.Throws<TokenException>(() => Build(Primitives,
            ("light", """{ "color": { "$type": "color", "bg": { "$value": "#fff" }, "fg": { "$value": "#000" } } }"""),
            ("dark", """{ "color": { "$type": "color", "bg": { "$value": "#000" } } }""")));

        Assert.Contains("missing", ex.Message);
        Assert.Contains("color.fg", ex.Message);
    }

    [Fact]
    public void Themes_must_agree_on_types()
    {
        var ex = Assert.Throws<TokenException>(() => Build(Primitives,
            ("light", """{ "x": { "$type": "color", "$value": "#fff" } }"""),
            ("dark", """{ "x": { "$type": "number", "$value": 1 } }""")));
        Assert.Equal("x", ex.TokenPath);
    }

    [Fact]
    public void Themes_may_not_redefine_primitives() =>
        Assert.Throws<TokenException>(() => Build(Primitives,
            ("light", """{ "gray": { "$type": "color", "light": { "$value": "#fff" } } }""")));

    [Fact]
    public void Primitives_may_not_reference_theme_tokens() =>
        Assert.Throws<TokenException>(() => Build(
            """{ "p": { "$type": "color", "$value": "{color.bg}" } }""",
            ("light", """{ "color": { "$type": "color", "bg": { "$value": "#fff" } } }""")));

    [Fact]
    public void Default_theme_must_exist() =>
        Assert.Throws<TokenException>(() => TokenBuild.Create(
            new TokenConfig { DefaultTheme = "sepia" }, Read(Primitives),
            new Dictionary<string, IReadOnlyList<RawToken>> { ["light"] = Read("{}") }));

    [Fact]
    public void Density_modes_must_match()
    {
        var config = new TokenConfig
        {
            DefaultTheme = "light",
            Density = new DensityConfig { Group = "size", Modes = ["compact", "comfortable"], Default = "compact" },
        };
        var primitives = Read("""
            { "size": { "$type": "dimension",
                "compact": { "sm": { "$value": "26px" }, "md": { "$value": "32px" } },
                "comfortable": { "sm": { "$value": "32px" } } } }
            """);

        Assert.Throws<TokenException>(() => TokenBuild.Create(config, primitives,
            new Dictionary<string, IReadOnlyList<RawToken>> { ["light"] = Read("{}") }));
    }
}

/// <summary>Emitter output is checked against the real design source so the tests document the generated API.</summary>
public class EmitterTests
{
    private static readonly TokenBuild Build = Real;

    private static string Emit(IEmitter emitter, string file) => emitter.Emit(Build).Single(f => f.RelativePath == file).Content;

    [Fact]
    public void Css_defines_light_on_root_and_dark_by_attribute_and_auto()
    {
        var css = Emit(new CssEmitter(), "tokens.css");

        Assert.Contains(":root,\n[data-sl-theme=\"light\"] {", css);
        Assert.Contains("[data-sl-theme=\"dark\"] {", css);
        Assert.Contains("@media (prefers-color-scheme: dark) {\n  [data-sl-theme=\"auto\"] {", css);
        Assert.Contains("--sl-color-background-canvas: #F6F7F9;", css);
        Assert.Contains("--sl-color-background-canvas: #0D1014;", css);
        Assert.DoesNotContain("\r", css);
    }

    [Fact]
    public void Css_density_aliases_point_at_mode_tokens()
    {
        var css = Emit(new CssEmitter(), "tokens.css");
        Assert.Contains("--sl-size-control-md: var(--sl-size-control-compact-md);", css);
        Assert.Contains("[data-sl-density=\"comfortable\"] {\n  --sl-size-control-sm: var(--sl-size-control-comfortable-sm);", css);
    }

    [Fact]
    public void Css_typography_expands_to_longhands_and_a_font_shorthand()
    {
        var css = Emit(new CssEmitter(), "tokens.css");
        Assert.Contains("--sl-typography-body-font-family: var(--sl-font-family-ui);", css);
        Assert.Contains("--sl-typography-body: 400 14px/1.5 var(--sl-font-family-ui);", css);
    }

    [Fact]
    public void Wpf_has_one_dictionary_per_theme_and_density_with_matching_keys()
    {
        var files = new WpfXamlEmitter().Emit(Build).Select(f => f.RelativePath).ToList();
        Assert.Equal(["Tokens.xaml", "Theme.Light.xaml", "Theme.Dark.xaml", "Density.Compact.xaml", "Density.Comfortable.xaml"], files);

        static IEnumerable<string> Keys(string xaml) =>
            System.Text.RegularExpressions.Regex.Matches(xaml, "x:Key=\"([^\"]+)\"").Select(m => m.Groups[1].Value);

        Assert.Equal(Keys(Emit(new WpfXamlEmitter(), "Theme.Light.xaml")), Keys(Emit(new WpfXamlEmitter(), "Theme.Dark.xaml")));
    }

    [Fact]
    public void Wpf_exposes_colours_brushes_corner_radii_and_text_styles()
    {
        var light = Emit(new WpfXamlEmitter(), "Theme.Light.xaml");
        var shared = Emit(new WpfXamlEmitter(), "Tokens.xaml");

        Assert.Contains("<Color x:Key=\"Sl.Color.Accent.Default\">#FF0E5E6F</Color>", light);
        Assert.Contains("<SolidColorBrush x:Key=\"Sl.Brush.Accent.Default\" Color=\"#FF0E5E6F\" />", light);
        Assert.Contains("<DropShadowEffect x:Key=\"Sl.Effect.E3\"", light);
        Assert.Contains("<CornerRadius x:Key=\"Sl.CornerRadius.Md\">6</CornerRadius>", shared);
        Assert.Contains("<Thickness x:Key=\"Sl.Thickness.4\">16</Thickness>", shared);
        Assert.Contains("<Style x:Key=\"Sl.Typography.Body\" TargetType=\"TextBlock\">", shared);
        Assert.Contains("<FontFamily x:Key=\"Sl.Font.Family.Ui\">Instrument Sans, Segoe UI</FontFamily>", shared);
    }

    [Fact]
    public void Xaml_never_contains_double_hyphen_inside_comments()
    {
        foreach (var file in new WpfXamlEmitter().Emit(Build).Concat(new AvaloniaXamlEmitter().Emit(Build)))
        {
            foreach (System.Text.RegularExpressions.Match m in System.Text.RegularExpressions.Regex.Matches(file.Content, "<!--(.*?)-->"))
                Assert.DoesNotContain("--", m.Groups[1].Value);
        }
    }

    [Fact]
    public void Generated_xaml_is_well_formed_xml()
    {
        foreach (var file in new WpfXamlEmitter().Emit(Build).Concat(new AvaloniaXamlEmitter().Emit(Build)))
            System.Xml.Linq.XDocument.Parse(file.Content);
    }

    [Fact]
    public void Avalonia_uses_theme_dictionaries_and_native_box_shadows()
    {
        var xaml = Emit(new AvaloniaXamlEmitter(), "Tokens.axaml");
        Assert.Contains("<ResourceDictionary x:Key=\"Light\">", xaml);
        Assert.Contains("<ResourceDictionary x:Key=\"Dark\">", xaml);
        Assert.Contains("<BoxShadows x:Key=\"Sl.Shadow.Control\">inset 0 1 0 0 #F2FFFFFF, 0 1 0 0 #0D12161C, 0 1 2 0 #0F12161C</BoxShadows>", xaml);
        Assert.Contains("<ControlTheme x:Key=\"Sl.Typography.H1\" TargetType=\"TextBlock\">", xaml);
        Assert.Contains("<SplineEasing x:Key=\"Sl.Motion.Easing.Standard\" X1=\"0.2\" Y1=\"0\" X2=\"0\" Y2=\"1\" />", xaml);
    }

    [Fact]
    public void Avalonia_letter_spacing_is_converted_from_em_to_pixels()
    {
        // h1: 36px at -0.03em = -1.08px
        var xaml = Emit(new AvaloniaXamlEmitter(), "Tokens.axaml");
        var h1 = xaml[xaml.IndexOf("Sl.Typography.H1", StringComparison.Ordinal)..];
        Assert.Contains("<Setter Property=\"LetterSpacing\" Value=\"-1.08\" />", h1[..h1.IndexOf("</ControlTheme>", StringComparison.Ordinal)]);
    }

    [Fact]
    public void CSharp_nests_shared_tokens_and_themes()
    {
        var cs = Emit(new CSharpEmitter(), "SlateTokens.g.cs");
        Assert.Contains("public static partial class SlateTokens", cs);
        Assert.Contains("public const double _4 = 16;", cs);
        Assert.Contains("public static class Themes", cs);
        Assert.Contains("public const string OnAccent = \"#05181C\";", cs); // dark theme on-accent
        Assert.Contains("public static readonly SlateTextStyle Body = new(", cs);
    }

    [Fact]
    public void Resolved_json_lists_every_token_with_platform_names()
    {
        var json = System.Text.Json.Nodes.JsonNode.Parse(Emit(new ResolvedJsonEmitter(), "tokens.resolved.json"))!;
        var canvas = json["themes"]!["dark"]!["color.background.canvas"]!;
        Assert.Equal("#0D1014", canvas["value"]!.GetValue<string>());
        Assert.Equal("--sl-color-background-canvas", canvas["cssVar"]!.GetValue<string>());
        Assert.Equal("Sl.Color.Background.Canvas", canvas["xamlKey"]!.GetValue<string>());
        Assert.Equal(Build.Shared.Count, json["shared"]!.AsObject().Count);
    }
}
