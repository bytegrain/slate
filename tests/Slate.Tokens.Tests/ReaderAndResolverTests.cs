using Slate.Tokens.Model;
using static Slate.Tokens.Tests.TestTokens;

namespace Slate.Tokens.Tests;

public class TokenFileReaderTests
{
    [Fact]
    public void Flattens_nested_groups_into_dotted_paths_in_source_order()
    {
        var tokens = Read("""
            { "color": { "$type": "color",
                "a": { "$value": "#000" },
                "nested": { "b": { "$value": "#fff", "$description": "white" } } } }
            """);

        Assert.Equal(["color.a", "color.nested.b"], tokens.Select(t => t.Path));
        Assert.All(tokens, t => Assert.Equal(TokenType.Color, t.Type));
        Assert.Equal("white", tokens[1].Description);
    }

    [Fact]
    public void Token_type_overrides_group_type()
    {
        var token = Read("""{ "g": { "$type": "color", "n": { "$type": "number", "$value": 2 } } }""").Single();
        Assert.Equal(TokenType.Number, token.Type);
    }

    [Fact]
    public void Missing_type_is_an_error_naming_the_token()
    {
        var ex = Assert.Throws<TokenException>(() => Read("""{ "a": { "b": { "$value": 1 } } }"""));
        Assert.Equal("a.b", ex.TokenPath);
    }

    [Theory]
    [InlineData("""{ "a.b": { "$type": "number", "$value": 1 } }""")]
    [InlineData("""{ "a{": { "$type": "number", "$value": 1 } }""")]
    public void Names_with_reserved_characters_are_rejected(string json) =>
        Assert.Throws<TokenException>(() => Read(json));

    [Fact]
    public void Unknown_type_is_rejected() =>
        Assert.Throws<TokenException>(() => Read("""{ "a": { "$type": "gradient", "$value": 1 } }"""));

    [Fact]
    public void Invalid_json_reports_the_source() =>
        Assert.Contains("test.json", Assert.Throws<TokenException>(() => Read("{ nope")).Message);

    [Fact]
    public void Comments_and_trailing_commas_are_allowed()
    {
        var tokens = Read("""
            { // the palette
              "n": { "$type": "number", "$value": 1, }, }
            """);
        Assert.Single(tokens);
    }
}

public class TokenResolverTests
{
    [Fact]
    public void Resolves_whole_token_references_through_chains()
    {
        var tokens = Resolve("""
            { "c": { "$type": "color",
                "base": { "$value": "#112233" },
                "mid":  { "$value": "{c.base}" },
                "top":  { "$value": "{c.mid}" } } }
            """);

        Assert.Equal(new ColorValue(0x11, 0x22, 0x33), tokens.Single(t => t.Path == "c.top").Value);
    }

    [Fact]
    public void Detects_cycles_and_reports_the_chain()
    {
        var ex = Assert.Throws<TokenException>(() => Resolve("""
            { "c": { "$type": "color",
                "a": { "$value": "{c.b}" },
                "b": { "$value": "{c.c}" },
                "c": { "$value": "{c.a}" } } }
            """));

        Assert.Contains("Circular reference", ex.Message);
        Assert.Contains("c.a -> c.b -> c.c -> c.a", ex.Message);
    }

    [Fact]
    public void Missing_reference_names_the_referring_token()
    {
        var ex = Assert.Throws<TokenException>(() => Resolve("""{ "c": { "$type": "color", "a": { "$value": "{c.nope}" } } }"""));
        Assert.Equal("c.a", ex.TokenPath);
        Assert.Contains("c.nope", ex.Message);
    }

    [Fact]
    public void Reference_to_a_different_type_is_an_error()
    {
        var ex = Assert.Throws<TokenException>(() => Resolve("""
            { "n": { "$type": "number", "$value": 2 },
              "c": { "$type": "color", "$value": "{n}" } }
            """));
        Assert.Contains("Number", ex.Message);
    }

    [Fact]
    public void Duplicate_paths_are_rejected()
    {
        var a = Read("""{ "x": { "$type": "number", "$value": 1 } }""");
        Assert.Throws<TokenException>(() => new Loading.TokenResolver(a.Concat(a)));
    }

    [Fact]
    public void Resolves_references_inside_shadow_layers()
    {
        var token = Single("""
            { "ink": { "$type": "color", "$value": "#12161C33" },
              "s": { "$type": "shadow", "$value": [
                { "color": "{ink}", "offsetX": "0px", "offsetY": "1px", "blur": "2px", "spread": "0px" },
                { "color": "#FFFFFF", "offsetX": "0px", "offsetY": "1px", "blur": "0px", "inset": true } ] } }
            """, "s");

        var shadow = token.As<ShadowValue>();
        Assert.Equal(2, shadow.Layers.Count);
        Assert.Equal(0x33, shadow.Layers[0].Color.A);
        Assert.True(shadow.Layers[1].Inset);
        Assert.Equal(0, shadow.Layers[1].Spread.Value); // spread is optional
        Assert.Equal("0px 1px 2px 0px #12161C33, inset 0px 1px 0px 0px #FFFFFF", shadow.ToCss());
    }

    [Fact]
    public void Typography_keeps_the_font_family_reference()
    {
        var token = Single("""
            { "ui": { "$type": "fontFamily", "$value": ["Instrument Sans", "sans-serif"] },
              "size": { "$type": "dimension", "$value": "14px" },
              "t": { "$type": "typography", "$value": {
                "fontFamily": "{ui}", "fontSize": "{size}", "fontWeight": 600, "lineHeight": 1.5, "letterSpacing": "-0.02em" } } }
            """, "t");

        var t = token.As<TypographyValue>();
        Assert.Equal("ui", t.FontFamilyRef);
        Assert.Equal(14, t.FontSize.Value);
        Assert.Equal(600, t.FontWeight);
        Assert.Equal(21, t.LineHeightPixels);
        Assert.Equal(-0.02, t.LetterSpacing.Value);
    }

    [Fact]
    public void Typography_requires_pixel_font_size() =>
        Assert.Throws<TokenException>(() => Resolve("""
            { "t": { "$type": "typography", "$value": {
                "fontFamily": "A", "fontSize": "1rem", "fontWeight": 400, "lineHeight": 1.5 } } }
            """));

    [Theory]
    [InlineData("\"semi-bold\"", 600)]
    [InlineData("\"regular\"", 400)]
    [InlineData("700", 700)]
    public void Font_weights_accept_numbers_and_keywords(string value, double expected) =>
        Assert.Equal(expected, Single($$"""{ "w": { "$type": "fontWeight", "$value": {{value}} } }""", "w").Value);

    [Fact]
    public void Cubic_bezier_x_values_must_be_in_range() =>
        Assert.Throws<TokenException>(() => Resolve("""{ "e": { "$type": "cubicBezier", "$value": [1.5, 0, 0, 1] } }"""));
}
