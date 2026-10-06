using System.Reflection;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Slate.Avalonia.Controls;

namespace Slate.Avalonia.Tests;

/// <summary>
/// Checks Slate.Avalonia against the canonical component API (design/api/components.json): every option exists
/// under its canonical name with the canonical value set. Native controls map a few options onto their own
/// properties (IsChecked, IsEnabled…); everything else lives on the Slate control or the <see cref="Sl"/> attached properties.
/// </summary>
public class ConformanceTests
{
    private static readonly JsonElement Api = LoadApi();

    private static JsonElement LoadApi()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var path = Path.Combine(dir.FullName, "design", "api", "components.json");
            if (File.Exists(path))
                return JsonDocument.Parse(File.ReadAllText(path)).RootElement.Clone();
        }
        throw new FileNotFoundException("design/api/components.json not found above the test output.");
    }

    /// <summary>The Avalonia type implementing each component; Native = options come from Sl.* attached properties.</summary>
    private static readonly Dictionary<string, (Type Type, bool Native)> Targets = new()
    {
        ["Button"] = (typeof(Button), true),
        ["TextField"] = (typeof(TextField), false),
        ["Checkbox"] = (typeof(CheckBox), true),
        ["Switch"] = (typeof(Switch), false),
        ["RadioGroup"] = (typeof(RadioGroup), false),
        ["Alert"] = (typeof(Alert), false),
        ["Badge"] = (typeof(Badge), false),
        ["Progress"] = (typeof(ProgressBar), true),
        ["Spinner"] = (typeof(LoadingSpinner), false),
        ["Card"] = (typeof(Card), false),
        ["AppShell"] = (typeof(AppShell), false),
        ["AppBar"] = (typeof(AppBar), false),
        ["NavItem"] = (typeof(NavItem), false),
        ["Container"] = (typeof(Container), false),
        ["Stack"] = (typeof(Stack), false),
        ["SnackbarHost"] = (typeof(SnackbarHost), false),
        ["Dialog"] = (typeof(DialogContent), false),
    };

    /// <summary>Canonical options that map onto a framework-native member.</summary>
    private static readonly Dictionary<string, string> NativeMembers = new()
    {
        ["Checked"] = "IsChecked",
        ["Disabled"] = "IsEnabled",
        ["Max"] = "Maximum",
        ["Indeterminate"] = "IsIndeterminate",
        ["ShowValue"] = "ShowProgressText",
        ["CheckedChanged"] = "IsCheckedChanged",
    };

    /// <summary>Documented spelling differences (docs/design/configurability.md, Avalonia notes).</summary>
    private static readonly Dictionary<(string Component, string Option), string> Renames = new()
    {
        [("Container", "MaxWidth")] = "ContainerMaxWidth", // Layoutable.MaxWidth (double) already exists
        [("Dialog", "MaxWidth")] = "DialogMaxWidth",    // ditto
        [("Checkbox", "Label")] = "Content",            // native CheckBox label is its content
    };

    public static TheoryData<string, string> Options()
    {
        var data = new TheoryData<string, string>();
        foreach (var component in Api.GetProperty("components").EnumerateObject().Where(c => !(c.Value.TryGetProperty("status", out var st) && st.GetString() == "planned")))
        foreach (var option in component.Value.GetProperty("options").EnumerateObject())
        {
            if (option.Value.TryGetProperty("platforms", out var platforms)
                && !platforms.EnumerateArray().Any(p => p.GetString() is "xaml" or "avalonia"))
                continue;
            data.Add(component.Name, option.Name);
        }
        return data;
    }

    [Fact]
    public void Every_component_in_the_contract_has_an_avalonia_target() =>
        Assert.All(Api.GetProperty("components").EnumerateObject().Where(c => !(c.Value.TryGetProperty("status", out var st) && st.GetString() == "planned")), c => Assert.True(Targets.ContainsKey(c.Name), $"No Avalonia target for {c.Name}"));

    [Theory, MemberData(nameof(Options))]
    public void Option_exists_with_the_canonical_type(string component, string option)
    {
        var spec = Api.GetProperty("components").GetProperty(component).GetProperty("options").GetProperty(option);
        var kind = spec.GetProperty("kind").GetString();
        var (type, native) = Targets[component];
        var name = Renames.GetValueOrDefault((component, option)) ?? (NativeMembers.TryGetValue(option, out var n) && HasMember(type, n) ? n : option);

        if (kind == "event")
        {
            var routed = FindField(type, name + "Event");
            var clr = type.GetEvent(name, BindingFlags.Public | BindingFlags.Instance);
            Assert.True(routed is not null || clr is not null, $"{component}.{option}: no event '{name}' on {type.Name}");
            return;
        }

        var property = FindAvaloniaProperty(type, name) ?? (native ? FindAttached(name) : null);
        var clrProperty = type.GetProperty(name, BindingFlags.Public | BindingFlags.Instance);
        Assert.True(property is not null || clrProperty is not null, $"{component}.{option}: no property '{name}' on {type.Name}{(native ? " or Sl" : "")}");

        var valueType = property?.PropertyType ?? clrProperty!.PropertyType;
        var underlying = Nullable.GetUnderlyingType(valueType) ?? valueType;
        var declared = spec.TryGetProperty("type", out var t) ? t.GetString() : null;

        if (kind == "bool")
            Assert.True(underlying == typeof(bool), $"{component}.{option} should be bool, is {valueType.Name}");

        if (declared is not null && Api.GetProperty("enums").TryGetProperty(declared, out var values))
        {
            Assert.True(underlying.IsEnum, $"{component}.{option} should be the {declared} enum, is {valueType.Name}");
            Assert.Equal(declared, underlying.Name);
            AssertSameValues(values, underlying);
        }
    }

    [Fact]
    public void Core_enums_match_the_contract_value_sets()
    {
        var core = typeof(SlateTokens).Assembly;
        foreach (var e in Api.GetProperty("enums").EnumerateObject())
        {
            var type = core.GetTypes().FirstOrDefault(t => t.IsEnum && t.Name == e.Name);
            Assert.True(type is not null, $"Slate.Core has no enum {e.Name}");
            AssertSameValues(e.Value, type!);
        }
    }

    private static void AssertSameValues(JsonElement values, Type enumType)
    {
        static string Norm(string s) => s.Replace("-", "").ToLowerInvariant();
        var expected = values.EnumerateArray().Select(v => Norm(v.GetString()!)).Order().ToList();
        var actual = Enum.GetNames(enumType).Select(Norm).Order().ToList();
        Assert.Equal(expected, actual);
    }

    private static bool HasMember(Type type, string name) =>
        FindAvaloniaProperty(type, name) is not null || type.GetProperty(name) is not null || type.GetEvent(name) is not null || FindField(type, name + "Event") is not null;

    private static AvaloniaProperty? FindAvaloniaProperty(Type type, string name) =>
        AvaloniaPropertyRegistry.Instance.GetRegistered(type).FirstOrDefault(p => p.Name == name)
        ?? (FindField(type, name + "Property")?.GetValue(null) as AvaloniaProperty);

    private static AvaloniaProperty? FindAttached(string name) => FindField(typeof(Sl), name + "Property")?.GetValue(null) as AvaloniaProperty;

    private static FieldInfo? FindField(Type type, string name)
    {
        for (var t = type; t is not null; t = t.BaseType)
            if (t.GetField(name, BindingFlags.Public | BindingFlags.Static) is { } f)
                return f;
        return null;
    }
}
