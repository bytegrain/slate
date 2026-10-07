using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace Slate.Wpf.ResourceTests;

/// <summary>
/// Checks Slate.Wpf against the canonical component API (design/api/components.json) and checks every property
/// named in Slate.Wpf's XAML (triggers, setters, template bindings, attached-property paths) exists — the markup
/// compiler doesn't validate those, so a rename would otherwise only fail at runtime on Windows.
/// </summary>
public partial class ConformanceTests
{
    private static readonly string Root = XamlResourceTests.RepoRoot;
    private static readonly JsonElement Api = JsonDocument.Parse(File.ReadAllText(Path.Combine(Root, "design", "api", "components.json"))).RootElement;

    /// <summary>Components another track owns; everything else in the contract is required, whatever its "status".</summary>
    private static readonly HashSet<string> NotYetRequired = [];

    /// <summary>
    /// Which WPF type implements each component, from its "xamlType" in components.json ("sl:X" = Slate control,
    /// a bare name = WPF's native control configured through sl:Sl.*).
    /// </summary>
    private static readonly Dictionary<string, (string Type, bool Native)> Implementations =
        Api.GetProperty("components").EnumerateObject()
           .Where(c => !NotYetRequired.Contains(c.Name))
           .ToDictionary(c => c.Name, c =>
           {
               var type = ForWpf(c.Value, "xamlType") ?? throw new InvalidOperationException($"{c.Name} has no xamlType in components.json");
               return (type, !type.StartsWith("sl:", StringComparison.Ordinal));
           });

    /// <summary>
    /// The WPF spelling of an option: its "wpf" override, else its "xaml" alias (shared with Avalonia), else the global
    /// "xamlConventions" entry (Disabled → IsEnabled), else the canonical name. All read from components.json.
    /// </summary>
    private static string SpellingOf(string component, string option)
    {
        var spec = Api.GetProperty("components").GetProperty(component).GetProperty("options").GetProperty(option);
        if (spec.TryGetProperty("wpf", out var w)) return w.GetString()!;
        if (spec.TryGetProperty("xaml", out var x)) return x.GetString()!;
        if (Api.TryGetProperty("xamlConventions", out var c) && c.TryGetProperty(option, out var conv)) return conv.GetString()!;
        return option;
    }

    /// <summary>A string, or the "wpf" member of a { "wpf": …, "avalonia": … } object.</summary>
    private static string? ForWpf(JsonElement element, string key) =>
        !element.TryGetProperty(key, out var v) ? null
        : v.ValueKind == JsonValueKind.String ? v.GetString()
        : v.TryGetProperty("wpf", out var w) ? w.GetString() : null;

    public static TheoryData<string> Components => new(Implementations.Keys);

    [Fact]
    public void Metadata_checks_can_fail()
    {
        var card = WpfMetadata.Find("sl:Card")!;
        Assert.True(WpfMetadata.HasMember(card, "Flush"));
        Assert.False(WpfMetadata.HasMember(card, "NotAnOption"));
        Assert.True(PropertyExists(WpfMetadata.Find("Button")!, "IsPressed"));
        Assert.False(PropertyExists(WpfMetadata.Find("Button")!, "IsSquishy"));
        Assert.True(PropertyExists(card, "sl:Sl.ActualCornerRadius"));
        Assert.False(PropertyExists(card, "sl:Sl.Wobble"));
        Assert.Equal("Tone", WpfMetadata.PropertyType(WpfMetadata.Find("sl:Badge")!, "Tone")!.Name);
    }

    private static IEnumerable<(string Name, JsonElement Spec)> OptionsOf(string component) =>
        Api.GetProperty("components").GetProperty(component).GetProperty("options").EnumerateObject()
           .Where(o => !o.Value.TryGetProperty("platforms", out var p) || p.EnumerateArray().Any(x => x.GetString() == "wpf"))
           .Select(o => (o.Name, o.Value));

    [Fact]
    public void Every_contract_component_has_a_wpf_implementation()
    {
        // "status": "planned" is ignored on purpose: a wave stays required here even while the contract still marks it planned.
        var contract = Api.GetProperty("components").EnumerateObject().Select(c => c.Name).Where(c => !NotYetRequired.Contains(c)).ToHashSet();
        Assert.Empty(contract.Except(Implementations.Keys));
        foreach (var (component, (type, _)) in Implementations)
            Assert.True(WpfMetadata.Find(type) is not null, $"{component}: type {type} not found");
    }

    [Theory, MemberData(nameof(Components))]
    public void Exposes_every_canonical_option(string component)
    {
        var (typeName, native) = Implementations[component];
        var type = WpfMetadata.Find(typeName)!;
        var sl = WpfMetadata.Sl;
        var missing = new List<string>();

        foreach (var (option, spec) in OptionsOf(component))
        {
            var name = SpellingOf(component, option);
            var onType = WpfMetadata.HasMember(type, name);
            var attached = native && sl.GetMethod("Get" + name, BindingFlags.Public | BindingFlags.Static) is not null;
            if (!onType && !attached)
            {
                missing.Add(option);
                continue;
            }

            // Enum-typed options must use the shared enum (e.g. Tone, not a platform copy).
            var enumName = spec.TryGetProperty("type", out var t) ? t.GetString() : null;
            if (enumName is not null && Api.GetProperty("enums").TryGetProperty(enumName, out _) && spec.GetProperty("kind").GetString() == "param")
            {
                var actual = onType ? WpfMetadata.PropertyType(type, name) : sl.GetMethod("Get" + name)!.ReturnType;
                if (actual is not null)
                    Assert.True(WpfMetadata.Unwrap(actual).Name == enumName, $"{component}.{option} is {actual.Name}, expected {enumName}");
            }
        }

        Assert.True(missing.Count == 0, $"{component} ({typeName}) is missing: {string.Join(", ", missing)}");
    }

    [GeneratedRegex("(?<=[a-z0-9])([A-Z])")]
    private static partial Regex Hump();

    [Fact]
    public void Shared_enums_match_the_contract_values()
    {
        foreach (var e in Api.GetProperty("enums").EnumerateObject())
        {
            var type = WpfMetadata.Core.GetType("Slate." + e.Name) ?? WpfMetadata.Core.GetType("Slate.Snackbars." + e.Name)
                       ?? WpfMetadata.Core.GetType("Slate.Dialogs." + e.Name);
            if (type is null) continue; // e.g. Radius values listed as strings only
            var names = type.GetFields(BindingFlags.Public | BindingFlags.Static).Select(f => Hump().Replace(f.Name, "-$1").ToLowerInvariant()).ToList();
            var expected = e.Value.EnumerateArray().Select(v => v.GetString()!).ToList();
            Assert.True(expected.SequenceEqual(names), $"{e.Name}: contract [{string.Join(", ", expected)}] vs Core [{string.Join(", ", names)}]");
        }
    }

    [Fact]
    public void Platform_does_not_redefine_shared_enums()
    {
        var shared = Api.GetProperty("enums").EnumerateObject().Select(e => e.Name).ToHashSet();
        var duplicates = WpfMetadata.Slate.GetTypes().Where(t => t.IsEnum && t.IsPublic && shared.Contains(t.Name)).Select(t => t.FullName).ToList();
        Assert.Empty(duplicates);
    }

    // ---- XAML property names ----

    private static readonly XNamespace X = "http://schemas.microsoft.com/winfx/2006/xaml";

    public static TheoryData<string> LibraryXaml => new(
        Directory.EnumerateFiles(Path.Combine(Root, "src", "Slate.Wpf", "Themes"), "*.xaml", SearchOption.AllDirectories)
            .Where(f => !f.Contains("Generated"))
            .Select(f => Path.GetRelativePath(Root, f)));

    /// <summary>"sl:Sl.Tone" / "Grid.Column" / "TextElement.Foreground" → (owner type, property).</summary>
    private static (Type? Owner, string Property) Attached(string qualified)
    {
        var dot = qualified.LastIndexOf('.');
        var owner = qualified[..dot];
        return (WpfMetadata.Find(owner), qualified[(dot + 1)..]);
    }

    private static bool PropertyExists(Type target, string property)
    {
        property = property.Trim('(', ')');
        if (property.Contains('.'))
        {
            var (owner, name) = Attached(property);
            return owner is null || WpfMetadata.HasDependencyProperty(owner, name); // unknown owner namespaces are not checked
        }
        return WpfMetadata.HasDependencyProperty(target, property);
    }

    [Theory, MemberData(nameof(LibraryXaml))]
    public void Triggers_setters_and_template_bindings_name_real_properties(string file)
    {
        var doc = XDocument.Load(Path.Combine(Root, file));
        var problems = new List<string>();

        foreach (var scope in doc.Descendants().Where(e => e.Name.LocalName is "Style" or "ControlTemplate" && e.Attribute("TargetType") is not null))
        {
            var targetName = scope.Attribute("TargetType")!.Value.Replace("{x:Type ", "").TrimEnd('}');
            if (WpfMetadata.Find(targetName) is not { } target)
            {
                problems.Add($"unknown TargetType {targetName}");
                continue;
            }

            // Elements named inside this scope's template, for TargetName setters.
            var named = scope.Descendants().Where(e => e.Attribute(X + "Name") is not null)
                             .GroupBy(e => e.Attribute(X + "Name")!.Value).ToDictionary(g => g.Key, g => g.First().Name.LocalName.Contains('.') ? null : g.First());

            foreach (var e in scope.Descendants().Where(d => d.Name.LocalName is "Setter" or "Trigger" or "Condition"))
            {
                // Skip elements that belong to a nested Style/ControlTemplate scope.
                var nearest = e.Ancestors().FirstOrDefault(a => a.Name.LocalName is "Style" or "ControlTemplate" or "DataTemplate");
                if (nearest != scope) continue;

                var property = (string?)e.Attribute("Property");
                if (property is null) continue;
                var owner = target;
                if ((string?)e.Attribute("TargetName") is { } targetElement)
                {
                    if (!named.TryGetValue(targetElement, out var element) || element is null) { problems.Add($"TargetName {targetElement} not found"); continue; }
                    var tag = element.Name.NamespaceName.StartsWith("clr-namespace:Slate.Wpf") ? "sl:" + element.Name.LocalName : element.Name.LocalName;
                    if (WpfMetadata.Find(tag) is not { } elementType) { problems.Add($"unknown element {tag}"); continue; }
                    owner = elementType;
                }
                if (!PropertyExists(owner, property))
                    problems.Add($"{targetName}: {(e.Attribute("TargetName") is { } tn ? tn.Value + "." : "")}{property}");
            }

            foreach (Match m in Regex.Matches(scope.ToString(), @"\{TemplateBinding\s+([A-Za-z.()]+)\}"))
            {
                if (scope.Name.LocalName == "ControlTemplate" && !PropertyExists(target, m.Groups[1].Value))
                    problems.Add($"{targetName}: TemplateBinding {m.Groups[1].Value}");
            }
        }

        foreach (Match m in Regex.Matches(File.ReadAllText(Path.Combine(Root, file)), @"Path=\((sl:[A-Za-z]+\.[A-Za-z]+)\)"))
        {
            var (owner, name) = Attached(m.Groups[1].Value);
            if (owner is null || !WpfMetadata.HasDependencyProperty(owner, name))
                problems.Add($"binding path ({m.Groups[1].Value})");
        }

        Assert.True(problems.Count == 0, $"{file}: {string.Join("; ", problems.Distinct())}");
    }

    // ---- resource keys used from code ----

    [Fact]
    public void Resource_keys_used_in_code_exist()
    {
        var defined = XamlResourceTests.AllDefinedKeys();
        var code = string.Concat(Directory.EnumerateFiles(Path.Combine(Root, "src", "Slate.Wpf"), "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}") && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
            .Select(File.ReadAllText));
        var keys = new HashSet<string>();
        foreach (Match m in Regex.Matches(code, "\\$?\"(Sl\\.[A-Za-z0-9_.{}()]+)\""))
        {
            var key = m.Groups[1].Value;
            if (!key.Contains('{')) { keys.Add(key); continue; }
            // Expand the two interpolations Styling uses: status names and size suffixes.
            foreach (var status in new[] { "Success", "Warning", "Danger", "Info" })
            foreach (var size in new[] { "Sm", "Md", "Lg" })
                keys.Add(Regex.Replace(Regex.Replace(key, @"\{name\}", status), @"\{SizeSuffix\(size\)\}", size));
        }

        var missing = keys.Where(k => !defined.Contains(k)).ToList();
        Assert.True(missing.Count == 0, $"Code references undefined keys: {string.Join(", ", missing)}");
        Assert.Contains("Sl.Component.Button.PaddingMd", keys);
        Assert.Contains("Sl.Component.Tree.Indent", keys);
    }

    [Theory, MemberData(nameof(Components))]
    public void Slate_controls_have_a_default_template(string component)
    {
        var (typeName, native) = Implementations[component];
        if (native) return;
        // Only templated controls need a theme style; panels and self-drawn elements (Stack, Spinner) don't.
        var templated = false;
        for (var t = WpfMetadata.Find(typeName); t is not null; t = t.BaseType)
            templated |= t.FullName == "System.Windows.Controls.Control";
        if (!templated) return;
        var themes = Path.Combine(Root, "src", "Slate.Wpf", "Themes");
        var styled = Directory.EnumerateFiles(themes, "*.xaml", SearchOption.AllDirectories)
            .Any(f => File.ReadAllText(f).Contains($"TargetType=\"{typeName}\""));
        Assert.True(styled, $"{component}: no implicit Style TargetType=\"{typeName}\" in Themes/");
    }

    /// <summary>Wave-2 surfaces must draw from their component tokens, not raw palette picks.</summary>
    [Theory]
    [InlineData("sl:Popover", "Sl.Component.Popover.")]
    [InlineData("sl:Tooltip", "Sl.Component.Tooltip.")]
    [InlineData("sl:Tab", "Sl.Component.Tabs.")]
    [InlineData("sl:Tabs", "Sl.Component.Tabs.")]
    [InlineData("sl:Avatar", "Sl.Component.Avatar.")]
    [InlineData("sl:Skeleton", "Sl.Component.Skeleton.")]
    [InlineData("sl:Select", "Sl.Component.Menu.")]
    [InlineData("sl:Select", "Sl.Component.Field.")]
    [InlineData("sl:DatePicker", "Sl.Component.Field.")]
    [InlineData("Sl.CalendarDay", "Sl.Component.Calendar.")]
    [InlineData("sl:TreeView", "Sl.Component.Tree.")]
    public void Wave2_styles_use_component_tokens(string style, string prefix)
    {
        var xaml = File.ReadAllText(Path.Combine(Root, "src", "Slate.Wpf", "Themes", "Generic.xaml"));
        var doc = XDocument.Parse(xaml);
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        var element = doc.Root!.Elements().FirstOrDefault(e => e.Name.LocalName == "Style"
            && ((string?)e.Attribute(x + "Key") == style || ((string?)e.Attribute("TargetType") == style && e.Attribute(x + "Key") is null)));
        Assert.True(element is not null, $"no style {style}");
        var body = element!.ToString();
        // Shared templates (StaticResource) count: the overlay panel and option rows live beside the style.
        foreach (Match m in Regex.Matches(body, "StaticResource (Sl\\.[A-Za-z.]+)"))
            body += doc.Root.Elements().FirstOrDefault(e => (string?)e.Attribute(x + "Key") == m.Groups[1].Value)?.ToString();
        Assert.Contains(prefix, body);
    }
}
