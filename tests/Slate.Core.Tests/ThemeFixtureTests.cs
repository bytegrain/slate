using System.Text.Json;
using System.Text.Json.Nodes;
using Slate.Theming;

namespace Slate.Core.Tests;

/// <summary>
/// Writes/validates tests/fixtures/theme-builder.json: ThemeBuilder outputs for a fixed set of options.
/// @slate/web's TypeScript port asserts it produces exactly the same values, so every platform derives
/// identical themes. Regenerate with SLATE_UPDATE_FIXTURES=1 dotnet test.
/// </summary>
public class ThemeFixtureTests
{
    private static readonly (string Id, SlateThemeOptions Options)[] Cases =
    [
        ("light-default", new() { Base = "light" }),
        ("dark-default", new() { Base = "dark" }),
        ("light-orange", new() { Base = "light", Accent = "#FF5A1F" }),
        ("dark-orange", new() { Base = "dark", Accent = "#FF5A1F" }),
        ("light-yellow", new() { Base = "light", Accent = "#FFFF00" }),
        ("dark-navy", new() { Base = "dark", Accent = "#000080" }),
        ("light-violet-soft", new() { Base = "light", Accent = "#5B3DF5", RadiusScale = 1.5 }),
        ("dark-square", new() { Base = "dark", RadiusScale = 0, FontFamily = "Inter, sans-serif" }),
        ("light-overrides", new() { Base = "light", Overrides = new Dictionary<string, string> { ["color.background.surface"] = "#FCFCFD", ["component.button.radius"] = "999px" } }),
    ];

    private static string FixturePath()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "global.json")))
                return Path.Combine(dir.FullName, "tests", "fixtures", "theme-builder.json");
        throw new DirectoryNotFoundException("Repository root not found.");
    }

    private static string Render()
    {
        var root = new JsonObject();
        foreach (var (id, options) in Cases)
        {
            var theme = ThemeBuilder.Build(options);
            var values = new JsonObject();
            foreach (var path in theme.ChangedPaths.Order(StringComparer.Ordinal))
                values[path] = theme.Values[path];
            root[id] = new JsonObject
            {
                ["options"] = JsonSerializer.SerializeToNode(new
                {
                    @base = options.Base, accent = options.Accent, radiusScale = options.RadiusScale,
                    fontFamily = options.FontFamily, overrides = options.Overrides,
                }),
                ["changed"] = values,
            };
        }
        return root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }).Replace("\r\n", "\n") + "\n";
    }

    [Fact]
    public void Fixture_matches_theme_builder_output()
    {
        var path = FixturePath();
        var expected = Render();
        if (Environment.GetEnvironmentVariable("SLATE_UPDATE_FIXTURES") == "1" || !File.Exists(path))
        {
            File.WriteAllText(path, expected);
            return;
        }
        Assert.Equal(File.ReadAllText(path).Replace("\r\n", "\n"), expected);
    }
}
