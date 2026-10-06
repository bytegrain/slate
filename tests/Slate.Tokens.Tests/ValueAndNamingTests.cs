using Slate.Tokens.Model;
using Slate.Tokens.Validation;

namespace Slate.Tokens.Tests;

public class ColorValueTests
{
    [Theory]
    [InlineData("#abc", 0xAA, 0xBB, 0xCC, 0xFF)]
    [InlineData("#0E5E6F", 0x0E, 0x5E, 0x6F, 0xFF)]
    [InlineData("#2BD4A424", 0x2B, 0xD4, 0xA4, 0x24)]
    public void Parses_hex_forms(string text, int r, int g, int b, int a) =>
        Assert.Equal(new ColorValue((byte)r, (byte)g, (byte)b, (byte)a), ColorValue.Parse(text, "x"));

    [Theory]
    [InlineData("0E5E6F")]
    [InlineData("#12345")]
    [InlineData("#GGGGGG")]
    [InlineData("rgb(0,0,0)")]
    public void Rejects_invalid_colours(string text) => Assert.Throws<TokenException>(() => ColorValue.Parse(text, "x"));

    [Fact]
    public void Formats_css_and_xaml_channel_order()
    {
        var c = ColorValue.Parse("#2BD4A424", "x");
        Assert.Equal("#2BD4A424", c.ToCssHex());
        Assert.Equal("#242BD4A4", c.ToXamlHex());
        Assert.Equal("#0E5E6F", ColorValue.Parse("#0E5E6F", "x").ToCssHex());
    }

    [Fact]
    public void Composites_translucent_colour_over_backdrop()
    {
        var half = new ColorValue(0, 0, 0, 128);
        Assert.Equal(new ColorValue(127, 127, 127), half.Over(new ColorValue(255, 255, 255)));
    }
}

public class ContrastTests
{
    private static ColorValue C(string hex) => ColorValue.Parse(hex, "x");

    [Fact]
    public void Black_on_white_is_21() => Assert.Equal(21, Contrast.Ratio(C("#000"), C("#fff")), 2);

    [Fact]
    public void Identical_colours_are_1() => Assert.Equal(1, Contrast.Ratio(C("#0E5E6F"), C("#0E5E6F")), 5);

    [Fact]
    public void Ratio_is_symmetric() =>
        Assert.Equal(Contrast.Ratio(C("#646E7B"), C("#FFF")), Contrast.Ratio(C("#FFF"), C("#646E7B")), 10);

    [Fact]
    public void Known_reference_value() => Assert.Equal(4.54, Contrast.Ratio(C("#767676"), C("#FFF")), 2);

    [Fact]
    public void Translucent_background_is_rejected() =>
        Assert.Throws<ArgumentException>(() => Contrast.Ratio(C("#000"), C("#FFFFFF80")));
}

public class DimensionAndDurationTests
{
    [Theory]
    [InlineData("16px", 16, "px")]
    [InlineData("-0.02em", -0.02, "em")]
    [InlineData("1.5rem", 1.5, "rem")]
    public void Parses_dimensions(string text, double value, string unit) =>
        Assert.Equal(new DimensionValue(value, unit), DimensionValue.Parse(text, "x"));

    [Theory]
    [InlineData("16")]
    [InlineData("px")]
    [InlineData("16pt")]
    public void Rejects_bad_dimensions(string text) => Assert.Throws<TokenException>(() => DimensionValue.Parse(text, "x"));

    [Theory]
    [InlineData("120ms", 120)]
    [InlineData("0.24s", 240)]
    public void Parses_durations(string text, double ms) => Assert.Equal(ms, DurationValue.Parse(text, "x").Milliseconds, 6);

    [Fact]
    public void Rejects_negative_duration() => Assert.Throws<TokenException>(() => DurationValue.Parse("-1ms", "x"));

    [Fact]
    public void Font_family_css_quotes_names_with_spaces_but_not_generics()
    {
        var f = new FontFamilyValue(["Instrument Sans", "Segoe UI", "system-ui", "sans-serif"]);
        Assert.Equal("\"Instrument Sans\", \"Segoe UI\", system-ui, sans-serif", f.ToCss());
        Assert.Equal(["Instrument Sans", "Segoe UI"], f.NativeFamilies);
    }
}

public class NamingTests
{
    [Theory]
    [InlineData("color.background.canvas", "--sl-color-background-canvas")]
    [InlineData("color.text.onAccent", "--sl-color-text-on-accent")]
    [InlineData("space.0_5", "--sl-space-0-5")]
    [InlineData("font.size.2xl", "--sl-font-size-2xl")]
    [InlineData("typography.bodyStrong", "--sl-typography-body-strong")]
    public void Css_variables(string path, string expected) => Assert.Equal(expected, Naming.CssVariable("sl", path));

    [Theory]
    [InlineData("color.text.onAccent", "Sl.Color.Text.OnAccent")]
    [InlineData("space.0_5", "Sl.Space.0_5")]
    public void Xaml_keys(string path, string expected) => Assert.Equal(expected, Naming.XamlKey("sl", path));

    [Fact]
    public void Xaml_key_with_replaced_group() =>
        Assert.Equal("Sl.Brush.Text.Primary", Naming.XamlKey("sl", "color.text.primary", "brush"));

    [Theory]
    [InlineData("2xl", "_2xl")]
    [InlineData("0_5", "_0_5")]
    [InlineData("onAccent", "OnAccent")]
    [InlineData("a-b", "A_b")]
    public void CSharp_identifiers(string segment, string expected) => Assert.Equal(expected, Naming.CSharpIdentifier(segment));
}
