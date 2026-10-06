using Slate.Tokens.Model;

namespace Slate.Tokens.Tests;

public class IconTests
{
    [Theory]
    [InlineData("M20 6 L9 17 L4 12")]
    [InlineData("M3 12 A9 9 0 1 0 21 12 Z")]
    [InlineData("M12 3.5 L21.5 20 L2.5 20 Z M12 10 L12 14")]
    [InlineData("M5 12 L-5.01 12")]
    public void Accepts_strict_path_data(string path) =>
        IconSet.Create(24, 2, [("ok", path)]);

    [Theory]
    [InlineData("M20 6 9 17")]          // implicit line-to
    [InlineData("M20,6 L9,17")]         // commas
    [InlineData("M20 6 l-5-5")]         // lower-case relative + packed negatives
    [InlineData("L9 17")]               // must start with a move
    [InlineData("M20 6  L9 17")]        // double space
    public void Rejects_ambiguous_path_data(string path) =>
        Assert.Throws<TokenException>(() => IconSet.Create(24, 2, [("bad", path)]));

    [Theory]
    [InlineData("Check")]
    [InlineData("chevron_down")]
    [InlineData("-x")]
    public void Names_must_be_kebab_case(string name) =>
        Assert.Throws<TokenException>(() => IconSet.Create(24, 2, [(name, "M0 0 L1 1")]));

    [Fact]
    public void Duplicate_names_are_rejected() =>
        Assert.Throws<TokenException>(() => IconSet.Create(24, 2, [("a", "M0 0 L1 1"), ("a", "M0 0 L1 1")]));

    [Fact]
    public void Real_icon_set_loads_and_contains_the_icons_components_rely_on()
    {
        var icons = TestTokens.Real.Icons.Icons.Select(i => i.Name).ToHashSet();
        foreach (var required in new[] { "check", "minus", "x", "chevron-down", "menu", "search", "info", "check-circle", "alert-triangle", "alert-circle", "sun", "moon" })
            Assert.Contains(required, icons);
    }

    [Fact]
    public void Emitters_produce_typescript_and_csharp()
    {
        var build = TestTokens.Real;
        var ts = new IconTypeScriptEmitter().Emit(build).Single().Content;
        var cs = new IconCSharpEmitter().Emit(build).Single().Content;
        Assert.Contains("\"chevron-down\": \"M6 9 L12 15 L18 9\",", ts);
        Assert.Contains("export type IconName = keyof typeof icons;", ts);
        Assert.Contains("public const string ChevronDown = \"M6 9 L12 15 L18 9\";", cs);
        Assert.Contains("[\"chevron-down\"] = ChevronDown,", cs);
    }
}
