using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace Slate.Wpf.ResourceTests;

/// <summary>
/// Static checks over Slate.Wpf's XAML: every Sl.* resource it references exists (in the generated token
/// dictionaries or Slate's own), no colours are hard-coded, and every merged dictionary / font file exists.
/// </summary>
public partial class XamlResourceTests
{
    private static readonly string Root = FindRoot();

    internal static string RepoRoot => Root;
    private static readonly string Lib = Path.Combine(Root, "src", "Slate.Wpf");
    private static readonly string Demo = Path.Combine(Root, "demos", "Slate.Wpf.Demo");
    private static readonly XNamespace X = "http://schemas.microsoft.com/winfx/2006/xaml";

    private static string FindRoot()
    {
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d is not null; d = d.Parent)
            if (File.Exists(Path.Combine(d.FullName, "global.json")) && Directory.Exists(Path.Combine(d.FullName, "design")))
                return d.FullName;
        throw new DirectoryNotFoundException("Slate repo root not found.");
    }

    private static IEnumerable<string> Xaml(string dir) =>
        Directory.Exists(dir)
            ? Directory.EnumerateFiles(dir, "*.xaml", SearchOption.AllDirectories)
                .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}") && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
            : [];

    private static IEnumerable<string> Generated => Xaml(Path.Combine(Lib, "Themes", "Generated"));
    private static IEnumerable<string> Authored => Xaml(Lib).Except(Generated);

    private static HashSet<string> KeysIn(IEnumerable<string> files) =>
        files.SelectMany(f => XDocument.Load(f).Descendants())
             .Select(e => (string?)e.Attribute(X + "Key"))
             .Where(k => k is not null)
             .ToHashSet()!;

    /// <summary>Every x:Key defined by the generated dictionaries and Slate.Wpf's own XAML.</summary>
    internal static HashSet<string> AllDefinedKeys() => KeysIn(Generated.Concat(Authored));

    [GeneratedRegex(@"\{(?:DynamicResource|StaticResource)\s+(Sl\.[^}\s]+)\}")]
    private static partial Regex ResourceReference();

    [GeneratedRegex("#[0-9A-Fa-f]{3,8}\\b")]
    private static partial Regex HexColour();

    public static TheoryData<string> AuthoredFiles => new(Authored.Concat(Xaml(Demo)).Select(f => Path.GetRelativePath(Root, f)));

    [Fact]
    public void Generated_dictionaries_exist_for_both_themes_and_densities()
    {
        var names = Generated.Select(Path.GetFileName).ToHashSet();
        Assert.Superset(new HashSet<string?> { "Tokens.xaml", "Theme.Light.xaml", "Theme.Dark.xaml", "Density.Compact.xaml", "Density.Comfortable.xaml" }, names);
    }

    [Theory, MemberData(nameof(AuthoredFiles))]
    public void Every_referenced_Sl_resource_is_defined(string file)
    {
        var defined = KeysIn(Generated.Concat(Authored));
        var text = File.ReadAllText(Path.Combine(Root, file));
        var missing = ResourceReference().Matches(text).Select(m => m.Groups[1].Value).Distinct().Where(k => !defined.Contains(k)).ToList();
        Assert.True(missing.Count == 0, $"{file} references undefined keys: {string.Join(", ", missing)}");
    }

    [Theory, MemberData(nameof(AuthoredFiles))]
    public void Is_well_formed_xml(string file) => XDocument.Load(Path.Combine(Root, file));

    [Fact]
    public void Library_xaml_never_hard_codes_hex_colours()
    {
        foreach (var file in Authored)
        {
            var hits = HexColour().Matches(File.ReadAllText(file)).Select(m => m.Value).ToList();
            Assert.True(hits.Count == 0, $"{Path.GetFileName(file)} hard-codes {string.Join(", ", hits)}; use a token.");
        }
    }

    [Fact]
    public void Themed_values_use_DynamicResource_so_theme_switching_is_live()
    {
        // StaticResource is only allowed for same-file styles/templates (keys defined in that file).
        foreach (var file in Authored)
        {
            var text = File.ReadAllText(file);
            var local = KeysIn([file]);
            var statics = Regex.Matches(text, @"\{StaticResource\s+([^}\s]+)\}").Select(m => m.Groups[1].Value).Where(k => !k.StartsWith('{'));
            var bad = statics.Where(k => !local.Contains(k)).Distinct().ToList();
            Assert.True(bad.Count == 0, $"{Path.GetFileName(file)} uses StaticResource for keys from other dictionaries: {string.Join(", ", bad)}");
        }
    }

    [Fact]
    public void Merged_dictionary_sources_point_at_real_files()
    {
        foreach (var file in Authored)
        {
            foreach (Match m in Regex.Matches(File.ReadAllText(file), @"Source=""pack://application:,,,/Slate\.Wpf;component/([^""]+)"""))
                Assert.True(File.Exists(Path.Combine(Lib, m.Groups[1].Value)), $"{Path.GetFileName(file)} merges missing {m.Groups[1].Value}");
        }
    }

    [Fact]
    public void SlateTheme_loads_files_that_exist()
    {
        var code = File.ReadAllText(Path.Combine(Lib, "SlateTheme.cs"));
        foreach (Match m in Regex.Matches(code, @"Load\(""([^""]+)""\)"))
            Assert.True(File.Exists(Path.Combine(Lib, m.Groups[1].Value)), $"SlateTheme loads missing {m.Groups[1].Value}");
        foreach (var name in new[] { "Theme.Light.xaml", "Theme.Dark.xaml", "Density.Compact.xaml", "Density.Comfortable.xaml" })
            Assert.True(File.Exists(Path.Combine(Lib, "Themes", "Generated", name)));
    }

    [Fact]
    public void Font_overrides_only_replace_generated_keys_and_fonts_are_embedded()
    {
        var generated = KeysIn(Generated);
        var fonts = Path.Combine(Lib, "Themes", "Fonts.xaml");
        foreach (var key in KeysIn([fonts]))
            Assert.Contains(key, generated);

        var ttf = Directory.GetFiles(Path.Combine(Root, "design", "fonts"), "*.ttf").Select(Path.GetFileName).ToList();
        Assert.Contains("InstrumentSans-Regular.ttf", ttf);
        Assert.Contains("IBMPlexMono-Regular.ttf", ttf);
        Assert.Contains(@"design\fonts\*.ttf", File.ReadAllText(Path.Combine(Lib, "Slate.Wpf.csproj")));
    }

    [Fact]
    public void Icon_names_used_in_xaml_exist_in_the_icon_set()
    {
        var icons = System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.Combine(Root, "design", "icons", "icons.json")))
            .RootElement.GetProperty("icons").EnumerateObject().Select(p => p.Name).ToHashSet();

        foreach (var file in Authored.Concat(Xaml(Demo)))
        {
            var text = File.ReadAllText(file);
            var used = Regex.Matches(text, @"(?:Kind|Sl\.StartIcon|Sl\.EndIcon|Icon|StartIcon|EndIcon|AppIcon)=""([a-z][a-z0-9-]*)""").Select(m => m.Groups[1].Value);
            var unknown = used.Where(u => !icons.Contains(u)).Distinct().ToList();
            Assert.True(unknown.Count == 0, $"{Path.GetFileName(file)} uses unknown icons: {string.Join(", ", unknown)}");
        }
    }
}
