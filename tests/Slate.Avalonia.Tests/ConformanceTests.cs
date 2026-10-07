using System.Reflection;
using System.Text.Json;
using Avalonia;
using Avalonia.Headless.XUnit;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Slate.Avalonia.Controls;
// Slate controls share their contract names with Avalonia built-ins (sl:Menu, sl:DatePicker, sl:TreeView).
using Menu = Slate.Avalonia.Controls.Menu;
using DatePicker = Slate.Avalonia.Controls.DatePicker;
using TreeView = Slate.Avalonia.Controls.TreeView;

namespace Slate.Avalonia.Tests;

/// <summary>
/// Checks Slate.Avalonia against the canonical component API (design/api/components.json): every option exists
/// under its canonical name with the canonical value set. Native controls map a few options onto their own
/// properties (IsChecked, IsEnabled…); everything else lives on the Slate control or the <see cref="Sl"/> attached properties.
/// Runs on the headless UI thread like every other test here: reflecting over Avalonia/Slate types from a thread-pool
/// thread while UI-thread tests initialise the same statics intermittently deadlocked the test run.
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

    /// <summary>
    /// The Avalonia type implementing a component, from its "xamlType" in components.json: "sl:X" is the Slate control
    /// Slate.Avalonia.Controls.X; a bare name is Avalonia's native control (Native = options come from Sl.* attached properties).
    /// </summary>
    private static (Type Type, bool Native) TargetOf(string component)
    {
        var spec = Api.GetProperty("components").GetProperty(component);
        var name = ForAvalonia(spec, "xamlType") ?? throw new InvalidOperationException($"{component} has no xamlType in components.json");
        return name.StartsWith("sl:", StringComparison.Ordinal)
            ? (typeof(Sl).Assembly.GetType("Slate.Avalonia.Controls." + name[3..]) ?? throw new InvalidOperationException($"{component}: {name} not found in Slate.Avalonia"), false)
            : (typeof(Button).Assembly.GetType("Avalonia.Controls." + name) ?? throw new InvalidOperationException($"{component}: Avalonia.Controls.{name} not found"), true);
    }

    /// <summary>
    /// The Avalonia spelling of an option: its "avalonia" override, else its "xaml" alias (shared with WPF), else the
    /// global "xamlConventions" entry (Disabled → IsEnabled), else the canonical name.
    /// </summary>
    private static string SpellingOf(string component, string option)
    {
        var spec = Api.GetProperty("components").GetProperty(component).GetProperty("options").GetProperty(option);
        if (spec.TryGetProperty("avalonia", out var a)) return a.GetString()!;
        if (spec.TryGetProperty("xaml", out var x)) return x.GetString()!;
        if (Api.TryGetProperty("xamlConventions", out var c) && c.TryGetProperty(option, out var conv)) return conv.GetString()!;
        return option;
    }

    /// <summary>A string, or the "avalonia" member of a { "wpf": …, "avalonia": … } object.</summary>
    private static string? ForAvalonia(JsonElement element, string key) =>
        !element.TryGetProperty(key, out var v) ? null
        : v.ValueKind == JsonValueKind.String ? v.GetString()
        : v.TryGetProperty("avalonia", out var a) ? a.GetString() : null;

    private static IEnumerable<JsonProperty> RequiredComponents =>
        Api.GetProperty("components").EnumerateObject().Where(c => c.Name == "DataGrid" // planned on the web, implemented here
            || !(c.Value.TryGetProperty("status", out var st) && st.GetString() == "planned"));

    public static TheoryData<string, string> Options()
    {
        var data = new TheoryData<string, string>();
        foreach (var component in RequiredComponents)
        foreach (var option in component.Value.GetProperty("options").EnumerateObject())
        {
            if (option.Value.TryGetProperty("platforms", out var platforms)
                && !platforms.EnumerateArray().Any(p => p.GetString() is "xaml" or "avalonia"))
                continue;
            data.Add(component.Name, option.Name);
        }
        return data;
    }

    [AvaloniaFact]
    public void Every_component_in_the_contract_has_an_avalonia_target() =>
        Assert.All(RequiredComponents, c => Assert.NotNull(TargetOf(c.Name).Type));

    [AvaloniaTheory, MemberData(nameof(Options))]
    public void Option_exists_with_the_canonical_type(string component, string option)
    {
        var spec = Api.GetProperty("components").GetProperty(component).GetProperty("options").GetProperty(option);
        var kind = spec.GetProperty("kind").GetString();
        var (type, native) = TargetOf(component);
        var name = SpellingOf(component, option);

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


    [AvaloniaFact]
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
