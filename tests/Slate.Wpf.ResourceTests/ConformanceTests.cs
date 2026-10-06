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

    /// <summary>Which WPF type implements each component, and whether it is a native control configured through sl:Sl.*.</summary>
    private static readonly Dictionary<string, (string Type, bool Native)> Implementations = new()
    {
        ["Button"] = ("Button", true),
        ["TextField"] = ("sl:TextField", false),
        ["Checkbox"] = ("CheckBox", true),
        ["Switch"] = ("sl:Switch", false),
        ["RadioGroup"] = ("sl:RadioGroup", false),
        ["Alert"] = ("sl:Alert", false),
        ["Badge"] = ("sl:Badge", false),
        ["Progress"] = ("ProgressBar", true),
        ["Spinner"] = ("sl:Spinner", false),
        ["Card"] = ("sl:Card", false),
        ["AppShell"] = ("sl:AppShell", false),
        ["AppBar"] = ("sl:AppBar", false),
        ["NavItem"] = ("sl:NavItem", false),
        ["Container"] = ("sl:Container", false),
        ["Stack"] = ("sl:Stack", false),
        ["SnackbarHost"] = ("sl:SnackbarHost", false),
        ["Dialog"] = ("sl:DialogContent", false),
    };

    /// <summary>
    /// Documented WPF spellings (src/Slate.Wpf/README.md): framework-native names, and options whose canonical name
    /// collides with an inherited FrameworkElement member of a different type.
    /// </summary>
    private static readonly Dictionary<string, string> Spellings = new()
    {
        ["*.Disabled"] = "IsEnabled",
        ["Checkbox.Checked"] = "IsChecked",
        ["Checkbox.CheckedChanged"] = "Checked",
        ["Switch.Checked"] = "IsChecked",
        ["Switch.CheckedChanged"] = "Checked",
        ["Progress.Max"] = "Maximum",
        ["Progress.Indeterminate"] = "IsIndeterminate",
        ["Container.MaxWidth"] = "ContainerMaxWidth",
        ["Dialog.MaxWidth"] = "DialogMaxWidth",
    };

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
        var contract = Api.GetProperty("components").EnumerateObject().Select(c => c.Name).ToHashSet();
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
            var name = Spellings.GetValueOrDefault($"{component}.{option}") ?? Spellings.GetValueOrDefault($"*.{option}") ?? option;
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
        var code = File.ReadAllText(Path.Combine(Root, "src", "Slate.Wpf", "Styling.cs"))
                   + File.ReadAllText(Path.Combine(Root, "src", "Slate.Wpf", "Services", "DialogHost.cs"));
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
    }
}
