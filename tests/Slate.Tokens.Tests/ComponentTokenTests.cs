using Slate.Tokens.Emitters;
using Slate.Tokens.Loading;
using Slate.Tokens.Model;

namespace Slate.Tokens.Tests;

public class ComponentTokenTests
{
    private static readonly TokenBuild Build = TestTokens.Real;

    [Fact]
    public void Component_tokens_are_layered_onto_every_theme()
    {
        foreach (var theme in Build.Themes)
            Assert.Contains(theme.Tokens, t => t.Path == "component.button.radius");
        Assert.DoesNotContain(Build.Shared, t => t.Path.StartsWith("component.", StringComparison.Ordinal));
    }

    [Fact]
    public void Component_colours_follow_their_theme()
    {
        var light = Build.Theme("light").Tokens.Single(t => t.Path == "component.card.background").Value;
        var dark = Build.Theme("dark").Tokens.Single(t => t.Path == "component.card.background").Value;
        Assert.NotEqual(light, dark);
    }

    [Fact]
    public void Aliases_are_recorded()
    {
        var token = Build.Theme("light").Tokens.Single(t => t.Path == "component.button.radius");
        Assert.Equal("radius.md", token.AliasOf);
        Assert.Null(Build.Theme("light").Tokens.Single(t => t.Path == "component.field.paddingX").AliasOf);
    }

    [Fact]
    public void Css_keeps_component_aliases_live_with_var_references()
    {
        var css = new CssEmitter().Emit(Build).Single().Content;
        Assert.Contains("--sl-component-button-radius: var(--sl-radius-md);", css);
        Assert.Contains("--sl-component-field-padding-x: 10px;", css);
    }

    [Fact]
    public void Xaml_exposes_component_scalars_corners_and_thicknesses()
    {
        var axaml = new AvaloniaXamlEmitter().Emit(Build).Single(f => f.RelativePath == "Tokens.axaml").Content;
        Assert.Contains("<x:Double x:Key=\"Sl.Component.Button.Radius\">6</x:Double>", axaml);
        Assert.Contains("<CornerRadius x:Key=\"Sl.Component.Button.Radius.Corner\">6</CornerRadius>", axaml);
        Assert.Contains("<Thickness x:Key=\"Sl.Component.Card.Padding.Thickness\">20</Thickness>", axaml);
        Assert.Contains("<FontWeight x:Key=\"Sl.Component.Button.FontWeight\">Medium</FontWeight>", axaml);

        var wpf = new WpfXamlEmitter().Emit(Build).Single(f => f.RelativePath == "Theme.Light.xaml").Content;
        Assert.Contains("x:Key=\"Sl.Component.Card.Shadow.Effect\"", wpf);
        Assert.Contains("<SolidColorBrush x:Key=\"Sl.Component.Card.Background.Brush\"", wpf);
    }

    [Fact]
    public void Generated_csharp_lists_aliases_per_theme()
    {
        var cs = new CSharpEmitter().Emit(Build).Single().Content;
        Assert.Contains("[\"component.button.radius\"] = \"radius.md\",", cs);
    }

    [Fact]
    public void Component_tokens_outside_the_component_namespace_are_rejected()
    {
        var dir = Directory.CreateTempSubdirectory();
        try
        {
            File.WriteAllText(Path.Combine(dir.FullName, "p.json"), """{ "n": { "$type": "number", "$value": 1 } }""");
            File.WriteAllText(Path.Combine(dir.FullName, "l.json"), "{}");
            File.WriteAllText(Path.Combine(dir.FullName, "c.json"), """{ "button": { "$type": "number", "x": { "$value": 1 } } }""");
            File.WriteAllText(Path.Combine(dir.FullName, "cfg.json"),
                """{ "primitives": ["p.json"], "themes": { "light": "l.json" }, "components": ["c.json"] }""");
            Assert.Throws<TokenException>(() => TokenBuild.Load(Path.Combine(dir.FullName, "cfg.json")));
        }
        finally { dir.Delete(true); }
    }
}
